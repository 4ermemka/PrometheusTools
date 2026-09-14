#nullable disable

using System;
using System.Collections.Generic;

namespace Assets.Shared.Systems.InventorySystem.Items
{
    /// <summary>
    /// Детерминированная сортировка неупорядоченного хранилища:
    /// сначала по Name, затем по ItemId.
    /// Ordinal-сравнение не зависит от locale конкретного клиента.
    /// </summary>
    public sealed class InventoryItemNameComparer<TItem> : IComparer<TItem>
        where TItem : class, IInventoryItem
    {
        public static InventoryItemNameComparer<TItem> Instance { get; } = new();

        private InventoryItemNameComparer()
        {
        }

        public int Compare(TItem x, TItem y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            var nameComparison = StringComparer.Ordinal.Compare(x.Name ?? string.Empty, y.Name ?? string.Empty);
            if (nameComparison != 0)
                return nameComparison;

            return StringComparer.Ordinal.Compare(x.ItemId ?? string.Empty, y.ItemId ?? string.Empty);
        }
    }
}
