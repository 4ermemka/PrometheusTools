#nullable disable

using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Items;
using System;
using System.Collections.Generic;

namespace Assets.Shared.Systems.InventorySystem.Storage
{
    public enum InventoryFillMode
    {
        AllRules = 0,
        AnyRule = 1
    }

    public interface IInventoryFillRule<TItem, in TContext>
        where TItem : class, IInventoryItem
    {
        bool ShouldInclude(TItem candidate, TContext context);
    }

    public delegate bool InventoryFillRule<TItem, in TContext>(
        TItem candidate,
        TContext context)
        where TItem : class, IInventoryItem;

    public sealed class DelegateInventoryFillRule<TItem, TContext> :
        IInventoryFillRule<TItem, TContext>
        where TItem : class, IInventoryItem
    {
        private readonly InventoryFillRule<TItem, TContext> _rule;

        public DelegateInventoryFillRule(InventoryFillRule<TItem, TContext> rule)
        {
            _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        }

        public bool ShouldInclude(TItem candidate, TContext context)
        {
            return _rule(candidate, context);
        }
    }

    /// <summary>
    /// Factory отвечает за создание отдельного экземпляра по шаблону.
    /// Переданный instanceId уже уникален; реализация должна записать его
    /// в созданный предмет.
    /// </summary>
    public interface IInventoryItemFactory<TItem>
        where TItem : class, IInventoryItem
    {
        TItem Create(TItem template, string instanceId);
    }

    public delegate TItem InventoryItemFactory<TItem>(
        TItem template,
        string instanceId)
        where TItem : class, IInventoryItem;

    public sealed class DelegateInventoryItemFactory<TItem> : IInventoryItemFactory<TItem>
        where TItem : class, IInventoryItem
    {
        private readonly InventoryItemFactory<TItem> _factory;

        public DelegateInventoryItemFactory(InventoryItemFactory<TItem> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public TItem Create(TItem template, string instanceId)
        {
            return _factory(template, instanceId);
        }
    }

    public interface IInfiniteItemStorage<TItem, TContext> : IUnorderedItemStorage<TItem>
        where TItem : class, IInventoryItem
    {
        IReadOnlyList<IInventoryFillRule<TItem, TContext>> FillRules { get; }
        InventoryFillMode FillMode { get; set; }
        IInventoryItemFactory<TItem> ItemFactory { get; }

        void SetItemFactory(IInventoryItemFactory<TItem> itemFactory);
        void AddFillRule(IInventoryFillRule<TItem, TContext> rule);
        bool RemoveFillRule(IInventoryFillRule<TItem, TContext> rule);
        void ClearFillRules();

        InventoryOperationResult<TItem> RefreshCatalog(
            IEnumerable<TItem> candidates,
            TContext context,
            InventoryActionOptions options = null);

        InventoryMaterializationResult<TItem> TryMaterialize(
            string templateItemId,
            InventoryActionOptions options = null);
    }

    public sealed class InventoryMaterializationResult<TItem>
        where TItem : class, IInventoryItem
    {
        public InventoryMaterializationResult(
            TItem item,
            InventoryOperationResult<TItem> operation)
        {
            Item = item;
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        }

        public TItem Item { get; }
        public InventoryOperationResult<TItem> Operation { get; }
        public bool Succeeded => Operation.Succeeded;
    }
}
