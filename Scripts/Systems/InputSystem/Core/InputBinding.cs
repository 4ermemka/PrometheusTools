using System;
using UnityEngine;

namespace Shared.Inputs
{
    public enum InputControlKind { Key, WheelUp, WheelDown }
    public enum ActionTrigger { Press, Hold }

    /// <summary>Только физический control. Режим действия не входит в привязку.</summary>
    [Serializable]
    public struct InputBinding : IEquatable<InputBinding>
    {
        [SerializeField] private InputControlKind kind;
        [SerializeField] private KeyCode key;
        public InputControlKind Kind => kind;
        public KeyCode Key => key;
        public bool IsEmpty => kind == InputControlKind.Key && key == KeyCode.None;
        public bool IsValid => Enum.IsDefined(typeof(InputControlKind), kind) &&
            (kind != InputControlKind.Key || Enum.IsDefined(typeof(KeyCode), key) && (int)key < (int)KeyCode.JoystickButton0);
        public bool IsMouseButton => kind == InputControlKind.Key && key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6;
        public InputBinding(KeyCode key) { kind = InputControlKind.Key; this.key = key; }
        public static InputBinding Wheel(bool up) => new() { kind = up ? InputControlKind.WheelUp : InputControlKind.WheelDown };
        public bool Equals(InputBinding other) => kind == other.kind && (kind != InputControlKind.Key || key == other.key);
        public override bool Equals(object obj) => obj is InputBinding other && Equals(other);
        public override int GetHashCode() => (int)kind * 397 ^ (kind == InputControlKind.Key ? (int)key : 0);
        public static bool operator ==(InputBinding a, InputBinding b) => a.Equals(b);
        public static bool operator !=(InputBinding a, InputBinding b) => !a.Equals(b);
        public override string ToString() => IsEmpty ? "Не назначено" : kind == InputControlKind.WheelUp ? "Колесо ↑" :
            kind == InputControlKind.WheelDown ? "Колесо ↓" : IsMouseButton ? $"Мышь {(int)key - (int)KeyCode.Mouse0 + 1}" : key.ToString();
    }

    public readonly struct InputSample
    {
        public readonly bool Pressed;
        public readonly bool Held;
        public InputSample(bool pressed, bool held) { Pressed = pressed; Held = held; }
    }

    /// <summary>Подменяется в тестах; core не вызывает статический UnityEngine.Input.</summary>
    public interface IInputSource
    {
        InputSample Read(InputBinding binding);
        bool AnyHeld { get; }
        bool TryGetPressed(Func<InputBinding, bool> accept, out InputBinding binding);
    }

    /// <summary>Необязательная точная величина жеста; старые backends сохраняют импульсы ±1.</summary>
    public interface IScrollInputSource
    {
        Vector2 ScrollDelta { get; }
    }
}
