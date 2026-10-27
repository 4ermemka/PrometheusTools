using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ManagersHolder : MonoBehaviour
{
    [SerializeField]
    [InspectorName("Managers")]
    public Manager[] _managers;
    //Список менеджеров
    protected Dictionary<Type, Manager> Managers;
    public static ManagersHolder Instance { get; private set; }

    private void Awake()
    {
        // Камера, сетка и ввод принадлежат загружаемой сцене. Сессия хранится отдельно.
        if (Instance == null)
        {
            Instance = this;
        }

        Managers = _managers.ToDictionary(m => m.GetType());

        foreach (var managerKvp in Managers)
        {
            managerKvp.Value.Init();
        }
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public T GetManager<T>() where T : Manager
    {
        if (Managers.TryGetValue(typeof(T), out var manager))
        {
            return manager as T;
        }
        return null;
    }
}
