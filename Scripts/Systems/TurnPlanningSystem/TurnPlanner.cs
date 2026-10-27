using System;
using System.Collections.Generic;
using System.Linq;

namespace Shared.TurnPlanning
{
    /// <summary>Чистое синхронное ядро: один вызов принимает одну команду и возвращает новую Data.</summary>
    public sealed class TurnPlanner
    {
        private readonly IPlanAccessPolicy access;
        private readonly IPlanColorSource colors;
        public ActionRegistry Actions { get; }

        public TurnPlanner(ActionRegistry actions, IPlanAccessPolicy access, IPlanColorSource colors = null)
        {
            Actions = actions ?? throw new ArgumentNullException(nameof(actions));
            this.access = access ?? throw new ArgumentNullException(nameof(access));
            this.colors = colors ?? new PlanColorSource();
        }

        public bool TryApply(PlanningState source, IPlanningWorld world, Guid clientId, PlanCommand command,
            out PlanningState result, out string error)
        {
            result = source;
            error = null;
            if (command == null) error = "Команда отсутствует.";
            else if (!source.Participants.TryGetValue(clientId, out var participant) || !participant.Connected)
                error = "Участник не подключён.";
            else if (command.Turn != source.Turn) error = "Ход уже изменился.";
            else if (command.Kind == PlanCommandKind.Confirm)
            {
                if (command.Revision != source.Revision) error = "План изменился. Проверьте его и подтвердите ещё раз.";
                else
                {
                    result = source.Copy();
                    result.Participants[clientId].Confirmed = true;
                    return true;
                }
            }
            else if (string.IsNullOrWhiteSpace(command.ObjectId) || !world.Contains(command.ObjectId))
                error = "Объект отсутствует.";
            else if (!access.CanEdit(participant, world, command.ObjectId)) error = "Нет права управлять объектом.";
            else
            {
                source.Plans.TryGetValue(command.ObjectId, out var oldPlan);
                if (command.ExpectedTail != oldPlan?.Last?.Value.Id) error = "План объекта уже изменился. Повторите действие.";
                else
                {
                    var copy = source.Copy();
                    if (!copy.Plans.TryGetValue(command.ObjectId, out var plan))
                        copy.Plans.Add(command.ObjectId, plan = new LinkedList<PlannedAction>());
                    if (command.Kind == PlanCommandKind.Append)
                    {
                        if (!Actions.TryGet(command.ActionId, out var action)) error = "Неизвестное действие.";
                        else if (command.Arguments == null) error = "Параметры действия отсутствуют.";
                        else
                        {
                            var step = new PlannedAction
                            {
                                Id = Guid.NewGuid().ToString("N"), ObjectId = command.ObjectId, ActionId = command.ActionId,
                                ClientId = clientId, Sequence = ++copy.NextSequence,
                                ClientSequence = ++copy.Participants[clientId].NextSequence,
                                ColorIndex = colors.Next(new HashSet<int>(plan.Select(s => s.ColorIndex))),
                                Arguments = new Dictionary<string, string>(command.Arguments)
                            };
                            error = action.Validate(world, step);
                            if (error == null) plan.AddLast(step);
                        }
                    }
                    else if (command.Kind == PlanCommandKind.Truncate)
                    {
                        var node = plan.First;
                        while (node != null && node.Value.Id != command.StepId) node = node.Next;
                        if (node == null) error = "Шаг уже удалён.";
                        else while (node != null)
                        {
                            var next = node.Next;
                            plan.Remove(node);
                            node = next;
                        }
                    }
                    else error = "Неизвестная команда планирования.";
                    if (error == null)
                    {
                        ResetConfirmations(copy);
                        copy.Revision++;
                        result = copy;
                        return true;
                    }
                }
            }
            return false;
        }

        public PlanningState SetConnected(PlanningState source, Guid id, bool connected, bool isGameMaster)
        {
            var result = source.Copy();
            if (!result.Participants.TryGetValue(id, out var participant))
                result.Participants.Add(id, participant = new Participant { ClientId = id });
            participant.Connected = connected;
            participant.IsGameMaster = isGameMaster;
            participant.Confirmed = false;
            // Присоединившийся должен увидеть и подтвердить актуальный план; остальные подтверждения сохраняются.
            result.Revision++;
            return result;
        }

        public bool CanExecute(PlanningState state)
            => state.Participants.Values.Any(p => p.Connected) && state.Participants.Values.Where(p => p.Connected).All(p => p.Confirmed);

        public IEnumerable<PlannedAction> OrderedActions(PlanningState state)
            => state.Plans.Values.SelectMany(p => p).OrderBy(s => s.Sequence);

        public PlanningState NextTurn(PlanningState source)
        {
            var result = source.Copy();
            result.Turn++;
            result.Revision++;
            result.Plans.Clear();
            ResetConfirmations(result);
            return result;
        }

        public static void ResetConfirmations(PlanningState state)
        {
            foreach (var participant in state.Participants.Values) participant.Confirmed = false;
        }
    }
}
