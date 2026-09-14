#nullable disable

using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Items;
using System;
using System.Collections.Generic;

namespace Assets.Shared.Systems.InventorySystem.Storage
{
    /// <summary>
    /// Изолирует lifecycle factory и единые invariants созданного экземпляра.
    /// Factory запрашивается через accessor, чтобы её можно было заменить
    /// после deserialization через SetItemFactory.
    /// </summary>
    internal sealed class InfiniteItemMaterializer<TItem>
        where TItem : class, IInventoryItem
    {
        private readonly Func<IInventoryItemFactory<TItem>> _factoryAccessor;

        public InfiniteItemMaterializer(
            Func<IInventoryItemFactory<TItem>> factoryAccessor)
        {
            _factoryAccessor = factoryAccessor ??
                throw new ArgumentNullException(nameof(factoryAccessor));
        }

        public bool TryCreate(
            TItem template,
            out TItem instance,
            out InventoryValidationResult validation,
            out Exception exception)
        {
            instance = null;
            exception = null;

            var factory = _factoryAccessor();
            if (factory == null)
            {
                validation = InventoryValidationResult.Deny(
                    "item_factory_not_configured",
                    "Infinite inventory has no item factory.");
                return false;
            }

            try
            {
                var instanceId = Guid.NewGuid().ToString("N");
                instance = factory.Create(template, instanceId);
                validation = Validate(template, instance, instanceId);
                return validation.IsAllowed;
            }
            catch (Exception creationException)
            {
                exception = creationException;
                validation = InventoryValidationResult.Deny(
                    "item_factory_exception",
                    $"{creationException.GetType().Name}: {creationException.Message}");
                return false;
            }
        }

        public InventoryValidationResult ValidateMany(
            IReadOnlyList<TItem> templates,
            IReadOnlyList<TItem> instances)
        {
            if (templates.Count != instances.Count)
            {
                return InventoryValidationResult.Deny(
                    "factory_result_count_mismatch",
                    "Item factory did not create one instance for every template.");
            }

            var instanceIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < instances.Count; index++)
            {
                var result = Validate(templates[index], instances[index]);
                if (!result.IsAllowed)
                    return result;
                if (!instanceIds.Add(instances[index].ItemId))
                {
                    return InventoryValidationResult.Deny(
                        "duplicate_materialized_item_id",
                        $"Factory created duplicate ItemId '{instances[index].ItemId}'.");
                }
            }

            return InventoryValidationResult.Allow();
        }

        public InventoryValidationResult Validate(
            TItem template,
            TItem instance,
            string expectedInstanceId = null)
        {
            if (instance == null)
                return InventoryValidationResult.Deny("factory_returned_null", "Item factory returned null.");
            if (string.IsNullOrWhiteSpace(instance.ItemId))
                return InventoryValidationResult.Deny(
                    "invalid_materialized_item_id",
                    "Materialized item must have a non-empty ItemId.");
            if (string.IsNullOrWhiteSpace(instance.Name))
                return InventoryValidationResult.Deny(
                    "invalid_materialized_item_name",
                    "Materialized item must have a Name.");
            if (expectedInstanceId != null &&
                !StringComparer.Ordinal.Equals(expectedInstanceId, instance.ItemId))
            {
                return InventoryValidationResult.Deny(
                    "factory_ignored_instance_id",
                    "Item factory must assign the provided unique instanceId.");
            }
            if (template != null &&
                StringComparer.Ordinal.Equals(template.ItemId, instance.ItemId))
            {
                return InventoryValidationResult.Deny(
                    "factory_reused_template_id",
                    "Materialized item must not reuse template ItemId.");
            }

            return InventoryValidationResult.Allow();
        }
    }
}
