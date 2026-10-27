using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shared.Inputs
{
    public interface IContextInputSource : IGameActionSource
    {
        InputContextChain Contexts { get; }
    }

    /// <summary>
    /// Двусвязная цепочка без MonoBehaviour и статического ввода.
    /// Удаление владельца снимает всех его потомков; токен относится к узлу, а не к позиции.
    /// </summary>
    public sealed class InputContextChain : IDisposable
    {
        private readonly IGameActionSource source;
        private bool changing;
        public InputContext Root { get; private set; }
        public InputContext Current { get; private set; }
        public long Revision { get; private set; }
        public event Action Changed;
        public event Action<Exception> ObserverFaulted;
        public InputContextChain(IGameActionSource source = null) => this.source = source;

        public IDisposable Enter(InputContext context) => Enter(context, false);
        private IDisposable Enter(InputContext context, bool replaceRoot)
        {
            CheckNew(context);
            if (changing) throw new InvalidOperationException("Cannot change contexts inside lifecycle callbacks.");
            changing = true;
            var previous = replaceRoot ? null : Current;
            try
            {
                // Init до установки ссылок: неудачный вход не отключает рабочий контекст.
                try
                {
                    context.Init();
                    context.ValidateActions(source);
                    context.Owner = this; context.Previous = previous;
                    context.Entered();
                }
                catch
                {
                    context.Owner = null; context.Previous = null;
                    Notify(context.DisposeDetached);
                    throw;
                }
                if (replaceRoot && Root != null) RemoveNodes(Root);
                if (previous != null) previous.Next = context;
                else Root = context;
                Current = context; Revision++;
                if (previous != null) Notify(previous.Covered);
                Notify(context.BecameCurrent);
            }
            finally { changing = false; }
            Notify(Changed);
            return new InputSubscription(() => Remove(context));
        }
        private void CheckNew(InputContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Owner != null || context.IsDisposed)
                throw new InvalidOperationException("Context already belongs to a chain or has been disposed.");
            if (context.ActionSource is IContextInputSource other && !ReferenceEquals(other.Contexts, this))
                throw new InvalidOperationException("Context and chain use different input sources.");
        }
        public void SetRoot(InputContext context)
        {
            Enter(context, true);
        }
        public bool Pop()
        {
            if (Current?.Previous == null) return false;
            return Remove(Current);
        }
        public bool Remove(InputContext context)
        {
            if (context == null || !ReferenceEquals(context.Owner, this)) return false;
            if (changing) throw new InvalidOperationException("Cannot change contexts inside lifecycle callbacks.");
            changing = true;
            try
            {
                RemoveNodes(context);
                if (Current != null) Notify(Current.BecameCurrent);
            }
            finally { changing = false; }
            Notify(Changed);
            return true;
        }
        private void RemoveNodes(InputContext context)
        {
                var closing = new List<InputContext>();
                for (var node = Current; node != context.Previous; node = node.Previous) closing.Add(node);
                Current = context.Previous;
                if (Current == null) Root = null;
                else Current.Next = null;
                // Отсоединяем всё до callbacks: Dispose токена в OnExited безопасен.
                foreach (var node in closing) { node.Owner = null; node.Previous = node.Next = null; }
                Revision++;
                foreach (var node in closing) Notify(node.DisposeDetached);
        }
        public bool DispatchAction(string id) => Dispatch(context => context.TryHandleAction(id));
        public bool DispatchScroll(Vector2 delta) => delta != Vector2.zero && Dispatch(context => context.TryHandleScroll(delta));
        private bool Dispatch(Func<InputContext, bool> handle)
        {
            long revision = Revision;
            for (var node = Current; node != null; node = node.Previous)
            {
                if (handle(node)) return true;
                // Даже callback с false мог сменить цепочку. Старый сигнал не переигрывается.
                if (Revision != revision) return true;
            }
            return false;
        }
        private void Notify(Action callback)
        {
            if (callback == null) return;
            foreach (Action handler in callback.GetInvocationList())
                try { handler(); } catch (Exception error) { try { ObserverFaulted?.Invoke(error); } catch { } }
        }
        public void Dispose() { if (Root != null) Remove(Root); }
    }
}

