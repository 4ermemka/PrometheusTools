using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shared.Inputs
{
    public interface IGameActionSource
    {
        GameInputAction GetAction(string id);
    }

    /// <summary>Стабильный объект события. Rebind не заменяет его и не теряет подписчиков.</summary>
    public sealed class GameInputAction
    {
        public string Id { get; }
        public ActionTrigger Trigger { get; }
        public event Action Performed;
        internal GameInputAction(GameActionDefinition definition) { Id = definition.Id; Trigger = definition.Trigger; }
        internal void Perform() => Performed?.Invoke();
        public IDisposable Subscribe(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            Performed += callback;
            return new InputSubscription(() => Performed -= callback);
        }
    }

    internal sealed class InputSubscription : IDisposable
    {
        private Action release;
        public InputSubscription(Action release) => this.release = release;
        public void Dispose() { var callback = release; release = null; callback?.Invoke(); }
    }

    /// <summary>
    /// Input → Binding → игровое действие. Hold выполняется каждый tick, включая первый.
    /// После переназначения/возврата из UI ждём отпускания controls: захват не играет действие.
    /// </summary>
    public sealed class GameActionRouter : IContextInputSource
    {
        private readonly List<GameInputAction> ordered = new();
        private readonly Dictionary<string, GameInputAction> actions = new(StringComparer.Ordinal);
        private int suspensionCount;
        private bool waitForNeutral;
        public BindingTable Bindings { get; }
        public bool IsSuspended => suspensionCount > 0;
        public InputContextChain Contexts { get; }

        public GameActionRouter(IEnumerable<GameActionDefinition> definitions)
        {
            Contexts = new InputContextChain(this);
            var list = new List<GameActionDefinition>(definitions);
            Bindings = new BindingTable(list);
            foreach (var definition in list)
            {
                if (!Enum.IsDefined(typeof(ActionTrigger), definition.Trigger)) throw new ArgumentException("Неизвестный режим действия.");
                var action = new GameInputAction(definition);
                actions.Add(action.Id, action);
                ordered.Add(action);
            }
            Bindings.Changed += RequireNeutral;
        }

        public GameInputAction GetAction(string id) => id != null && actions.TryGetValue(id, out var action) ? action : null;
        public void RequireNeutral() => waitForNeutral = true;
        public IDisposable Suspend()
        {
            suspensionCount++;
            RequireNeutral();
            return new InputSubscription(() => { suspensionCount--; RequireNeutral(); });
        }
        public void Tick(IInputSource source)
        {
            if (IsSuspended) return;
            if (waitForNeutral)
            {
                if (!source.AnyHeld) waitForNeutral = false;
                return;
            }
            long contextRevision = Contexts.Revision;
            var scroll = source is IScrollInputSource scrolling ? scrolling.ScrollDelta :
                new Vector2(0, source.Read(InputBinding.Wheel(true)).Pressed ? 1 :
                    source.Read(InputBinding.Wheel(false)).Pressed ? -1 : 0);
            bool scrollConsumed = Contexts.DispatchScroll(scroll);
            if (Contexts.Revision != contextRevision) return;
            foreach (var action in ordered)
            {
                // Обработчик предыдущего действия мог открыть меню или изменить привязки.
                if (IsSuspended || waitForNeutral) break;
                var binding = Bindings[action.Id];
                if (binding.IsEmpty) continue;
                if (scrollConsumed && binding.Kind != InputControlKind.Key) continue;
                var sample = source.Read(binding);
                if (action.Trigger == ActionTrigger.Press ? sample.Pressed : sample.Held)
                {
                    if (!Contexts.DispatchAction(action.Id)) action.Perform();
                    if (Contexts.Revision != contextRevision) break;
                }
            }
        }
    }
}
