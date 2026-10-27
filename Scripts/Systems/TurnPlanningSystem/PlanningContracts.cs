using System;
using System.Collections.Generic;

namespace Shared.TurnPlanning
{
    public interface IPlanningWorld
    {
        bool Contains(string objectId);
        Guid OwnerOf(string objectId);
        string GetParameter(string objectId, string key);
        void SetParameter(string objectId, string key, string value);
    }

    public interface IPlanAccessPolicy
    {
        bool CanEdit(Participant participant, IPlanningWorld world, string objectId);
    }

    public sealed class OwnerAccessPolicy : IPlanAccessPolicy
    {
        public bool CanEdit(Participant participant, IPlanningWorld world, string objectId)
            => participant.IsGameMaster || world.OwnerOf(objectId) == participant.ClientId;
    }

    /// <summary>Validate не меняет мир. Execute работает на транзакционной копии мира вызывающей системы.</summary>
    public interface IPlanAction
    {
        string Id { get; }
        string Validate(IPlanningWorld world, PlannedAction step);
        void Execute(IPlanningWorld world, PlannedAction step);
    }

    /// <summary>Позволяет зарегистрировать в runtime функции, включая адаптер существующего IInteraction.Execute.</summary>
    public sealed class DelegatePlanAction : IPlanAction
    {
        private readonly Func<IPlanningWorld, PlannedAction, string> validate;
        private readonly Action<IPlanningWorld, PlannedAction> execute;
        public string Id { get; }
        public DelegatePlanAction(string id, Func<IPlanningWorld, PlannedAction, string> validate,
            Action<IPlanningWorld, PlannedAction> execute)
        {
            Id = id;
            this.validate = validate ?? throw new ArgumentNullException(nameof(validate));
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }
        public string Validate(IPlanningWorld world, PlannedAction step) => validate(world, step);
        public void Execute(IPlanningWorld world, PlannedAction step) => execute(world, step);
    }

    public sealed class ActionRegistry
    {
        private readonly Dictionary<string, IPlanAction> actions = new(StringComparer.Ordinal);
        public void Register(IPlanAction action)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.Id)) throw new ArgumentException("Action ID is required.");
            actions.Add(action.Id, action); // Повторная регистрация — ошибка конфигурации.
        }
        public bool TryGet(string id, out IPlanAction action)
        {
            action = null;
            return id != null && actions.TryGetValue(id, out action);
        }
        public IPlanAction Get(string id) => actions[id];
    }

    public interface IPlanColorSource { int Next(ISet<int> used); }

    public sealed class PlanColorSource : IPlanColorSource
    {
        private readonly Random random = new();
        private readonly int paletteSize;
        public PlanColorSource(int paletteSize = 12)
        {
            if (paletteSize < 1) throw new ArgumentOutOfRangeException(nameof(paletteSize));
            this.paletteSize = paletteSize;
        }
        public int Next(ISet<int> used)
        {
            var available = new List<int>();
            for (int i = 0; i < paletteSize; i++) if (!used.Contains(i)) available.Add(i);
            if (available.Count > 0) return available[random.Next(available.Count)];
            int extra = paletteSize;
            while (used.Contains(extra)) extra++;
            return extra;
        }
    }
}
