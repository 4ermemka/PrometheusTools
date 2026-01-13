using System;
using System.Collections.Generic;

namespace Assets.Shared.ChangeDetector
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Базовый класс для всех синхронизируемых объектов.
    /// 
    /// Функции:
    /// 1) Через атрибуты [Sync] определяет, какие свойства/поля участвуют в сетевой синхронизации.
    /// 2) Предоставляет универсальный SetProperty для отслеживания изменений значений.
    /// 3) Автоматически находит и подписывается на вложенные TrackableNode (рефлексивно),
    ///    пробрасывая их изменения вверх с добавлением сегмента пути.
    /// </summary>
    public abstract class TrackableNode
    {
        /// <summary>
        /// Событие изменения любого синхронизируемого поля в этом объекте или во вложенных TrackableNode.
        /// Путь Path содержит последовательность сегментов от текущего узла вниз до конкретного поля.
        /// </summary>
        public event Action<FieldChange>? Changed;

        /// <summary>
        /// Кэш: имя свойства/поля → помечено ли оно [Sync].
        /// Используется в SetProperty, чтобы понимать, надо ли поднимать событие для данного свойства.
        /// </summary>
        private readonly Dictionary<string, bool> _syncMap = new();

        /// <summary>
        /// Кэш вложенных TrackableNode по имени поля/свойства.
        /// Нужен для переподписки и предотвращения утечек событий.
        /// </summary>
        private readonly Dictionary<string, TrackableNode?> _childNodes = new();

        /// <summary>
        /// Конструктор базового класса.
        /// 
        /// Делает два важных шага:
        /// 1) Строит карту sync-полей по атрибутам [Sync] (BuildSyncMapAndWireChildren).
        /// 2) Находит все поля/свойства-типа TrackableNode, при необходимости создаёт их
        ///    и подписывается на их события Changed.
        /// 
        /// Вызывается из конструктора любого наследника перед его собственными действиями.
        /// </summary>
        protected TrackableNode()
        {
            BuildSyncMapAndWireChildren();
        }

        /// <summary>
        /// Строит карту синхронизируемых полей и автоматически подписывается на вложенные TrackableNode.
        /// 
        /// Делает следующее:
        /// - перебирает все поля и свойства экземпляра через рефлексию;
        /// - запоминает, какие члены помечены атрибутом [Sync];
        /// - для членов, тип которых наследует TrackableNode:
        ///     * если значение уже есть — подписывается на его Changed;
        ///     * если значение null и тип не абстрактный — создаёт экземпляр и подписывается.
        /// 
        /// Этот метод вызывается один раз в конструкторе и не должен вызываться вручную.
        /// </summary>
        private void BuildSyncMapAndWireChildren()
        {
            var type = GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var member in type.GetMembers(flags))
            {
                // 1. Флаг [Sync] для SetProperty
                var hasSync = member.GetCustomAttribute<SyncAttribute>() != null;
                _syncMap[member.Name] = hasSync;

                // 2. Попытка найти TrackableNode-поля/свойства
                Type memberType = null;
                Func<object?> getter = null;
                Action<object?> setter = null;

                if (member is PropertyInfo pi)
                {
                    memberType = pi.PropertyType;
                    if (!pi.CanRead)
                        continue;

                    getter = () => pi.GetValue(this);
                    if (pi.CanWrite)
                        setter = v => pi.SetValue(this, v);
                }
                else if (member is FieldInfo fi)
                {
                    memberType = fi.FieldType;
                    getter = () => fi.GetValue(this);
                    if (!fi.IsInitOnly)
                        setter = v => fi.SetValue(this, v);
                }

                if (memberType == null)
                    continue;

                // Нас интересуют только наследники TrackableNode
                if (!typeof(TrackableNode).IsAssignableFrom(memberType))
                    continue;

                // 3. Получаем текущее значение, при необходимости создаём новый экземпляр
                var current = getter();
                TrackableNode childNode;

                if (current is TrackableNode existingNode)
                {
                    childNode = existingNode;
                }
                else
                {
                    // Не создаём экземпляр для абстрактных типов
                    if (memberType.IsAbstract)
                        continue;

                    childNode = (TrackableNode)Activator.CreateInstance(memberType);
                    setter?.Invoke(childNode);
                }

                // 4. Подписываемся на изменения дочернего узла
                WireChild(member.Name, childNode);
            }
        }

        /// <summary>
        /// Подписывает TrackableNode-дочерний объект на bubbling изменений.
        /// 
        /// Делает следующее:
        /// - если на этом имени уже был ребёнок — отписывается от его Changed;
        /// - сохраняет нового ребёнка в кэш;
        /// - подписывается на его Changed, добавляя к пути сегмент с именем поля/свойства.
        /// </summary>
        /// <param name="childName">Имя свойства/поля, содержащего дочерний TrackableNode.</param>
        /// <param name="child">Экземпляр TrackableNode, который нужно подписать.</param>
        private void WireChild(string childName, TrackableNode child)
        {
            if (_childNodes.TryGetValue(childName, out var existing) && existing != null)
            {
                existing.Changed -= GetChildHandler(childName);
            }

            _childNodes[childName] = child;
            child.Changed += GetChildHandler(childName);
        }

        /// <summary>
        /// Возвращает обработчик изменений для конкретного дочернего узла.
        /// 
        /// Хендлер:
        /// - берёт исходный FieldChange дочернего узла;
        /// - добавляет в начало пути сегмент с именем дочернего поля/свойства;
        /// - поднимает событие Changed уже на текущем уровне.
        /// 
        /// Это и есть "bubbling" изменений вверх по дереву.
        /// </summary>
        /// <param name="childName">Имя дочернего свойства/поля.</param>
        private Action<FieldChange> GetChildHandler(string childName)
        {
            return change =>
            {
                var newPath = new List<FieldPathSegment> { new FieldPathSegment(childName) };
                newPath.AddRange(change.Path);
                Changed?.Invoke(new FieldChange(newPath, change.OldValue, change.NewValue));
            };
        }

        /// <summary>
        /// Универсальный сеттер для полей/свойств всех наследников.
        /// 
        /// Функции:
        /// 1) Сравнивает старое и новое значение (без поднятия события, если они равны).
        /// 2) Обновляет поле.
        /// 3) Если свойство помечено [Sync], генерирует FieldChange с путём из одного сегмента.
        /// 4) Если новое значение является TrackableNode, автоматически переподписывает его
        ///    как дочерний (WireChild), чтобы его изменения "всплывали" вверх.
        /// 
        /// Использование:
        ///     [Sync]
        ///     public int Hp
        ///     {
        ///         get => _hp;
        ///         set => SetProperty(ref _hp, value);
        ///     }
        /// </summary>
        /// <typeparam name="T">Тип поля.</typeparam>
        /// <param name="field">Ссылка на приватное поле, где хранится значение.</param>
        /// <param name="value">Новое значение.</param>
        /// <param name="propertyName">
        /// Имя свойства. Передаётся автоматически через CallerMemberName,
        /// поэтому вручную указывать не нужно.
        /// </param>
        /// <returns>true, если значение реально изменилось.</returns>
        protected bool SetProperty<T>(
            ref T field,
            T value,
            [CallerMemberName] string propertyName = ""
        )
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            var oldValue = field;
            field = value;

            // Если свойство помечено [Sync], формируем событие об изменении
            if (_syncMap.TryGetValue(propertyName, out var isSync) && isSync)
            {
                var path = new List<FieldPathSegment> { new FieldPathSegment(propertyName) };
                Changed?.Invoke(new FieldChange(path, oldValue, value));
            }

            // Если новое значение само является TrackableNode — считаем его дочерним узлом
            if (value is TrackableNode childNode)
            {
                WireChild(propertyName, childNode);
            }

            return true;
        }

        /// <summary>
        /// Поднимает изменение локального поля вручную.
        /// 
        /// Используется для редких случаев, когда требуется явно сформировать FieldChange
        /// без использования SetProperty (например, при батчевых операциях или сложных патчах).
        /// </summary>
        /// <typeparam name="T">Тип значения.</typeparam>
        /// <param name="fieldName">Имя поля/свойства.</param>
        /// <param name="oldValue">Старое значение.</param>
        /// <param name="newValue">Новое значение.</param>
        protected void RaiseLocalChange(
            string fieldName,
            object? oldValue,
            object? newValue
        )
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment(fieldName) };
            RaiseChange(new FieldChange(path, oldValue, newValue));
        }

        /// <summary>
        /// Поднимает событие Changed для текущего узла.
        /// 
        /// Используется всеми наследниками и вспомогательными классами (в том числе коллекциями)
        /// как единая точка "выхода" изменений наружу. Вместо прямого вызова события Changed
        /// (который запрещён для event в базовом классе) нужно всегда вызывать этот метод.
        /// 
        /// Также упрощает дальнейшие расширения: логирование, фильтрацию, буферизацию патчей
        /// можно централизованно добавить сюда, не трогая остальной код.
        /// </summary>
        /// <param name="change">Описание изменения (путь, старое и новое значение).</param>
        protected void RaiseChange(FieldChange change)
        {
            Changed?.Invoke(change);
        }

    }

}