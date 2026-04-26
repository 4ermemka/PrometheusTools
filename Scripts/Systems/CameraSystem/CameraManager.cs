using System;
using System.Net;
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
    [SerializeField] private float ZoomSpeed = 4f;          // скорость сглаживания зума

    [Header("Вращение")]
    [SerializeField] private float RotationSpeed = 120f;    // градусов в секунду

    [Header("Следование за мышью")]
    [SerializeField] private float CameraFollowSpeed = 15f; // 0 = мгновенно (старое поведение)

    // --- Приватные переменные ---
    private float _zoomAmount = 3f;          // текущее значение зума (1..3)
    private float _targetZoomAmount = 3f;    // целевой зум
    private float _targetYAngle;             // целевой угол для RotationTransform
    private Vector3 _targetLocalPosition;    // целевая локальная позиция камеры

    private void Start()
    {
        // Инициализируем текущие и целевые значения
        _zoomAmount = 3f;
        _targetZoomAmount = _zoomAmount;
        _targetYAngle = RotationTransform.eulerAngles.y;
        _targetLocalPosition = CameraTransform.localPosition;
        ApplyZoom(); // установить начальную позицию OffsetTransform
    }

    private void Update()
    {
        // 1. Плавный зум
        if (!Mathf.Approximately(_zoomAmount, _targetZoomAmount))
        {
            _zoomAmount = Mathf.MoveTowards(_zoomAmount, _targetZoomAmount, ZoomSpeed * Time.deltaTime);
            ApplyZoom();
        }

        // 2. Плавное вращение RotationTransform
        float currentYAngle = RotationTransform.eulerAngles.y;
        float newYAngle = Mathf.MoveTowardsAngle(currentYAngle, _targetYAngle, RotationSpeed * Time.deltaTime);
        RotationTransform.rotation = Quaternion.Euler(0f, newYAngle, 0f);

        // 3. Плавное движение камеры за мышью
        RotationTransform.position = FocusPoint.position; // центр всегда на фокусе

        Vector3 camForward = CameraTransform.forward;
        float fixedDepth = Vector3.Dot(FocusPoint.position - CameraTransform.position, camForward);

        Plane plane = new Plane(camForward, FocusPoint.position);
        Ray ray = Camera.ScreenPointToRay(Input.mousePosition);

        Quaternion targetWorldRot = Quaternion.LookRotation(FocusPoint.position - OffsetTransform.position, Vector3.up);
        OffsetTransform.localRotation = Quaternion.Inverse(RotationTransform.rotation) * targetWorldRot;

        if (plane.Raycast(ray, out float enter))
        {
            Vector3 mouseWorld = ray.GetPoint(enter);
            Vector3 midPoint = (FocusPoint.position + mouseWorld) * 0.5f;
            Vector3 desiredWorldPos = midPoint - camForward * fixedDepth;

            // Преобразуем в локальное смещение
            Vector3 desiredLocalOffset = Quaternion.Inverse(OffsetTransform.rotation) *
                                         (desiredWorldPos - OffsetTransform.position);
            desiredLocalOffset.z = 0f;
            _targetLocalPosition = desiredLocalOffset * Multiplier;
        }

        // Интерполяция позиции камеры
        if (CameraFollowSpeed > 0f)
            CameraTransform.localPosition = Vector3.Lerp(CameraTransform.localPosition, _targetLocalPosition,
                                                         CameraFollowSpeed * Time.deltaTime);
        else
            CameraTransform.localPosition = _targetLocalPosition;

        // Камера всегда смотрит на среднюю точку (можно тоже сгладить при желании)
        // Но для стабильности оставим мгновенный LookAt, чтобы не было "плывущего" изображения.
        // Если нужна плавность и тут, добавьте Quaternion.Slerp для CameraTransform.rotation.
    }

    // ---- Публичные методы (вызываются из InputManager) ----

    public void SetFocusPoint(Transform newFocusPoint)
    {
        FocusPoint = newFocusPoint;
    }

    public void ZoomIn()
    {
        _targetZoomAmount = Mathf.Clamp(_targetZoomAmount - ZoomStep, 1f, 3f);
    }

    public void ZoomOut()
    {
        _targetZoomAmount = Mathf.Clamp(_targetZoomAmount + ZoomStep, 1f, 3f);
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

    // Вспомогательная функция: применяет текущий зум к OffsetTransform
    private void ApplyZoom()
    {
        float z = _zoomAmount;
        OffsetTransform.localPosition = new Vector3(5f * z, FocusPoint.position.y + 1.5f * (z * z), -5f * z);
    }
}