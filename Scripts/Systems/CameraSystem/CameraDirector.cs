using System;
using UnityEngine;

namespace Shared.CameraSystem
{
    /// <summary>
    /// Две функции активного режима вычисляются каждый кадр. Корень — базовая камера.
    /// Как в InputContext, закрытие владельца закрывает и вложенные режимы.
    /// Класс не зависит от сцены, ввода или Unity lifecycle.
    /// </summary>
    public sealed class CameraDirector : IDisposable
    {
        public sealed class Mode : IDisposable
        {
            private CameraDirector owner;
            internal readonly Func<Vector3> Position, LookAt;
            public Mode Previous { get; internal set; }
            public Mode Next { get; internal set; }
            public bool IsActive => owner != null;

            internal Mode(CameraDirector owner, Func<Vector3> position, Func<Vector3> lookAt)
            {
                Position = position ?? throw new ArgumentNullException(nameof(position));
                LookAt = lookAt ?? throw new ArgumentNullException(nameof(lookAt));
                this.owner = owner;
            }
            public void Dispose() => owner?.Remove(this);
            internal void Detach() { owner = null; Previous = Next = null; }
        }

        public Mode Root { get; }
        public Mode Current { get; private set; }
        public bool IsDefault => Current == Root;

        public CameraDirector(Func<Vector3> position, Func<Vector3> lookAt)
        {
            Root = Current = new Mode(this, position, lookAt);
        }

        public Mode Enter(Func<Vector3> position, Func<Vector3> lookAt)
        {
            CheckAlive();
            var mode = new Mode(this, position, lookAt) { Previous = Current };
            Current.Next = mode;
            Current = mode;
            return mode;
        }

        public void Evaluate(out Vector3 position, out Vector3 lookAt)
        {
            CheckAlive();
            // Один и тот же узел для обеих функций даже если callback сменит режим.
            var mode = Current;
            position = mode.Position();
            lookAt = mode.LookAt();
        }

        public bool Pop()
        {
            if (Current == null || IsDefault) return false;
            Remove(Current);
            return true;
        }
        public void Reset() { if (Root.Next != null) Remove(Root.Next); }

        private void Remove(Mode mode)
        {
            if (mode == Root) return; // Корень нельзя снять обычным Dispose/Pop.
            var previous = mode.Previous;
            while (Current != previous)
            {
                var closing = Current;
                Current = closing.Previous;
                closing.Detach();
            }
            previous.Next = null;
        }

        private void CheckAlive()
        {
            if (Current == null) throw new ObjectDisposedException(nameof(CameraDirector));
        }

        public void Dispose()
        {
            if (Current == null) return;
            Reset();
            Root.Detach();
            Current = null;
        }
    }
}
