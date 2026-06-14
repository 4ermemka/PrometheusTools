using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Selectable))]
public class GridCell : MonoBehaviour, IGridCell, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    // Собственные события, которые пробрасывают холдеру
    public Action<IGridCell> OnCellPointerEnterEvent { get; set; }
    public Action<IGridCell> OnCellPointerExitEvent { get; set; }
    public Action<IGridCell> OnCellPointerClickEvent { get; set; }

    public void OnPointerEnter(PointerEventData eventData) => OnCellPointerEnterEvent?.Invoke(this);
    public void OnPointerExit(PointerEventData eventData) => OnCellPointerExitEvent?.Invoke(this);
    public void OnPointerClick(PointerEventData eventData) => OnCellPointerClickEvent?.Invoke(this);

    [SerializeField] private Vector3Int gridPosition;
    [SerializeField] private float worldY;
    [SerializeField] private Quaternion rotation = Quaternion.identity;
    [SerializeField] private bool isValid;

    // Единый список соседей (заполняется через SetNeighbors)
    [SerializeField] private List<GridCell> neighbours = new();

    public Vector3Int GridPosition => gridPosition;
    public float WorldY => worldY;
    public Quaternion Rotation => rotation;
    public bool IsValid => isValid;

    public IEnumerable<IGridCell> Neighbors => neighbours;

    public ISelectable Selection => GetComponent<Selectable>();

    public Vector3 GetWorldPosition(float cellSize) =>
        new Vector3(gridPosition.x * cellSize, worldY, gridPosition.z * cellSize);

    public void Initialize(Vector3Int pos, float y, Quaternion rot, bool valid)
    {
        gridPosition = pos;
        worldY = y;
        rotation = rot;
        isValid = valid;
        name = $"Cell_{pos.x}_{pos.y}_{pos.z}";
        transform.rotation = rot;
    }

    // Новый метод: принимает список соседей (заменил старый SetNeighbors с 4 параметрами)
    public void SetNeighbors(List<GridCell> newNeighbours)
    {
        neighbours.Clear();
        if (newNeighbours != null)
            neighbours.AddRange(newNeighbours);
    }
}