using System;
using UnityEngine;

/// <summary>
/// Базовый класс для подписки на события ввода
/// </summary>
public abstract class InputHandler : IDisposable
{
    public void Dispose()
    {

    }

    /// <summary>
    /// Подписаться на действие по имени с защитой от дублирования
    /// </summary>
    protected void SubscribeToAction(string actionName,
        Action onPressed = null,
        Action onHeld = null,
        Action onReleased = null)
    {
        if (InputManager.Instance == null)
        {
            Debug.LogError($"{GetType().Name}: InputManager.Instance is null!");
            return;
        }

        var keyAction = InputManager.Instance.GetAction(actionName);
        if (keyAction == null)
        {
            Debug.LogError($"{GetType().Name}: KeyAction '{actionName}' is null!");
            return;
        }

        // Сохраняем подписки для возможности отписки
        if (onPressed != null)
        {
            // Проверяем, не подписаны ли уже
            keyAction.OnPressed += onPressed;
        }

        if (onHeld != null)
        {
            keyAction.OnHeld += onHeld;
        }

        if (onReleased != null)
        {
            keyAction.OnReleased += onReleased;
        }
    }

    protected void UnsubscribeFromAction(string actionName,
        Action onPressed = null,
        Action onHeld = null,
        Action onReleased = null)
    {
        if (InputManager.Instance == null)
        {
            Debug.LogError($"{GetType().Name}: InputManager.Instance is null!");
            return;
        }

        var keyAction = InputManager.Instance.GetAction(actionName);
        if (keyAction == null)
        {
            Debug.LogError($"{GetType().Name}: KeyAction '{actionName}' is null!");
            return;
        }

        // Сохраняем подписки для возможности отписки
        if (onPressed != null)
        {
            // Проверяем, не подписаны ли уже
            keyAction.OnPressed -= onPressed;
        }

        if (onHeld != null)
        {
            keyAction.OnHeld -= onHeld;
        }

        if (onReleased != null)
        {
            keyAction.OnReleased -= onReleased;
        }
    }
}