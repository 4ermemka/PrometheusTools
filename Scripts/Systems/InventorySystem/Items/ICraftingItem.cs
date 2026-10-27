#nullable disable

namespace Assets.Shared.Systems.InventorySystem.Items
{
    /// <summary>
    /// Минимальный контракт для расходуемых стопок. WithQuantity должен вернуть новый объект с тем же ItemId
    /// и заданным положительным количеством, сохранив параметры предмета; исходный объект менять нельзя.
    /// Полностью израсходованная стопка удаляется из слота, а не превращается в предмет с Quantity = 0.
    /// </summary>
    public interface ICraftingItem<TItem> : IInventoryItem where TItem : class, IInventoryItem
    {
        int Quantity { get; }
        TItem WithQuantity(int amount);
    }
}
