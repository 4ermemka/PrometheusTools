using System;
using System.Collections.Generic;
using Shared.Inputs;
using UnityEngine;

public interface IBindingStore
{
    BindingOverride[] Load();
    void Save(BindingOverride[] overrides);
}

[Serializable]
public struct BindingOverride
{
    public string actionId;
    public InputBinding binding;
}

/// <summary>Локальные настройки, отдельно от WorldState и авторского ScriptableObject.</summary>
public sealed class PlayerPrefsBindingStore : IBindingStore
{
    [Serializable] private sealed class Document { public int version = 1; public BindingOverride[] entries; }
    private readonly string key;
    public PlayerPrefsBindingStore(string profileId) => key = "input.bindings.v1." + profileId;
    public BindingOverride[] Load()
    {
        if (!PlayerPrefs.HasKey(key)) return Array.Empty<BindingOverride>();
        var document = JsonUtility.FromJson<Document>(PlayerPrefs.GetString(key));
        return document != null && document.version == 1 ? document.entries ?? Array.Empty<BindingOverride>() : Array.Empty<BindingOverride>();
    }
    public void Save(BindingOverride[] overrides)
    {
        PlayerPrefs.SetString(key, JsonUtility.ToJson(new Document { entries = overrides }));
        PlayerPrefs.Save();
    }

    public static void Apply(BindingTable table, IEnumerable<BindingOverride> overrides)
    {
        if (overrides == null) return;
        foreach (var entry in overrides)
            if (entry.actionId != null && table.Bindings.ContainsKey(entry.actionId) && entry.binding.IsValid)
                table.Rebind(entry.actionId, entry.binding);
    }
}
