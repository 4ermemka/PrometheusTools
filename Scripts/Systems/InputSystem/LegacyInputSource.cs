using System;
using System.Linq;
using Shared.Inputs;
using UnityEngine;

/// <summary>Unity Input Manager backend. Перечень KeyCode вычисляется один раз, не каждый кадр.</summary>
public sealed class LegacyInputSource : IInputSource, IScrollInputSource
{
    public Vector2 ScrollDelta => Input.mouseScrollDelta;
    private static readonly KeyCode[] Keys = ((KeyCode[])Enum.GetValues(typeof(KeyCode)))
        .Distinct().Where(k => k != KeyCode.None && (int)k < (int)KeyCode.JoystickButton0).OrderBy(k => (int)k).ToArray();
    public bool AnyHeld
    {
        get
        {
            if (Input.mouseScrollDelta.y != 0) return true;
            foreach (var key in Keys) if (Input.GetKey(key)) return true;
            return false;
        }
    }
    public InputSample Read(InputBinding binding)
    {
        if (binding.IsEmpty) return default;
        if (binding.Kind == InputControlKind.Key) return new InputSample(Input.GetKeyDown(binding.Key), Input.GetKey(binding.Key));
        bool pulse = binding.Kind == InputControlKind.WheelUp ? Input.mouseScrollDelta.y > 0 : Input.mouseScrollDelta.y < 0;
        return new InputSample(pulse, pulse);
    }
    public bool TryGetPressed(Func<InputBinding, bool> accept, out InputBinding binding)
    {
        foreach (var key in Keys)
        {
            binding = new InputBinding(key);
            if (Input.GetKeyDown(key) && (accept == null || accept(binding))) return true;
        }
        binding = InputBinding.Wheel(Input.mouseScrollDelta.y > 0);
        if (Input.mouseScrollDelta.y != 0 && (accept == null || accept(binding))) return true;
        binding = default;
        return false;
    }
}
