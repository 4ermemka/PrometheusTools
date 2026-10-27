using System;
using System.Collections.Generic;

namespace Shared.Inputs
{
    /// <summary>
    /// Общая политика Editor и runtime: последнее назначение получает control,
    /// прежние владельцы получают None. Сначала меняются ВСЕ записи, затем идёт событие.
    /// </summary>
    public sealed class BindingTable
    {
        private readonly Dictionary<string, InputBinding> bindings = new(StringComparer.Ordinal);
        public event Action Changed;
        public InputBinding this[string id] => bindings[id];
        public IReadOnlyDictionary<string, InputBinding> Bindings => bindings;

        public BindingTable(IEnumerable<GameActionDefinition> definitions)
        {
            foreach (var definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || bindings.ContainsKey(definition.Id))
                    throw new ArgumentException("У каждого действия должен быть уникальный непустой ID.");
                bindings.Add(definition.Id, default);
                Rebind(definition.Id, definition.DefaultBinding);
            }
        }

        public void Rebind(string id, InputBinding binding)
        {
            if (!bindings.ContainsKey(id)) throw new KeyNotFoundException($"Неизвестное действие '{id}'.");
            if (!binding.IsValid) throw new ArgumentException("Неподдерживаемая привязка.", nameof(binding));
            if (bindings[id] == binding) return;
            if (!binding.IsEmpty)
            {
                string owner = null;
                foreach (var pair in bindings)
                    if (pair.Key != id && pair.Value == binding) { owner = pair.Key; break; }
                if (owner != null) bindings[owner] = default;
            }
            bindings[id] = binding;
            Changed?.Invoke();
        }

        public void ReplaceWith(BindingTable defaults)
        {
            if (defaults.bindings.Count != bindings.Count) throw new ArgumentException("Каталоги действий не совпадают.");
            foreach (string id in bindings.Keys)
                if (!defaults.bindings.ContainsKey(id)) throw new ArgumentException("Каталоги действий не совпадают.");
            foreach (var pair in defaults.bindings) bindings[pair.Key] = pair.Value;
            Changed?.Invoke();
        }
    }
}
