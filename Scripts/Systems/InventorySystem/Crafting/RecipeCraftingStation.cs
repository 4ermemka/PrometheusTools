#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Shared.Systems.InventorySystem.Actions;
using Assets.Shared.Systems.InventorySystem.Core;
using Assets.Shared.Systems.InventorySystem.Items;
using Assets.Shared.Systems.InventorySystem.Slots;

namespace Assets.Shared.Systems.InventorySystem.Crafting
{
    /// <summary>
    /// Общее ядро крафта: строит preview по входному инвентарю, а при подтверждении проверяет
    /// назначение, списывает ингредиенты и выдаёт именно показанный предмет одной согласованной операцией.
    /// Не зависит от UI. Владелец должен вызвать Dispose, чтобы снять подписки с Input и Output.
    /// Контекст обновляется через SetContext; predicates и фабрики не должны менять предметы/инвентари.
    /// </summary>
    public sealed class RecipeCraftingStation<TItem, TContext> :
        CraftingStationBase<TItem, CraftCommitRequest<TItem>>,
        ICraftingStation<TItem, CraftCommitRequest<TItem>>, IDisposable
        where TItem : class, ICraftingItem<TItem>
    {
        // Сюда может выдать предмет только подтверждение этой станции. Обычный перенос разрешён наружу,
        // но обмен с занятой ячейкой не должен занести посторонний предмет обратно в выход.
        private sealed class CraftOutputInventory : SlottedInventory<TItem>
        {
            private readonly IInventory<TItem> _owner;
            public CraftOutputInventory(IInventory<TItem> owner) : base(1, name: "Crafted output") { _owner = owner; }
            protected override InventoryValidationResult ValidateCore(InventoryAction<TItem> action)
            {
                if (ReferenceEquals(action.Target, this) &&
                    (action.Kind == InventoryActionKind.Add || action.Kind == InventoryActionKind.TransferIn ||
                     (action.Kind == InventoryActionKind.Craft && !ReferenceEquals(action.Source, _owner))))
                    return Deny("output_take_only", "Only recipe confirmation can place an item in the crafted output slot.");
                return base.ValidateCore(action);
            }
        }

        // План будущего состояния; заполняется проверкой и применяется только после её полного успеха.
        private sealed class PreparedCommit
        {
            public TItem[] InputNext;
            public TItem[] TargetNext;
            public InventoryAction<TItem> InputAction;
            public InventoryAction<TItem> TargetAction;
        }

        private readonly IReadOnlyList<CraftingRecipe<TItem, TContext>> _recipes;
        private IReadOnlyList<CraftPreview<TItem>> _previews = Array.Empty<CraftPreview<TItem>>();
        private string _selectedRecipe;
        private long _generation;
        private long _inputRevision = -1;
        private long _outputRevision = -1;
        private bool _dirty = true;
        private bool _busy;
        private bool _refreshing;
        private bool _disposed;
        private bool _notifyingIdle;
        private PreparedCommit _prepared;
        private InventoryAction<TItem> _activeAction;

        /// <summary>Подключается к готовому входному инвентарю, копирует список рецептов и сразу строит доступные preview.</summary>
        public RecipeCraftingStation(SlottedInventory<TItem> input,
            IEnumerable<CraftingRecipe<TItem, TContext>> recipes, TContext context)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
            var array = (recipes ?? throw new ArgumentNullException(nameof(recipes))).ToArray();
            if (array.Any(x => x == null)) throw new ArgumentException("Recipes cannot contain null.", nameof(recipes));
            if (array.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != array.Length)
                throw new ArgumentException("Recipe identities must be unique.", nameof(recipes));
            _recipes = Array.AsReadOnly(array);
            Output = new CraftOutputInventory(this);
            Context = context;
            Input.SlotsChanged += InventoryChanged;
            Output.SlotsChanged += InventoryChanged;
            RefreshPreviews();
        }

        public SlottedInventory<TItem> Input { get; }
        public SlottedInventory<TItem> Output { get; }
        public TContext Context { get; private set; }
        public IReadOnlyList<CraftPreview<TItem>> Previews { get { RefreshPreviews(); return _previews; } }
        public CraftPreview<TItem> Preview => Previews.FirstOrDefault(x => x.RecipeId == _selectedRecipe) ?? Previews.FirstOrDefault();
        public event Action PreviewChanged;
        /// <summary>Во время защищённых проверок и уведомлений менять контекст или выбор рецепта нельзя.</summary>
        public bool IsBusy => _busy || _refreshing;
        /// <summary>Безопасная точка для применения отложенных изменений контекста владельцем станции.</summary>
        public event Action BecameIdle;

