using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Selectable))]
public class GridCell : MonoBehaviour, IGridCell, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    // Провайдер подписывает постоянные методы-посредники, поэтому поздние подписчики тоже получают события.
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

    // Направленный граф; генератор выбирает до одного соседа в каждом из четырёх направлений.
    [SerializeField] private List<GridCell> neighbours = new();

    public Vector3Int GridPosition => gridPosition;
    public float WorldY => worldY;
    public Quaternion Rotation => rotation;
    public bool IsValid => isValid;

    public IEnumerable<IGridCell> Neighbors => neighbours;

    private Selectable selection;
    internal GridProvider Owner { get; set; }
    internal List<GridCell> NeighborCells => neighbours;

    public ISelectable Selection => selection != null ? selection : selection = GetComponent<Selectable>();

    // Сигнатура сохранена для IGridCell. Transform уже учитывает смещение сетки и точную высоту поверхности.
    public Vector3 GetWorldPosition(float cellSize) => transform.position;

    public void Initialize(Vector3Int pos, float y, Quaternion rot, bool valid)
    {
        var owner = Owner;
        if (owner != null) owner.RemoveCell(this);
        gridPosition = pos;
        worldY = y;
        rotation = rot;
        isValid = valid;
        name = $"Cell_{pos.x}_{pos.y}_{pos.z}";
        transform.rotation = rot;
        if (owner != null) owner.RegisterCell(this);
    }

    // Копирует список; временный буфер генератора можно переиспользовать.
    public void SetNeighbors(List<GridCell> newNeighbours)
    {
        if (ReferenceEquals(neighbours, newNeighbours)) return;
        neighbours.Clear();
        if (newNeighbours != null)
            neighbours.AddRange(newNeighbours);
    }

    private void OnDestroy()
    {
        if (Owner != null) Owner.RemoveCell(this);
    }
}
