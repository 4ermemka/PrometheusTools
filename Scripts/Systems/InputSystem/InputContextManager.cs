using System;
using Shared.Inputs;

/// <summary>Unity-владелец корня. Алгоритм цепочки находится в тестируемом InputContextChain.</summary>
public class InputContextManager : Manager
{
    private InputContextChain chain;
    public InputContext CurrentContext => chain?.Current;
    public InputContextChain Chain => chain;
    public void SetRootContext(InputContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        var source = context.ActionSource ?? InputManager.Instance;
        var next = (source as IContextInputSource)?.Contexts ?? new InputContextChain(source);
        next.SetRoot(context);
        var previous = chain;
        chain = next;
        if (previous != null && previous != next) previous.Dispose();
    }
    public IDisposable AddContext(InputContext context)
    {
        if (chain == null) throw new InvalidOperationException("SetRootContext must be called first.");
        return chain.Enter(context);
    }
    public bool EscapeContext() => chain != null && chain.Pop();
    protected virtual void OnDestroy() => chain?.Dispose();
}

