using System;
using System.Collections.Generic;
using Shared.Inputs;
using UnityEngine;

/// <summary>
/// Unity lifecycle/input backend. Игровая логика слушает действие, а не физическую клавишу.
/// Каталог фиксируется при Initialize; пользовательские привязки живут отдельно от asset.
/// </summary>
[DefaultExecutionOrder(-100)]
public class InputManager : Manager, IContextInputSource
{
    public static Action OnStarted;
    public static InputManager Instance { get; private set; }
    [SerializeField] private GameInputProfile profile;
    [SerializeField] private bool saveBindings = true;

    // Миграционный мост сцен v1: имена полей сохранены, новый профиль имеет приоритет.
    [Serializable] private sealed class LegacyKeyEntry { public string name; public KeyCode key; }
    [Serializable] public struct ScrollEntry { public string name; }
    [SerializeField, HideInInspector] private LegacyKeyEntry[] keyActions;
    [SerializeField, HideInInspector] private ScrollEntry[] scrollUpEntries;
    [SerializeField, HideInInspector] private ScrollEntry[] scrollDownEntries;

    private readonly List<Func<bool>> gates = new();
    private IReadOnlyList<GameActionDefinition> definitions;
    private IInputSource source;
    private IBindingStore store;
    private bool initialized;
    private bool applicationFocused = true;
    private Func<InputBinding, bool> captureFilter;
    public GameActionRouter Router { get; private set; }
    public RebindCapture Capture { get; private set; }
    public InputContextChain Contexts { get { Initialize(); return Router.Contexts; } }
    public IReadOnlyList<GameActionDefinition> Definitions => definitions;
    public bool AnyInputHeld => source != null && source.AnyHeld;
    public event Action BindingsChanged;
    public event Action CaptureChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { enabled = false; Destroy(this); return; }
        Instance = this;
        Initialize();
        // Жизнью root управляет ManagersHolder; child нельзя передавать DontDestroyOnLoad.
        OnStarted?.Invoke();
    }
    public override void Init() { Initialize(); base.Init(); }

    public void Initialize(IInputSource inputSource = null, IBindingStore bindingStore = null)
    {
        if (initialized) return;
        source = inputSource ?? new LegacyInputSource();
        definitions = profile != null ? profile.Actions : MigrateLegacy();
        Router = new GameActionRouter(definitions);
        store = bindingStore ?? (saveBindings && profile != null ? new PlayerPrefsBindingStore(profile.ProfileId) : null);
        if (store != null)
        {
            try { PlayerPrefsBindingStore.Apply(Router.Bindings, store.Load()); }
            catch (Exception error) { Debug.LogWarning($"Input: настройки не прочитаны, используются defaults. {error.Message}", this); }
        }
        Router.Bindings.Changed += OnBindingsChanged;
        initialized = true;
    }

    public GameInputAction GetAction(string id) { Initialize(); return Router.GetAction(id); }
    public InputBinding GetBinding(string id) { Initialize(); return Router.Bindings[id]; }
    public void Rebind(string id, InputBinding binding) { Initialize(); Router.Bindings.Rebind(id, binding); }
    public IDisposable Suspend() { Initialize(); return Router.Suspend(); }
    public IDisposable AddGate(Func<bool> isBlocked)
    {
        if (isBlocked == null) throw new ArgumentNullException(nameof(isBlocked));
        gates.Add(isBlocked);
        return new InputSubscription(() => gates.Remove(isBlocked));
    }

    public void BeginRebind(string id, Func<InputBinding, bool> accept = null)
    {
        Initialize();
        CancelRebind();
        Capture = new RebindCapture(Router, id);
        captureFilter = accept;
        Capture.Finished += OnCaptureFinished;
        CaptureChanged?.Invoke();
    }
    public void CancelRebind() => Capture?.Dispose();
    private void OnCaptureFinished()
    {
        Capture.Finished -= OnCaptureFinished;
        Capture = null;
        captureFilter = null;
        CaptureChanged?.Invoke();
    }
    public void ResetBindings()
    {
        Initialize();
        CancelRebind();
        Router.Bindings.ReplaceWith(new BindingTable(definitions));
    }
    private void OnBindingsChanged()
    {
        if (store != null)
        {
            var result = new BindingOverride[definitions.Count];
            for (int i = 0; i < definitions.Count; i++)
                result[i] = new BindingOverride { actionId = definitions[i].Id, binding = Router.Bindings[definitions[i].Id] };
            try { store.Save(result); }
            catch (Exception error) { Debug.LogWarning($"Input: привязки действуют, но сохранить их не удалось. {error.Message}", this); }
        }
        BindingsChanged?.Invoke();
    }
    private void Update()
    {
        if (!initialized || !applicationFocused) return;
        if (Capture != null) { Capture.Tick(source, captureFilter); return; }
        foreach (var gate in gates)
            if (gate()) { Router.RequireNeutral(); return; }
        Router.Tick(source);
    }
    private void OnApplicationFocus(bool focused)
    {
        applicationFocused = focused;
        if (!focused) CancelRebind();
        Router?.RequireNeutral();
    }
    private void OnDisable() { CancelRebind(); Router?.RequireNeutral(); }
    private void OnDestroy()
    {
        CancelRebind();
        if (Router != null) Router.Bindings.Changed -= OnBindingsChanged;
        Router?.Contexts.Dispose();
        if (Instance == this) Instance = null;
        gates.Clear();
    }
    private IReadOnlyList<GameActionDefinition> MigrateLegacy()
    {
        var actions = new List<GameActionDefinition>();
        if (keyActions != null)
            foreach (var entry in keyActions)
                if (entry != null && !string.IsNullOrWhiteSpace(entry.name))
                    actions.Add(new GameActionDefinition(entry.name, entry.name, ActionTrigger.Press, new InputBinding(entry.key)));
        AddWheel(scrollUpEntries, true);
        AddWheel(scrollDownEntries, false);
        return actions;
        void AddWheel(ScrollEntry[] entries, bool up)
        {
            if (entries == null) return;
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.name)) continue;
                actions.RemoveAll(a => a.Id == entry.name);
                actions.Add(new GameActionDefinition(entry.name, entry.name, ActionTrigger.Hold, InputBinding.Wheel(up)));
            }
        }
    }
}
