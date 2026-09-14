#nullable disable

using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Core;
using Assets.Shared.Systems.InventorySystem.Items;

namespace Assets.Shared.Systems.InventorySystem.Crafting
{
    /// <summary>
    /// Recipe, порядок, skill checks и набор input/output storages задаются
    /// конкретным TRequest и реализацией.
    /// </summary>
    public interface ICraftingStation<TItem, in TRequest> :
        IInventoryProcess<TItem, TRequest>
        where TItem : class, IInventoryItem
    {
    }

    public abstract class CraftingStationBase<TItem, TRequest> :
        InventoryProcessBase<TItem, TRequest>,
        ICraftingStation<TItem, TRequest>
        where TItem : class, IInventoryItem
    {
        protected CraftingStationBase(string inventoryId = null, string name = null)
            : base(inventoryId, name)
        {
        }

        protected sealed override InventoryActionKind ProcessKind =>
            InventoryActionKind.Craft;
    }
}
