using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Индекс статической сетки и запросы к её графу. XZ отсчитываются от Transform провайдера,
/// Y — от мирового нуля. Геометрия сохраняется в сцене, словари восстанавливаются лениво.
/// </summary>
public class GridProvider : Manager, IGridProvider, IPathFinder<GridCell>
{
    [Min(0.001f)] [SerializeField] private float cellSize = 1f;
    [SerializeField] private Transform cellsContainer;
    private readonly Dictionary<Vector3Int, GridCell> grid = new();
    private readonly Dictionary<Vector2Int, List<GridCell>> cellsByXZ = new();
    private ReadOnlyCollection<IGridCell> allCells;
    private bool populated;

    public event Action<IGridCell> OnCellPointerEnter;
    public event Action<IGridCell> OnCellPointerExit;
    public event Action<IGridCell> OnCellPointerClick;
    public float CellSize => cellSize;

    /// <summary>Read-only снимок; повторное чтение не выделяет память до изменения состава сетки.</summary>
    public IReadOnlyCollection<IGridCell> AllCells
    {
        get
        {
            EnsureGridPopulated();
            if (allCells == null)
            {
                var cells = new List<IGridCell>(grid.Count);
                foreach (var cell in grid.Values) if (cell != null && cell.IsValid) cells.Add(cell);
                allCells = cells.AsReadOnly();
            }
            return allCells;
        }
    }

    public override void Init() { EnsureGridPopulated(); base.Init(); }
    private void Start() => EnsureGridPopulated();
    private void OnDestroy() => ClearAllCells();

    private void EnsureGridPopulated()
    {
        if (!populated) RebuildIndex();
    }

    /// <summary>Читает активные ячейки контейнера после ручных правок/Undo; соседей не пересчитывает.</summary>
    public void RebuildIndex()
    {
        ClearAllCells();
        if (cellsContainer != null)
            foreach (var cell in cellsContainer.GetComponentsInChildren<GridCell>()) RegisterCell(cell);
    }

    public void RegisterCell(GridCell cell)
    {
        EnsureGridPopulated();
        if (cell == null || !cell.IsValid || grid.ContainsKey(cell.GridPosition)) return;
        if (cell.Owner != null && cell.Owner != this) cell.Owner.RemoveCell(cell);
        grid.Add(cell.GridPosition, cell);
        var xz = new Vector2Int(cell.GridPosition.x, cell.GridPosition.z);
        if (!cellsByXZ.TryGetValue(xz, out var column)) cellsByXZ.Add(xz, column = new List<GridCell>());
        column.Add(cell);
        cell.Owner = this;
        // Посредники читают текущий event, а не копируют список слушателей в момент регистрации.
        cell.OnCellPointerEnterEvent += ForwardEnter;
        cell.OnCellPointerExitEvent += ForwardExit;
        cell.OnCellPointerClickEvent += ForwardClick;
        allCells = null;
    }

    public void RemoveCell(GridCell cell)
    {
        // Чужой дубликат координат не должен удалять зарегистрированный объект.
        if (ReferenceEquals(cell, null) || !grid.TryGetValue(cell.GridPosition, out var registered) ||
            !ReferenceEquals(registered, cell)) return;
        grid.Remove(cell.GridPosition);
        var xz = new Vector2Int(cell.GridPosition.x, cell.GridPosition.z);
        if (cellsByXZ.TryGetValue(xz, out var column))
        {
            column.Remove(cell);
            if (column.Count == 0) cellsByXZ.Remove(xz);
        }
        Detach(cell);
        allCells = null;
    }

    /// <summary>Очищает индекс и подписки, но не объекты. До RebuildIndex автопоиска больше нет.</summary>
    public void ClearAllCells()
    {
        foreach (var cell in grid.Values) if (!ReferenceEquals(cell, null)) Detach(cell);
        grid.Clear();
        cellsByXZ.Clear();
        allCells = null;
        populated = true;
    }

