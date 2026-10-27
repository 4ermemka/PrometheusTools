using System;
using System.Collections.Generic;
using Shared.Inputs;
using UnityEngine;

/// <summary>Авторский каталог: игрок меняет только runtime BindingTable, не этот asset.</summary>
[CreateAssetMenu(menuName = "Input/Game Input Profile")]
public sealed class GameInputProfile : ScriptableObject
{
    [SerializeField] private string profileId = "";
    [SerializeField] private List<GameActionDefinition> actions = new();
    private Dictionary<string, InputBinding> previousBindings;
    public string ProfileId => profileId;
    public IReadOnlyList<GameActionDefinition> Actions => actions;

    public void Configure(string id, IEnumerable<GameActionDefinition> definitions)
    {
        profileId = id;
        actions = new List<GameActionDefinition>(definitions);
        NormalizeBindings(null);
    }

    public void AssignDefaultBinding(string actionId, InputBinding binding)
    {
        foreach (var action in actions) if (action.Id == actionId) action.SetDefaultBinding(binding);
        NormalizeBindings(actionId);
    }

    /// <summary>Также вызывается Inspector-ом при правке SerializedProperty, paste и Undo.</summary>
    public void NormalizeBindings(string changedActionId)
    {
        var table = new BindingTable(actions);
        if (!string.IsNullOrEmpty(changedActionId))
            foreach (var action in actions)
                if (action.Id == changedActionId) { table.Rebind(action.Id, action.DefaultBinding); break; }
        foreach (var action in actions) action.SetDefaultBinding(table[action.Id]);
        RememberBindings();
    }

    private void OnEnable() => RememberBindings();
    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(profileId)) profileId = Guid.NewGuid().ToString("N");
        string changed = null;
        foreach (var action in actions)
            if (action != null && !string.IsNullOrEmpty(action.Id) && (previousBindings == null ||
                !previousBindings.TryGetValue(action.Id, out var before) || before != action.DefaultBinding)) changed = action.Id;
        // Во время ввода ID строка может временно быть пустой/дублирующейся. Inspector
        // показывает ошибку; runtime всё равно fail-fast проверяет законченный каталог.
        try { NormalizeBindings(changed); }
        catch (ArgumentException) { }
    }

    private void RememberBindings()
    {
        previousBindings = new Dictionary<string, InputBinding>(StringComparer.Ordinal);
        foreach (var action in actions)
            if (action != null && !string.IsNullOrEmpty(action.Id)) previousBindings[action.Id] = action.DefaultBinding;
    }
}
