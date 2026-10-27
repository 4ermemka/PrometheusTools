using System;
using UnityEngine;

namespace Shared.Inputs
{
    [Serializable]
    public sealed class GameActionDefinition
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private string category;
        [SerializeField] private ActionTrigger trigger;
        [SerializeField] private InputBinding defaultBinding;
        public string Id => id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        public string Category => category;
        public ActionTrigger Trigger => trigger;
        public InputBinding DefaultBinding => defaultBinding;

        public GameActionDefinition(string id, string displayName, ActionTrigger trigger, InputBinding binding, string category = "")
        {
            this.id = id; this.displayName = displayName; this.trigger = trigger;
            defaultBinding = binding; this.category = category;
        }
        internal void SetDefaultBinding(InputBinding binding) => defaultBinding = binding;
    }
}
