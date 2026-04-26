// Selectable.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
public class Selectable : MonoBehaviour, ISelectable
{
    [Tooltip("Скрытый материал, полностью прозрачный (можно назначить вручную или загрузить по умолчанию)")]
    [SerializeField] private Material _hiddenMaterial;

    private MeshRenderer _renderer;
    private Material[] _baseMaterials;              // оригинальные материалы объекта
    private Dictionary<string, bool> _selections;   // состояние каждого типа
    private Dictionary<string, Material> _materialInstances; // экземпляры материалов для этого объекта
    private string[] _typeNames;                    // список имён типов в том же порядке, что и слоты

    // Количество типов выделений (и, соответственно, дополнительных слотов)
    private int TypeCount => _typeNames.Length;

    public bool IsSelected(string typeName) =>
        _selections.TryGetValue(typeName, out var selected) && selected;

    private void Start()
    {
        _renderer = GetComponent<MeshRenderer>();
        _baseMaterials = _renderer.sharedMaterials; // сохраняем оригинальные (без выделений)

        // Загружаем все типы выделений
        var definitions = SelectionTypeManager.Definitions;
        _typeNames = definitions.Keys.ToArray();

        // Инициализируем словари
        _selections = new Dictionary<string, bool>(_typeNames.Length);
        _materialInstances = new Dictionary<string, Material>(_typeNames.Length);

        foreach (var kvp in definitions)
        {
            string name = kvp.Key;
            _selections[name] = false;

            // Создаём уникальный экземпляр материала для этого объекта
            var srcMat = kvp.Value.Material;
            _materialInstances[name] = srcMat != null ? new Material(srcMat) : null;
        }

        // Гарантируем наличие скрытого материала (можно загрузить из Resources)
        if (_hiddenMaterial == null)
            _hiddenMaterial = CreateHiddenMaterial();

        // Строим полный массив: базовые + скрытые
        Material[] fullArray = new Material[_baseMaterials.Length + TypeCount];
        _baseMaterials.CopyTo(fullArray, 0);
        for (int i = 0; i < TypeCount; i++)
            fullArray[_baseMaterials.Length + i] = _hiddenMaterial;

        _renderer.materials = fullArray;
    }

    public void Select(string typeName)
    {
        if (!_selections.ContainsKey(typeName))
        {
            Debug.LogWarning($"Selectable: тип выделения '{typeName}' не найден.");
            return;
        }
        if (_selections[typeName]) return; // уже выделен
        _selections[typeName] = true;
        Redraw();
    }

    public void Deselect(string typeName)
    {
        if (!_selections.ContainsKey(typeName)) return;
        if (!_selections[typeName]) return;
        _selections[typeName] = false;
        Redraw();
    }

    /// <summary> Получить/изменить материал выделения конкретного типа на этом объекте. </summary>
    public Material GetSelectionMaterial(string typeName)
    {
        _materialInstances.TryGetValue(typeName, out var mat);
        return mat;
    }

    /// <summary> Установить новый материал для типа выделения (заменит текущий экземпляр). </summary>
    public void SetSelectionMaterial(string typeName, Material newMaterial)
    {
        if (!_materialInstances.ContainsKey(typeName)) return;
        _materialInstances[typeName] = newMaterial;
        Redraw();
    }

    private void Redraw()
    {
        var mats = _renderer.materials; // получаем текущий массив, НЕ создаём новый
        for (int i = 0; i < TypeCount; i++)
        {
            string typeName = _typeNames[i];
            bool active = _selections[typeName];
            Material targetMat = active ? _materialInstances[typeName] : _hiddenMaterial;
            // Слоты выделений идут после базовых
            mats[_baseMaterials.Length + i] = targetMat != null ? targetMat : _hiddenMaterial;
        }
        _renderer.materials = mats; // присваиваем тот же массив, просто обновлённый
    }

    private static Material CreateHiddenMaterial()
    {
        // Простейший полностью прозрачный материал (можно заменить загрузкой из Resources)
        var mat = new Material(Shader.Find("Unlit/Transparent"));
        mat.color = new Color(0, 0, 0, 0);
        mat.mainTexture = Texture2D.blackTexture; // черная, альфа = 1? Нет, у blackTexture альфа = 1.
                                                  // Чтобы альфа точно была 0, можно создать 1x1 текстуру с Color.clear.
                                                  // Но проще задать альфу через _Color, она перемножается.

        // Дополнительно выключаем запись глубины (по умолчанию и так Off, но перестрахуемся)
        mat.SetInt("_ZWrite", 0);
        return mat;
    }
}