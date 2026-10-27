#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Shared.SyncSystem.Core;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Core;
using Assets.Shared.Systems.InventorySystem.Items;

namespace Assets.Shared.Systems.InventorySystem.Slots
{
    /// <summary>
    /// Упорядоченная модель: один экземпляр предмета (возможно, стопка) на ячейку, null означает пустую ячейку.
    /// Правила и действия наследуются от InventoryBase. Модель не знает о Unity UI, prefab-ах и рецептах.
    /// Предметы считаются неизменяемыми; все операции и события выполняются синхронно в одном игровом потоке.
    /// </summary>
    public class SlottedInventory<TItem> : InventoryBase<TItem> where TItem : class, IInventoryItem
    {
        // Снимок содержимого для существующего SyncSystem; это ещё не настройка сетевого обмена или сохранения.
        protected readonly Sync<TItem[]> SlotsState = new Sync<TItem[]>();
        private SlotRule<TItem>[] _slotRules;
        private IReadOnlyList<TItem> _view;
        private bool _mutating;
        private bool _assigning;

        public SlottedInventory(int slotCount, string inventoryId = null, string name = null) : base(inventoryId, name)
        {
            if (slotCount < 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
            SlotsState.SetValueSilent(new TItem[slotCount]);
            _slotRules = new SlotRule<TItem>[slotCount];
            _view = Array.AsReadOnly(SlotsState.Value);
        }

        public int SlotCount => SlotsState.Value.Length;
        /// <summary>Версия содержимого/правил/вместимости; станция использует её для проверки актуальности preview.</summary>
        public long Revision { get; private set; }
        public IReadOnlyList<TItem> Slots => _view;
        public TItem this[int index] => SlotsState.Value[index];
        /// <summary>Уведомление после изменения. Обработчики читают состояние; повторная мутация внутри события запрещена.</summary>
        public event Action SlotsChanged;

        public SlotRule<TItem> GetRule(int index)
        {
            if (!InRange(index)) throw new ArgumentOutOfRangeException(nameof(index));
            return _slotRules[index];
        }

        /// <summary>Меняет фильтр одной ячейки, только если уже лежащий предмет ему соответствует. null снимает фильтр.</summary>
        public InventoryValidationResult SetRule(int index, SlotRule<TItem> rule)
        {
            return ExecuteSingle(InventoryActionKind.Update, Array.Empty<TItem>(), () =>
            {
                if (!InRange(index)) return Deny("slot_out_of_range", "The slot does not exist.");
                return rule == null || rule.Accepts(this[index]) ? InventoryValidationResult.Allow()
                    : Deny("slot_rejected", "The occupied slot does not accept its existing item.");
            }, () => { _slotRules[index] = rule; Revision++; }, "SetSlotRule").Validation;
        }

        /// <summary>Меняет вместимость, сохраняя индексы. Не удаляет занятые ячейки при уменьшении.</summary>
        public InventoryOperationResult<TItem> TryResize(int slotCount)
        {
            TItem[] next = null;
            return ExecuteSingle(InventoryActionKind.Update, Array.Empty<TItem>(), () =>
            {
                if (slotCount < 0) return Deny("invalid_capacity", "Slot count must be nonnegative.");
                if (slotCount < SlotCount && Slots.Skip(slotCount).Any(item => item != null))
                    return Deny("occupied_slots", "Resizing would remove an occupied slot.");
                next = new TItem[slotCount];
                Array.Copy(SlotsState.Value, next, Math.Min(slotCount, SlotCount));
                return InventoryValidationResult.Allow();
            }, () => { Array.Resize(ref _slotRules, slotCount); AssignSnapshot(next); }, "ResizeSlots");
        }

        /// <summary>Заменяет вместимость и все фильтры атомарно. Число правил определяет новое число ячеек.</summary>
        public InventoryValidationResult TryConfigure(IReadOnlyList<SlotRule<TItem>> rules)
        {
            SlotRule<TItem>[] nextRules = null;
            TItem[] nextSlots = null;
            return ExecuteSingle(InventoryActionKind.Update, Array.Empty<TItem>(), () =>
            {
                if (rules == null) return Deny("rules_required", "A complete slot rule list is required.");
                // Снимок списка защищает выполняемую операцию от изменения массива вызывающим кодом.
                nextRules ??= rules.ToArray();
                if (nextRules.Length < SlotCount && Slots.Skip(nextRules.Length).Any(item => item != null))
                    return Deny("occupied_slots", "Reconfiguration would remove an occupied slot.");
                nextSlots = new TItem[nextRules.Length];
                Array.Copy(SlotsState.Value, nextSlots, Math.Min(nextSlots.Length, SlotCount));
                for (int i = 0; i < nextRules.Length; i++)
                    if (nextRules[i] != null && !nextRules[i].Accepts(nextSlots[i]))
                        return Deny("slot_rejected", "A proposed slot rule rejects its existing item.");
                return InventoryValidationResult.Allow();
            }, () => { _slotRules = nextRules; AssignSnapshot(nextSlots); }, "ConfigureSlots").Validation;
        }

        /// <summary>
        /// Добавляет весь экземпляр: объединяет совместимые стопки или выбирает пустую ячейку.
        /// Добавление атомарно; для переноса владения и частичного заполнения используйте TryMoveTo.
        /// </summary>
        public InventoryOperationResult<TItem> TryAdd(TItem item, int slot = -1)
        {
            TItem[] next = null;
            return ExecuteSingle(InventoryActionKind.Add, item == null ? Array.Empty<TItem>() : new[] { item }, () =>
            {
                var validation = PrepareAddition(item, slot, out next, out _);
                return validation;
            }, () => AssignSnapshot(next));
        }

        /// <summary>Удаляет всю стопку из ячейки. Для частичного расхода рецептурное ядро формирует новый остаток.</summary>
        public InventoryOperationResult<TItem> TryRemove(int slot)
        {
            TItem[] next = null;
            return ExecuteSingle(InventoryActionKind.Remove, InRange(slot) && this[slot] != null ? new[] { this[slot] } : Array.Empty<TItem>(), () =>
            {
                if (!InRange(slot)) return Deny("slot_out_of_range", "The slot does not exist.");
                if (this[slot] == null) return Deny("slot_empty", "The slot is empty.");
                next = CopySlots(); next[slot] = null;
                return InventoryValidationResult.Allow();
            }, () => AssignSnapshot(next));
        }

        /// <summary>
        /// Переносит выбранное количество (-1 = всё), сливает совместимые стопки до лимита.
        /// Остаток сохраняется в источнике. Обмен несовместимых предметов разрешён только для всей стопки.
        /// </summary>
        public InventoryOperationResult<TItem> TryMoveTo(SlottedInventory<TItem> target, int sourceSlot, int targetSlot, int quantity = -1)
        {
            if (target == null) return Rejected(Deny("target_required", "A target inventory is required."));
            bool same = ReferenceEquals(target, this);
            if (!TryEnterMutation()) return BusyResult();
            if (!same && !target.TryEnterMutation()) { ExitMutation(); return BusyResult(); }
            try
            {
                if (!InRange(sourceSlot) || this[sourceSlot] == null)
                    return Rejected(Deny("source_slot_empty", "The source slot must contain an item."));
                var item = this[sourceSlot];
                int available = StackItems<TItem>.Quantity(item);
                int amount = quantity == -1 ? available : quantity;
                if (amount < 1 || amount > available) return Rejected(Deny("invalid_quantity", "Requested amount is not available."));
                if (targetSlot == -1) targetSlot = target.FindDestination(item, amount, false, same ? sourceSlot : -1);
                if (!target.InRange(targetSlot)) return Rejected(Deny("target_unavailable", "No suitable destination slot exists."));
                if (same && sourceSlot == targetSlot) return InventoryOperationResult<TItem>.Completed(Guid.NewGuid().ToString("N"));
                if (target.Slots.Where((_, i) => !same || i != sourceSlot).Any(value => value?.ItemId == item.ItemId))
                    return Rejected(Deny("duplicate_item", "The destination already contains this identity."));
                var swapped = target[targetSlot];
                var sourceNext = CopySlots(); var targetNext = same ? sourceNext : target.CopySlots();
                TItem transferred;
                if (swapped != null && StackItems<TItem>.Compatible(item, swapped))
                {
                    amount = Math.Min(amount, StackItems<TItem>.Limit(swapped) - StackItems<TItem>.Quantity(swapped));
                    if (amount <= 0) return Rejected(Deny("stack_full", "The destination stack is full."));
                    targetNext[targetSlot] = StackItems<TItem>.Copy(swapped, StackItems<TItem>.Quantity(swapped) + amount, swapped.ItemId);
                    transferred = StackItems<TItem>.Copy(item, amount, item.ItemId);
                    sourceNext[sourceSlot] = amount == available ? null : StackItems<TItem>.Copy(item, available - amount, item.ItemId);
                    swapped = null; // Слияние не создаёт обратный TransferOut.
                }
                else if (swapped != null)
                {
                    if (amount != available) return Rejected(Deny("partial_swap", "A partial stack cannot swap with a different item."));
                    sourceNext[sourceSlot] = swapped; targetNext[targetSlot] = transferred = item;
                }
                else
                {
                    transferred = StackItems<TItem>.Copy(item, amount, amount == available ? item.ItemId : Guid.NewGuid().ToString("N"));
                    sourceNext[sourceSlot] = amount == available ? null : StackItems<TItem>.Copy(item, available - amount, item.ItemId);
                    targetNext[targetSlot] = transferred;
                }
                var options = new InventoryActionOptions { OperationId = Guid.NewGuid().ToString("N") };
                var actions = new List<InventoryAction<TItem>> {
                    CreateAction(InventoryActionKind.TransferOut, this, target, new[] { transferred }, options),
                    target.CreateAction(InventoryActionKind.TransferIn, this, target, new[] { transferred }, options) };
                var owners = new List<SlottedInventory<TItem>> { this, target };
                if (swapped != null)
                {
                    actions.Add(target.CreateAction(InventoryActionKind.TransferOut, target, this, new[] { swapped }, options)); owners.Add(target);
                    actions.Add(CreateAction(InventoryActionKind.TransferIn, target, this, new[] { swapped }, options)); owners.Add(this);
                }
                InventoryValidationResult Validate()
                {
                    for (int i = 0; i < actions.Count; i++)
                    {
                        var owner = owners[i];
                        var valid = owner.EvaluateAction(actions[i], () => owner.ValidateSnapshot(ReferenceEquals(owner, this) ? sourceNext : targetNext));
                        if (!valid.IsAllowed) return valid;
                    }
                    return InventoryValidationResult.Allow();
                }
                InventoryOperationResult<TItem> Reject(InventoryValidationResult validation)
                {
                    for (int i = 0; i < actions.Count; i++) owners[i].PublishRejected(actions[i], validation);
                    return InventoryOperationResult<TItem>.Rejected(options.OperationId, validation, null, actions.ToArray());
                }
                var validation = Validate(); if (!validation.IsAllowed) return Reject(validation);
                for (int i = 0; i < actions.Count; i++) owners[i].PublishExecuting(actions[i]);
                validation = Validate(); if (!validation.IsAllowed) return Reject(validation);
                var sourceOld = CopySlots(); var targetOld = same ? sourceOld : target.CopySlots();
                // Обе стороны уже должны иметь конечное состояние, прежде чем UI или станция получат уведомление.
                AssignSnapshot(sourceNext); if (!same) target.AssignSnapshot(targetNext);
                NotifyChanged(sourceOld); if (!same) target.NotifyChanged(targetOld);
                for (int i = 0; i < actions.Count; i++) owners[i].PublishExecuted(actions[i]);
                return InventoryOperationResult<TItem>.Completed(options.OperationId, actions.ToArray());
            }
            catch (Exception exception) { return Rejected(Deny("validation_exception", exception.Message), exception); }
            finally { ExitMutation(); if (!same) target.ExitMutation(); }
        }

        // Общий путь локальной мутации: проверка -> ActionExecuting -> повторная проверка -> изменение -> события.
        // Вторая проверка учитывает условия/правила, которые могли измениться в обработчике ActionExecuting.
        private InventoryOperationResult<TItem> ExecuteSingle(InventoryActionKind kind, IEnumerable<TItem> items,
            Func<InventoryValidationResult> validate, Action assign, string name = null)
        {
            if (!TryEnterMutation()) return BusyResult();
            var action = CreateAction(kind, this, this, items, actionName: name);
            try
            {
                var validation = EvaluateAction(action, validate);
                if (!validation.IsAllowed) { PublishRejected(action, validation); return Rejected(validation, null, action); }
                PublishExecuting(action);
                validation = EvaluateAction(action, validate);
                if (!validation.IsAllowed) { PublishRejected(action, validation); return Rejected(validation, null, action); }
                var old = CopySlots(); assign(); NotifyChanged(old); PublishExecuted(action);
                return InventoryOperationResult<TItem>.Completed(action.OperationId, action);
            }
            finally { ExitMutation(); }
        }

        // Это защита от повторного входа из синхронных callbacks, а не блокировка для многопоточности.
        internal bool TryEnterMutation() { if (_mutating) return false; _mutating = true; return true; }
        internal void ExitMutation() => _mutating = false;
        internal TItem[] CopySlots() => (TItem[])SlotsState.Value.Clone();
        internal bool InRange(int slot) => slot >= 0 && slot < SlotCount;

        internal int FindEmptySlot(TItem item)
        {
            for (int i = 0; i < SlotCount; i++)
                if (this[i] == null && (_slotRules[i] == null || _slotRules[i].Accepts(item))) return i;
            return -1;
        }

        internal InventoryValidationResult PrepareAddition(TItem item, int slot, out TItem[] next, out int actualSlot)
        {
            next = null; actualSlot = slot;
            if (item == null) return Deny("item_required", "An item is required.");
            if (Slots.Any(value => value?.ItemId == item.ItemId)) return Deny("duplicate_item", "An item identity is already present.");
            if (slot == -1) actualSlot = FindDestination(item, StackItems<TItem>.Quantity(item), true);
            if (!InRange(actualSlot)) return Deny("target_unavailable", "No suitable destination slot exists.");
            var existing = this[actualSlot];
            next = CopySlots();
            if (existing != null)
            {
                if (!StackItems<TItem>.Compatible(item, existing)) return Deny("slot_occupied", "The destination slot contains a different item.");
                long total = (long)StackItems<TItem>.Quantity(existing) + StackItems<TItem>.Quantity(item);
                if (total > StackItems<TItem>.Limit(existing)) return Deny("stack_full", "The entire item does not fit in this stack.");
                next[actualSlot] = StackItems<TItem>.Copy(existing, (int)total, existing.ItemId);
            }
            else next[actualSlot] = item;
            return ValidateSnapshot(next);
        }

        private int FindDestination(TItem item, int quantity, bool requireAll, int excluded = -1)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                var existing = this[i];
                if (i == excluded || existing == null || !StackItems<TItem>.Compatible(item, existing)) continue;
                int room = StackItems<TItem>.Limit(existing) - StackItems<TItem>.Quantity(existing);
                if (room < (requireAll ? quantity : 1)) continue;
                var combined = StackItems<TItem>.Copy(existing, StackItems<TItem>.Quantity(existing) + Math.Min(room, quantity), existing.ItemId);
                if (_slotRules[i] == null || _slotRules[i].Accepts(combined)) return i;
            }
            var candidate = StackItems<TItem>.Copy(item, quantity, item.ItemId);
            return FindEmptySlot(candidate);
        }

