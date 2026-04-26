using System.Collections.Generic;
using UnityEngine;

public class InputContextManager : Manager
{
    [SerializeField] public InputContext CurrentContext;

    /// <summary>
    /// Добавление нового контекста
    /// </summary>
    /// <param name="newContext">Новый контекст</param>
    public void AddContext(InputContext newContext)
    {
        CurrentContext.Deactivate(); // Выключить реакцию инпут у основного
        var previousContext = CurrentContext; // Запомнить его в отдельный слот
        newContext.Previous = previousContext; // Назначить новому ссылку на предыдущий

        CurrentContext = newContext; // Переназначить новый
        CurrentContext.Init(); // Запустить (инициализация, анимации и т.д.)
        CurrentContext.Activate(); // Включить реакцию на инпут
    }

    public void EscapeContext()
    {
        var previousContext = CurrentContext.Previous; // Вытащить прошлый контекст

        if (previousContext != null) // Если есть прошлый -- откат к нему. Если нет -- не реагируем, это корень.
        {
            CurrentContext.Deactivate(); //Выключить реакцию на инпут (обрубить подписки)
            CurrentContext.Dispose(); // Удалить текущий

            CurrentContext = previousContext; // Назначить предыдущий основным
            CurrentContext.Activate(); // Включить реакцию на инпут
        }
    }
}
