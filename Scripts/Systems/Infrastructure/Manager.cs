using UnityEngine;

public class Manager : MonoBehaviour
{
    public string Name {  get; private set; }

    public virtual void Init()
    {
        Name = $"[{name}][{GetType().Name}]";
        Debug.Log($"{Name} Запущен!");
    }
}
