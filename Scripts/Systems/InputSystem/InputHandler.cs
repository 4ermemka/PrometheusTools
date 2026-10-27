using System;
using System.Collections.Generic;
using Shared.Inputs;

/// <summary>Владеет подписками контекста и снимает их с тех же объектов событий, даже после смены singleton.</summary>
public abstract class InputHandler : IDisposable
{
    private readonly IGameActionSource source;
    internal IGameActionSource ActionSource => source;
    private readonly Dictionary<(string id, Action callback), IDisposable> subscriptions = new();
    protected InputHandler(IGameActionSource source = null) => this.source = source;
    protected void SubscribeToAction(string actionId, Action onPerformed)
    {
        if (onPerformed == null || subscriptions.ContainsKey((actionId, onPerformed))) return;
        var action = (source ?? InputManager.Instance)?.GetAction(actionId);
        if (action == null) throw new InvalidOperationException($"{GetType().Name}: действие '{actionId}' отсутствует в Input Profile.");
        subscriptions.Add((actionId, onPerformed), action.Subscribe(onPerformed));
    }
    protected void UnsubscribeFromAction(string actionId, Action onPerformed)
    {
        if (!subscriptions.TryGetValue((actionId, onPerformed), out var token)) return;
        token.Dispose();
        subscriptions.Remove((actionId, onPerformed));
    }
    public virtual void Dispose()
    {
        foreach (var token in subscriptions.Values) token.Dispose();
        subscriptions.Clear();
    }
}
