using UnityEngine;
using MinecraftClone.Player;

namespace MinecraftClone.UI
{
    // Экран смерти: красный фон, «Вы погибли!», счёт и кнопка «Возродиться».
    // Добавляется автоматически вместе с PlayerHotbarUI.
    public class DeathScreenUI : MonoBehaviour
    {
        public static bool IsShown { get; private set; }

        private PlayerHealth _health;
        private Font _font;
        private Texture2D _btnNormal, _btnHover;
        private GUIStyle _title, _text, _btn;

        private void Awake()
        {
            _btnNormal = Resources.Load<Texture2D>("UI/button");
            _btnHover = Resources.Load<Texture2D>("UI/button_hover");
        }

        private void OnDisable() { IsShown = false; }
        private void OnDestroy() { IsShown = false; }

        private void Update()
        {
            if (_health == null)
            {
                IsShown = false;
                PlayerHotbarUI hb = PlayerHotbarUI.Instance;
                if (hb != null && hb.Player != null) _health = hb.Player.GetComponent<PlayerHealth>();
                return;
            }

            bool dead = _health.IsDead;
            if (dead && !IsShown)
            {
                IsShown = true;
                PlayerController pc = _health.GetComponent<PlayerController>();
                if (pc != null) pc.SetFrozen(true);   // замораживает управление и показывает курсор
            }
            else if (!dead && IsShown)
            {
                IsShown = false;
            }
        }

        private void EnsureStyles()
        {
            if (_title != null) return;

            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                if (f != null && f.name.ToLower().Contains("minecraft")) { _font = f; break; }

            _title = new GUIStyle { alignment = TextAnchor.MiddleCenter };
            _text = new GUIStyle { alignment = TextAnchor.MiddleLeft };
            _btn = new GUIStyle { alignment = TextAnchor.MiddleCenter };
            foreach (var s in new[] { _title, _text, _btn })
            {
                s.normal.textColor = Color.white;
                if (_font != null) s.font = _font;
            }
        }

        private static void Shadowed(Rect r, string s, GUIStyle st, Color c)
        {
            float o = Mathf.Max(2f, st.fontSize / 10f);
            GUI.contentColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), s, st);
            GUI.contentColor = c;
            GUI.Label(r, s, st);
            GUI.contentColor = Color.white;
        }

        private void OnGUI()
        {
            if (!IsShown || _health == null) return;

            GUI.depth = -10;   // поверх сердечек и хотбара
            EnsureStyles();

            float k = Mathf.Clamp(Screen.height / 720f, 0.7f, 2f);
            _title.fontSize = Mathf.RoundToInt(56f * k);
            _text.fontSize = Mathf.RoundToInt(24f * k);
            _btn.fontSize = Mathf.RoundToInt(20f * k);

            // красный фон
            Color old = GUI.color;
            GUI.color = new Color(0.31f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;

            float cx = Screen.width / 2f;
            float y = Screen.height * 0.22f;

            // заголовок
            Shadowed(new Rect(cx - 400f, y, 800f, 70f * k), "Вы погибли!", _title, Color.white);

            // счёт: «Счёт:» белым, число жёлтым
            y += 110f * k;
            string a = "Счёт:";
            string b = _health.Score.ToString();
            float gap = _text.fontSize * 0.5f;
            float wa = _text.CalcSize(new GUIContent(a)).x;
            float wb = _text.CalcSize(new GUIContent(b)).x;
            float x = cx - (wa + gap + wb) / 2f;
            float h = 36f * k;
            Shadowed(new Rect(x, y, wa, h), a, _text, Color.white);
            Shadowed(new Rect(x + wa + gap, y, wb, h), b, _text, new Color(1f, 1f, 0.33f));

            // кнопка
            y += 90f * k;
            float bw = Mathf.Min(400f * k, Screen.width * 0.6f);
            float bh = 40f * k;
            Rect br = new Rect(cx - bw / 2f, y, bw, bh);
            bool hover = br.Contains(Event.current.mousePosition);

            if (_btnNormal == null && Event.current.type == EventType.Repaint)
            {
                GUI.color = hover ? new Color(0.52f, 0.52f, 0.65f) : new Color(0.42f, 0.42f, 0.42f);
                GUI.DrawTexture(br, Texture2D.whiteTexture);
                GUI.color = old;
            }

            bool clicked = McGui.ButtonFrame(br, _btnNormal, _btnHover);
            Shadowed(br, "Возродиться", _btn, hover ? new Color(1f, 1f, 0.6f) : Color.white);

            if (clicked) _health.RequestRespawn();
        }
    }
}