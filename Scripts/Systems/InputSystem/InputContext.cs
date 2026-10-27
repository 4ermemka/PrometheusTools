using System;
using System.Collections.Generic;
using Shared.Inputs;
using UnityEngine;

public class InputAction
{
    public string Name { get; }
    public Action OnPerformed { get; }
    public InputAction(string name, Action onPerformed) { Name = name; OnPerformed = onPerformed; }
}

/// <summary>
/// Узел цепочки реакций. Клавиши и Press/Hold принадлежат профилю.
/// true из TryHandle означает поглощение; false передаёт сигнал Previous.
/// Ссылки меняет только InputContextChain. Один экземпляр используется для одного входа.
/// </summary>
[Serializable]
public abstract class InputContext : InputHandler
{
    public InputContext Previous { get; internal set; }
    public InputContext Next { get; internal set; }
    public bool IsCurrent => Owner?.Current == this;
    public bool IsDisposed { get; private set; }
    internal InputContextChain Owner { get; set; }
    protected List<InputAction> Actions = new();
    private bool active;

    protected InputContext(IGameActionSource source = null, string cancelActionId = "Cancel") : base(source)
    {
        if (!string.IsNullOrEmpty(cancelActionId)) Actions.Add(new InputAction(cancelActionId, Escape));
    }
    public virtual void Init() { }
    internal void ValidateActions(IGameActionSource source)
    {
        if (source == null) return;
        foreach (var action in Actions)
            if (source.GetAction(action.Name) == null)
                throw new InvalidOperationException($"{GetType().Name}: action '{action.Name}' is missing from the profile.");
    }
    protected virtual void OnEscape() { }
    protected virtual void OnEntered() { }
    protected virtual void OnExited() { }
    protected virtual void OnBecameCurrent() { }
    protected virtual void OnCovered() { }
    internal void Entered() { active = true; OnEntered(); }
    internal void BecameCurrent() => OnBecameCurrent();
    internal void Covered() => OnCovered();

    // Совместимость вызовов v1. Контексты больше не подписываются напрямую на Performed.
    public void Activate() { if (IsDisposed) throw new ObjectDisposedException(GetType().Name); active = true; }
    public void Deactivate() => active = false;
    public virtual bool TryHandleAction(string actionId)
    {
        if (!active || IsDisposed) return false;
        foreach (var action in Actions)
        {
            if (action.Name != actionId) continue;
            action.OnPerformed?.Invoke();
            return true;
        }
        return false;
    }
    public virtual bool TryHandleScroll(Vector2 delta) => false;
    protected virtual void Escape()
    {
        if (Owner != null && Owner.Pop()) OnEscape();
    }
    public override void Dispose()
    {
        if (Owner != null) Owner.Remove(this);
        else DisposeDetached();
    }
    internal void DisposeDetached()
    {
        if (IsDisposed) return;
        IsDisposed = true; active = false;
        try { OnExited(); }
        finally { base.Dispose(); }
    }
}

