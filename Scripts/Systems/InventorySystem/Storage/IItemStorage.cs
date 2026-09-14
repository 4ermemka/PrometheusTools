#nullable disable

using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Core;
using Assets.Shared.Systems.InventorySystem.Items;
using System;
using System.Collections.Generic;

namespace Assets.Shared.Systems.InventorySystem.Storage
{
    /// <summary>
    /// Хранилище конкретных экземпляров предметов.
    /// Порядок Items предназначен только для отображения и не является
    /// адресом предмета; все mutation используют стабильный ItemId.
    /// </summary>
    public interface IItemStorage<TItem> : IInventory<TItem>
        where TItem : class, IInventoryItem
    {
        IReadOnlyList<TItem> Items { get; }
        int Count { get; }
        IComparer<TItem> SortComparer { get; }

        bool Contains(string itemId);
        bool TryGetItem(string itemId, out TItem item);
        IReadOnlyList<TItem> FindItems(Func<TItem, bool> predicate);

        InventoryValidationResult CanAdd(TItem item);
        InventoryValidationResult CanAddRange(IEnumerable<TItem> items);
        InventoryValidationResult CanRemove(string itemId);
        InventoryValidationResult CanRemoveRange(IEnumerable<string> itemIds);

        InventoryOperationResult<TItem> TryAdd(
            TItem item,
            InventoryActionOptions options = null);

        InventoryOperationResult<TItem> TryAddRange(
            IEnumerable<TItem> items,
            InventoryActionOptions options = null);

        InventoryOperationResult<TItem> TryRemove(
            string itemId,
            InventoryActionOptions options = null);

        InventoryOperationResult<TItem> TryRemoveRange(
            IEnumerable<string> itemIds,
            InventoryActionOptions options = null);

        InventoryOperationResult<TItem> TryClear(
            InventoryActionOptions options = null);

        InventoryOperationResult<TItem> TryTransferTo(
            IItemStorage<TItem> target,
            string itemId,
            InventoryActionOptions options = null);

        InventoryOperationResult<TItem> TryTransferTo(
            IItemStorage<TItem> target,
            IEnumerable<string> itemIds,
            InventoryActionOptions options = null);
    }

    /// <summary>
    /// Явно фиксирует текущую модель: ручного порядка/слотов нет,
    /// Items всегда возвращается в сортировке SortComparer.
    /// </summary>
    public interface IUnorderedItemStorage<TItem> : IItemStorage<TItem>
        where TItem : class, IInventoryItem
    {
    }
}
