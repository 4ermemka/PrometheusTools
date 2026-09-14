#nullable disable

using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Items;
using System;
using System.Collections.Generic;

namespace Assets.Shared.Systems.InventorySystem.Core
{
    /// <summary>
    /// Общий контракт механики, которая рассматривается как inventory,
    /// но выполняет доменный процесс: craft, exchange, trade и т.п.
    /// TRequest полностью определяется конкретной игрой.
    /// </summary>
    public interface IInventoryProcess<TItem, in TRequest> : IInventory<TItem>
        where TItem : class, IInventoryItem
    {
        InventoryValidationResult CanExecute(TRequest request);

        InventoryOperationResult<TItem> TryExecute(
            TRequest request,
            InventoryActionOptions options = null);
    }

    public abstract class InventoryProcessBase<TItem, TRequest> :
        InventoryBase<TItem>,
        IInventoryProcess<TItem, TRequest>
        where TItem : class, IInventoryItem
    {
        protected InventoryProcessBase(string inventoryId = null, string name = null)
            : base(inventoryId, name)
        {
        }

        protected abstract InventoryActionKind ProcessKind { get; }
        protected virtual string ProcessName => ProcessKind.ToString();

        public InventoryValidationResult CanExecute(TRequest request)
        {
            var action = BuildProcessAction(
                request,
                new InventoryActionOptions { Origin = InventoryActionOrigin.Local });
            return EvaluateAction(action, () => ValidateRequest(request, action));
        }

        public InventoryOperationResult<TItem> TryExecute(
            TRequest request,
            InventoryActionOptions options = null)
        {
            var action = BuildProcessAction(request, options);
            return TryExecuteAction(
                action,
                () => ExecuteRequest(request, action),
                () => ValidateRequest(request, action));
        }

        protected virtual InventoryAction<TItem> BuildProcessAction(
            TRequest request,
            InventoryActionOptions options)
        {
            return CreateAction(
                ProcessKind,
                this,
                this,
                GetAffectedItems(request) ?? Array.Empty<TItem>(),
                options,
                ProcessName,
                GetMetadata(request));
        }

        protected virtual IReadOnlyDictionary<string, string> GetMetadata(TRequest request)
        {
            return null;
        }

        protected abstract IReadOnlyList<TItem> GetAffectedItems(TRequest request);

        protected abstract InventoryValidationResult ValidateRequest(
            TRequest request,
            InventoryAction<TItem> action);

        /// <summary>
        /// Реализация отвечает за конкретную атомарность нескольких хранилищ.
        /// Все проверки должны быть выполнены в ValidateRequest до mutation.
        /// </summary>
        protected abstract void ExecuteRequest(
            TRequest request,
            InventoryAction<TItem> action);
    }
}
