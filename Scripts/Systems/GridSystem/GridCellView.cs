using UnityEngine;

[ExecuteAlways]
public class GridCellView : MonoBehaviour
{
    [SerializeField] private MeshRenderer mainRenderer;
    private GridCellData data;
    private GridController controller;

    public void Initialize(GridCellData cellData)
    {
        data = cellData;
        controller = GetComponentInParent<GridController>();
        name = $"Cell_{data.gridPosition.x}_{data.gridPosition.y}_{data.gridPosition.z}";
        transform.rotation = data.rotation;
    }

    public void Highlight(bool active)
    {
        if (mainRenderer != null)
            mainRenderer.material.color = active ? Color.red : Color.white;
    }

    public GridCellData GetData() => data;

    public GridCellData GetNeighbor(Vector3Int direction)
    {
        if (direction == Vector3Int.right) return data.neighborRight;
        if (direction == Vector3Int.left) return data.neighborLeft;
        if (direction == Vector3Int.forward) return data.neighborUp;
        if (direction == Vector3Int.back) return data.neighborDown;
        return null;
    }

    private void OnDestroy()
    {
        if (controller != null && data != null)
        {
            controller.RemoveCellData(data);
        }
    }
}