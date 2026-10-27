using System;
using Assets.Shared.Systems.InventorySystem.Items;

namespace Assets.Shared.Systems.InventorySystem.Slots
{
    /// <summary>Общие проверки контракта копирования; предметы без opt-in остаются единичными.</summary>
    internal static class StackItems<TItem> where TItem : class, IInventoryItem
    {
        internal static int Quantity(TItem item) => item is IStackableItem<TItem> stack ? stack.Quantity : 1;
        internal static int Limit(TItem item) => item is IStackableItem<TItem> stack ? stack.MaxStack : 1;
        internal static bool Compatible(TItem a, TItem b) =>
            a is IStackableItem<TItem> left && b is IStackableItem<TItem> right &&
            left.CanStackWith(b) && right.CanStackWith(a);
        internal static TItem Copy(TItem item, int amount, string id)
        {
            if (amount == Quantity(item) && id == item.ItemId) return item;
            if (!(item is IStackableItem<TItem> stack)) throw new InvalidOperationException("Item does not support stacks.");
            var result = stack.WithStack(amount, id);
            if (result == null || ReferenceEquals(result, item) || result.GetType() != item.GetType() ||
                result.ItemId != id || Quantity(result) != amount ||
                amount < 1 || amount > Limit(result))
                throw new InvalidOperationException("WithStack violated its quantity, identity or compatibility contract.");
            return result;
        }
    }
}
