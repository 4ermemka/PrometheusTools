using System;
using System.Collections.Generic;
using UnityEngine;

public interface IGridProvider
{
    public event Action<IGridCell> OnCellPointerEnter;
    public event Action<IGridCell> OnCellPointerExit;
    public event Action<IGridCell> OnCellPointerClick;

    IGridCell GetCellAt(Vector3 worldPosition);
    IGridCell GetClosestCell(Vector3 worldPosition);
    List<IGridCell> GetCellsInRadius(Vector3 worldCenter, float radius);
    List<IGridCell> GetNeighborsOfOrder(IGridCell cell, int order);
    IReadOnlyCollection<IGridCell> AllCells { get; }
    float CellSize { get; }
}