using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Editor wiring нового действия к любому UnityEvent без нового класса/enum.</summary>
public sealed class GameActionListener : MonoBehaviour
{
    [SerializeField] private InputManager inputManager;
    [SerializeField] private string actionId;
    [SerializeField] private UnityEvent onPerformed = new();
    private IDisposable subscription;
    private void OnEnable() { InputManager.OnStarted += Connect; Connect(); }
    private void Connect()
    {
        if (subscription != null) return;
        var manager = inputManager != null ? inputManager : InputManager.Instance;
        if (manager == null) return;
        var action = manager.GetAction(actionId);
        if (action == null) { Debug.LogError($"{name}: действие '{actionId}' отсутствует.", this); return; }
        subscription = action.Subscribe(onPerformed.Invoke);
    }
    private void OnDisable()
    {
        InputManager.OnStarted -= Connect;
        subscription?.Dispose();
        subscription = null;
    }
}