    private void Detach(GridCell cell)
    {
        cell.OnCellPointerEnterEvent -= ForwardEnter;
        cell.OnCellPointerExitEvent -= ForwardExit;
        cell.OnCellPointerClickEvent -= ForwardClick;
        if (ReferenceEquals(cell.Owner, this)) cell.Owner = null;
    }

    private void ForwardEnter(IGridCell cell) => OnCellPointerEnter?.Invoke(cell);
    private void ForwardExit(IGridCell cell) => OnCellPointerExit?.Invoke(cell);
    private void ForwardClick(IGridCell cell) => OnCellPointerClick?.Invoke(cell);

    public IGridCell GetCellAt(Vector3 worldPosition)
        => GetCellAt(WorldToGridPos(worldPosition));

    /// <summary>Прямое разрешение сетевого адреса, без потери реальной высоты ячейки.</summary>
    public IGridCell GetCellAt(Vector3Int gridPosition)
    {
        EnsureGridPopulated();
        grid.TryGetValue(gridPosition, out var cell);
        return cell != null && cell.IsValid ? cell : null;
    }

    public IGridCell GetClosestCell(Vector3 worldPosition)
    {
        EnsureGridPopulated();
        GridCell closest = null;
        float minDistance = float.PositiveInfinity;
        // Округлённая ячейка не обязательно ближайшая: WorldY отличается от GridPosition.y.
        foreach (var cell in grid.Values)
        {
            if (cell == null || !cell.IsValid) continue;
            float distance = (worldPosition - cell.GetWorldPosition(cellSize)).sqrMagnitude;
            if (distance < minDistance) { minDistance = distance; closest = cell; }
        }
        return closest;
    }

    public List<IGridCell> GetCellsInRadius(Vector3 center, float radius)
    {
        var result = new List<IGridCell>();
        GetCellsInRadius(center, radius, result);
        return result;
    }

