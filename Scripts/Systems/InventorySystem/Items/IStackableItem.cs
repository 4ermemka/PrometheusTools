namespace Assets.Shared.Systems.InventorySystem.Items
{
    /// <summary>
    /// Неизменяемая стопка. WithStack создаёт копию с указанной идентичностью, сохраняя остальные параметры.
    /// CanStackWith сравнивает вид и значимые параметры; проверяется в обоих направлениях.
    /// </summary>
    public interface IStackableItem<TItem> : ICraftingItem<TItem> where TItem : class, IInventoryItem
    {
        int MaxStack { get; }
        bool CanStackWith(TItem other);
        TItem WithStack(int quantity, string itemId);
    }
}
