using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary> Единый интерфейс для всех действий ввода </summary>
public interface IInputAction
{
    string Name { get; }
    event Action OnPressed;
    event Action OnHeld;
    event Action OnReleased;
    void ProcessUpdate();
}

/// <summary> Действие клавиши (аналогично предыдущей версии) </summary>
[Serializable]
public class KeyAction : IInputAction
{
    public string name;
    public KeyCode key;

    public event Action OnPressed;
    public event Action OnHeld;
    public event Action OnReleased;

    public string Name => name;

    public void ProcessUpdate()
    {
        if (Input.GetKeyDown(key))
            OnPressed?.Invoke();
        else if (Input.GetKeyUp(key))
            OnReleased?.Invoke();
        else if (Input.GetKey(key))
            OnHeld?.Invoke();
    }
}

/// <summary> Действие прокрутки колёсика (вверх или вниз) </summary>
public class ScrollAction : IInputAction
{
    public enum Direction { Up, Down }

    private readonly string actionName;
    private readonly Direction direction;

    // состояние для эмуляции Pressed/Held/Released
    private bool isActive;
    private bool wasActive;

    public event Action OnPressed;
    public event Action OnHeld;
    public event Action OnReleased;
    public string Name => actionName;

    public ScrollAction(string name, Direction dir)
    {
        actionName = name;
        direction = dir;
    }

    public void ProcessUpdate()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        isActive = (direction == Direction.Up && scroll > 0f) ||
                   (direction == Direction.Down && scroll < 0f);

        if (isActive && !wasActive)
            OnPressed?.Invoke();
        else if (isActive && wasActive)
            OnHeld?.Invoke();
        else if (!isActive && wasActive)
            OnReleased?.Invoke();

        wasActive = isActive;
    }
}

/// <summary> Центральный обработчик ввода </summary>
public class InputManager : Manager
{
    public static Action OnStarted;

    [Header("Клавиши")]
    [SerializeField]
    private KeyAction[] keyActions = {
        new KeyAction { name = "ToggleToolbar", key = KeyCode.Tab }
    };

    [Serializable]
    public struct ScrollEntry
    {
        public string name;
    }

    [Header("Прокрутка вверх")]
    [SerializeField] private ScrollEntry[] scrollUpEntries;

    [Header("Прокрутка вниз")]
    [SerializeField] private ScrollEntry[] scrollDownEntries;

    private List<IInputAction> allActions = new List<IInputAction>();
    private Dictionary<string, IInputAction> actionsMap = new Dictionary<string, IInputAction>();
    private Dictionary<KeyCode, KeyAction> keysMap = new Dictionary<KeyCode, KeyAction>();

    public static InputManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Initialize();
            DontDestroyOnLoad(gameObject);
            OnStarted?.Invoke();
        }
        else Destroy(gameObject);
    }

    private void Initialize()
    {
        // Клавиши
        foreach (var action in keyActions)
        {
            if (action == null) continue;
            allActions.Add(action);
            actionsMap[action.name] = action;
            keysMap[action.key] = action;
        }

        // Скролл вверх
        if (scrollUpEntries != null)
        {
            foreach (var entry in scrollUpEntries)
            {
                if (string.IsNullOrEmpty(entry.name)) continue;
                var scrollAction = new ScrollAction(entry.name, ScrollAction.Direction.Up);
                allActions.Add(scrollAction);
                actionsMap[entry.name] = scrollAction;
            }
        }

        // Скролл вниз
        if (scrollDownEntries != null)
        {
            foreach (var entry in scrollDownEntries)
            {
                if (string.IsNullOrEmpty(entry.name)) continue;
                var scrollAction = new ScrollAction(entry.name, ScrollAction.Direction.Down);
                allActions.Add(scrollAction);
                actionsMap[entry.name] = scrollAction;
            }
        }
    }

    private void Update()
    {
        foreach (var action in allActions)
        {
            action.ProcessUpdate();
        }
    }

    /// <summary> Получить любое действие ввода по имени (клавиатура или прокрутка) </summary>
    public IInputAction GetAction(string actionName)
    {
        actionsMap.TryGetValue(actionName, out var action);
        return action;
    }

    /// <summary> Получить действие клавиатуры (для обратной совместимости) </summary>
    //public KeyAction GetKeyAction(string actionName)
    //{
    //    return GetAction(actionName) as KeyAction;
    //}
}