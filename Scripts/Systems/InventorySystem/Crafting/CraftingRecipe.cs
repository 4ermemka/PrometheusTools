#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Items;

namespace Assets.Shared.Systems.InventorySystem.Crafting
{
    /// <summary>
    /// Описание одного рецепта: стабильный Id, состав, условие доступности и фабрика результата.
    /// Сам рецепт ничего не расходует. Один рецепт производит одну стопку результата.
    /// </summary>
    public sealed class CraftingRecipe<TItem, TContext> where TItem : class, ICraftingItem<TItem>
    {
        public CraftingRecipe(string id, string name, IEnumerable<RecipeIngredient<TItem>> ingredients,
            Func<IReadOnlyList<TItem>, TContext, TItem> createResult,
            Func<TContext, InventoryValidationResult> condition = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Recipe identity is required.", nameof(id));
            var list = (ingredients ?? throw new ArgumentNullException(nameof(ingredients))).ToArray();
            if (list.Any(x => x == null)) throw new ArgumentException("Ingredients cannot contain null.", nameof(ingredients));
            Id = id; Name = name ?? id; Ingredients = Array.AsReadOnly(list);
            CreateResult = createResult ?? throw new ArgumentNullException(nameof(createResult));
            Condition = condition;
        }
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<RecipeIngredient<TItem>> Ingredients { get; }
        /// <summary>
        /// Вызывается при построении preview. Получает снимок ВСЕХ входных ячеек в порядке индексов, включая null.
        /// Должна создать отдельный предмет с новым ItemId, без списания, наград и иных побочных действий.
        /// </summary>
        public Func<IReadOnlyList<TItem>, TContext, TItem> CreateResult { get; }
        /// <summary>Проверяет внешние требования (навык, рецепт и т.п.). null разрешает рецепт без условий.</summary>
        public Func<TContext, InventoryValidationResult> Condition { get; }
    }
}
