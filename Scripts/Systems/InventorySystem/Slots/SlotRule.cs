#nullable disable

using System;
using Assets.Shared.Systems.InventorySystem.Items;

namespace Assets.Shared.Systems.InventorySystem.Slots
{
    /// <summary>
    /// Фильтр содержимого одной ячейки. Predicate описывает допустимый предмет; пустая ячейка допустима всегда.
    /// Отличается от IInventoryActionRule: тот ограничивает операции, например право на перенос или расход.
    /// </summary>
    public sealed class SlotRule<TItem> where TItem : class, IInventoryItem
    {
        private readonly Func<TItem, bool> _accepts;
        public SlotRule(string name = null, Func<TItem, bool> accepts = null)
        { Name = name; _accepts = accepts; }
        public string Name { get; }
        public bool Accepts(TItem item) => item == null || _accepts == null || _accepts(item);
    }
}
