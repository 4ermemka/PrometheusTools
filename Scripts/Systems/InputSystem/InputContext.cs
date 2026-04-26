using System;
using System.Collections.Generic;
using UnityEngine;

public class InputAction
{
    public string Name { get; set; }
    public Action OnPressed;
    public Action OnHeld;
    public Action OnReleased;

    public InputAction(string name, Action onPressed = null, Action onHeld = null, Action onReleased = null)
    {
        Name = name;
        OnPressed = onPressed;
        OnHeld = onHeld;
        OnReleased = onReleased;
    }
}

[Serializable]
public abstract class InputContext : InputHandler
{
    /// <summary>
    /// Ссылка на прошлый контекст
    /// </summary>
    public InputContext Previous;

    protected InputContextManager _contextManager;
    protected List<InputAction> Actions = new();

    public InputContext()
    {
        _contextManager = ManagersHolder.Instance.GetManager<InputContextManager>();
        Actions.Add(new InputAction(name: "Esc", onPressed: Escape));
    }

    public virtual void Init()
    { 

    }

    protected virtual void OnEscape() {}

    #region Base

    public void Activate()
    {
        Subscribe();
    }

    public void Deactivate()
    { 
        Unsubscribe();
    }

    protected virtual void Subscribe()
    {
        foreach (var action in Actions)
        {
            SubscribeToAction(
                actionName: action.Name,
                onPressed: action.OnPressed,
                onHeld: action.OnHeld,
                onReleased: action.OnReleased
            );
        }
    }

    protected virtual void Unsubscribe()
    {
        foreach (var action in Actions)
        {
            UnsubscribeFromAction(
                actionName: action.Name,
                onPressed: action.OnPressed,
                onHeld: action.OnHeld,
                onReleased: action.OnReleased
            );
        }
    }

    protected virtual void Escape()
    {
        _contextManager.EscapeContext();
        OnEscape();
        this.Dispose();
    }

    #endregion
}