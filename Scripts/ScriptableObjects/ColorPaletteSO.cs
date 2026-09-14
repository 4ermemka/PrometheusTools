using UnityEngine;

[CreateAssetMenu(menuName = "Rendering/Color Palette", fileName = "ColorPalette")]
public class ColorPaletteSO : ScriptableObject
{
    [Tooltip("Цвета палитры (используется максимум 256)")]
    public Color[] colors = new Color[] { Color.black, Color.white };

    [Tooltip("Использовать LAB DeltaE (более точное цветоразличие)")]
    public bool useLABDistance = true;
}
