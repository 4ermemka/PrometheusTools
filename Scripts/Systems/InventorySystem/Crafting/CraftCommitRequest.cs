#nullable disable

using Assets.Shared.Systems.InventorySystem.Items;
using Assets.Shared.Systems.InventorySystem.Slots;

namespace Assets.Shared.Systems.InventorySystem.Crafting
{
    /// <summary>
    /// Запрос подтверждения: какой preview видел игрок и куда нужно выдать его результат.
    /// TargetSlot = -1 ищет свободную подходящую ячейку; запрос не изменяет инвентари до TryExecute.
    /// </summary>
    public sealed class CraftCommitRequest<TItem> where TItem : class, ICraftingItem<TItem>
    {
        public CraftCommitRequest(CraftPreview<TItem> preview, SlottedInventory<TItem> target, int targetSlot = -1)
        { Preview = preview; Target = target; TargetSlot = targetSlot; }
        public CraftPreview<TItem> Preview { get; }
        public SlottedInventory<TItem> Target { get; }
        public int TargetSlot { get; }
    }
}
