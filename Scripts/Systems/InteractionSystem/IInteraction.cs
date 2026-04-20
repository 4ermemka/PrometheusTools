namespace Assets.Shared.Systems.InteractionSystem
{
    /// <summary>
    /// Интерация
    /// </summary>
    public interface IInteraction
    {
        (bool isCorrect, string description) Execute();

    }

    public interface IInteraction<SourceT, TargetT> : IInteraction
        where SourceT : IInteractionSource
        where TargetT : IInteractionHandler
    {
        public SourceT Source { get; set; }
        public TargetT Target { get; set; }
    }
}