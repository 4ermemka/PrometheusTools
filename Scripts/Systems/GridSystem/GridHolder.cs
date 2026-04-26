using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class GridHolder : MonoBehaviour, IGridProvider, IPathFinder<GridCell>
{
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private Transform cellsContainer;

    private Dictionary<Vector3Int, GridCell> grid = new();
    private Dictionary<Vector2Int, List<GridCell>> cellsByXZ = new();

    public event Action<IGridCell> OnCellPointerEnter;
    public event Action<IGridCell> OnCellPointerExit;
    public event Action<IGridCell> OnCellPointerClick;

    public float CellSize => cellSize;
    public IReadOnlyCollection<IGridCell> AllCells => grid.Values.Where(c => c.IsValid).ToList().AsReadOnly();

    private void Start()
    {
        EnsureGridPopulated();
    }

    /// <summary> Если словарь пуст, заполняет его из cellsContainer. </summary>
    private void EnsureGridPopulated()
    {
        if (grid.Count > 0 || cellsContainer == null) return;
        var cells = cellsContainer.GetComponentsInChildren<GridCell>();
        foreach (var cell in cells)
        {
            if (cell != null && cell.IsValid)
            {
                RegisterCell(cell);
                // Подписка на события (если ещё не подписана) – делаем безопасно
                cell.OnCellPointerEnterEvent -= OnCellPointerEnter;
                cell.OnCellPointerEnterEvent += OnCellPointerEnter;
                cell.OnCellPointerExitEvent -= OnCellPointerExit;
                cell.OnCellPointerExitEvent += OnCellPointerExit;
                cell.OnCellPointerClickEvent -= OnCellPointerClick;
                cell.OnCellPointerClickEvent += OnCellPointerClick;
            }
        }
    }

    // Остальные методы без изменений (RegisterCell, RemoveCell, ClearAllCells, IGridProvider, IPathFinder, WorldToGridPos и т.д.)

    private void OnDrawGizmosSelected()
    {
        // ПЕРЕД ОТРИСОВКОЙ: лениво заполняем словарь, если он пуст
        EnsureGridPopulated();

        if (grid.Count == 0) return;
        var cam = SceneView.currentDrawingSceneView?.camera;
        if (cam == null) return;
        float maxDist = 30f;

        Gizmos.color = Color.cyan;
        foreach (var cell in grid.Values)
        {
            if (cell == null || !cell.IsValid) continue;
            Vector3 pos = cell.GetWorldPosition(cellSize);
            if (Vector3.Distance(cam.transform.position, pos) > maxDist) continue;
            Gizmos.DrawSphere(pos, 0.1f);
        }
    }

    public void DrawConnections()
    {
        // ПЕРЕД ОТРИСОВКОЙ: лениво заполняем словарь, если он пуст
        EnsureGridPopulated();

        if (grid.Count == 0) return;
        var cam = SceneView.currentDrawingSceneView?.camera;
        if (cam == null) return;
        float maxDist = 30f;
        Handles.color = new Color(1, 1, 0, 0.5f);

        foreach (var cell in grid.Values)
        {
            if (cell == null || !cell.IsValid) continue;
            Vector3 pos = cell.GetWorldPosition(cellSize);
            if (Vector3.Distance(cam.transform.position, pos) > maxDist) continue;

            foreach (var neighbor in cell.Neighbors)
            {
                if (neighbor is not GridCell neighborCell) continue;
                if (CompareGridPositions(cell.GridPosition, neighborCell.GridPosition) < 0)
                {
                    Vector3 neighborPos = neighborCell.GetWorldPosition(cellSize);
                    Handles.DrawLine(pos, neighborPos);
                }
            }
        }
    }

    // Генератор вызывает этот метод, чтобы добавить ячейку в словари
    public void RegisterCell(GridCell cell)
    {
        if (cell == null || grid.ContainsKey(cell.GridPosition)) return;
        grid[cell.GridPosition] = cell;
        Vector2Int xz = new(cell.GridPosition.x, cell.GridPosition.z);
        if (!cellsByXZ.ContainsKey(xz)) cellsByXZ[xz] = new List<GridCell>();
        cellsByXZ[xz].Add(cell);
    }

    // Ячейка сама вызывает этот метод при OnDestroy
    public void RemoveCell(GridCell cell)
    {
        if (cell == null) return;
        grid.Remove(cell.GridPosition);
        Vector2Int xz = new(cell.GridPosition.x, cell.GridPosition.z);
        if (cellsByXZ.TryGetValue(xz, out List<GridCell> list))
        {
            list.Remove(cell);
            if (list.Count == 0) cellsByXZ.Remove(xz);
        }
    }

    // Очистка всех данных (объекты удаляет генератор)
    public void ClearAllCells()
    {
        grid.Clear();
        cellsByXZ.Clear();
    }

    // Реализация IGridProvider
    public IGridCell GetCellAt(Vector3 worldPos)
    {
        Vector3Int gridPos = WorldToGridPos(worldPos);
        grid.TryGetValue(gridPos, out GridCell cell);
        return cell;
    }

    public IGridCell GetClosestCell(Vector3 worldPos)
    {
        Vector3Int gridPos = WorldToGridPos(worldPos);
        if (grid.TryGetValue(gridPos, out GridCell cell)) return cell;

        GridCell closest = null;
        float minDist = float.MaxValue;
        foreach (var c in grid.Values.Where(c => c.IsValid))
        {
            float dist = Vector3.Distance(worldPos, c.GetWorldPosition(cellSize));
            if (dist < minDist) { minDist = dist; closest = c; }
        }
        return closest;
    }

    public List<IGridCell> GetCellsInRadius(Vector3 worldCenter, float radius)
    {
        List<IGridCell> result = new();
        Vector3Int center = WorldToGridPos(worldCenter);
        int intRadius = Mathf.CeilToInt(radius / cellSize);

        for (int x = -intRadius; x <= intRadius; x++)
        {
            for (int z = -intRadius; z <= intRadius; z++)
            {
                if (Mathf.Abs(x) + Mathf.Abs(z) > intRadius) continue;
                Vector2Int checkXZ = new(center.x + x, center.z + z);
                if (cellsByXZ.TryGetValue(checkXZ, out var cells))
                    result.AddRange(cells.Where(c => c.IsValid));
            }
        }
        return result;
    }

    public List<IGridCell> GetNeighborsOfOrder(IGridCell cell, int order)
    {
        if (cell == null || order < 0) return new List<IGridCell>();
        HashSet<IGridCell> visited = new() { cell };
        List<IGridCell> frontier = new() { cell };

        for (int i = 0; i < order; i++)
        {
            List<IGridCell> next = new();
            foreach (var c in frontier)
            {
                foreach (var n in c.Neighbors)
                {
                    if (!visited.Contains(n))
                    {
                        visited.Add(n);
                        next.Add(n);
                    }
                }
            }
            frontier = next;
        }
        visited.Remove(cell);
        return visited.ToList();
    }

    // Реализация IPathFinder<GridCell>
    public List<GridCell> FindPath(GridCell start, GridCell end, Func<GridCell, GridCell, bool> predicate)
    {
        if (start == null || end == null) return null;
        var openSet = new Queue<GridCell>();
        var cameFrom = new Dictionary<GridCell, GridCell>();
        openSet.Enqueue(start);
        cameFrom[start] = null;

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();
            if (current == end)
            {
                var path = new List<GridCell>();
                while (current != null)
                {
                    path.Add(current);
                    current = cameFrom[current];
                }
                path.Reverse();
                return path;
            }
            foreach (var neighbor in current.Neighbors.OfType<GridCell>())
            {
                if (!cameFrom.ContainsKey(neighbor) && predicate(current, neighbor))
                {
                    cameFrom[neighbor] = current;
                    openSet.Enqueue(neighbor);
                }
            }
        }
        return null;
    }

    private Vector3Int WorldToGridPos(Vector3 worldPos) =>
        new Vector3Int(
            Mathf.RoundToInt((worldPos.x - transform.position.x) / cellSize),
            Mathf.RoundToInt(worldPos.y / cellSize),
            Mathf.RoundToInt((worldPos.z - transform.position.z) / cellSize)
        );

    private int CompareGridPositions(Vector3Int a, Vector3Int b)
    {
        int cmpX = a.x.CompareTo(b.x);
        if (cmpX != 0) return cmpX;
        int cmpY = a.y.CompareTo(b.y);
        if (cmpY != 0) return cmpY;
        return a.z.CompareTo(b.z);
    }
}