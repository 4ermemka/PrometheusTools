using System;

namespace Shared.Inputs
{
    /// <summary>Захват отделён от UI. Исходный click открытия и уже удерживаемые клавиши пропускаются.</summary>
    public sealed class RebindCapture : IDisposable
    {
        private readonly GameActionRouter router;
        private readonly IDisposable suspension;
        private bool armed;
        public string ActionId { get; }
        public bool IsActive { get; private set; } = true;
        public event Action Finished;
        public RebindCapture(GameActionRouter router, string actionId)
        {
            this.router = router ?? throw new ArgumentNullException(nameof(router));
            if (router.GetAction(actionId) == null) throw new ArgumentException("Неизвестное действие.", nameof(actionId));
            ActionId = actionId;
            suspension = router.Suspend();
        }
        public void Tick(IInputSource source, Func<InputBinding, bool> accept = null)
        {
            if (!IsActive) return;
            if (!armed) { armed = !source.AnyHeld; return; }
            if (!source.TryGetPressed(accept, out var binding)) return;
            router.Bindings.Rebind(ActionId, binding);
            Dispose();
        }
        public void Dispose()
        {
            if (!IsActive) return;
            IsActive = false;
            suspension.Dispose();
            Finished?.Invoke();
        }
    }
}
