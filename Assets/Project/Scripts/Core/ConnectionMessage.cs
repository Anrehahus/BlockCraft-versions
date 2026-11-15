using UnityEngine;

namespace MinecraftClone.UI
{
    // Плашка с сообщением об ошибке подключения. Переживает смену сцены.
    public class ConnectionMessage : MonoBehaviour
    {
        private static ConnectionMessage _inst;
        private string _text;
        private float _hideAt;
        private GUIStyle _box;
        private Texture2D _bg;

        public static void Show(string text, float seconds = 12f)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_inst == null)
            {
                var go = new GameObject("[ConnectionMessage]");
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<ConnectionMessage>();
            }
            _inst._text = text;
            _inst._hideAt = Time.unscaledTime + seconds;
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(_text)) return;
            if (Time.unscaledTime > _hideAt) { _text = null; return; }

            GUI.depth = -1000;
            if (_box == null)
            {
                _bg = new Texture2D(1, 1);
                _bg.SetPixel(0, 0, new Color(0.35f, 0.05f, 0.05f, 0.95f));
                _bg.Apply();
                _box = new GUIStyle(GUI.skin.box)
                {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 20,
                    padding = new RectOffset(16, 16, 12, 44)
                };
                _box.normal.background = _bg;
                _box.normal.textColor = Color.white;
            }

            float w = Mathf.Min(Screen.width - 40f, 720f);
            Rect r = new Rect((Screen.width - w) * 0.5f, 30f, w, 140f);
            GUI.Box(r, _text, _box);
            if (GUI.Button(new Rect(r.xMax - 100f, r.yMax - 38f, 88f, 28f), "OK")) _text = null;
        }
    }
}