        /// <summary>Обновляет внешние условия и инвалидирует старые preview, даже если ссылка на контекст та же.</summary>
        public void SetContext(TContext context)
        {
            EnsureConfigurable();
            Context = context; _dirty = true; RefreshPreviews();
        }

        /// <summary>Запоминает выбор; если он сейчас недоступен, свойство Preview показывает первый доступный вариант.</summary>
        public InventoryValidationResult SelectRecipe(string recipeId)
        {
            EnsureConfigurable();
            if (!_recipes.Any(x => x.Id == recipeId)) return Deny("recipe_not_found", "The recipe does not exist.");
            if (_selectedRecipe == recipeId) return InventoryValidationResult.Allow();
            _selectedRecipe = recipeId; _dirty = true; RefreshPreviews(); return InventoryValidationResult.Allow();
        }

        /// <summary>Подтверждает захваченный preview с выдачей в отдельный целевой инвентарь; собственный Input не допускается.</summary>
        public InventoryOperationResult<TItem> TryClaim(CraftPreview<TItem> preview, SlottedInventory<TItem> target, int targetSlot = -1)
            => TryExecute(new CraftCommitRequest<TItem>(preview, target, targetSlot));

        /// <summary>Подтверждение без переноса мышью: готовый предмет остаётся в Output до получения игроком.</summary>
        public InventoryOperationResult<TItem> TryConfirm() => TryClaim(Preview, Output, 0);

        /// <summary>
        /// Согласованная транзакция Input + Output + назначение. Все участники защищены от повторного входа
        /// на время проверки, присваивания и уведомлений. Это синхронный API игрового потока, не thread-safe lock.
        /// </summary>
        public override InventoryOperationResult<TItem> TryExecute(CraftCommitRequest<TItem> request, InventoryActionOptions options = null)
        {
            if (_disposed) return Reject("station_disposed", "The station has been disposed.");
            if (_busy || _refreshing) return SlottedInventory<TItem>.BusyResult();
            RefreshPreviews();
            var participants = Participants(request);
            if (!TryLock(participants)) return SlottedInventory<TItem>.BusyResult();
            _busy = true;
            var action = BuildProcessAction(request, options);
            _activeAction = action;
            try
            {
                var validation = EvaluateAction(action, () => ValidateRequest(request, action));
                if (!validation.IsAllowed) return RejectAction(action, validation);
                PublishExecuting(action);
                Input.PublishExecuting(_prepared.InputAction);
                request.Target.PublishExecuting(_prepared.TargetAction);
                // Обработчики Executing могут изменить внешнее условие или правила действий.
                // Проверяем ещё раз, но не генерируем заново предмет, который пользователь уже оценил.
                validation = EvaluateAction(action, () => ValidateRequest(request, action));
                if (!validation.IsAllowed) return RejectAction(action, validation);
                var prepared = _prepared;
                var inputOld = Input.CopySlots(); var targetOld = request.Target.CopySlots();
                // Ни один SlotsChanged не должен увидеть уже потраченный вход при ещё не выданном результате.
                Input.AssignSnapshot(prepared.InputNext);
                request.Target.AssignSnapshot(prepared.TargetNext);
                _dirty = true; _generation++; _previews = Array.Empty<CraftPreview<TItem>>();
                Input.NotifyChanged(inputOld);
                request.Target.NotifyChanged(targetOld);
                Input.PublishExecuted(prepared.InputAction);
                request.Target.PublishExecuted(prepared.TargetAction);
                PublishExecuted(action);
                return InventoryOperationResult<TItem>.Completed(action.OperationId, action, prepared.InputAction, prepared.TargetAction);
            }
            finally
            {
                _prepared = null; _activeAction = null; _busy = false;
                foreach (var participant in participants) participant.ExitMutation();
                RefreshPreviews();
                NotifyIdle();
            }
        }

        /// <summary>Проверяет запрос без расхода. Это не резервирование: TryExecute выполнит проверки повторно.</summary>
        public override InventoryValidationResult CanExecute(CraftCommitRequest<TItem> request)
        {
            if (_disposed) return Deny("station_disposed", "The station has been disposed.");
            if (_busy || _refreshing) return Deny("inventory_busy", "The station is already being changed.");
            RefreshPreviews();
            var participants = Participants(request);
            if (!TryLock(participants)) return Deny("inventory_busy", "A participating inventory is already being changed.");
            _busy = true;
            try
            {
                var action = BuildProcessAction(request, null);
                _activeAction = action;
                return EvaluateAction(action, () => ValidateRequest(request, action));
            }
            finally
            {
                _prepared = null; _activeAction = null; _busy = false;
                foreach (var participant in participants) participant.ExitMutation();
                NotifyIdle();
            }
        }