    /// <summary>
    /// Заполняет переиспользуемый список. Сохранена семантика ромба:
    /// |dx|+|dz| &lt;= ceil(radius/CellSize), все этажи, без проверки достижимости.
    /// </summary>
    public void GetCellsInRadius(Vector3 center, float radius, List<IGridCell> result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        result.Clear();
        if (float.IsNaN(radius) || float.IsInfinity(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
        if (radius < 0) return;
        EnsureGridPopulated();
        var position = WorldToGridPos(center);
        double steps = Math.Ceiling((double)radius / cellSize);
        // Для большого радиуса выгоднее обойти существующие колонки, а не пустые координаты.
        if (steps * steps * 2 + steps * 2 + 1 > cellsByXZ.Count)
        {
            foreach (var pair in cellsByXZ)
                if (Math.Abs((long)pair.Key.x - position.x) + Math.Abs((long)pair.Key.y - position.z) <= steps)
                    AddValid(pair.Value, result);
            return;
        }
        int range = (int)steps;
        for (int x = -range; x <= range; x++)
        {
            int depth = range - Math.Abs(x);
            for (int z = -depth; z <= depth; z++)
                if (cellsByXZ.TryGetValue(new Vector2Int(position.x + x, position.z + z), out var column))
                    AddValid(column, result);
        }
    }

    private static void AddValid(List<GridCell> cells, List<IGridCell> result)
    {
        foreach (var cell in cells) if (cell != null && cell.IsValid) result.Add(cell);
    }

    /// <summary>Все достижимые ячейки до order рёбер включительно, без исходной, а не только последнее кольцо.</summary>
    public List<IGridCell> GetNeighborsOfOrder(IGridCell cell, int order)
    {
        EnsureGridPopulated();
        var result = new List<IGridCell>();
        if (order <= 0 || cell is not GridCell start || !Contains(start)) return result;
        var visited = new HashSet<GridCell> { start };
        var queue = new Queue<GridCell>();
        queue.Enqueue(start);
        for (int depth = 0; depth < order && queue.Count != 0; depth++)
        {
            int count = queue.Count;
            while (count-- > 0)
                foreach (var neighbor in queue.Dequeue().NeighborCells)
                    if (Contains(neighbor) && visited.Add(neighbor))
                    {
                        result.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
        }
        return result;
    }

    /// <summary>BFS: кратчайший путь по числу рёбер. null predicate разрешает все существующие связи.</summary>
    public List<GridCell> FindPath(GridCell start, GridCell end, Func<GridCell, GridCell, bool> predicate)
    {
        EnsureGridPopulated();
        if (!Contains(start) || !Contains(end)) return null;
        var queue = new Queue<GridCell>();
        var previous = new Dictionary<GridCell, GridCell> { [start] = null };
        queue.Enqueue(start);
        while (queue.Count != 0)
        {
            var current = queue.Dequeue();
            if (current == end)
            {
                var path = new List<GridCell>();
                for (; current != null; current = previous[current]) path.Add(current);
                path.Reverse();
                return path;
            }
            foreach (var neighbor in current.NeighborCells)
                if (Contains(neighbor) && !previous.ContainsKey(neighbor) && (predicate == null || predicate(current, neighbor)))
                {
                    previous.Add(neighbor, current);
                    queue.Enqueue(neighbor);
                }
        }
        return null;
    }

    private bool Contains(GridCell cell) => cell != null && cell.IsValid &&
        grid.TryGetValue(cell.GridPosition, out var registered) && ReferenceEquals(cell, registered);

    public Vector3Int WorldToGridPos(Vector3 worldPosition)
    {
        ValidateCellSize();
        var origin = transform.position;
        return new Vector3Int(Mathf.RoundToInt((worldPosition.x - origin.x) / cellSize),
            Mathf.RoundToInt(worldPosition.y / cellSize), Mathf.RoundToInt((worldPosition.z - origin.z) / cellSize));
    }

    /// <summary>Центр колонки с фактической высотой поверхности вместо округлённой GridPosition.y.</summary>
    public Vector3 GridToWorldPos(Vector3Int position, float worldY)
    {
        ValidateCellSize();
        var origin = transform.position;
        return new Vector3(origin.x + position.x * cellSize, worldY, origin.z + position.z * cellSize);
    }

    private void ValidateCellSize()
    {
        if (!(cellSize > 0) || float.IsInfinity(cellSize)) throw new InvalidOperationException("Grid CellSize must be finite and positive.");
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        EnsureGridPopulated();
        var camera = SceneView.currentDrawingSceneView?.camera;
        if (camera == null) return;
        var cameraPosition = camera.transform.position;
        Gizmos.color = Color.cyan;
        foreach (var cell in grid.Values)
            if (cell != null && cell.IsValid && (cameraPosition - cell.transform.position).sqrMagnitude <= 900f)
                Gizmos.DrawSphere(cell.transform.position, .1f);
    }
#endif

    public void DrawConnections()
    {
#if UNITY_EDITOR
        EnsureGridPopulated();
        var camera = SceneView.currentDrawingSceneView?.camera;
        if (camera == null) return;
        var cameraPosition = camera.transform.position;
        Handles.color = new Color(1, 1, 0, .5f);
        foreach (var cell in grid.Values)
        {
            if (cell == null || !cell.IsValid || (cameraPosition - cell.transform.position).sqrMagnitude > 900f) continue;
            foreach (var neighbor in cell.NeighborCells)
                if (Contains(neighbor) && (CompareGridPositions(cell.GridPosition, neighbor.GridPosition) < 0 ||
                    !neighbor.NeighborCells.Contains(cell))) Handles.DrawLine(cell.transform.position, neighbor.transform.position);
        }
#endif
    }

    private static int CompareGridPositions(Vector3Int a, Vector3Int b)
    {
        int value = a.x.CompareTo(b.x);
        if (value != 0) return value;
        value = a.y.CompareTo(b.y);
        return value != 0 ? value : a.z.CompareTo(b.z);
    }
}
