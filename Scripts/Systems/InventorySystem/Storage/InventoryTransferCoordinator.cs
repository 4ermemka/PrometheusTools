#nullable disable

using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Items;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Assets.Shared.Systems.InventorySystem.Storage
{
    /// <summary>
    /// Координирует двухстороннюю transfer-операцию. Обе стороны проходят
    /// validation до первого изменения. Если mutation неожиданно падает,
    /// coordinator пытается вернуть оба хранилища в исходное состояние;
    /// compensating mutations также образуют сетевые patch.
    /// </summary>
    internal static class InventoryTransferCoordinator<TItem>
        where TItem : class, IInventoryItem
    {
        public static InventoryOperationResult<TItem> TryTransfer(
            ItemStorageBase<TItem> source,
            IItemStorage<TItem> target,
            IReadOnlyList<TItem> items,
            InventoryValidationResult preparationResult,
            InventoryActionOptions options)
        {
            var normalizedOptions = source.NormalizeOperationOptions(options);

            if (target is not ItemStorageBase<TItem> targetStorage)
            {
                return RejectUnsupportedTarget(
                    source,
                    target,
                    items,
                    normalizedOptions);
            }

            if (ReferenceEquals(source, targetStorage))
            {
                return RejectSameStorage(source, targetStorage, items, normalizedOptions);
            }

            var sourceAction = source.CreateAction(
                InventoryActionKind.TransferOut,
                source,
                targetStorage,
                items,
                normalizedOptions);
            var targetAction = targetStorage.CreateAction(
                InventoryActionKind.TransferIn,
                source,
                targetStorage,
                items,
                normalizedOptions);

            var sourceValidation = source.EvaluateAction(
                sourceAction,
                () => preparationResult.IsAllowed
                    ? source.ValidateRemoval(sourceAction)
                    : preparationResult);
            var targetValidation = targetStorage.EvaluateAction(
                targetAction,
                () => preparationResult.IsAllowed
                    ? targetStorage.ValidateAddition(targetAction, replacingAll: false)
                    : preparationResult);

            var rejection = !sourceValidation.IsAllowed ? sourceValidation : targetValidation;
            if (!rejection.IsAllowed)
            {
                source.PublishRejected(sourceAction, rejection);
                targetStorage.PublishRejected(targetAction, rejection);
                return InventoryOperationResult<TItem>.Rejected(
                    sourceAction.OperationId,
                    rejection,
                    null,
                    sourceAction,
                    targetAction);
            }

            source.PublishExecuting(sourceAction);
            targetStorage.PublishExecuting(targetAction);

            try
            {
                source.RemoveItemsCore(items);
                targetStorage.AddItemsCore(items);
            }
            catch (Exception exception)
            {
                var rollbackErrors = Rollback(source, targetStorage, items);
                var message = $"{exception.GetType().Name}: {exception.Message}";
                if (rollbackErrors.Count > 0)
                    message += $" Rollback errors: {string.Join(" | ", rollbackErrors)}";

                var failure = InventoryValidationResult.Deny(
                    "transfer_mutation_exception",
                    message);
                source.PublishRejected(sourceAction, failure);
                targetStorage.PublishRejected(targetAction, failure);
                return InventoryOperationResult<TItem>.Rejected(
                    sourceAction.OperationId,
                    failure,
                    exception,
                    sourceAction,
                    targetAction);
            }

            source.PublishExecuted(sourceAction);
            targetStorage.PublishExecuted(targetAction);
            return InventoryOperationResult<TItem>.Completed(
                sourceAction.OperationId,
                sourceAction,
                targetAction);
        }

        private static IReadOnlyList<string> Rollback(
            ItemStorageBase<TItem> source,
            ItemStorageBase<TItem> target,
            IReadOnlyList<TItem> items)
        {
            var errors = new List<string>();

            try
            {
                var itemsAddedToTarget = items
                    .Where(item => target.Contains(item.ItemId))
                    .ToArray();
                target.RemoveItemsCore(itemsAddedToTarget);
            }
            catch (Exception exception)
            {
                errors.Add($"target: {exception.GetType().Name}: {exception.Message}");
            }

            try
            {
                var itemsMissingFromSource = items
                    .Where(item => !source.Contains(item.ItemId))
                    .ToArray();
                source.AddItemsCore(itemsMissingFromSource);
            }
            catch (Exception exception)
            {
                errors.Add($"source: {exception.GetType().Name}: {exception.Message}");
            }

            return errors;
        }

        private static InventoryOperationResult<TItem> RejectUnsupportedTarget(
            ItemStorageBase<TItem> source,
            IItemStorage<TItem> target,
            IReadOnlyList<TItem> items,
            InventoryActionOptions options)
        {
            var action = source.CreateAction(
                InventoryActionKind.TransferOut,
                source,
                target,
                items,
                options);
            var validation = InventoryValidationResult.Deny(
                "unsupported_storage_implementation",
                "Atomic transfer requires both storages to inherit ItemStorageBase<TItem>.");
            source.PublishRejected(action, validation);
            return InventoryOperationResult<TItem>.Rejected(
                action.OperationId,
                validation,
                null,
                action);
        }

        private static InventoryOperationResult<TItem> RejectSameStorage(
            ItemStorageBase<TItem> source,
            ItemStorageBase<TItem> target,
            IReadOnlyList<TItem> items,
            InventoryActionOptions options)
        {
            var action = source.CreateAction(
                InventoryActionKind.TransferOut,
                source,
                target,
                items,
                options);
            var validation = InventoryValidationResult.Deny(
                "same_storage",
                "Unordered storage has no meaningful internal move operation.");
            source.PublishRejected(action, validation);
            return InventoryOperationResult<TItem>.Rejected(
                action.OperationId,
                validation,
                null,
                action);
        }
    }
}
