using Shared.CameraSystem;
using UnityEngine;

/// <summary>Применяет функции CameraDirector в LateUpdate. Геометрия и поведение находятся вне контроллера.</summary>
[DefaultExecutionOrder(200), DisallowMultipleComponent, RequireComponent(typeof(IsometricCamera))]
public class CameraManager : Manager
{
    // Имя поля и GUID скрипта сохранены для Camera и ManagersHolder в существующей сцене.
    [SerializeField] private Camera Camera;
    [SerializeField] private IsometricCamera defaultCamera;
    private CameraDirector director;

    public Camera ControlledCamera => Camera;
    public IsometricCamera DefaultCamera
    {
        get
        {
            if (defaultCamera == null) defaultCamera = GetComponent<IsometricCamera>();
            return defaultCamera;
        }
    }
    public CameraDirector Director => director ?? (director = new CameraDirector(DefaultCamera.GetPosition, DefaultCamera.GetLookAt));

    public void Configure(Camera camera, IsometricCamera behaviour)
    {
        if (camera == null) throw new System.ArgumentNullException(nameof(camera));
        if (behaviour == null) throw new System.ArgumentNullException(nameof(behaviour));
        director?.Dispose();
        director = null;
        Camera = camera;
        defaultCamera = behaviour;
    }

    public void SetFocusPoint(Transform point) => DefaultCamera.Focus = point;
    public void Orbit(float direction)
    {
        if (Director.IsDefault) DefaultCamera.Orbit(direction);
    }

    private void LateUpdate() => Tick(Time.deltaTime);
    public void Tick(float deltaTime)
    {
        if (Camera == null) Camera = GetComponentInChildren<Camera>();
        if (Camera == null) return;
        var activeDirector = Director;
        DefaultCamera.Tick(deltaTime, activeDirector.IsDefault);
        activeDirector.Evaluate(out var position, out var lookAt);
        var direction = lookAt - position;
        // Совпавшие точки допустимы: сохраняем последнюю ориентацию вместо LookRotation(0).
        var rotation = direction.sqrMagnitude > 0.000001f
            ? Quaternion.LookRotation(direction, Vector3.up) : Camera.transform.rotation;
        Camera.transform.SetPositionAndRotation(position, rotation);
    }

    private void OnDisable()
    {
        director?.Reset();
        if (defaultCamera != null) defaultCamera.ClearInput();
    }
    private void OnDestroy() => director?.Dispose();
}
