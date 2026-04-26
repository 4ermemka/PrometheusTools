// SelectionTypeManager.cs
using System.Collections.Generic;
using UnityEngine;

public static class SelectionTypeManager
{
    private static Dictionary<string, SelectionTypeDefinition> _definitions;
    private static bool _initialized = false;
    private static bool Initialized 
    {
        get => _initialized;
        set => _initialized = value;
    }

    public static IReadOnlyDictionary<string, SelectionTypeDefinition> Definitions
    {
        get
        {
            Init();
            return _definitions;
        }
    }

    private static void Init()
    {
        if (Initialized) return;
        Initialized = true;
    
        _definitions = new Dictionary<string, SelectionTypeDefinition>();
        var assets = Resources.LoadAll<SelectionTypeDefinition>("SelectionTypes");
        foreach (var def in assets)
        {
            if (!string.IsNullOrEmpty(def.TypeName) && !_definitions.ContainsKey(def.TypeName))
                _definitions.Add(def.TypeName, def);
        }
        //Debug.Log($"[SelectionTypeManager] Loaded {assets.Length} assets");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Initialized = false;
        _definitions = null;
    }

    // Можно добавить метод обновления/перезагрузки при изменении ассетов в редакторе
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void EditorReset() => Initialized = false;

#endif
}