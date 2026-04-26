using UnityEngine;

public interface ISelectable
{
    public bool IsSelected(string typeName);
    public void Select(string typeName);
    public void Deselect(string typeName);
}
