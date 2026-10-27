using UnityEngine;

namespace Shared.UI
{
    /// <summary>Сообщает фасаду о добавлении/удалении страниц внутри Body, без поиска дерева каждый кадр.</summary>
    public sealed class TabPageHost : MonoBehaviour
    {
        private void OnTransformChildrenChanged() => GetComponentInParent<TabPanel>()?.InvalidateHierarchy();
    }
}
