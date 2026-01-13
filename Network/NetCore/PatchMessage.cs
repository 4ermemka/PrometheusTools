using Assets.Shared.ChangeDetector;
using System.Collections.Generic;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>
    /// Сетевое сообщение-патч: путь до поля и новое значение.
    /// Отправляется от клиента к хосту и от хоста ко всем клиентам.
    /// </summary>
    public sealed class PatchMessage
    {
        /// <summary>
        /// Путь до изменённого поля в дереве WorldState.
        /// Например: [ "Counters", "[1]", "Value" ].
        /// </summary>
        public List<FieldPathSegment> Path { get; set; }

        /// <summary>
        /// Новое значение для указанного поля (после применения патча).
        /// </summary>
        public object NewValue { get; set; }

        public PatchMessage()
        {
            Path = new List<FieldPathSegment>();
        }
    }

}