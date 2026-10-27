using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Shared.UI
{
    /// <summary>Шаблон закладки. Hover во время drag переключает страницу, не забирая pointerDrag.</summary>
    [RequireComponent(typeof(Button))]
    public sealed class TabButton : MonoBehaviour, IPointerEnterHandler
    {
        [SerializeField] private Image icon;
        [SerializeField] private Text label;
        [SerializeField] private Image background;
        [SerializeField] private Color normal = new Color32(38, 51, 63, 255);
        [SerializeField] private Color selected = new Color32(48, 103, 108, 255);
        private TabPanel page;
        public TabPanel Page => page;
        private void Awake() => GetComponent<Button>().onClick.AddListener(Select);
        public void Bind(TabPanel value, bool active)
        {
            page = value;
            label.text = value.Caption;
            icon.sprite = value.Icon;
            icon.enabled = value.Icon != null;
            background.color = active ? selected : normal;
        }
        public void Select() => page?.Open();
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (eventData.dragging || eventData.pointerDrag != null) Select();
        }
    }
}
