using System;
using System.Collections.Generic;
using UnityEngine;

public class GridGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform targetsContainer;
    [SerializeField] private GridHolder gridHolder;
    [SerializeField] private GridCell cellPrefab;
    [SerializeField] private Transform cellsContainer;

    [Header("Settings")]
    [SerializeField] private float heightOffset = 0.05f;
    [SerializeField] private float verticalMergeThreshold = 1.5f;
    [SerializeField] private float neighborHeightThreshold = 1.5f;
    [SerializeField] private LayerMask scanLayerMask = ~0;
    [SerializeField] private float maxSlopeAngle = 45f;
    [SerializeField] private float minClearanceHeight = 2.0f;
    [SerializeField] private float clearanceCheckRadius = 0.1f;
    [SerializeField] private float clearanceRayOffset = 0.1f;
    [SerializeField] private LayerMask obstacleLayerMask = 0;

    private Dictionary<Vector3Int, GridCell> generatedCells = new();
    private Dictionary<Vector2Int, List<RaycastHit>> hitsByXZ = new();
    private Collider[] targetCollidersCache;

    public void GenerateGrid()
    {
        ClearExisting();
        if (!ValidateReferences()) return;

        var bounds = CalculateBounds();
        Vector3Int min = WorldToGridPos(bounds.min);
        Vector3Int max = WorldToGridPos(bounds.max);
        float rayStartY = bounds.max.y + 10f;
        float totalDistance = rayStartY - bounds.min.y + 10f;

        LayerMask obstacleMask = obstacleLayerMask.value != 0 ? obstacleLayerMask : scanLayerMask;
        targetCollidersCache = targetsContainer.GetComponentsInChildren<Collider>();

        RaycastHit[] hitBuffer = new RaycastHit[10];
        for (int x = min.x; x <= max.x; x++)
        {
            for (int z = min.z; z <= max.z; z++)
            {
                Vector3 origin = GridToWorldPos(new Vector3Int(x, 0, z)) + Vector3.up * rayStartY;
                int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, totalDistance, scanLayerMask);
                if (hitCount == 0) continue;

                var validHits = new List<RaycastHit>();
                for (int i = 0; i < hitCount; i++)
                {
                    var hit = hitBuffer[i];
                    if (!IsTargetChild(hit.transform) || AngleTooSteep(hit.normal)) continue;
                    validHits.Add(hit);
                }
                validHits.Sort((a, b) => b.point.y.CompareTo(a.point.y));
                if (validHits.Count > 0)
                    hitsByXZ[new Vector2Int(x, z)] = validHits;
            }
        }

        foreach (var kvp in hitsByXZ)
        {
            Vector2Int xz = kvp.Key;
            var hits = kvp.Value;
            float lastY = float.NegativeInfinity;

            foreach (var hit in hits)
            {
                float y = hit.point.y + heightOffset;
                if (lastY > float.NegativeInfinity && lastY - y < gridHolder.CellSize * verticalMergeThreshold)
                    continue;

                Vector3 worldPos = new Vector3(xz.x * gridHolder.CellSize + transform.position.x, y, xz.y * gridHolder.CellSize + transform.position.z);
                Vector3Int gridPos = WorldToGridPos(worldPos);

                if (!ValidateClearance(hit, obstacleMask)) continue;

                Quaternion rot = Quaternion.FromToRotation(Vector3.up, hit.normal);
                GridCell cell = Instantiate(cellPrefab, cellsContainer);
                cell.transform.position = worldPos;
                cell.Initialize(gridPos, y, rot, true);
                generatedCells[gridPos] = cell;
                gridHolder.RegisterCell(cell);   // <-- РАСКОММЕНТИРОВАНО

                lastY = y;
            }
        }

        SetAllNeighbors();
        hitsByXZ.Clear();
        generatedCells.Clear();
    }

    private bool ValidateClearance(RaycastHit hit, LayerMask obstacleMask)
    {
        Vector3 rayStart = hit.point + Vector3.up * clearanceRayOffset;
        if (Physics.SphereCast(rayStart, clearanceCheckRadius, Vector3.up, out RaycastHit upHit, minClearanceHeight, obstacleMask))
        {
            if (upHit.collider != hit.collider) return false;
        }
        return true;
    }

    private void SetAllNeighbors()
    {
        foreach (var cell in generatedCells.Values)
        {
            Vector2Int xz = new(cell.GridPosition.x, cell.GridPosition.z);
            // Формируем список соседей из четырёх направлений
            List<GridCell> neighbourList = new List<GridCell>(4);
            AddIfNotNull(neighbourList, FindBestNeighbor(xz + Vector2Int.up, cell.WorldY));
            AddIfNotNull(neighbourList, FindBestNeighbor(xz + Vector2Int.down, cell.WorldY));
            AddIfNotNull(neighbourList, FindBestNeighbor(xz + Vector2Int.left, cell.WorldY));
            AddIfNotNull(neighbourList, FindBestNeighbor(xz + Vector2Int.right, cell.WorldY));
            cell.SetNeighbors(neighbourList);
        }
    }

    private void AddIfNotNull(List<GridCell> list, GridCell cell)
    {
        if (cell != null) list.Add(cell);
    }

    private GridCell FindBestNeighbor(Vector2Int xz, float currentY)
    {
        if (!hitsByXZ.TryGetValue(xz, out var hits)) return null;
        GridCell best = null;
        float minDiff = float.MaxValue;
        foreach (var hit in hits)
        {
            Vector3 world = hit.point + Vector3.up * heightOffset;
            Vector3Int pos = WorldToGridPos(world);
            if (generatedCells.TryGetValue(pos, out var cell) && cell.IsValid)
            {
                float diff = Mathf.Abs(cell.WorldY - currentY);
                if (diff <= gridHolder.CellSize * neighborHeightThreshold && diff < minDiff)
                {
                    minDiff = diff;
                    best = cell;
                }
            }
        }
        return best;
    }

    public void ClearExisting()
    {
        gridHolder.ClearAllCells();

        if (cellsContainer != null)
        {
            for (int i = cellsContainer.childCount - 1; i >= 0; i--)
                DestroyImmediate(cellsContainer.GetChild(i).gameObject);
        }
        else
        {
            var existing = gridHolder.GetComponentsInChildren<GridCell>();
            foreach (var cell in existing)
                DestroyImmediate(cell.gameObject);
        }
    }

    private bool ValidateReferences()
    {
        if (targetsContainer == null) Debug.LogError("TargetsContainer not assigned!");
        if (gridHolder == null) Debug.LogError("GridHolder not assigned!");
        if (cellPrefab == null) Debug.LogError("Cell Prefab not assigned!");
        if (cellsContainer == null) Debug.LogError("CellsContainer not assigned!");
        return targetsContainer && gridHolder && cellPrefab && cellsContainer;
    }

    private Bounds CalculateBounds()
    {
        var renderers = targetsContainer.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(targetsContainer.position, Vector3.one * 10f);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    private bool IsTargetChild(Transform t) => t.IsChildOf(targetsContainer);
    private bool AngleTooSteep(Vector3 normal) => Vector3.Angle(normal, Vector3.up) > maxSlopeAngle;

    private Vector3Int WorldToGridPos(Vector3 worldPos) =>
        new Vector3Int(
            Mathf.RoundToInt((worldPos.x - transform.position.x) / gridHolder.CellSize),
            Mathf.RoundToInt(worldPos.y / gridHolder.CellSize),
            Mathf.RoundToInt((worldPos.z - transform.position.z) / gridHolder.CellSize)
        );

    private Vector3 GridToWorldPos(Vector3Int gridPos) =>
        new Vector3(
            gridPos.x * gridHolder.CellSize + transform.position.x,
            0,
            gridPos.z * gridHolder.CellSize + transform.position.z
        );
}