        internal InventoryValidationResult ValidateSnapshot(TItem[] slots)
        {
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < slots.Length; i++)
            {
                var item = slots[i]; if (item == null) continue;
                if (string.IsNullOrWhiteSpace(item.ItemId)) return Deny("item_id_required", "An item must have a stable identity.");
                if (!identities.Add(item.ItemId)) return Deny("duplicate_item", "An item identity cannot occupy several slots.");
                if (StackItems<TItem>.Quantity(item) < 1 || StackItems<TItem>.Quantity(item) > StackItems<TItem>.Limit(item))
                    return Deny("invalid_quantity", "Stack quantity must be within its limit.");
                if (i < _slotRules.Length && _slotRules[i] != null && !_slotRules[i].Accepts(item))
                    return Deny("slot_rejected", "The slot rule rejects this item.");
            }
            return InventoryValidationResult.Allow();
        }

        // SetValueSilent обычно поднимает Patched. Откладываем внешние уведомления до присваивания
        // снимков всем участникам, затем отправляем по одному локальному Changed и SlotsChanged.
        internal void AssignSnapshot(TItem[] slots)
        {
            _assigning = true;
            try { SlotsState.SetValueSilent(slots); _view = Array.AsReadOnly(slots); Revision++; }
            finally { _assigning = false; }
        }

        internal void NotifyChanged(TItem[] old)
        {
            try { OnChanged(nameof(SlotsState), old, CopySlots()); } catch { /* A committed mutation cannot be undone by observers. */ }
            NotifySlotsChanged();
        }

        private void NotifySlotsChanged()
        {
            if (SlotsChanged == null) return;
            foreach (Action observer in SlotsChanged.GetInvocationList()) { try { observer(); } catch { } }
        }

        protected override void OnPatched(string path, object value)
        {
            if (_assigning) return;
            try { base.OnPatched(path, value); } catch { }
        }

        public override object GetValue(string path) => path == nameof(SlotsState) ? CopySlots() : base.GetValue(path);

        public override void BuildSnapshot(string currentPath, Dictionary<string, object> snapshot)
        {
            base.BuildSnapshot(currentPath, snapshot);
            snapshot[string.IsNullOrEmpty(currentPath) ? nameof(SlotsState) : currentPath + "." + nameof(SlotsState)] = CopySlots();
        }

        /// <summary>
        /// Вход для доверенного снимка SyncSystem, отдельно от пользовательских Try-операций.
        /// Проверяет целостность предметов и текущие фильтры; не выполняет обычный lifecycle ActionExecuting/Executed.
        /// Привязку к серверу, сериализацию TItem и проверку полномочий задаёт интеграция игры.
        /// </summary>
        public override void ApplyPatch(string path, object value)
        {
            if (_mutating) return;
            if (path != nameof(SlotsState)) { base.ApplyPatch(path, value); return; }
            var slots = SyncValueConverter.ConvertValue<TItem[]>(value);
            if (slots == null) throw new ArgumentException("A slot snapshot cannot be null.", nameof(value));
            var next = (TItem[])slots.Clone();
            if (!TryEnterMutation()) return;
            try
            {
                var validation = ValidateSnapshot(next);
                if (!validation.IsAllowed) throw new ArgumentException(validation.Message, nameof(value));
                Array.Resize(ref _slotRules, next.Length); AssignSnapshot(next);
                OnPatched(nameof(SlotsState), CopySlots()); NotifySlotsChanged();
            }
            finally { ExitMutation(); }
        }

        internal static InventoryValidationResult Deny(string code, string message) => InventoryValidationResult.Deny(code, message);
        internal static InventoryOperationResult<TItem> BusyResult() => Rejected(Deny("inventory_busy", "A participating inventory is already being changed."));
        internal static InventoryOperationResult<TItem> Rejected(InventoryValidationResult validation, Exception exception = null, params InventoryAction<TItem>[] actions)
            => InventoryOperationResult<TItem>.Rejected(actions.Length == 0 ? null : actions[0].OperationId, validation, exception, actions);
    }
}
