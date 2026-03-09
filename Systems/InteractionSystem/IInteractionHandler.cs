namespace Assets.Shared.Systems.InteractionSystem 
{
    public interface IInteractionHandler
    { 
        string Name { get => GetType().Name; }
    }

    public interface IInteractionHandler<TInteraction> : IInteractionHandler
    where TInteraction : IInteraction
    {
        bool HandleInteraction(TInteraction interaction);
    }
}
