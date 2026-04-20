using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class GridCell
{
    // Глобальные координаты ячейки (привязаны к сетке мира)
    public Vector3Int gridPosition { get; private set; }

    // Ссылки на соседей по 4-м основным направлениям
    public GridCell neighborUp { get; set; }
    public GridCell neighborDown { get; set; }
    public GridCell neighborLeft { get; set; }
    public GridCell neighborRight { get; set; }

    // Удобное свойство для перебора всех соседей
    public IEnumerable<GridCell> Neighbors
    {
        get
        {
            if (neighborUp != null) yield return neighborUp;
            if (neighborDown != null) yield return neighborDown;
            if (neighborLeft != null) yield return neighborLeft;
            if (neighborRight != null) yield return neighborRight;
        }
    }

    // Флаг, была ли ячейка успешно сгенерирована на объекте
    public bool isValid { get; set; } = false;

    public GridCell(Vector3Int gridPos)
    {
        gridPosition = gridPos;
    }

    // Получает мировую позицию центра ячейки (для рендеринга, отладки)
    public Vector3 GetWorldPosition()
    {
        // Предполагаем, что размер ячейки = 1. Центр находится в gridPosition.
        return new Vector3(gridPosition.x, gridPosition.y, gridPosition.z);
    }
}