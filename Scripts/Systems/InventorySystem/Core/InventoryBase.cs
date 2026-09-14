#nullable disable

using Assets.Shared.SyncSystem.Core;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Assets.Shared.Systems.InventorySystem.Core
{
    /// <summary>
    /// Общий lifecycle любой inventory-механики:
    /// validation rules -> ActionExecuting -> mutation -> ActionExecuted.
    /// </summary>
    public abstract class InventoryBase<TItem> : TrackableNode, IInventory<TItem>
        where TItem : class, IInventoryItem
    {
        // Имена этих полей являются частью path-based snapshot/patch контракта.
        protected readonly Sync<string> InventoryIdState = new();
        protected readonly Sync<string> NameState = new();

        private readonly List<IInventoryActionRule<TItem>> _rules = new();
        private readonly ReadOnlyCollection<IInventoryActionRule<TItem>> _readOnlyRules;

        protected InventoryBase(string inventoryId = null, string name = null)
        {
            _readOnlyRules = _rules.AsReadOnly();

            InventoryIdState.SetValueSilent(
                string.IsNullOrWhiteSpace(inventoryId)
                    ? Guid.NewGuid().ToString("N")
                    : inventoryId);
            NameState.SetValueSilent(
                string.IsNullOrWhiteSpace(name)
                    ? GetType().Name
                    : name);
        }

        public event EventHandler<InventoryActionEventArgs<TItem>> ActionExecuting;
        public event EventHandler<InventoryActionEventArgs<TItem>> ActionExecuted;
        public event EventHandler<InventoryActionRejectedEventArgs<TItem>> ActionRejected;
        public event EventHandler<InventoryObserverFaultedEventArgs<TItem>> ObserverFaulted;

        public string InventoryId => InventoryIdState.Value;
        public string Name => NameState.Value;
        public Type ItemType => typeof(TItem);
        public IReadOnlyList<IInventoryActionRule<TItem>> Rules => _readOnlyRules;

        public void AddRule(IInventoryActionRule<TItem> rule)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));
            if (!_rules.Contains(rule))
                _rules.Add(rule);
        }

        public bool RemoveRule(IInventoryActionRule<TItem> rule)
        {
            return rule != null && _rules.Remove(rule);
        }

        public void ClearRules()
        {
            _rules.Clear();
        }

        public InventoryValidationResult CanExecute(InventoryAction<TItem> action)
        {
            return EvaluateAction(action);
        }

        protected internal InventoryAction<TItem> CreateAction(
            InventoryActionKind kind,
            IInventory<TItem> source,
            IInventory<TItem> target,
            IEnumerable<TItem> items,
            InventoryActionOptions options = null,
            string actionName = null,
            IReadOnlyDictionary<string, string> additionalMetadata = null)
        {
            options ??= new InventoryActionOptions();
            var metadata = MergeMetadata(options.Metadata, additionalMetadata);

            return new InventoryAction<TItem>(
                options.ResolveOperationId(),
                kind,
                actionName,
                options.Origin,
                source,
                target,
                items,
                options.Reason,
                metadata);
        }

        protected internal InventoryValidationResult EvaluateAction(
            InventoryAction<TItem> action,
            Func<InventoryValidationResult> additionalValidation = null)
        {
            if (action == null)
                return InventoryValidationResult.Deny("action_is_null", "Inventory action is required.");

            if (!ReferenceEquals(action.Source, this) && !ReferenceEquals(action.Target, this))
            {
                return InventoryValidationResult.Deny(
                    "inventory_not_participant",
                    $"Inventory {InventoryId} is not a participant of operation {action.OperationId}.");
            }

            var coreResult = ValidateCore(action);
            if (coreResult == null)
            {
                return InventoryValidationResult.Deny(
                    "core_validation_returned_null",
                    $"{GetType().Name}.ValidateCore returned null.");
            }
            if (!coreResult.IsAllowed)
                return coreResult;

            if (additionalValidation != null)
            {
                InventoryValidationResult additionalResult;
                try
                {
                    additionalResult = additionalValidation();
                }
                catch (Exception exception)
                {
                    return InventoryValidationResult.Deny(
                        "validation_exception",
                        $"{exception.GetType().Name}: {exception.Message}");
                }

                if (additionalResult == null)
                {
                    return InventoryValidationResult.Deny(
                        "validation_returned_null",
                        "Additional inventory validation returned null.");
                }
                if (!additionalResult.IsAllowed)
                    return additionalResult;
            }

            foreach (var rule in _rules.ToArray())
            {
                InventoryValidationResult ruleResult;
                try
                {
                    ruleResult = rule.Evaluate(action);
                }
                catch (Exception exception)
                {
                    return InventoryValidationResult.Deny(
                        "rule_exception",
                        $"{rule.GetType().Name}: {exception.GetType().Name}: {exception.Message}");
                }

                if (ruleResult == null)
                {
                    return InventoryValidationResult.Deny(
                        "rule_returned_null",
                        $"Rule {rule.GetType().Name} returned null.");
                }
                if (!ruleResult.IsAllowed)
                    return ruleResult;
            }

            return InventoryValidationResult.Allow();
        }

        protected InventoryOperationResult<TItem> TryExecuteAction(
            InventoryAction<TItem> action,
            Action mutation,
            Func<InventoryValidationResult> additionalValidation = null)
        {
            if (action == null)
            {
                var validation = InventoryValidationResult.Deny(
                    "action_is_null",
                    "Inventory action is required.");
                return InventoryOperationResult<TItem>.Rejected(null, validation, null);
            }

            var validationResult = EvaluateAction(action, additionalValidation);
            if (!validationResult.IsAllowed)
            {
                PublishRejected(action, validationResult);
                return InventoryOperationResult<TItem>.Rejected(
                    action.OperationId,
                    validationResult,
                    null,
                    action);
            }

            PublishExecuting(action);

            try
            {
                mutation?.Invoke();
            }
            catch (Exception exception)
            {
                var mutationFailure = InventoryValidationResult.Deny(
                    "mutation_exception",
                    $"{exception.GetType().Name}: {exception.Message}");
                PublishRejected(action, mutationFailure);
                return InventoryOperationResult<TItem>.Rejected(
                    action.OperationId,
                    mutationFailure,
                    exception,
                    action);
            }

            PublishExecuted(action);
            return InventoryOperationResult<TItem>.Completed(action.OperationId, action);
        }

        protected virtual InventoryValidationResult ValidateCore(InventoryAction<TItem> action)
        {
            return InventoryValidationResult.Allow();
        }

        protected internal void PublishExecuting(InventoryAction<TItem> action)
        {
            InvokeActionHandlers(ActionExecuting, new InventoryActionEventArgs<TItem>(action), "executing", action);
        }

        protected internal void PublishExecuted(InventoryAction<TItem> action)
        {
            InvokeActionHandlers(ActionExecuted, new InventoryActionEventArgs<TItem>(action), "executed", action);
        }

        protected internal void PublishRejected(
            InventoryAction<TItem> action,
            InventoryValidationResult validation)
        {
            InvokeActionHandlers(
                ActionRejected,
                new InventoryActionRejectedEventArgs<TItem>(action, validation),
                "rejected",
                action);
        }

        private void InvokeActionHandlers<TEventArgs>(
            EventHandler<TEventArgs> handlers,
            TEventArgs eventArgs,
            string stage,
            InventoryAction<TItem> action)
            where TEventArgs : EventArgs
        {
            if (handlers == null)
                return;

            foreach (EventHandler<TEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, eventArgs);
                }
                catch (Exception exception)
                {
                    PublishObserverFault(stage, action, exception);
                }
            }
        }

        private void PublishObserverFault(
            string stage,
            InventoryAction<TItem> action,
            Exception exception)
        {
            if (ObserverFaulted == null)
                return;

            var eventArgs = new InventoryObserverFaultedEventArgs<TItem>(stage, action, exception);
            foreach (EventHandler<InventoryObserverFaultedEventArgs<TItem>> handler
                     in ObserverFaulted.GetInvocationList())
            {
                try
                {
                    handler(this, eventArgs);
                }
                catch
                {
                    // Observer ошибок не должен рекурсивно ломать inventory pipeline.
                }
            }
        }

        private static IReadOnlyDictionary<string, string> MergeMetadata(
            IReadOnlyDictionary<string, string> first,
            IReadOnlyDictionary<string, string> second)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (first != null)
            {
                foreach (var pair in first)
                    result[pair.Key] = pair.Value;
            }
            if (second != null)
            {
                foreach (var pair in second)
                    result[pair.Key] = pair.Value;
            }
            return result;
        }
    }
}
