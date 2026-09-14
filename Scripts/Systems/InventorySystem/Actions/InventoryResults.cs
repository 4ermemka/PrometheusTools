#nullable disable

using Assets.Shared.Systems.InventorySystem.Items;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Assets.Shared.Systems.InventorySystem.Actions
{
    /// <summary>
    /// Результат проверки. Code предназначен для логики/UI, Message — для
    /// диагностики и отображения.
    /// </summary>
    public sealed class InventoryValidationResult
    {
        private InventoryValidationResult(bool isAllowed, string code, string message)
        {
            IsAllowed = isAllowed;
            Code = code;
            Message = message;
        }

        public bool IsAllowed { get; }
        public string Code { get; }
        public string Message { get; }

        public static InventoryValidationResult Allow()
        {
            return new InventoryValidationResult(true, null, null);
        }

        public static InventoryValidationResult Deny(string code, string message)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Rejection code is required.", nameof(code));

            return new InventoryValidationResult(false, code, message);
        }
    }

    /// <summary>
    /// Общий результат как одиночного действия, так и составной операции.
    /// </summary>
    public sealed class InventoryOperationResult<TItem>
        where TItem : class, IInventoryItem
    {
        private InventoryOperationResult(
            bool succeeded,
            string operationId,
            IEnumerable<InventoryAction<TItem>> actions,
            InventoryValidationResult validation,
            Exception exception)
        {
            Succeeded = succeeded;
            OperationId = operationId;
            Actions = Array.AsReadOnly((actions ?? Enumerable.Empty<InventoryAction<TItem>>()).ToArray());
            Validation = validation;
            Exception = exception;
        }

        public bool Succeeded { get; }
        public string OperationId { get; }
        public IReadOnlyList<InventoryAction<TItem>> Actions { get; }
        public InventoryValidationResult Validation { get; }
        public Exception Exception { get; }

        public IReadOnlyList<TItem> AffectedItems =>
            Array.AsReadOnly(Actions.SelectMany(action => action.Items).Distinct().ToArray());

        public static InventoryOperationResult<TItem> Completed(
            string operationId,
            params InventoryAction<TItem>[] actions)
        {
            return new InventoryOperationResult<TItem>(
                true,
                operationId,
                actions,
                InventoryValidationResult.Allow(),
                null);
        }

        public static InventoryOperationResult<TItem> Rejected(
            string operationId,
            InventoryValidationResult validation,
            Exception exception,
            params InventoryAction<TItem>[] actions)
        {
            if (validation == null)
                throw new ArgumentNullException(nameof(validation));

            return new InventoryOperationResult<TItem>(
                false,
                operationId,
                actions,
                validation,
                exception);
        }
    }

    public interface IInventoryActionRule<TItem>
        where TItem : class, IInventoryItem
    {
        InventoryValidationResult Evaluate(InventoryAction<TItem> action);
    }

    public delegate InventoryValidationResult InventoryActionRule<TItem>(
        InventoryAction<TItem> action)
        where TItem : class, IInventoryItem;

    /// <summary>
    /// Позволяет подключить правило одной функцией без отдельного класса.
    /// </summary>
    public sealed class DelegateInventoryActionRule<TItem> : IInventoryActionRule<TItem>
        where TItem : class, IInventoryItem
    {
        private readonly InventoryActionRule<TItem> _rule;

        public DelegateInventoryActionRule(InventoryActionRule<TItem> rule)
        {
            _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        }

        public InventoryValidationResult Evaluate(InventoryAction<TItem> action)
        {
            return _rule(action) ?? InventoryValidationResult.Deny(
                "rule_returned_null",
                $"Rule {_rule.Method.Name} returned null.");
        }
    }
}
