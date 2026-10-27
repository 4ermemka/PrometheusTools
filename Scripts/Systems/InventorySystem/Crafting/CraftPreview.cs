#nullable disable

using System;
using System.Collections.Generic;
using Assets.Shared.Systems.InventorySystem.Items;

namespace Assets.Shared.Systems.InventorySystem.Crafting
{
    /// <summary>
    /// Показанный пользователю вариант, ещё не хранящийся ни в одном инвентаре.
    /// Вместе с Result содержит токен владельца/поколения и план расхода. Передавайте весь preview в TryClaim;
    /// прямой TryAdd(Result) обойдёт расход ресурсов и потому не является подтверждением крафта.
    /// </summary>
    public sealed class CraftPreview<TItem> where TItem : class, ICraftingItem<TItem>
    {
        internal CraftPreview(object owner, long generation, long inputRevision, string recipeId, string name,
            TItem result, int[] consumed)
        {
            Owner = owner; Generation = generation; InputRevision = inputRevision;
            RecipeId = recipeId; Name = name; Result = result;
            Consumptions = Array.AsReadOnly((int[])consumed.Clone());
        }
        public string RecipeId { get; }
        public string Name { get; }
        public TItem Result { get; }
        internal object Owner { get; }
        // Новое поколение после смены входов, выхода, выбора или контекста делает старый drag недействительным.
        internal long Generation { get; }
        internal long InputRevision { get; }
        internal IReadOnlyList<int> Consumptions { get; }
    }
}
