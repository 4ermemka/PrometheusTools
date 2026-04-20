// IGridProvider.cs
using System.Collections.Generic;
using UnityEngine;

public interface IGridProvider
{
    /// <summary> Получить ячейку по мировым координатам. </summary>
    GridCellData GetCellAt(Vector3 worldPosition);

    /// <summary> Получить все ячейки в радиусе (Манхэттен). </summary>
    List<GridCellData> GetCellsInRadius(Vector3 worldCenter, float radius);

    /// <summary> Все сгенерированные валидные ячейки. </summary>
    IReadOnlyCollection<GridCellData> AllCells { get; }

    /// <summary> Размер ячейки в единицах мира. </summary>
    float CellSize { get; }
}