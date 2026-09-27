using UnityEngine;
using UnityEngine.UI;

namespace GearInvest.Unity
{
    /// <summary>Shared widget construction only. Typed authoring modes own their fields and lifecycle.</summary>
    public static class GearInvestAuthoringWidgets
    {
        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        { var go = new GameObject(name, typeof(RectTransform)); go.layer = 5; var r = go.GetComponent<RectTransform>(); r.SetParent(parent, false); r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); return r; }
        public static RectTransform Box(Transform parent, string name, float x, float y, float w, float h, Color color)
        { var r = Rect(parent, name, x, y, w, h); r.gameObject.AddComponent<Image>().color = color; return r; }
        public static Text Label(Font font, Transform parent, string text, float x, float y, float w, float h, int size, Color color)
        { var r = Rect(parent, text, x, y, w, h); var t = r.gameObject.AddComponent<Text>(); t.font = font; t.text = text; t.fontSize = size; t.color = color; t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; return t; }
        public static InputField Field(Font font, Transform parent, string key, string title, string value, float y)
        {
            var color = new Color(.9f, .94f, 1); Label(font, parent, title, 12, y, 290, 29, 11, color);
            var box = Box(parent, "input-" + key, 307, y, 379, 29, new Color(.14f, .17f, .22f));
            var field = box.gameObject.AddComponent<InputField>(); field.textComponent = Label(font, box, "", 5, 1, 369, 27, 12, color);
            field.characterLimit = 8192; field.text = value; return field;
        }
        public static Button Button(Font font, Transform parent, string key, string title, float x, float y, float width, UnityEngine.Events.UnityAction action)
        {
            var box = Box(parent, "button-" + key, x, y, width, 32, new Color(.14f, .28f, .4f)); var button = box.gameObject.AddComponent<Button>();
            button.targetGraphic = box.GetComponent<Image>(); Label(font, box, title, 4, 5, width - 8, 26, 12, new Color(.9f, .94f, 1));
            button.onClick.AddListener(action); return button;
        }
    }
}
