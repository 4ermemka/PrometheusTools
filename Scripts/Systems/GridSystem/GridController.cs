using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class GridController : MonoBehaviour, IGridProvider
{
    [Header("References")]
    [SerializeField] private Transform targetsContainer;
    [SerializeField] private Transform cellsContainer;
    [SerializeField] private GridCellView cellPrefab;

    [Header("Grid Settings")]
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private float heightOffset = 0.05f;
    [SerializeField] private Vector2 gridOffset = new Vector2(0.5f, 0.5f);

    [Header("Vertical Rules")]
    [SerializeField] private float verticalMergeThreshold = 1.5f;
    [SerializeField] private float neighborHeightThreshold = 1.5f;

    [Header("Scan Settings")]
    [SerializeField] private LayerMask scanLayerMask = ~0;
    [SerializeField] private float maxSlopeAngle = 45f;

    [Header("Clearance Settings")]
    [SerializeField] private float minClearanceHeight = 2.0f;
    [SerializeField] private float clearanceCheckRadius = 0.1f;
    [SerializeField] private float clearanceRayOffset = 0.1f;
    [SerializeField] private LayerMask obstacleLayerMask = 0;

    [Header("Immersion Check")]
    [SerializeField] private bool checkImmersion = true;
    [SerializeField] private float immersionCheckRadius = 0.4f; // чуть меньше половины cellSize

    [Header("Debug Visualization")]
    [SerializeField] private bool drawCellCenters = true;
    [SerializeField] private bool drawRejectedCells = false;
    [SerializeField] private bool drawConnections = true;
    [SerializeField] private Color connectionColor = Color.yellow;
    [Range(0f, 1f)]
    [SerializeField] private float connectionAlpha = 1f;

    private Dictionary<Vector3Int, GridCellData> grid = new();
    private Dictionary<Vector2Int, List<GridCellData>> cellsByXZ = new();
    private HashSet<Vector3> rejectedPositions = new();

    private float rayStartY;
    private float totalRayDistance;

    private struct HitInfo
    {
        public Vector3 point;
        public Vector3 normal;
        public Collider collider;
    }

    public float CellSize => cellSize;
    public IReadOnlyCollection<GridCellData> AllCells => grid.Values.Where(c => c.isValid).ToList().AsReadOnly();

    // --- IGridProvider ---
    public GridCellData GetCellAt(Vector3 worldPosition)
    {
        Vector3Int gridPos = WorldToGridPosition(worldPosition);
        grid.TryGetValue(gridPos, out GridCellData cell);
        return cell;
    }

    public List<GridCellData> GetCellsInRadius(Vector3 worldCenter, float radius)
    {
        List<GridCellData> result = new();
        Vector3Int centerPos = WorldToGridPosition(worldCenter);
        int intRadius = Mathf.CeilToInt(radius / cellSize);

        for (int x = -intRadius; x <= intRadius; x++)
        {
            for (int z = -intRadius; z <= intRadius; z++)
            {
                if (Mathf.Abs(x) + Mathf.Abs(z) > intRadius) continue;
                Vector2Int checkXZ = new(centerPos.x + x, centerPos.z + z);
                if (cellsByXZ.TryGetValue(checkXZ, out var cells))
                    foreach (var cell in cells)
                        if (cell.isValid) result.Add(cell);
            }
        }
        return result;
    }

    // --- Генерация ---
    public void GenerateGrid()
    {
        ClearGrid();
        rejectedPositions.Clear();

        if (targetsContainer == null || cellPrefab == null)
        {
            Debug.LogError("Missing required references!");
            return;
        }

        if (cellsContainer == null)
        {
            GameObject go = new("CellsContainer");
            go.transform.SetParent(transform);
            cellsContainer = go.transform;
        }

        LayerMask obstacleMask = obstacleLayerMask.value != 0 ? obstacleLayerMask : scanLayerMask;

        Bounds bounds = CalculateTotalBounds();
        Vector3Int min = WorldToGridPosition(bounds.min);
        Vector3Int max = WorldToGridPosition(bounds.max);

        rayStartY = bounds.max.y + 10f;
        totalRayDistance = rayStartY - bounds.min.y + 10f;

        Debug.Log($"Bounds: {bounds.min} - {bounds.max}. Ray start Y: {rayStartY}, total distance: {totalRayDistance}");

        Dictionary<Vector2Int, List<HitInfo>> hitInfosByXZ = new();

        for (int x = min.x; x <= max.x; x++)
        {
            for (int z = min.z; z <= max.z; z++)
            {
                CollectHitInfosAt(x, z, hitInfosByXZ, obstacleMask);
            }
        }

        foreach (var kvp in hitInfosByXZ)
        {
            Vector2Int xz = kvp.Key;
            var hits = kvp.Value;
            hits.Sort((a, b) => b.point.y.CompareTo(a.point.y));

            float lastY = float.NegativeInfinity;
            foreach (var hitInfo in hits)
            {
                float y = hitInfo.point.y + heightOffset;
                Vector3 worldPos = new(
                    xz.x * cellSize + gridOffset.x,
                    y,
                    xz.y * cellSize + gridOffset.y
                );
                Vector3Int gridPos = WorldToGridPosition(worldPos);

                if (lastY != float.NegativeInfinity)
                {
                    float diff = lastY - y;
                    if (diff < cellSize * verticalMergeThreshold)
                        continue;
                }

                if (!IsPositionValid(worldPos, hitInfo, obstacleMask))
                {
                    rejectedPositions.Add(worldPos);
                    continue;
                }

                Quaternion cellRotation = Quaternion.FromToRotation(Vector3.up, hitInfo.normal);

                GridCellData cell = new(gridPos);
                cell.isValid = true;
                cell.worldY = y;
                cell.rotation = cellRotation;
                grid[gridPos] = cell;

                if (!cellsByXZ.ContainsKey(xz))
                    cellsByXZ[xz] = new List<GridCellData>();
                cellsByXZ[xz].Add(cell);

                lastY = y;
            }
        }

        BuildNeighborLinks();
        InstantiateCellViews();

        Debug.Log($"Generated {AllCells.Count} cells. Rejected: {rejectedPositions.Count} positions.");
    }

    private bool IsPositionValid(Vector3 worldPos, HitInfo hitInfo, LayerMask obstacleMask)
    {
        // 1. Проверка свободного пространства над точкой
        Vector3 rayStart = hitInfo.point + Vector3.up * clearanceRayOffset;

        if (clearanceCheckRadius <= 0f)
        {
            if (Physics.Raycast(rayStart, Vector3.up, out RaycastHit upHit, minClearanceHeight, obstacleMask))
            {
                if (upHit.collider != hitInfo.collider)
                    return false;
            }
        }
        else
        {
            if (Physics.SphereCast(rayStart, clearanceCheckRadius, Vector3.up, out RaycastHit sphereHit, minClearanceHeight, obstacleMask))
            {
                if (sphereHit.collider != hitInfo.collider)
                    return false;
            }
        }

        // 2. Проверка, не находится ли точка внутри другого коллайдера (погружение)
        if (checkImmersion)
        {
            Vector3 checkCenter = worldPos + Vector3.up * (cellSize * 0.5f); // центр объёма ячейки
            Collider[] overlaps = Physics.OverlapSphere(checkCenter, immersionCheckRadius, obstacleMask);
            foreach (var col in overlaps)
            {
                if (col != hitInfo.collider)
                    return false;
            }
        }

        return true;
    }

    private void CollectHitInfosAt(int x, int z, Dictionary<Vector2Int, List<HitInfo>> dict, LayerMask obstacleMask)
    {
        float worldX = x * cellSize + gridOffset.x;
        float worldZ = z * cellSize + gridOffset.y;
        Vector3 rayOrigin = new(worldX, rayStartY, worldZ);
        RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, totalRayDistance, scanLayerMask);
        System.Array.Sort(hits, (a, b) => b.point.y.CompareTo(a.point.y));

        List<HitInfo> validHits = new();
        foreach (var hit in hits)
        {
            if (!hit.transform.IsChildOf(targetsContainer))
                continue;
            if (Vector3.Angle(hit.normal, Vector3.up) > maxSlopeAngle)
                continue;

            validHits.Add(new HitInfo
            {
                point = hit.point,
                normal = hit.normal,
                collider = hit.collider
            });
        }

        if (validHits.Count > 0)
            dict[new Vector2Int(x, z)] = validHits;
    }

    private void BuildNeighborLinks()
    {
        foreach (var cell in grid.Values.Where(c => c.isValid))
        {
            Vector2Int xz = new(cell.gridPosition.x, cell.gridPosition.z);

            cell.neighborRight = GetBestNeighborAtXZ(xz + Vector2Int.right, cell.worldY);
            cell.neighborLeft = GetBestNeighborAtXZ(xz + Vector2Int.left, cell.worldY);
            cell.neighborUp = GetBestNeighborAtXZ(xz + Vector2Int.up, cell.worldY);
            cell.neighborDown = GetBestNeighborAtXZ(xz + Vector2Int.down, cell.worldY);
        }
    }

    private GridCellData GetBestNeighborAtXZ(Vector2Int targetXZ, float currentY)
    {
        if (!cellsByXZ.TryGetValue(targetXZ, out var candidates))
            return null;

        GridCellData best = null;
        float minHeightDiff = float.MaxValue;

        foreach (var candidate in candidates)
        {
            if (!candidate.isValid) continue;
            float diff = Mathf.Abs(candidate.worldY - currentY);
            if (diff <= cellSize * neighborHeightThreshold && diff < minHeightDiff)
            {
                minHeightDiff = diff;
                best = candidate;
            }
        }
        return best;
    }

    private void InstantiateCellViews()
    {
        foreach (var cell in grid.Values.Where(c => c.isValid))
        {
            GridCellView view = Instantiate(cellPrefab, cellsContainer);
            view.transform.position = cell.GetWorldPosition(cellSize);
            view.Initialize(cell);
            cell.view = view;
        }
    }

    public void RemoveCellData(GridCellData cell)
    {
        if (cell == null) return;

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Remove Grid Cell");
#endif

        grid.Remove(cell.gridPosition);
        Vector2Int xz = new(cell.gridPosition.x, cell.gridPosition.z);
        if (cellsByXZ.TryGetValue(xz, out var list))
        {
            list.Remove(cell);
            if (list.Count == 0)
                cellsByXZ.Remove(xz);
        }

        foreach (var neighbor in cell.Neighbors.ToList())
        {
            if (neighbor.neighborUp == cell) neighbor.neighborUp = null;
            if (neighbor.neighborDown == cell) neighbor.neighborDown = null;
            if (neighbor.neighborLeft == cell) neighbor.neighborLeft = null;
            if (neighbor.neighborRight == cell) neighbor.neighborRight = null;
        }

        cell.isValid = false;
    }

    public void ClearGrid()
    {
        if (cellsContainer != null)
        {
            var children = new List<GameObject>();
            for (int i = 0; i < cellsContainer.childCount; i++)
                children.Add(cellsContainer.GetChild(i).gameObject);

#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, "Clear Grid");
            foreach (var child in children)
                UnityEditor.Undo.DestroyObjectImmediate(child);
#else
            foreach (var child in children)
                DestroyImmediate(child);
#endif
        }

        grid.Clear();
        cellsByXZ.Clear();
        rejectedPositions.Clear();
    }

    private Bounds CalculateTotalBounds()
    {
        var renderers = targetsContainer.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning("No Renderers found. Using fallback bounds.");
            return new Bounds(targetsContainer.position, Vector3.one * 10f);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private Vector3Int WorldToGridPosition(Vector3 worldPos)
    {
        return new Vector3Int(
            Mathf.RoundToInt((worldPos.x - gridOffset.x) / cellSize),
            Mathf.RoundToInt(worldPos.y / cellSize),
            Mathf.RoundToInt((worldPos.z - gridOffset.y) / cellSize)
        );
    }

    public void DrawEditorConnections()
    {
        if (!drawConnections) return;

        Color col = connectionColor;
        col.a = connectionAlpha;
        Handles.color = col;

        foreach (var cell in grid.Values.Where(c => c.isValid))
        {
            Vector3 center = cell.GetWorldPosition(cellSize);
            DrawConnection(center, cell.neighborRight);
            DrawConnection(center, cell.neighborUp);
            DrawConnection(center, cell.neighborLeft);
            DrawConnection(center, cell.neighborDown);
        }
    }

    private void DrawConnection(Vector3 from, GridCellData to)
    {
        if (to != null && to.isValid)
        {
#if UNITY_EDITOR
            UnityEditor.Handles.DrawLine(from, to.GetWorldPosition(cellSize), 2f);
#endif
        }
    }

    private void OnDrawGizmos()
    {
        if (drawCellCenters)
        {
            Gizmos.color = Color.cyan;
            foreach (var cell in grid.Values.Where(c => c.isValid))
            {
                Gizmos.DrawSphere(cell.GetWorldPosition(cellSize), 0.05f);
            }
        }

        if (drawRejectedCells)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            foreach (var pos in rejectedPositions)
            {
                // Показываем область проверки на погружение (маленькая сфера)
                if (checkImmersion)
                {
                    Vector3 checkCenter = pos + Vector3.up * (cellSize * 0.5f);
                    Gizmos.DrawWireSphere(checkCenter, immersionCheckRadius);
                }

                Vector3 start = pos + Vector3.up * clearanceRayOffset;
                Vector3 end = start + Vector3.up * minClearanceHeight;
                if (clearanceCheckRadius > 0f)
                {
                    DrawWireCylinder(start, end, clearanceCheckRadius);
                }
                else
                {
                    Gizmos.DrawLine(start, end);
                }
            }
        }
    }

    private void DrawWireCylinder(Vector3 start, Vector3 end, float radius)
    {
        Vector3 up = (end - start).normalized;
        float height = Vector3.Distance(start, end);
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up);

        int segments = 16;
        Vector3 prevBottom = start + rotation * new Vector3(radius, 0, 0);
        Vector3 prevTop = end + rotation * new Vector3(radius, 0, 0);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 offset = rotation * new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            Vector3 bottom = start + offset;
            Vector3 top = end + offset;

            Gizmos.DrawLine(prevBottom, bottom);
            Gizmos.DrawLine(prevTop, top);
            Gizmos.DrawLine(bottom, top);

            prevBottom = bottom;
            prevTop = top;
        }
    }
}