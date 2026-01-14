using System;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Помечает поле/свойство как синхронизируемое — изменения будут отслеживаться
    /// и отправляться по сети всем клиентам.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
    public class SyncAttribute : Attribute
    {

    }
}