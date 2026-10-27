#nullable disable

using System;
using System.Collections.Generic;
using Assets.Shared.Systems.InventorySystem.Items;

namespace Assets.Shared.Systems.InventorySystem.Crafting
{
    /// <summary>Распределяет доступные количества между требованиями рецепта, не изменяя входные предметы.</summary>
    internal static class RecipeMatcher<TItem> where TItem : class, ICraftingItem<TItem>
    {
        // Задача потока: источник -> требования рецепта -> совместимые стопки -> сток.
        // Обратные рёбра позволяют пересмотреть выбор: «любая древесина» не должна занять
        // единственную берёзу, если следующий ингредиент требует именно берёзу. Количества обрабатываются пачками.
        internal static bool TryMatch(IReadOnlyList<TItem> slots, IReadOnlyList<RecipeIngredient<TItem>> ingredients, out int[] consumed)
        {
            int slotStart = 1 + ingredients.Count, sink = slotStart + slots.Count, count = sink + 1;
            var residual = new int[count, count];
            long needed = 0;
            for (int i = 0; i < ingredients.Count; i++)
            {
                var ingredient = ingredients[i]; residual[0, i + 1] = ingredient.Quantity; needed += ingredient.Quantity;
                for (int j = 0; j < slots.Count; j++)
                {
                    var item = slots[j];
                    if (item != null && item.Quantity > 0 && (!ingredient.SlotIndex.HasValue || ingredient.SlotIndex.Value == j) && ingredient.Accepts(item))
                        residual[i + 1, slotStart + j] = Math.Min(ingredient.Quantity, item.Quantity);
                }
            }
            for (int j = 0; j < slots.Count; j++) residual[slotStart + j, sink] = Math.Max(0, slots[j]?.Quantity ?? 0);
            long matched = 0;
            while (matched < needed)
            {
                var parent = new int[count]; for (int i = 0; i < count; i++) parent[i] = -1;
                parent[0] = 0; var queue = new Queue<int>(); queue.Enqueue(0);
                while (queue.Count > 0 && parent[sink] < 0)
                {
                    int from = queue.Dequeue();
                    for (int to = 1; to < count; to++)
                        if (parent[to] < 0 && residual[from, to] > 0) { parent[to] = from; queue.Enqueue(to); }
                }
                if (parent[sink] < 0) { consumed = null; return false; }
                int amount = int.MaxValue;
                for (int node = sink; node != 0; node = parent[node]) amount = Math.Min(amount, residual[parent[node], node]);
                for (int node = sink; node != 0; node = parent[node])
                { residual[parent[node], node] -= amount; residual[node, parent[node]] += amount; }
                matched += amount;
            }
            // Результат индексируется входными ячейками, а не ингредиентами: сколько списать из каждой стопки.
            consumed = new int[slots.Count];
            for (int j = 0; j < slots.Count; j++) consumed[j] = residual[sink, slotStart + j];
            return true;
        }
    }
}
