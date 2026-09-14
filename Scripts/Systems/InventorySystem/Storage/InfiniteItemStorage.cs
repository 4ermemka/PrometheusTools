#nullable disable

using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Assets.Shared.Systems.InventorySystem.Storage
{
    /// <summary>
    /// Items здесь являются каталогом шаблонов. Materialize и Transfer
    /// создают новый экземпляр через ItemFactory и не удаляют шаблон.
    /// </summary>
    [Serializable]
    public class InfiniteItemStorage<TItem, TContext> :
        UnorderedItemStorage<TItem>,
        IInfiniteItemStorage<TItem, TContext>
        where TItem : class, IInventoryItem
    {
        private readonly List<IInventoryFillRule<TItem, TContext>> _fillRules = new();
        private readonly ReadOnlyCollection<IInventoryFillRule<TItem, TContext>> _readOnlyFillRules;
        private readonly InfiniteItemMaterializer<TItem> _materializer;

        public InfiniteItemStorage()
        {
            _readOnlyFillRules = _fillRules.AsReadOnly();
            _materializer = new InfiniteItemMaterializer<TItem>(() => ItemFactory);
        }

        public InfiniteItemStorage(
            string inventoryId,
            IInventoryItemFactory<TItem> itemFactory,
            string name = null,
            IComparer<TItem> sortComparer = null,
            IEnumerable<TItem> initialTemplates = null)
            : base(inventoryId, name, sortComparer, initialTemplates)
        {
            _readOnlyFillRules = _fillRules.AsReadOnly();
            ItemFactory = itemFactory;
            _materializer = new InfiniteItemMaterializer<TItem>(() => ItemFactory);
        }

        public IReadOnlyList<IInventoryFillRule<TItem, TContext>> FillRules =>
            _readOnlyFillRules;

        public InventoryFillMode FillMode { get; set; } = InventoryFillMode.AllRules;
        public IInventoryItemFactory<TItem> ItemFactory { get; private set; }

        public void SetItemFactory(IInventoryItemFactory<TItem> itemFactory)
        {
            ItemFactory = itemFactory ?? throw new ArgumentNullException(nameof(itemFactory));
        }

        public void AddFillRule(IInventoryFillRule<TItem, TContext> rule)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));
            if (!_fillRules.Contains(rule))
                _fillRules.Add(rule);
        }

        public bool RemoveFillRule(IInventoryFillRule<TItem, TContext> rule)
        {
            return rule != null && _fillRules.Remove(rule);
        }

        public void ClearFillRules()
        {
            _fillRules.Clear();
        }

        public InventoryOperationResult<TItem> RefreshCatalog(
            IEnumerable<TItem> candidates,
            TContext context,
            InventoryActionOptions options = null)
        {
            var normalizedOptions = NormalizeOperationOptions(options);

            if (candidates == null)
            {
                return RejectRefresh(
                    normalizedOptions,
                    InventoryValidationResult.Deny(
                        "candidates_are_null",
                        "Catalog candidates are required."));
            }

            TItem[] filteredItems;
            try
            {
                filteredItems = candidates
                    .Where(candidate => candidate != null && MatchesFillRules(candidate, context))
                    .ToArray();
            }
            catch (Exception exception)
            {
                return RejectRefresh(
                    normalizedOptions,
                    InventoryValidationResult.Deny(
                        "fill_rule_exception",
                        $"{exception.GetType().Name}: {exception.Message}"),
                    exception);
            }

            return TryReplaceAll(
                filteredItems,
                InventoryActionKind.Refresh,
                normalizedOptions,
                "RefreshCatalog");
        }

        public InventoryMaterializationResult<TItem> TryMaterialize(
            string templateItemId,
            InventoryActionOptions options = null)
        {
            var normalizedOptions = NormalizeOperationOptions(options);
            if (!TryGetItem(templateItemId, out var template))
            {
                return RejectMaterialization(
                    normalizedOptions,
                    template,
                    InventoryValidationResult.Deny(
                        "template_not_found",
                        $"Template '{templateItemId}' is not present in infinite inventory {InventoryId}."));
            }

            if (!_materializer.TryCreate(
                    template,
                    out var instance,
                    out var creationResult,
                    out var exception))
            {
                return RejectMaterialization(
                    normalizedOptions,
                    template,
                    creationResult,
                    exception);
            }

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TemplateItemId"] = template.ItemId
            };
            var action = CreateAction(
                InventoryActionKind.Materialize,
                this,
                null,
                new[] { instance },
                normalizedOptions,
                additionalMetadata: metadata);
            var operation = TryExecuteAction(
                action,
                mutation: null,
                additionalValidation: () => _materializer.Validate(template, instance));

            return new InventoryMaterializationResult<TItem>(
                operation.Succeeded ? instance : null,
                operation);
        }

        public override InventoryOperationResult<TItem> TryTransferTo(
            IItemStorage<TItem> target,
            string itemId,
            InventoryActionOptions options = null)
        {
            return TryTransferTo(target, new[] { itemId }, options);
        }

        public override InventoryOperationResult<TItem> TryTransferTo(
            IItemStorage<TItem> target,
            IEnumerable<string> itemIds,
            InventoryActionOptions options = null)
        {
            var normalizedOptions = NormalizeOperationOptions(options);
            var templates = ResolveTemplates(itemIds, out var templateValidation);

            if (target is not ItemStorageBase<TItem> targetStorage)
            {
                var unsupportedAction = CreateAction(
                    InventoryActionKind.Materialize,
                    this,
                    target,
                    templates,
                    normalizedOptions);
                var unsupportedResult = InventoryValidationResult.Deny(
                    "unsupported_storage_implementation",
                    "Atomic infinite transfer requires target to inherit ItemStorageBase<TItem>.");
                PublishRejected(unsupportedAction, unsupportedResult);
                return InventoryOperationResult<TItem>.Rejected(
                    unsupportedAction.OperationId,
                    unsupportedResult,
                    null,
                    unsupportedAction);
            }

            if (ReferenceEquals(this, targetStorage))
            {
                var sameStorageAction = CreateAction(
                    InventoryActionKind.Materialize,
                    this,
                    targetStorage,
                    templates,
                    normalizedOptions);
                var sameStorageResult = InventoryValidationResult.Deny(
                    "same_storage",
                    "A materialized instance cannot be transferred back into its template catalog.");
                PublishRejected(sameStorageAction, sameStorageResult);
                return InventoryOperationResult<TItem>.Rejected(
                    sameStorageAction.OperationId,
                    sameStorageResult,
                    null,
                    sameStorageAction);
            }

            var createdItems = new List<TItem>();
            Exception factoryException = null;
            var creationValidation = templateValidation;
            if (creationValidation.IsAllowed)
            {
                foreach (var template in templates)
                {
                    if (!_materializer.TryCreate(
                            template,
                            out var instance,
                            out creationValidation,
                            out factoryException))
                        break;
                    createdItems.Add(instance);
                }
            }

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TemplateItemIds"] = string.Join(",", templates.Select(item => item.ItemId))
            };
            var sourceAction = CreateAction(
                InventoryActionKind.Materialize,
                this,
                targetStorage,
                createdItems,
                normalizedOptions,
                additionalMetadata: metadata);
            var targetAction = targetStorage.CreateAction(
                InventoryActionKind.TransferIn,
                this,
                targetStorage,
                createdItems,
                normalizedOptions,
                additionalMetadata: metadata);

            var sourceValidation = EvaluateAction(
                sourceAction,
                () => creationValidation.IsAllowed
                    ? _materializer.ValidateMany(templates, createdItems)
                    : creationValidation);
            var targetValidation = targetStorage.EvaluateAction(
                targetAction,
                () => creationValidation.IsAllowed
                    ? targetStorage.ValidateAddition(targetAction, replacingAll: false)
                    : creationValidation);

            var rejection = !sourceValidation.IsAllowed ? sourceValidation : targetValidation;
            if (!rejection.IsAllowed)
            {
                PublishRejected(sourceAction, rejection);
                targetStorage.PublishRejected(targetAction, rejection);
                return InventoryOperationResult<TItem>.Rejected(
                    sourceAction.OperationId,
                    rejection,
                    factoryException,
                    sourceAction,
                    targetAction);
            }

            PublishExecuting(sourceAction);
            targetStorage.PublishExecuting(targetAction);

            try
            {
                targetStorage.AddItemsCore(createdItems);
            }
            catch (Exception exception)
            {
                Exception rollbackException = null;
                try
                {
                    var addedItems = createdItems
                        .Where(item => targetStorage.Contains(item.ItemId))
                        .ToArray();
                    targetStorage.RemoveItemsCore(addedItems);
                }
                catch (Exception caughtRollbackException)
                {
                    rollbackException = caughtRollbackException;
                }

                var message = $"{exception.GetType().Name}: {exception.Message}";
                if (rollbackException != null)
                {
                    message +=
                        $" Rollback error: {rollbackException.GetType().Name}: {rollbackException.Message}";
                }
                var failure = InventoryValidationResult.Deny(
                    "infinite_transfer_mutation_exception",
                    message);
                PublishRejected(sourceAction, failure);
                targetStorage.PublishRejected(targetAction, failure);
                return InventoryOperationResult<TItem>.Rejected(
                    sourceAction.OperationId,
                    failure,
                    exception,
                    sourceAction,
                    targetAction);
            }

            PublishExecuted(sourceAction);
            targetStorage.PublishExecuted(targetAction);
            return InventoryOperationResult<TItem>.Completed(
                sourceAction.OperationId,
                sourceAction,
                targetAction);
        }

        private bool MatchesFillRules(TItem candidate, TContext context)
        {
            if (_fillRules.Count == 0)
                return true;

            return FillMode == InventoryFillMode.AllRules
                ? _fillRules.All(rule => rule.ShouldInclude(candidate, context))
                : _fillRules.Any(rule => rule.ShouldInclude(candidate, context));
        }

        private TItem[] ResolveTemplates(
            IEnumerable<string> itemIds,
            out InventoryValidationResult validation)
        {
            if (itemIds == null)
            {
                validation = InventoryValidationResult.Deny(
                    "template_ids_are_null",
                    "Template ids are required.");
                return Array.Empty<TItem>();
            }

            string[] ids;
            try
            {
                ids = itemIds.ToArray();
            }
            catch (Exception exception)
            {
                validation = InventoryValidationResult.Deny(
                    "template_ids_enumeration_failed",
                    $"{exception.GetType().Name}: {exception.Message}");
                return Array.Empty<TItem>();
            }

            if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace))
            {
                validation = InventoryValidationResult.Deny(
                    "invalid_template_id",
                    "At least one non-empty template id is required.");
                return Array.Empty<TItem>();
            }
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            {
                validation = InventoryValidationResult.Deny(
                    "duplicate_template_id",
                    "The same template cannot be materialized twice in one operation.");
                return Array.Empty<TItem>();
            }

            var templates = new List<TItem>(ids.Length);
            foreach (var id in ids)
            {
                if (!TryGetItem(id, out var template))
                {
                    validation = InventoryValidationResult.Deny(
                        "template_not_found",
                        $"Template '{id}' is not present in infinite inventory {InventoryId}.");
                    return templates.ToArray();
                }
                templates.Add(template);
            }

            validation = InventoryValidationResult.Allow();
            return templates.ToArray();
        }

        private InventoryOperationResult<TItem> RejectRefresh(
            InventoryActionOptions options,
            InventoryValidationResult validation,
            Exception exception = null)
        {
            var action = CreateAction(
                InventoryActionKind.Refresh,
                this,
                this,
                Array.Empty<TItem>(),
                options,
                "RefreshCatalog");
            PublishRejected(action, validation);
            return InventoryOperationResult<TItem>.Rejected(
                action.OperationId,
                validation,
                exception,
                action);
        }

        private InventoryMaterializationResult<TItem> RejectMaterialization(
            InventoryActionOptions options,
            TItem template,
            InventoryValidationResult validation,
            Exception exception = null)
        {
            var items = template == null ? Array.Empty<TItem>() : new[] { template };
            var action = CreateAction(
                InventoryActionKind.Materialize,
                this,
                null,
                items,
                options);
            PublishRejected(action, validation);
            var operation = InventoryOperationResult<TItem>.Rejected(
                action.OperationId,
                validation,
                exception,
                action);
            return new InventoryMaterializationResult<TItem>(null, operation);
        }
    }

    /// <summary>
    /// Convenience-вариант, когда fill context не нужен.
    /// </summary>
    [Serializable]
    public class InfiniteItemStorage<TItem> : InfiniteItemStorage<TItem, object>
        where TItem : class, IInventoryItem
    {
        public InfiniteItemStorage()
        {
        }

        public InfiniteItemStorage(
            string inventoryId,
            IInventoryItemFactory<TItem> itemFactory,
            string name = null,
            IComparer<TItem> sortComparer = null,
            IEnumerable<TItem> initialTemplates = null)
            : base(inventoryId, itemFactory, name, sortComparer, initialTemplates)
        {
        }

        public InventoryOperationResult<TItem> RefreshCatalog(
            IEnumerable<TItem> candidates,
            InventoryActionOptions options = null)
        {
            return RefreshCatalog(candidates, null, options);
        }
    }
}
