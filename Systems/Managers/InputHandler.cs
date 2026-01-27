using UnityEngine;

/// <summary>
/// Базовый класс для подписки на события ввода
/// </summary>
public abstract class InputHandler : MonoBehaviour
{
    private bool _isSubscribed = false;

    protected virtual void Start()
    {
        // Подписываемся при старте, если объект активен
        if (isActiveAndEnabled && !_isSubscribed)
        {
            SubscribeEvents();
        }
    }

    protected virtual void OnEnable()
    {
        // Подписываемся только если еще не подписаны
        if (!_isSubscribed)
        {
            SubscribeEvents();
        }
    }

    protected virtual void OnDisable()
    {
        // Отписываемся при выключении
        UnsubscribeEvents();
    }

    protected virtual void OnDestroy()
    {
        // Отписываемся при уничтожении
        UnsubscribeEvents();
    }

    /// <summary>
    /// Подписаться на события
    /// </summary>
    protected abstract void SubscribeEvents();

    /// <summary>
    /// Отписаться от событий
    /// </summary>
    protected abstract void UnsubscribeEvents();

    /// <summary>
    /// Подписаться на действие по имени с защитой от дублирования
    /// </summary>
    protected void SubscribeToAction(string actionName,
        System.Action onPressed = null,
        System.Action onHeld = null,
        System.Action onReleased = null)
    {
        if (InputManager.Instance == null)
        {
            Debug.LogError($"{name}: InputManager.Instance is null!");
            return;
        }

        var keyAction = InputManager.Instance.GetKeyAction(actionName);
        if (keyAction == null)
        {
            Debug.LogError($"{name}: KeyAction '{actionName}' is null!");
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

        _isSubscribed = true;
    }
}