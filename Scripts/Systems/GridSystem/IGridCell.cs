using System;
using System.Collections.Generic;
using UnityEngine;

public interface IGridCell
{
    public Action<IGridCell> OnCellPointerEnterEvent {  get; }
    public Action<IGridCell> OnCellPointerExitEvent { get; }
    public Action<IGridCell> OnCellPointerClickEvent { get; }

    public ISelectable Selection { get; }

    Vector3Int GridPosition { get; }
    float WorldY { get; }
    Quaternion Rotation { get; }
    IEnumerable<IGridCell> Neighbors { get; }
    bool IsValid { get; }
    Vector3 GetWorldPosition(float cellSize);
}