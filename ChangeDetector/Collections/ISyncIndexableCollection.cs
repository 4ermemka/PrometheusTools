using System.Collections;
using UnityEngine;

namespace Assets.Shared.ChangeDetector.Collections
{
    public interface ISyncIndexableCollection : ITrackableCollection
    {
        // Количество элементов (для проверки диапазона)
        int Count { get; }

        // Получение/замена элемента по "сегменту пути"
        // segmentName может быть "[3]" / "3" / "SomeKey" — сама коллекция знает, как его трактовать.
        object? GetElement(string segmentName);
        void SetElement(string segmentName, object? value);
    }

}