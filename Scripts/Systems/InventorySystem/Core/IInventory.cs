#nullable disable

using Assets.Shared.SyncSystem.Core;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Items;
using System;
using System.Collections.Generic;

namespace Assets.Shared.Systems.InventorySystem.Core
{
    /// <summary>
    /// Негeneric marker позволяет обнаруживать разные инвентари в Data-graph
    /// через reflection, не зная тип их содержимого.
    /// </summary>
    public interface IInventory : ITrackable
    {
        string InventoryId { get; }
        string Name { get; }
        Type ItemType { get; }
    }

    public interface IInventory<TItem> : IInventory
        where TItem : class, IInventoryItem
    {
        event EventHandler<InventoryActionEventArgs<TItem>> ActionExecuting;
        event EventHandler<InventoryActionEventArgs<TItem>> ActionExecuted;
        event EventHandler<InventoryActionRejectedEventArgs<TItem>> ActionRejected;
        event EventHandler<InventoryObserverFaultedEventArgs<TItem>> ObserverFaulted;

        IReadOnlyList<IInventoryActionRule<TItem>> Rules { get; }

        void AddRule(IInventoryActionRule<TItem> rule);
        bool RemoveRule(IInventoryActionRule<TItem> rule);
        void ClearRules();

        /// <summary>
        /// Side-effect free проверка встроенных и подключённых правил.
        /// Events при preview не вызываются.
        /// </summary>
        InventoryValidationResult CanExecute(InventoryAction<TItem> action);
    }
}
