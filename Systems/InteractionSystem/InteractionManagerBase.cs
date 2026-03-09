using Shared.Tools;
using UnityEngine;

namespace Assets.Shared.Systems.InteractionSystem
{
    public class InteractionManagerBase
    {
        private AutoConsumingQueue<IInteraction> _handlingQueue;

        public InteractionManagerBase() 
        { 
            _handlingQueue = new AutoConsumingQueue<IInteraction>(HandleElement);
        }

        public void AddInteraction(IInteraction interaction)
        { 
            _handlingQueue.Enqueue(interaction);
        }

        private void HandleElement(IInteraction interaction)
        {
            (bool result, string description) = interaction.Execute();
            if (!result)
            {
                Debug.LogError($"[{this.GetType().Name}] Handling interaction [{interaction.ToString()}] error, description: {description}");
            }
        }
    }
}