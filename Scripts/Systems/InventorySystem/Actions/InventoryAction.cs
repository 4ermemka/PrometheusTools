#nullable disable

using Assets.Shared.Systems.InventorySystem.Core;
using Assets.Shared.Systems.InventorySystem.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Assets.Shared.Systems.InventorySystem.Actions
{
    /// <summary>
    /// Стандартные виды действий. Custom оставляет системе открытый extension point.
    /// </summary>
    public enum InventoryActionKind
    {
        Add = 0,
        Remove = 1,
        Clear = 2,
        Update = 3,
        TransferOut = 4,
        TransferIn = 5,
        Refresh = 6,
        Materialize = 7,
        Craft = 8,
        Exchange = 9,
        Trade = 10,
        Custom = 1000
    }

    /// <summary>
    /// Источник действия важен для реакции UI и предотвращения сетевого echo.
    /// </summary>
    public enum InventoryActionOrigin
    {
        Local = 0,
        Remote = 1,
        System = 2
    }

    /// <summary>
    /// Необязательные параметры стандартной операции.
    /// Metadata предназначена для простых контекстных значений, а не для
    /// хранения доменного состояния.
    /// </summary>
    public sealed class InventoryActionOptions
    {
        public string OperationId { get; set; }
        public InventoryActionOrigin Origin { get; set; } = InventoryActionOrigin.Local;
        public string Reason { get; set; }
        public IReadOnlyDictionary<string, string> Metadata { get; set; }

        internal string ResolveOperationId()
        {
            return string.IsNullOrWhiteSpace(OperationId)
                ? Guid.NewGuid().ToString("N")
                : OperationId;
        }

        internal InventoryActionOptions WithOperationId(string operationId)
        {
            return new InventoryActionOptions
            {
                OperationId = operationId,
                Origin = Origin,
                Reason = Reason,
                Metadata = Metadata
            };
        }
    }

    /// <summary>
    /// Неизменяемое описание одной части inventory-операции.
    /// Transfer порождает две части с общим OperationId: TransferOut и TransferIn.
    /// </summary>
    public sealed class InventoryAction<TItem>
        where TItem : class, IInventoryItem
    {
        internal InventoryAction(
            string operationId,
            InventoryActionKind kind,
            string actionName,
            InventoryActionOrigin origin,
            IInventory<TItem> source,
            IInventory<TItem> target,
            IEnumerable<TItem> items,
            string reason,
            IReadOnlyDictionary<string, string> metadata)
        {
            OperationId = operationId ?? throw new ArgumentNullException(nameof(operationId));
            Kind = kind;
            ActionName = string.IsNullOrWhiteSpace(actionName) ? kind.ToString() : actionName;
            Origin = origin;
            Source = source;
            Target = target;
            Items = Array.AsReadOnly((items ?? Enumerable.Empty<TItem>()).ToArray());
            Reason = reason;

            var metadataCopy = metadata == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(metadata, StringComparer.Ordinal);
            Metadata = new ReadOnlyDictionary<string, string>(metadataCopy);
        }

        public string OperationId { get; }
        public InventoryActionKind Kind { get; }
        public string ActionName { get; }
        public InventoryActionOrigin Origin { get; }
        public IInventory<TItem> Source { get; }
        public IInventory<TItem> Target { get; }
        public IReadOnlyList<TItem> Items { get; }
        public string Reason { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }

        public string SourceInventoryId => Source?.InventoryId;
        public string TargetInventoryId => Target?.InventoryId;
    }

    public sealed class InventoryActionEventArgs<TItem> : EventArgs
        where TItem : class, IInventoryItem
    {
        public InventoryActionEventArgs(InventoryAction<TItem> action)
        {
            Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        public InventoryAction<TItem> Action { get; }
    }

    public sealed class InventoryActionRejectedEventArgs<TItem> : EventArgs
        where TItem : class, IInventoryItem
    {
        public InventoryActionRejectedEventArgs(
            InventoryAction<TItem> action,
            InventoryValidationResult validation)
        {
            Action = action ?? throw new ArgumentNullException(nameof(action));
            Validation = validation ?? throw new ArgumentNullException(nameof(validation));
        }

        public InventoryAction<TItem> Action { get; }
        public InventoryValidationResult Validation { get; }
    }

    /// <summary>
    /// Ошибка пользовательского observer-а не должна отменять уже выполненную
    /// mutation. Это событие позволяет диагностировать такие ошибки отдельно.
    /// </summary>
    public sealed class InventoryObserverFaultedEventArgs<TItem> : EventArgs
        where TItem : class, IInventoryItem
    {
        public InventoryObserverFaultedEventArgs(
            string stage,
            InventoryAction<TItem> action,
            Exception exception)
        {
            Stage = stage;
            Action = action;
            Exception = exception;
        }

        public string Stage { get; }
        public InventoryAction<TItem> Action { get; }
        public Exception Exception { get; }
    }
}