        protected override IReadOnlyList<TItem> GetAffectedItems(CraftCommitRequest<TItem> request)
            => request?.Preview?.Result == null ? Array.Empty<TItem>() : new[] { request.Preview.Result };

        protected override IReadOnlyDictionary<string, string> GetMetadata(CraftCommitRequest<TItem> request)
            => new Dictionary<string, string> { ["recipeId"] = request?.Preview?.RecipeId ?? string.Empty };

        protected override InventoryValidationResult ValidateCore(InventoryAction<TItem> action)
        {
            if ((_busy || _refreshing) && !ReferenceEquals(action, _activeAction))
                return Deny("inventory_busy", "The station is already being changed.");
            return base.ValidateCore(action);
        }

        // Сначала проверяем токен preview, условия и план расхода, затем готовим остатки и место для результата.
        // Даже ошибка назначения не может потратить ресурс: здесь только локальные копии будущих состояний.
        protected override InventoryValidationResult ValidateRequest(CraftCommitRequest<TItem> request, InventoryAction<TItem> action)
        {
            _prepared = null;
            if (_disposed) return Deny("station_disposed", "The station has been disposed.");
            var preview = request?.Preview;
            if (preview == null || !ReferenceEquals(preview.Owner, this) || preview.Generation != _generation ||
                preview.InputRevision != Input.Revision || !_previews.Contains(preview))
                return Deny("stale_preview", "The displayed recipe is no longer current.");
            if (Output.Slots.Any(x => x != null)) return Deny("output_occupied", "Take the existing crafted output first.");
            if (request.Target == null || ReferenceEquals(request.Target, Input))
                return Deny("invalid_target", "Crafted output requires a separate target inventory.");
            var recipe = _recipes.First(x => x.Id == preview.RecipeId);
            var condition = recipe.Condition == null ? InventoryValidationResult.Allow() : recipe.Condition(Context);
            if (condition == null) return Deny("condition_returned_null", "The recipe condition returned null.");
            if (!condition.IsAllowed) return condition;
            if (!RecipeMatcher<TItem>.TryMatch(Input.Slots, recipe.Ingredients, out var live) || !live.SequenceEqual(preview.Consumptions))
                return Deny("ingredients_changed", "The recipe ingredients are no longer valid.");
            if (preview.Result == null || preview.Result.Quantity <= 0)
                return Deny("invalid_result", "The recipe must produce a positive quantity.");
            if (Input.Slots.Any(item => item != null && item.ItemId == preview.Result.ItemId))
                return Deny("result_identity_conflict", "A crafted result needs its own item identity.");
            var nextInput = Input.CopySlots();
            var affected = new List<TItem>();
            for (int i = 0; i < nextInput.Length; i++)
            {
                int amount = preview.Consumptions[i]; if (amount == 0) continue;
                var item = nextInput[i]; affected.Add(item);
                int remainder = item.Quantity - amount;
                if (remainder < 0) return Deny("insufficient_quantity", "An ingredient quantity changed.");
                nextInput[i] = remainder == 0 ? null : item.WithQuantity(remainder);
                if (remainder > 0 && (nextInput[i] == null || ReferenceEquals(nextInput[i], item) ||
                    nextInput[i].ItemId != item.ItemId || nextInput[i].Quantity != remainder))
                    return Deny("invalid_remainder", "WithQuantity must return a new value with the requested quantity and original identity.");
            }
            var addition = request.Target.PrepareAddition(preview.Result, request.TargetSlot, out var nextTarget, out _);
            if (!addition.IsAllowed) return addition;
            var options = new InventoryActionOptions { OperationId = action.OperationId, Origin = action.Origin, Reason = action.Reason, Metadata = action.Metadata };
            var inputAction = Input.CreateAction(InventoryActionKind.Remove, Input, this, affected, options, "ConsumeRecipeIngredients");
            var targetKind = ReferenceEquals(request.Target, Output) ? InventoryActionKind.Craft : InventoryActionKind.Add;
            var targetAction = request.Target.CreateAction(targetKind, this, request.Target, new[] { preview.Result }, options, "DeliverCraftedResult");
            var inputValidation = Input.EvaluateAction(inputAction, () => Input.ValidateSnapshot(nextInput));
            if (!inputValidation.IsAllowed) return inputValidation;
            var targetValidation = request.Target.EvaluateAction(targetAction, () => request.Target.ValidateSnapshot(nextTarget));
            if (!targetValidation.IsAllowed) return targetValidation;
            _prepared = new PreparedCommit { InputNext = nextInput, TargetNext = nextTarget, InputAction = inputAction, TargetAction = targetAction };
            return InventoryValidationResult.Allow();
        }

