using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class GridCellData
{
    public Vector3Int gridPosition { get; private set; }
    public float worldY { get; set; }
    public Quaternion rotation { get; set; } = Quaternion.identity;

    public GridCellData neighborUp { get; set; }
    public GridCellData neighborDown { get; set; }
    public GridCellData neighborLeft { get; set; }
    public GridCellData neighborRight { get; set; }

    public GridCellView view { get; set; }
    public bool isValid { get; set; }

    public IEnumerable<GridCellData> Neighbors
    {
        get
        {
            if (neighborUp != null) yield return neighborUp;
            if (neighborDown != null) yield return neighborDown;
            if (neighborLeft != null) yield return neighborLeft;
            if (neighborRight != null) yield return neighborRight;
        }
    }

    public GridCellData(Vector3Int pos)
    {
        gridPosition = pos;
        isValid = false;
    }

    public Vector3 GetWorldPosition(float cellSize)
    {
        return new Vector3(gridPosition.x * cellSize, worldY, gridPosition.z * cellSize);
    }
}