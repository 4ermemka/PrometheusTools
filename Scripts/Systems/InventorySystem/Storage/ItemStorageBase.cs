#nullable disable

using Assets.Shared.SyncSystem.Collections;
using Assets.Shared.SyncSystem.Core;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Core;
using Assets.Shared.Systems.InventorySystem.Items;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Assets.Shared.Systems.InventorySystem.Storage
{
    /// <summary>
    /// Готовая реализация неупорядоченного хранилища.
    /// Локальные mutation проходят action pipeline и изменяют SyncList,
    /// поэтому TrackableNode поднимает Changed для формирования сетевого patch.
    /// Входящий SyncList.Patched преобразуется обратно в ActionExecuted с
    /// Origin.Remote для локальной реакции.
    /// </summary>
    public abstract class ItemStorageBase<TItem> : InventoryBase<TItem>, IUnorderedItemStorage<TItem>
        where TItem : class, IInventoryItem
    {
        // Имя ItemsState является частью path-based snapshot/patch контракта.
        protected readonly SyncList<TItem> ItemsState = new();

        private readonly IComparer<TItem> _sortComparer;
        private readonly InventoryPatchActionPublisher<TItem> _patchPublisher;

        protected ItemStorageBase(
            string inventoryId = null,
            string name = null,
            IComparer<TItem> sortComparer = null,
            IEnumerable<TItem> initialItems = null)
            : base(inventoryId, name)
        {
            _sortComparer = sortComparer ?? InventoryItemNameComparer<TItem>.Instance;
            _patchPublisher = new InventoryPatchActionPublisher<TItem>(
                ItemsState,
                this,
                PublishExecuted);

            if (initialItems == null)
                return;

            var preparedItems = initialItems.ToArray();
            var validation = ValidateItemSet(preparedItems, checkExistingItems: false);
            if (!validation.IsAllowed)
                throw new ArgumentException(validation.Message, nameof(initialItems));

            AddItemsCore(preparedItems);
        }

        public IReadOnlyList<TItem> Items =>
            Array.AsReadOnly(ItemsState.OrderBy(item => item, _sortComparer).ToArray());

        public int Count => ItemsState.Count;
        public IComparer<TItem> SortComparer => _sortComparer;

        public bool Contains(string itemId)
        {
            return TryGetItem(itemId, out _);
        }

        public bool TryGetItem(string itemId, out TItem item)
        {
            item = null;
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            for (var index = 0; index < ItemsState.Count; index++)
            {
                var candidate = ItemsState[index];
                if (candidate != null &&
                    StringComparer.Ordinal.Equals(candidate.ItemId, itemId))
                {
                    item = candidate;
                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<TItem> FindItems(Func<TItem, bool> predicate)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return Array.AsReadOnly(Items.Where(predicate).ToArray());
        }

        public InventoryValidationResult CanAdd(TItem item)
        {
            return CanAddRange(new[] { item });
        }

        public InventoryValidationResult CanAddRange(IEnumerable<TItem> items)
        {
            var preparedItems = PrepareItems(items, out var preparationResult);
            var action = CreateAction(
                InventoryActionKind.Add,
                null,
                this,
                preparedItems,
                new InventoryActionOptions { Origin = InventoryActionOrigin.Local });

            return EvaluateAction(
                action,
                () => preparationResult.IsAllowed
                    ? ValidateAddition(action, replacingAll: false)
                    : preparationResult);
        }

        public InventoryValidationResult CanRemove(string itemId)
        {
            return CanRemoveRange(new[] { itemId });
        }

        public InventoryValidationResult CanRemoveRange(IEnumerable<string> itemIds)
        {
            var preparedItems = ResolveItems(itemIds, out var preparationResult);
            var action = CreateAction(
                InventoryActionKind.Remove,
                this,
                null,
                preparedItems,
                new InventoryActionOptions { Origin = InventoryActionOrigin.Local });

            return EvaluateAction(
                action,
                () => preparationResult.IsAllowed
                    ? ValidateRemoval(action)
                    : preparationResult);
        }

        public virtual InventoryOperationResult<TItem> TryAdd(
            TItem item,
            InventoryActionOptions options = null)
        {
            return TryAddRange(new[] { item }, options);
        }

        public virtual InventoryOperationResult<TItem> TryAddRange(
            IEnumerable<TItem> items,
            InventoryActionOptions options = null)
        {
            var preparedItems = PrepareItems(items, out var preparationResult);
            var action = CreateAction(
                InventoryActionKind.Add,
                null,
                this,
                preparedItems,
                options);

            return TryExecuteAction(
                action,
                () => AddItemsCore(preparedItems),
                () => preparationResult.IsAllowed
                    ? ValidateAddition(action, replacingAll: false)
                    : preparationResult);
        }

        public virtual InventoryOperationResult<TItem> TryRemove(
            string itemId,
            InventoryActionOptions options = null)
        {
            return TryRemoveRange(new[] { itemId }, options);
        }

        public virtual InventoryOperationResult<TItem> TryRemoveRange(
            IEnumerable<string> itemIds,
            InventoryActionOptions options = null)
        {
            var preparedItems = ResolveItems(itemIds, out var preparationResult);
            var action = CreateAction(
                InventoryActionKind.Remove,
                this,
                null,
                preparedItems,
                options);

            return TryExecuteAction(
                action,
                () => RemoveItemsCore(preparedItems),
                () => preparationResult.IsAllowed
                    ? ValidateRemoval(action)
                    : preparationResult);
        }

        public virtual InventoryOperationResult<TItem> TryClear(
            InventoryActionOptions options = null)
        {
            var items = ItemsState.ToArray();
            var action = CreateAction(
                InventoryActionKind.Clear,
                this,
                null,
                items,
                options);

            return TryExecuteAction(
                action,
                ClearItemsCore,
                () => items.Length == 0
                    ? ValidateRemovalCore(action)
                    : ValidateRemoval(action));
        }

        public virtual InventoryOperationResult<TItem> TryTransferTo(
            IItemStorage<TItem> target,
            string itemId,
            InventoryActionOptions options = null)
        {
            return TryTransferTo(target, new[] { itemId }, options);
        }

        public virtual InventoryOperationResult<TItem> TryTransferTo(
            IItemStorage<TItem> target,
            IEnumerable<string> itemIds,
            InventoryActionOptions options = null)
        {
            var preparedItems = ResolveItems(itemIds, out var preparationResult);
            return InventoryTransferCoordinator<TItem>.TryTransfer(
                this,
                target,
                preparedItems,
                preparationResult,
                options);
        }

        /// <summary>
        /// Extension point для capacity, type whitelist, ownership и других
        /// ограничений конкретного хранилища.
        /// </summary>
        protected virtual InventoryValidationResult ValidateAdditionCore(
            InventoryAction<TItem> action,
            int resultingCount)
        {
            return InventoryValidationResult.Allow();
        }

        /// <summary>
        /// Extension point для locked/quest/equipped items и role policies.
        /// </summary>
        protected virtual InventoryValidationResult ValidateRemovalCore(
            InventoryAction<TItem> action)
        {
            return InventoryValidationResult.Allow();
        }

        protected InventoryOperationResult<TItem> TryReplaceAll(
            IEnumerable<TItem> items,
            InventoryActionKind kind,
            InventoryActionOptions options = null,
            string actionName = null)
        {
            var preparedItems = PrepareItems(items, out var preparationResult, allowEmpty: true);
            var action = CreateAction(
                kind,
                this,
                this,
                preparedItems,
                options,
                actionName);

            return TryExecuteAction(
                action,
                () =>
                {
                    ClearItemsCore();
                    AddItemsCore(preparedItems);
                },
                () => preparationResult.IsAllowed
                    ? ValidateAddition(action, replacingAll: true)
                    : preparationResult);
        }

        protected internal InventoryValidationResult ValidateAddition(
            InventoryAction<TItem> action,
            bool replacingAll)
        {
            if (replacingAll && action.Items.Count == 0)
                return ValidateAdditionCore(action, resultingCount: 0);

            var itemSetValidation = ValidateItemSet(action.Items, checkExistingItems: !replacingAll);
            if (!itemSetValidation.IsAllowed)
                return itemSetValidation;

            var resultingCount = replacingAll
                ? action.Items.Count
                : Count + action.Items.Count;
            return ValidateAdditionCore(action, resultingCount);
        }

        protected internal InventoryValidationResult ValidateRemoval(
            InventoryAction<TItem> action)
        {
            if (action.Items.Count == 0)
                return InventoryValidationResult.Deny("no_items", "At least one item is required.");

            foreach (var item in action.Items)
            {
                if (item == null || !Contains(item.ItemId))
                {
                    return InventoryValidationResult.Deny(
                        "item_not_found",
                        $"Item '{item?.ItemId ?? "<null>"}' is not present in inventory {InventoryId}.");
                }
            }

            return ValidateRemovalCore(action);
        }

        protected internal void AddItemsCore(IEnumerable<TItem> items)
        {
            foreach (var item in items)
            {
                ItemsState.Add(item);
                _patchPublisher.SubscribeToItem(item);
            }
        }

        protected internal void RemoveItemsCore(IEnumerable<TItem> items)
        {
            foreach (var item in items)
            {
                var index = FindIndex(item.ItemId);
                if (index < 0)
                    continue;

                var storedItem = ItemsState[index];
                _patchPublisher.UnsubscribeFromItem(storedItem);
                ItemsState.RemoveAt(index);
            }
        }

        protected internal InventoryActionOptions NormalizeOperationOptions(
            InventoryActionOptions options)
        {
            options ??= new InventoryActionOptions();
            return options.WithOperationId(options.ResolveOperationId());
        }

        private void ClearItemsCore()
        {
            foreach (var item in ItemsState.ToArray())
                _patchPublisher.UnsubscribeFromItem(item);
            ItemsState.Clear();
        }

        private TItem[] PrepareItems(
            IEnumerable<TItem> items,
            out InventoryValidationResult result,
            bool allowEmpty = false)
        {
            if (items == null)
            {
                result = InventoryValidationResult.Deny("items_are_null", "Items collection is required.");
                return Array.Empty<TItem>();
            }

            try
            {
                var preparedItems = items.ToArray();
                result = allowEmpty && preparedItems.Length == 0
                    ? InventoryValidationResult.Allow()
                    : ValidateItemSet(preparedItems, checkExistingItems: false);
                return preparedItems;
            }
            catch (Exception exception)
            {
                result = InventoryValidationResult.Deny(
                    "items_enumeration_failed",
                    $"{exception.GetType().Name}: {exception.Message}");
                return Array.Empty<TItem>();
            }
        }

        private TItem[] ResolveItems(
            IEnumerable<string> itemIds,
            out InventoryValidationResult result)
        {
            if (itemIds == null)
            {
                result = InventoryValidationResult.Deny("item_ids_are_null", "Item ids are required.");
                return Array.Empty<TItem>();
            }

            string[] ids;
            try
            {
                ids = itemIds.ToArray();
            }
            catch (Exception exception)
            {
                result = InventoryValidationResult.Deny(
                    "item_ids_enumeration_failed",
                    $"{exception.GetType().Name}: {exception.Message}");
                return Array.Empty<TItem>();
            }

            if (ids.Length == 0)
            {
                result = InventoryValidationResult.Deny("no_items", "At least one item id is required.");
                return Array.Empty<TItem>();
            }

            if (ids.Any(string.IsNullOrWhiteSpace))
            {
                result = InventoryValidationResult.Deny("invalid_item_id", "ItemId cannot be empty.");
                return Array.Empty<TItem>();
            }

            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            {
                result = InventoryValidationResult.Deny(
                    "duplicate_item_id",
                    "An operation cannot contain the same ItemId more than once.");
                return Array.Empty<TItem>();
            }

            var resolvedItems = new List<TItem>(ids.Length);
            foreach (var id in ids)
            {
                if (!TryGetItem(id, out var item))
                {
                    result = InventoryValidationResult.Deny(
                        "item_not_found",
                        $"Item '{id}' is not present in inventory {InventoryId}.");
                    return resolvedItems.ToArray();
                }
                resolvedItems.Add(item);
            }

            result = InventoryValidationResult.Allow();
            return resolvedItems.ToArray();
        }

        private InventoryValidationResult ValidateItemSet(
            IReadOnlyCollection<TItem> items,
            bool checkExistingItems)
        {
            if (items == null || items.Count == 0)
                return InventoryValidationResult.Deny("no_items", "At least one item is required.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item == null)
                    return InventoryValidationResult.Deny("item_is_null", "Inventory item cannot be null.");
                if (string.IsNullOrWhiteSpace(item.ItemId))
                    return InventoryValidationResult.Deny("invalid_item_id", "ItemId cannot be empty.");
                if (string.IsNullOrWhiteSpace(item.Name))
                    return InventoryValidationResult.Deny(
                        "invalid_item_name",
                        $"Item '{item.ItemId}' must have a Name for deterministic sorting.");
                if (!ids.Add(item.ItemId))
                {
                    return InventoryValidationResult.Deny(
                        "duplicate_item_id",
                        $"ItemId '{item.ItemId}' occurs more than once in the operation.");
                }
                if (checkExistingItems && Contains(item.ItemId))
                {
                    return InventoryValidationResult.Deny(
                        "item_already_exists",
                        $"Item '{item.ItemId}' already exists in inventory {InventoryId}.");
                }
            }

            return InventoryValidationResult.Allow();
        }

        private int FindIndex(string itemId)
        {
            for (var index = 0; index < ItemsState.Count; index++)
            {
                var item = ItemsState[index];
                if (item != null && StringComparer.Ordinal.Equals(item.ItemId, itemId))
                    return index;
            }
            return -1;
        }

    }

    /// <summary>
    /// Используемая по умолчанию реализация. Для capacity или специальных
    /// ограничений достаточно наследоваться и переопределить validation hooks.
    /// </summary>
    [Serializable]
    public class UnorderedItemStorage<TItem> : ItemStorageBase<TItem>
        where TItem : class, IInventoryItem
    {
        public UnorderedItemStorage()
        {
        }

        public UnorderedItemStorage(
            string inventoryId,
            string name = null,
            IComparer<TItem> sortComparer = null,
            IEnumerable<TItem> initialItems = null)
            : base(inventoryId, name, sortComparer, initialItems)
        {
        }
    }
}
