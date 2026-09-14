#nullable disable

using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Core;
using Assets.Shared.Systems.InventorySystem.Items;

namespace Assets.Shared.Systems.InventorySystem.Trading
{
    /// <summary>
    /// Базовый двусторонний/многосторонний обмен. Состав участников,
    /// подтверждения и правила владения задаются TRequest.
    /// </summary>
    public interface IExchangeInventory<TItem, in TRequest> :
        IInventoryProcess<TItem, TRequest>
        where TItem : class, IInventoryItem
    {
    }

    public abstract class ExchangeInventoryBase<TItem, TRequest> :
        InventoryProcessBase<TItem, TRequest>,
        IExchangeInventory<TItem, TRequest>
        where TItem : class, IInventoryItem
    {
        protected ExchangeInventoryBase(string inventoryId = null, string name = null)
            : base(inventoryId, name)
        {
        }

        protected sealed override InventoryActionKind ProcessKind =>
            InventoryActionKind.Exchange;
    }
}
