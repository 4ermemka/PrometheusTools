using System;
using System.Collections.Generic;
using UnityEngine;

public interface IGridProvider
{
    public event Action<IGridCell> OnCellPointerEnter;
    public event Action<IGridCell> OnCellPointerExit;
    public event Action<IGridCell> OnCellPointerClick;

    /// <summary>По округлённому XYZ-ключу; может вернуть null между уровнями поверхности.</summary>
    IGridCell GetCellAt(Vector3 worldPosition);
    IGridCell GetClosestCell(Vector3 worldPosition);
    /// <summary>Ромб по XZ в шагах ceil(radius/CellSize), все этажи, не евклидова сфера.</summary>
    List<IGridCell> GetCellsInRadius(Vector3 worldCenter, float radius);
    /// <summary>Достижимые за не более order рёбер, без исходной ячейки.</summary>
    List<IGridCell> GetNeighborsOfOrder(IGridCell cell, int order);
    /// <summary>Кэшированный read-only снимок зарегистрированных валидных ячеек.</summary>
    IReadOnlyCollection<IGridCell> AllCells { get; }
    float CellSize { get; }
}
