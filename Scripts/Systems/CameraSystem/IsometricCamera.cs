using UnityEngine;

namespace Shared.CameraSystem
{
    /// <summary>Базовые функции камеры: круг вокруг одного фокуса, неизменные дистанция и наклон.</summary>
    [DisallowMultipleComponent]
    public sealed class IsometricCamera : MonoBehaviour
    {
        public Transform Focus;
        [Tooltip("Ориентация сетки. Если не задана, используются мировые X/Z.")]
        public Transform GridFrame;
        public Vector3 FocusOffset = new Vector3(0, 0.5f, 0);
        [Min(0.1f)] public float Distance = 12;
        [Range(1, 89)] public float Elevation = 40;
        public float Angle = 45;
        [Min(0)] public float OrbitSpeed = 60;
        private float orbitInput;

        public Vector3 GetLookAt() => (Focus != null ? Focus.position : Vector3.zero) + FocusOffset;

        public Vector3 GetPosition()
        {
            float yaw = Angle + (GridFrame != null ? GridFrame.eulerAngles.y : 0);
            return GetLookAt() + Quaternion.Euler(Mathf.Clamp(Elevation, 1, 89), yaw, 0)
                * Vector3.back * Mathf.Max(0.1f, Distance);
        }

        /// <summary>Вызывается действием Hold каждый кадр; после отпускания вращение сразу прекращается.</summary>
        public void Orbit(float direction) => orbitInput += Mathf.Clamp(direction, -1, 1);
        public void Tick(float deltaTime, bool active = true)
        {
            if (active)
                Angle = Mathf.Repeat(Angle + Mathf.Clamp(orbitInput, -1, 1) * Mathf.Max(0, OrbitSpeed) * Mathf.Max(0, deltaTime), 360);
            // Накопленный ввод не переносится через катсцену/диалог.
            ClearInput();
        }
        public void ClearInput() => orbitInput = 0;
        private void OnDisable() => ClearInput();
    }
}
