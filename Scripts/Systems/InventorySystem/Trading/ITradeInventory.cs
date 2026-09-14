#nullable disable

using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Items;

namespace Assets.Shared.Systems.InventorySystem.Trading
{
    /// <summary>
    /// Специализация exchange для торговли. Цена, валюта, скидки,
    /// доступность и направление сделки остаются частью TRequest/реализации.
    /// </summary>
    public interface ITradeInventory<TItem, in TRequest> :
        IExchangeInventory<TItem, TRequest>
        where TItem : class, IInventoryItem
    {
    }

    public abstract class TradeInventoryBase<TItem, TRequest> :
        Assets.Shared.Systems.InventorySystem.Core.InventoryProcessBase<TItem, TRequest>,
        ITradeInventory<TItem, TRequest>
        where TItem : class, IInventoryItem
    {
        protected TradeInventoryBase(string inventoryId = null, string name = null)
            : base(inventoryId, name)
        {
        }

        protected sealed override InventoryActionKind ProcessKind =>
            InventoryActionKind.Trade;
    }
}
