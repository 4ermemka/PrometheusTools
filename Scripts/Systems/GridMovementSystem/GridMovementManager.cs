using UnityEngine;

public class GridMovementManager : Manager
{
    private IGridProvider _gridProvider;

    public override void Init()
    {
        _gridProvider = ManagersHolder.Instance.GetManager<GridProvider>();
        base.Init();
    }

    public void MoveTo(Transform target, IGridCell cell)
    {
        target.position = cell.GetWorldPosition(_gridProvider.CellSize);
    }
}
