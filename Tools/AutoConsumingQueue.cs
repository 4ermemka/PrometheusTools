using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Shared.Tools
{
    /// <summary>
    /// Потокобезопасная очередь, автоматически обрабатывающая поступившие элементы.
    /// При добавлении элемента запускается фоновый цикл обработки, который завершается,
    /// когда очередь становится пустой. Новое добавление вновь активирует цикл.
    /// </summary>
    /// <typeparam name="T">Тип элементов очереди</typeparam>
    public class AutoConsumingQueue<T> : IDisposable
    {
        // Потокобезопасная очередь элементов
        private readonly ConcurrentQueue<T> _items = new ConcurrentQueue<T>();

        // Делегат обработки одного элемента
        private readonly Action<T> _processor;

        // CancellationToken для остановки цикла при уничтожении объекта
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        // Флаг состояния обработки (0 – неактивна, 1 – активна)
        private int _isProcessing = 0;

        // Задача, выполняющая цикл обработки (нужна для отслеживания завершения)
        private Task _processingTask;

        // Объект для синхронизации запуска (используется для ожидания завершения)
        private readonly SemaphoreSlim _startLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Создаёт очередь с указанным обработчиком элементов.
        /// </summary>
        /// <param name="processor">Делегат, вызываемый для каждого извлечённого элемента.</param>
        public AutoConsumingQueue(Action<T> processor)
        {
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        }

        /// <summary>
        /// Текущее количество элементов в очереди.
        /// </summary>
        public int Count => _items.Count;

        /// <summary>
        /// Добавляет элемент в очередь и при необходимости запускает цикл обработки.
        /// </summary>
        /// <param name="item">Элемент для добавления.</param>
        public void Enqueue(T item)
        {
            _items.Enqueue(item);
            TryStartProcessing();
        }

        /// <summary>
        /// Пытается запустить цикл обработки, если он ещё не активен.
        /// </summary>
        private void TryStartProcessing()
        {
            // Атомарно устанавливаем флаг обработки с 0 на 1.
            // Если удалось (предыдущее значение было 0) – запускаем цикл.
            if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) == 0)
            {
                // Запускаем цикл в отдельной задаче, чтобы не блокировать вызывающий поток.
                _processingTask = Task.Run(ProcessLoopAsync);
            }
        }

        /// <summary>
        /// Асинхронный цикл обработки. Выполняется, пока в очереди есть элементы.
        /// </summary>
        private async Task ProcessLoopAsync()
        {
            try
            {
                // Цикл продолжается, пока не запрошена отмена и очередь не пуста.
                while (!_cts.IsCancellationRequested && !_items.IsEmpty)
                {
                    // Пытаемся извлечь элемент.
                    if (_items.TryDequeue(out T item))
                    {
                        try
                        {
                            // Выполняем пользовательский обработчик.
                            _processor(item);
                        }
                        catch (Exception ex)
                        {
                            // Логируем исключение, но продолжаем обработку.
                            Debug.LogError($"Ошибка обработки элемента: {ex}");
                        }
                    }
                    else
                    {
                        // Если очередь пуста, но цикл всё ещё активен – даём шанс другим потокам.
                        await Task.Yield();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Ожидаемая отмена – ничего не делаем.
            }
            finally
            {
                // Сбрасываем флаг обработки, разрешая новый запуск.
                Interlocked.Exchange(ref _isProcessing, 0);

                // Если после завершения цикла в очереди снова появились элементы
                // (например, добавленные в момент сброса флага) – запускаем обработку снова.
                if (!_items.IsEmpty && !_cts.IsCancellationRequested)
                {
                    TryStartProcessing();
                }
            }
        }

        /// <summary>
        /// Очищает очередь и отменяет текущую обработку.
        /// Уже запущенные обработчики не прерываются, но новые элементы не будут обработаны,
        /// если только после остановки не будет вызван Enqueue.
        /// </summary>
        public void StopAndClear()
        {
            _cts.Cancel();

            // Очищаем очередь
            while (_items.TryDequeue(out _)) { }

            // Сбрасываем флаг принудительно
            Interlocked.Exchange(ref _isProcessing, 0);
        }

        /// <summary>
        /// Ожидает завершения текущего цикла обработки.
        /// </summary>
        public async Task WaitForCompletionAsync()
        {
            var task = _processingTask;
            if (task != null)
            {
                await task;
            }
        }

        /// <summary>
        /// Очищает ресурсы.
        /// </summary>
        public void Dispose()
        {
            StopAndClear();
            _cts.Dispose();
            _startLock.Dispose();
        }
    }
}