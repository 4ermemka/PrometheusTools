#nullable disable

using Assets.Shared.SyncSystem.Collections;
using Assets.Shared.SyncSystem.Core;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Core;
using Assets.Shared.Systems.InventorySystem.Items;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Assets.Shared.Systems.InventorySystem.Storage
{
    /// <summary>
    /// Переводит низкоуровневые Patched events SyncList/предметов в единый
    /// высокоуровневый ActionExecuted с Origin.Remote.
    /// </summary>
    internal sealed class InventoryPatchActionPublisher<TItem>
        where TItem : class, IInventoryItem
    {
        private readonly SyncList<TItem> _itemsState;
        private readonly IInventory<TItem> _inventory;
        private readonly Action<InventoryAction<TItem>> _publish;
        private readonly Dictionary<TItem, Action<string, object>> _itemHandlers =
            new(ReferenceComparer<TItem>.Instance);

        public InventoryPatchActionPublisher(
            SyncList<TItem> itemsState,
            IInventory<TItem> inventory,
            Action<InventoryAction<TItem>> publish)
        {
            _itemsState = itemsState ?? throw new ArgumentNullException(nameof(itemsState));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _itemsState.Patched += HandleCollectionPatched;
        }

        public void SubscribeToItem(TItem item)
        {
            if (item is not ITrackable trackable || _itemHandlers.ContainsKey(item))
                return;

            Action<string, object> handler = (path, value) =>
                HandleItemPatched(item, path);
            trackable.Patched += handler;
            _itemHandlers[item] = handler;
        }

        public void UnsubscribeFromItem(TItem item)
        {
            if (item is not ITrackable trackable ||
                !_itemHandlers.TryGetValue(item, out var handler))
                return;

            trackable.Patched -= handler;
            _itemHandlers.Remove(item);
        }

        private void RebuildItemSubscriptions()
        {
            foreach (var pair in _itemHandlers.ToArray())
            {
                if (pair.Key is ITrackable trackable)
                    trackable.Patched -= pair.Value;
            }
            _itemHandlers.Clear();

            foreach (var item in _itemsState)
                SubscribeToItem(item);
        }

        private void HandleCollectionPatched(string path, object value)
        {
            RebuildItemSubscriptions();

            var kind = ResolveRemoteActionKind(path);
            var affectedItems = ResolvePatchedItems(value);
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Path"] = path ?? string.Empty
            };
            var action = new InventoryAction<TItem>(
                Guid.NewGuid().ToString("N"),
                kind,
                kind.ToString(),
                InventoryActionOrigin.Remote,
                IsSource(kind) ? _inventory : null,
                IsTarget(kind) ? _inventory : null,
                affectedItems,
                null,
                metadata);

            _publish(action);
        }

        private void HandleItemPatched(TItem item, string path)
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Path"] = path ?? string.Empty
            };
            var action = new InventoryAction<TItem>(
                Guid.NewGuid().ToString("N"),
                InventoryActionKind.Update,
                InventoryActionKind.Update.ToString(),
                InventoryActionOrigin.Remote,
                _inventory,
                _inventory,
                new[] { item },
                null,
                metadata);
            _publish(action);
        }

        private static bool IsSource(InventoryActionKind kind)
        {
            return kind != InventoryActionKind.Add &&
                   kind != InventoryActionKind.TransferIn;
        }

        private static bool IsTarget(InventoryActionKind kind)
        {
            return kind != InventoryActionKind.Remove &&
                   kind != InventoryActionKind.Clear;
        }

        private static InventoryActionKind ResolveRemoteActionKind(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return InventoryActionKind.Refresh;
            if (path.StartsWith("add/", StringComparison.Ordinal) ||
                path.StartsWith("insert/", StringComparison.Ordinal))
                return InventoryActionKind.Add;
            if (path.StartsWith("remove/", StringComparison.Ordinal))
                return InventoryActionKind.Remove;
            if (path.StartsWith("clear", StringComparison.Ordinal))
                return InventoryActionKind.Clear;
            return InventoryActionKind.Update;
        }

        private static IReadOnlyList<TItem> ResolvePatchedItems(object value)
        {
            if (value is TItem item)
                return Array.AsReadOnly(new[] { item });
            if (value is IEnumerable<TItem> items)
                return Array.AsReadOnly(items.Where(candidate => candidate != null).ToArray());
            return Array.Empty<TItem>();
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T>
            where T : class
        {
            public static ReferenceComparer<T> Instance { get; } = new();

            public bool Equals(T x, T y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(T obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
