using System.Collections.Generic;
using UnityEngine;

namespace MinecraftClone.UI
{
    /// <summary>Общие функции IMGUI-меню (пауза, настройки): спрайты с 9-slice, у которых рамка масштабируется вместе с кнопкой.</summary>
    public static class McGui
    {
        private static readonly Dictionary<(Texture2D, int), GUIStyle> Styles = new Dictionary<(Texture2D, int), GUIStyle>();

        public static Texture2D Load(string name)
        {
            Texture2D t = Resources.Load<Texture2D>("UI/" + name);
            if (t != null) { t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp; }
            return t;
        }

        /// <summary>
        /// Рисует текстуру растянутой по Rect с 9-slice. Один пиксель текстуры занимает целое число экранных пикселей,
        /// поэтому рамка остаётся чёткой, без швов и размытия.
        /// </summary>
        public static void DrawSliced(Rect r, Texture2D t, float srcBorder = 3f)
        {
            if (t == null || Event.current.type != EventType.Repaint) return;
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;

            int b = Mathf.RoundToInt(srcBorder);
            if (!Styles.TryGetValue((t, b), out GUIStyle st) || st == null)
            {
                st = new GUIStyle { border = new RectOffset(b, b, b, b) };
                st.normal.background = t;
                Styles[(t, b)] = st;
            }

            float s = Mathf.Max(1f, Mathf.Round(r.height / t.height));   // экранных пикселей на один пиксель текстуры
            float w = Mathf.Max(2 * b, Mathf.Round(r.width / s));        // размер в пикселях текстуры
            float h = Mathf.Max(2 * b, Mathf.Round(r.height / s));

            Matrix4x4 old = GUI.matrix;
            GUI.matrix = old * Matrix4x4.TRS(new Vector3(Mathf.Round(r.x), Mathf.Round(r.y), 0f),
                                             Quaternion.identity, new Vector3(s, s, 1f));
            st.Draw(new Rect(0f, 0f, w, h), GUIContent.none, false, false, false, false);
            GUI.matrix = old;
        }

        /// <summary>Фон кнопки (обычный/наведение) + клик. Текст рисуйте отдельно.</summary>
        public static bool ButtonFrame(Rect r, Texture2D normal, Texture2D hover)
        {
            bool over = r.Contains(Event.current.mousePosition);
            DrawSliced(r, over && hover != null ? hover : normal);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }
    }
}