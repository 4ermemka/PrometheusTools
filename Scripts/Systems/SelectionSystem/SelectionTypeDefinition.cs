// SelectionTypeDefinition.cs
using UnityEngine;

[CreateAssetMenu(fileName = "SelectionType", menuName = "Selection/Type Definition")]
public class SelectionTypeDefinition : ScriptableObject
{
    [SerializeField] private string _typeName;   // Уникальное имя (ключ)
    [SerializeField] private Material _material; // Материал выделения

    public string TypeName => _typeName;
    public Material Material => _material;
}