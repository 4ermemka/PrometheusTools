using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Центральный обработчик ввода с настройкой через инспектор
/// </summary>
public class InputManager : MonoBehaviour
{
    [Serializable]
    public class KeyAction
    {
        public string name;
        public KeyCode key;

        // События для этой клавиши
        public event Action OnPressed;
        public event Action OnHeld;
        public event Action OnReleased;

        public void InvokePressed() => OnPressed?.Invoke();
        public void InvokeHeld() => OnHeld?.Invoke();
        public void InvokeReleased() => OnReleased?.Invoke();
    }

    [Header("Настройка клавиш")]
    [SerializeField]
    private KeyAction[] keyActions = {
        new KeyAction { name = "ToggleToolbar", key = KeyCode.Tab }
    };

    // Словарь для быстрого доступа
    private Dictionary<string, KeyAction> actionsMap = new Dictionary<string, KeyAction>();
    private Dictionary<KeyCode, KeyAction> keysMap = new Dictionary<KeyCode, KeyAction>();

    public static InputManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null) Destroy(gameObject);
        else
        {
            Instance = this;
            Initialize();
        }
    }

    void Initialize()
    {
        foreach (var action in keyActions)
        {
            actionsMap[action.name] = action;
            keysMap[action.key] = action;
        }
    }

    void Update()
    {
        // Обрабатываем только указанные клавиши
        foreach (var action in keyActions)
        {
            if (Input.GetKeyDown(action.key))
            {
                action.InvokePressed();
                //Debug.Log($"Key: {action.key} pressed");
            }
            else if (Input.GetKeyUp(action.key))
            {
                action.InvokeReleased();
                //Debug.Log($"Key: {action.key} released");
            }
            else if (Input.GetKey(action.key))
            { 
                action.InvokeHeld();
                //Debug.Log($"Key: {action.key} held");
            }
        }
    }

    /// <summary>
    /// Получить структуру с событиями для клавиши
    /// </summary>
    public KeyAction GetKeyAction(string actionName)
    {
        return actionsMap.TryGetValue(actionName, out var action) ? action : default;
    }
}