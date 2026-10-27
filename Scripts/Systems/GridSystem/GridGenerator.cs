using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Строит статическую сетку по коллайдерам. Сначала проверяет конфигурацию и границы,
/// затем заменяет ячейки. Промежуточные raycast-буферы переиспользуются между колонками.
/// </summary>
public class GridGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform targetsContainer;
    [SerializeField] private GridProvider gridHolder;
    [SerializeField] private GridCell cellPrefab;
    [SerializeField] private Transform cellsContainer;

    [Header("Settings")]
    [SerializeField] private float heightOffset = 0.05f;
    [Min(0)] [SerializeField] private float verticalMergeThreshold = 1.5f;
    [Min(0)] [SerializeField] private float neighborHeightThreshold = 1.5f;
    [SerializeField] private LayerMask scanLayerMask = ~0;
    [Range(0, 90)] [SerializeField] private float maxSlopeAngle = 45f;
    [Min(0)] [SerializeField] private float minClearanceHeight = 2.0f;
    [Min(0)] [SerializeField] private float clearanceCheckRadius = 0.1f;
    [Min(0)] [SerializeField] private float clearanceRayOffset = 0.1f;
    [SerializeField] private LayerMask obstacleLayerMask = 0;
    [Min(1)] [SerializeField] private int maxScanColumns = 1000000;

    private readonly Dictionary<Vector3Int, GridCell> generatedCells = new();
    private readonly Dictionary<Vector2Int, List<GridCell>> columns = new();
    private readonly HashSet<Collider> targetColliders = new();
    private readonly List<GridCell> neighborBuffer = new(4);
    private RaycastHit[] hitBuffer = new RaycastHit[16];
    private static readonly IComparer<RaycastHit> HighestFirst =
        Comparer<RaycastHit>.Create((a, b) => b.point.y.CompareTo(a.point.y));

    public void GenerateGrid()
    {
        if (!ValidateReferences()) return;
        // bounds и физика должны видеть позиции после изменений Transform в этом же кадре.
        Physics.SyncTransforms();
        if (!CollectTargets(out var bounds))
        {
            Debug.LogWarning("Grid: no active non-trigger colliders in TargetsContainer matching ScanLayerMask. Existing grid retained.", this);
            return;
        }
        var min = gridHolder.WorldToGridPos(bounds.min);
        var max = gridHolder.WorldToGridPos(bounds.max);
        double columnCount = ((long)max.x - min.x + 1d) * ((long)max.z - min.z + 1d);
        if (min.x == int.MinValue || min.z == int.MinValue || max.x == int.MaxValue || max.z == int.MaxValue ||
            min.x > max.x || min.z > max.z || columnCount > maxScanColumns)
        {
            targetColliders.Clear();
            Debug.LogError("Grid: scan area exceeds MaxScanColumns or supported coordinates. Existing grid retained.", this);
            return;
        }
        float rayStartY = bounds.max.y + 10f;
        float distance = rayStartY - bounds.min.y + 10f;
        float slopeCosine = Mathf.Cos(maxSlopeAngle * Mathf.Deg2Rad);
        float mergeDistance = gridHolder.CellSize * verticalMergeThreshold;
        int obstacleMask = obstacleLayerMask.value != 0 ? obstacleLayerMask.value : scanLayerMask.value;
#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        if (!Application.isPlaying) Undo.SetCurrentGroupName("Generate Grid");
#endif
        ClearExisting();
        try
        {
            for (int x = min.x; x <= max.x; x++)
                for (int z = min.z; z <= max.z; z++)
                {
                    var origin = gridHolder.GridToWorldPos(new Vector3Int(x, 0, z), rayStartY);
                    int count = ScanColumn(origin, distance);
                    Array.Sort(hitBuffer, 0, count, HighestFirst);
                    float lastY = float.PositiveInfinity;
                    for (int i = 0; i < count; i++)
                    {
                        var hit = hitBuffer[i];
                        if (!targetColliders.Contains(hit.collider) || hit.normal.y < slopeCosine) continue;
                        float y = hit.point.y + heightOffset;
                        if (lastY - y < mergeDistance || !ValidateClearance(hit, obstacleMask)) continue;
                        var world = gridHolder.GridToWorldPos(new Vector3Int(x, 0, z), y);
                        var position = gridHolder.WorldToGridPos(world);
                        // Несколько поверхностей могут округлиться в один ключ даже при mergeThreshold = 0.
                        if (generatedCells.ContainsKey(position)) continue;
                        var cell = Instantiate(cellPrefab, world, Quaternion.FromToRotation(Vector3.up, hit.normal), cellsContainer);
                        cell.Initialize(position, y, cell.transform.rotation, true);
#if UNITY_EDITOR
                        if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(cell.gameObject, "Create Grid Cell");
#endif
                        generatedCells.Add(position, cell);
                        var xz = new Vector2Int(x, z);
                        if (!columns.TryGetValue(xz, out var column)) columns.Add(xz, column = new List<GridCell>());
                        column.Add(cell);
                        gridHolder.RegisterCell(cell);
                        lastY = y;
                    }
                }
            SetAllNeighbors();
        }
        finally
        {
            generatedCells.Clear();
            columns.Clear();
            targetColliders.Clear();
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.CollapseUndoOperations(undoGroup);
#endif
        }
    }

    private int ScanColumn(Vector3 origin, float distance)
    {
        // NonAlloc не сообщает об обрезании результата: заполненный буфер надо увеличить и повторить запрос.
        while (true)
        {
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, distance, scanLayerMask, QueryTriggerInteraction.Ignore);
            if (count < hitBuffer.Length) return count;
            Array.Resize(ref hitBuffer, checked(hitBuffer.Length * 2));
        }
    }

    private bool CollectTargets(out Bounds bounds)
    {
        targetColliders.Clear();
        bounds = default;
        foreach (var collider in targetsContainer.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger || (scanLayerMask.value & (1 << collider.gameObject.layer)) == 0) continue;
            if (targetColliders.Count == 0) bounds = collider.bounds;
            else bounds.Encapsulate(collider.bounds);
            targetColliders.Add(collider);
        }
        return targetColliders.Count != 0;
    }

    private bool ValidateClearance(RaycastHit hit, int obstacleMask)
    {
        if (minClearanceHeight <= 0) return true;
        var origin = hit.point + Vector3.up * clearanceRayOffset;
        bool blocked = clearanceCheckRadius > 0
            ? Physics.SphereCast(origin, clearanceCheckRadius, Vector3.up, out var obstruction,
                minClearanceHeight, obstacleMask, QueryTriggerInteraction.Ignore)
            : Physics.Raycast(origin, Vector3.up, out obstruction, minClearanceHeight, obstacleMask, QueryTriggerInteraction.Ignore);
        return !blocked || obstruction.collider == hit.collider;
    }

    private void SetAllNeighbors()
    {
        foreach (var cell in generatedCells.Values)
        {
            var xz = new Vector2Int(cell.GridPosition.x, cell.GridPosition.z);
            neighborBuffer.Clear();
            AddNeighbor(xz + Vector2Int.up, cell.WorldY);
            AddNeighbor(xz + Vector2Int.down, cell.WorldY);
            AddNeighbor(xz + Vector2Int.left, cell.WorldY);
            AddNeighbor(xz + Vector2Int.right, cell.WorldY);
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RecordObject(cell, "Connect Grid Cell");
#endif
            cell.SetNeighbors(neighborBuffer);
        }
    }

    private void AddNeighbor(Vector2Int xz, float y)
    {
        if (!columns.TryGetValue(xz, out var column)) return;
        GridCell best = null;
        float bestDifference = float.PositiveInfinity;
        float limit = gridHolder.CellSize * neighborHeightThreshold;
        foreach (var cell in column)
        {
            float difference = Mathf.Abs(cell.WorldY - y);
            if (difference <= limit && difference < bestDifference) { best = cell; bestDifference = difference; }
        }
        if (best != null) neighborBuffer.Add(best);
    }

    /// <summary>Удаляет только GridCell в выделенном контейнере. В Play выключает их до отложенного Destroy.</summary>
    public void ClearExisting()
    {
        if (gridHolder == null) { Debug.LogError("GridHolder not assigned!", this); return; }
        gridHolder.ClearAllCells();
        if (cellsContainer == null) return;
        var existing = cellsContainer.GetComponentsInChildren<GridCell>(true);
        foreach (var cell in existing)
        {
            if (cell == null || cell.transform == cellsContainer) continue;
            // Оставляем вспомогательные дочерние объекты контейнера; префаб-источник должен лежать отдельно.
            if (Application.isPlaying)
            {
                cell.gameObject.SetActive(false);
                Destroy(cell.gameObject);
            }
            else
            {
#if UNITY_EDITOR
                Undo.DestroyObjectImmediate(cell.gameObject);
#else
                Destroy(cell.gameObject);
#endif
            }
        }
        generatedCells.Clear();
        columns.Clear();
    }

    private bool ValidateReferences()
    {
        if (targetsContainer == null || gridHolder == null || cellPrefab == null || cellsContainer == null)
        {
            Debug.LogError("Grid: assign TargetsContainer, GridHolder, CellPrefab and CellsContainer.", this);
            return false;
        }
        if (!(gridHolder.CellSize > 0) || float.IsInfinity(gridHolder.CellSize) || maxScanColumns < 1 ||
            !Finite(heightOffset) || !NonNegative(verticalMergeThreshold) || !NonNegative(neighborHeightThreshold) ||
            !NonNegative(minClearanceHeight) || !NonNegative(clearanceCheckRadius) ||
            !NonNegative(clearanceRayOffset) || !Finite(maxSlopeAngle) || maxSlopeAngle < 0 || maxSlopeAngle > 90)
        {
            Debug.LogError("Grid: invalid cell size, slope or distance settings.", this);
            return false;
        }
        if (cellPrefab.transform.IsChildOf(cellsContainer) || cellsContainer.IsChildOf(targetsContainer) ||
            targetsContainer.IsChildOf(cellsContainer))
        {
            Debug.LogError("Grid: use separate target/cell hierarchies and keep CellPrefab outside CellsContainer.", this);
            return false;
        }
        return true;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool NonNegative(float value) => value >= 0 && Finite(value);
}
