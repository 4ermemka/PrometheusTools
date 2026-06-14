using UnityEngine;

[RequireComponent(typeof(Transform))]
public class CameraManager : Manager
{
    private Transform RotationTransform => GetComponent<Transform>();

    [Header("Основные ссылки")]
    [SerializeField] private Transform FocusPoint;
    [SerializeField] private Transform OffsetTransform;
    [SerializeField] private Transform CameraTransform;
    [SerializeField] private Camera Camera;

    [Header("Чувствительность мыши")]
    [SerializeField, Range(0f, 1f)] private float Multiplier = 1f;

    [Header("Зум")]
    [SerializeField, Range(0f, 1f)] private float ZoomStep = 0.1f;
    [SerializeField] private float ZoomSpeed = 4f;
    [SerializeField] private float minZoom = 1f;          // <-- новый параметр
    [SerializeField] private float maxZoom = 3f;          // <-- новый параметр
    [SerializeField, Range(1f, 3f)] private float _zoomAmount;

    [Header("Вращение")]
    [SerializeField] private float RotationSpeed = 120f;

    [Header("Следование за мышью")]
    [SerializeField] private float CameraFollowSpeed = 15f;

    [Header("Ортографическая камера")]
    [SerializeField] private bool useOrthographic = true;
    [SerializeField] private float minOrthoSize = 2f;     // максимальное приближение (зум 1)
    [SerializeField] private float maxOrthoSize = 8f;     // максимальный обзор (зум 3)

    [Header("Дуга камеры")]
    [SerializeField] private float arcRadius = 15f;
    [SerializeField, Range(0f, 90f)] private float maxTiltAngle = 75f;

    // Внутренние переменные
    private float _targetZoomAmount;
    private float _targetYAngle;
    private Vector3 _targetLocalPosition;

    private void Start()
    {
        // Инициализируем текущее значение зума
        _zoomAmount = maxZoom;
        _targetZoomAmount = _zoomAmount;
        _targetYAngle = RotationTransform.eulerAngles.y;
        _targetLocalPosition = CameraTransform.localPosition;

        ApplyCameraMode();
        ApplyZoom();
    }

    private void ApplyCameraMode()
    {
        if (Camera != null)
            Camera.orthographic = useOrthographic;
    }

    private void OnValidate()
    {
        // Реакция на изменения в инспекторе
        if (Camera == null)
            Camera = GetComponent<Camera>();
        ApplyCameraMode();
        ApplyZoom();
    }

    private void Update()
    {
        // Плавный зум
        if (!Mathf.Approximately(_zoomAmount, _targetZoomAmount))
        {
            _zoomAmount = Mathf.MoveTowards(_zoomAmount, _targetZoomAmount, ZoomSpeed * Time.deltaTime);
            ApplyZoom();
        }

        // Плавное вращение
        float currentYAngle = RotationTransform.eulerAngles.y;
        float newYAngle = Mathf.MoveTowardsAngle(currentYAngle, _targetYAngle, RotationSpeed * Time.deltaTime);
        RotationTransform.rotation = Quaternion.Euler(0f, newYAngle, 0f);
        RotationTransform.position = FocusPoint.position;

        // Следование за мышью
        Vector3 camForward = CameraTransform.forward;
        float fixedDepth = Vector3.Dot(FocusPoint.position - CameraTransform.position, camForward);
        Plane plane = new Plane(camForward, FocusPoint.position);
        Ray ray = Camera.ScreenPointToRay(Input.mousePosition);
        if (plane.Raycast(ray, out float enter))
        {
            Vector3 mouseWorld = ray.GetPoint(enter);
            Vector3 midPoint = (FocusPoint.position + mouseWorld) * 0.5f;
            Vector3 desiredWorldPos = midPoint - camForward * fixedDepth;
            Vector3 desiredLocalOffset = Quaternion.Inverse(OffsetTransform.rotation) *
                                         (desiredWorldPos - OffsetTransform.position);
            desiredLocalOffset.z = 0f;
            _targetLocalPosition = desiredLocalOffset * Multiplier;
        }

        if (CameraFollowSpeed > 0f)
            CameraTransform.localPosition = Vector3.Lerp(CameraTransform.localPosition, _targetLocalPosition,
                                                         CameraFollowSpeed * Time.deltaTime);
        else
            CameraTransform.localPosition = _targetLocalPosition;
    }

    private void ApplyZoom()
    {
        float t = 1f - Mathf.InverseLerp(minZoom, maxZoom, _zoomAmount);
        float tiltAngle = Mathf.Lerp(0f, maxTiltAngle * Mathf.Deg2Rad, t);

        // Позиция на дуге
        float y = arcRadius * Mathf.Cos(tiltAngle);
        float zOffset = -arcRadius * Mathf.Sin(tiltAngle);
        OffsetTransform.localPosition = new Vector3(0f, y, zOffset);

        // Наклон камеры: при tiltAngle = 0 смотрим вертикально вниз (pitch = 90°),
        // при tiltAngle = maxTiltAngle — почти горизонтально (pitch = 90° - maxTiltAngle°)
        float pitch = 90f - tiltAngle * Mathf.Rad2Deg;
        OffsetTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // Размер orthographicSize
        if (Camera != null && Camera.orthographic)
        {
            Camera.orthographicSize = Mathf.Lerp(maxOrthoSize, minOrthoSize, t);
        }
    }

    public void ZoomOut()
    {
        _targetZoomAmount = Mathf.Clamp(_targetZoomAmount - ZoomStep, minZoom, maxZoom);
    }

    public void ZoomIn()
    {
        _targetZoomAmount = Mathf.Clamp(_targetZoomAmount + ZoomStep, minZoom, maxZoom);
    }

    public void RotateLeft(int amount)
    {
        _targetYAngle += amount;
        _targetYAngle = Mathf.Repeat(_targetYAngle, 360f);
    }

    public void RotateRight(int amount)
    {
        _targetYAngle -= amount;
        _targetYAngle = Mathf.Repeat(_targetYAngle, 360f);
    }

    public void SetFocusPoint(Transform newFocusPoint)
    {
        FocusPoint = newFocusPoint;
    }
}