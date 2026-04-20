namespace Assets.Shared.Systems.InteractionSystem
{
    public interface IInteractionSource
    {
        string Name { get => GetType().Name; }
    }

    public interface IInteractionSource<TInteraction> : IInteractionSource
    where TInteraction : IInteraction
    {
        bool HandleCallBack(TInteraction interaction);
    }
}