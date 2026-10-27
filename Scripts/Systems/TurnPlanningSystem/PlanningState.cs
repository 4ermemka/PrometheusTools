using System;
using System.Collections.Generic;

namespace Shared.TurnPlanning
{
    // DTO содержат только данные. После публикации через Sync их нельзя менять на месте.
    public sealed class PlannedAction
    {
        public string Id { get; set; }
        public string ObjectId { get; set; }
        public string ActionId { get; set; }
        public Guid ClientId { get; set; }
        public long Sequence { get; set; }
        public long ClientSequence { get; set; }
        public int ColorIndex { get; set; }
        public Dictionary<string, string> Arguments { get; set; } = new();

        public PlannedAction Copy() => new()
        {
            Id = Id, ObjectId = ObjectId, ActionId = ActionId, ClientId = ClientId,
            Sequence = Sequence, ClientSequence = ClientSequence, ColorIndex = ColorIndex,
            Arguments = new Dictionary<string, string>(Arguments)
        };
    }

    public sealed class Participant
    {
        public Guid ClientId { get; set; }
        public bool IsGameMaster { get; set; }
        public bool Connected { get; set; }
        public bool Confirmed { get; set; }
        public long NextSequence { get; set; }
        public Participant Copy() => (Participant)MemberwiseClone();
    }

    public sealed class PlanningState
    {
        public int Turn { get; set; } = 1;
        // Revision относится к содержимому планов. Одновременные подтверждения не конфликтуют.
        public long Revision { get; set; }
        public long NextSequence { get; set; }
        public Dictionary<Guid, Participant> Participants { get; set; } = new();
        public Dictionary<string, LinkedList<PlannedAction>> Plans { get; set; } = new(StringComparer.Ordinal);

        public PlanningState Copy()
        {
            var result = new PlanningState { Turn = Turn, Revision = Revision, NextSequence = NextSequence };
            foreach (var pair in Participants) result.Participants.Add(pair.Key, pair.Value.Copy());
            foreach (var pair in Plans)
            {
                var list = new LinkedList<PlannedAction>();
                foreach (var step in pair.Value) list.AddLast(step.Copy());
                result.Plans.Add(pair.Key, list);
            }
            return result;
        }
    }

    public enum PlanCommandKind { Append, Truncate, Confirm }

    public sealed class PlanCommand
    {
        public PlanCommandKind Kind { get; set; }
        public int Turn { get; set; }
        public long Revision { get; set; }
        public string ObjectId { get; set; }
        public string ActionId { get; set; }
        public string StepId { get; set; }
        public string ExpectedTail { get; set; }
        public Dictionary<string, string> Arguments { get; set; } = new();
    }
}
