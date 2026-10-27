#nullable disable

using System;
using Assets.Shared.Systems.InventorySystem.Items;

namespace Assets.Shared.Systems.InventorySystem.Crafting
{
    /// <summary>
    /// Одна потребность рецепта: predicate предмета и суммарное количество.
    /// Без SlotIndex количество собирается из любых подходящих стопок; с индексом — только из этой ячейки.
    /// Порядок объектов в списке Ingredients сам по себе не задаёт порядок входных слотов.
    /// </summary>
    public sealed class RecipeIngredient<TItem> where TItem : class, IInventoryItem
    {
        public RecipeIngredient(Func<TItem, bool> accepts, int quantity = 1, int? slotIndex = null)
        {
            Accepts = accepts ?? throw new ArgumentNullException(nameof(accepts));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (slotIndex < 0) throw new ArgumentOutOfRangeException(nameof(slotIndex));
            Quantity = quantity; SlotIndex = slotIndex;
        }
        public Func<TItem, bool> Accepts { get; }
        public int Quantity { get; }
        public int? SlotIndex { get; }
    }
}