        protected override void ExecuteRequest(CraftCommitRequest<TItem> request, InventoryAction<TItem> action)
        {
            // В отличие от простого процесса здесь нужны несколько согласованных хранилищ.
            // Поэтому весь lifecycle реализует override TryExecute, в том числе при вызове через базовый тип.
            throw new InvalidOperationException("Use the coordinated crafting transaction.");
        }

        private InventoryOperationResult<TItem> RejectAction(InventoryAction<TItem> action, InventoryValidationResult validation)
        {
            PublishRejected(action, validation);
            return InventoryOperationResult<TItem>.Rejected(action.OperationId, validation, null, action);
        }

        private List<SlottedInventory<TItem>> Participants(CraftCommitRequest<TItem> request)
        {
            var result = new List<SlottedInventory<TItem>> { Input, Output };
            if (request?.Target != null && !result.Contains(request.Target)) result.Add(request.Target);
            return result;
        }

        private static bool TryLock(List<SlottedInventory<TItem>> participants)
        {
            for (int i = 0; i < participants.Count; i++)
            {
                if (participants[i].TryEnterMutation()) continue;
                for (int j = 0; j < i; j++) participants[j].ExitMutation();
                return false;
            }
            return true;
        }

        private void InventoryChanged() { _dirty = true; RefreshPreviews(); }

        // Кэш обновляется по Revision/dirty. До изменения состояния сохраняется тот же объект результата;
        // фабрика не вызывается заново при каждом чтении UI или попытке подтверждения.
        private void RefreshPreviews()
        {
            if (_disposed || _busy || _refreshing || (!_dirty && _inputRevision == Input.Revision && _outputRevision == Output.Revision)) return;
            _refreshing = true;
            // Событие входа приходит ещё под его защитой от мутации. Удерживаем дополнительно
            // полученные защиты до завершения фабрик и подписчиков, чтобы исключить повторный вход.
            bool ownInput = Input.TryEnterMutation(), ownOutput = Output.TryEnterMutation();
            try
            {
                _generation++; _dirty = false; _inputRevision = Input.Revision; _outputRevision = Output.Revision;
                var previews = new List<CraftPreview<TItem>>();
                if (!Output.Slots.Any(x => x != null))
                {
                    var inputSnapshot = Array.AsReadOnly(Input.CopySlots());
                    foreach (var recipe in _recipes)
                    {
                        try
                        {
                            var condition = recipe.Condition == null ? InventoryValidationResult.Allow() : recipe.Condition(Context);
                            if (condition == null || !condition.IsAllowed || !RecipeMatcher<TItem>.TryMatch(inputSnapshot, recipe.Ingredients, out var consumed)) continue;
                            var result = recipe.CreateResult(inputSnapshot, Context);
                            if (result == null || result.Quantity <= 0 || string.IsNullOrWhiteSpace(result.ItemId)) continue;
                            previews.Add(new CraftPreview<TItem>(this, _generation, Input.Revision, recipe.Id, recipe.Name, result, consumed));
                        }
                        catch { /* Ошибочный рецепт пропускается, чтобы не прервать уведомление уже изменённого инвентаря. */ }
                    }
                }
                _previews = previews.AsReadOnly();
                if (PreviewChanged != null)
                    foreach (Action observer in PreviewChanged.GetInvocationList()) { try { observer(); } catch { } }
            }
            finally
            {
                if (ownInput) Input.ExitMutation(); if (ownOutput) Output.ExitMutation();
                _refreshing = false;
                NotifyIdle();
            }
        }

        private void NotifyIdle()
        {
            if (_busy || _refreshing || _notifyingIdle || BecameIdle == null) return;
            _notifyingIdle = true;
            try
            {
                foreach (Action observer in BecameIdle.GetInvocationList())
                    try { observer(); } catch { /* Ошибка владельца не отменяет завершённую операцию. */ }
            }
            finally { _notifyingIdle = false; }
        }

        private void EnsureConfigurable()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
            if (_busy || _refreshing) throw new InvalidOperationException("Cannot change recipe selection or context during an inventory callback.");
        }

        /// <summary>
        /// Снимает подписки и аннулирует preview. Не возвращает ресурсы, не очищает хранилища и не сохраняет данные.
        /// После Dispose подтверждать крафт нельзя; владелец решает судьбу Input и готового Output.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            if (_busy || _refreshing) throw new InvalidOperationException("Cannot dispose a station during an inventory callback.");
            _disposed = true; _previews = Array.Empty<CraftPreview<TItem>>(); _generation++;
            Input.SlotsChanged -= InventoryChanged; Output.SlotsChanged -= InventoryChanged;
        }

        private static InventoryValidationResult Deny(string code, string message) => InventoryValidationResult.Deny(code, message);
        private static InventoryOperationResult<TItem> Reject(string code, string message) => SlottedInventory<TItem>.Rejected(Deny(code, message));
    }
}
