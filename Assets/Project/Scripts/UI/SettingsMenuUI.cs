using System;
using UnityEngine;
using MinecraftClone.Management;

namespace MinecraftClone.UI
{
    public class SettingsMenuUI : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        public static int ClosedFrame { get; private set; } = -1;

        private static SettingsMenuUI _inst;

        private enum Page { Main, Audio, Controls }

        private Page _page;
        private Action _onClose;
        private GameSettings.Bind? _rebinding;
        private string _msg = "";

        private GUIStyle _title, _label, _btn, _btnText, _btnShadow;
        private Font _font;

        private Texture2D _tNormal, _tHover;
        private Texture2D _track, _trackHover, _handle, _handleHover;

        public static void Open(Action onClose)
        {
            if (_inst == null)
            {
                var go = new GameObject("[Settings]");
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<SettingsMenuUI>();
            }
            _inst._page = Page.Main;
            _inst._onClose = onClose;
            _inst._rebinding = null;
            _inst._msg = "";
            IsOpen = true;
        }

        private void OnDestroy() { if (_inst == this) { _inst = null; IsOpen = false; } }

        private void Update()
        {
            if (!IsOpen || _rebinding.HasValue) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Back();
        }

        private void Back()
        {
            if (_page != Page.Main) { _page = Page.Main; _msg = ""; return; }

            GameSettings.SaveAll();
            IsOpen = false;
            ClosedFrame = Time.frameCount;
            var cb = _onClose;
            _onClose = null;
            cb?.Invoke();
        }

        // ---------- Рисование ----------

        private static Texture2D Tex(Color fill, Color border)
        {
            var t = new Texture2D(6, 6) { filterMode = FilterMode.Point };
            for (int y = 0; y < 6; y++)
                for (int x = 0; x < 6; x++)
                    t.SetPixel(x, y, (x == 0 || y == 0 || x == 5 || y == 5) ? border : fill);
            t.Apply();
            return t;
        }

        private bool Btn(Rect r, string text)
        {
            bool clicked = McGui.ButtonFrame(r, _tNormal, _tHover);            // только фон
            bool hover = r.Contains(Event.current.mousePosition);
            int fs = _btn.fontSize;
            _btnText.fontSize = fs;
            float maxW = r.width - 16f;
            float tw = _btnText.CalcSize(new GUIContent(text)).x;
            if (tw > maxW) fs = Mathf.Max(8, Mathf.FloorToInt(fs * maxW / tw));   // сжимаем, пока не влезет
            _btnText.fontSize = _btnShadow.fontSize = fs;
            float o = Mathf.Max(1f, _btn.fontSize / 9f);
            GUI.contentColor = new Color(0.25f, 0.25f, 0.25f);
            GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), text, _btnShadow);
            GUI.contentColor = hover ? new Color(1f, 1f, 0.6f) : Color.white;
            GUI.Label(r, text, _btnText);
            GUI.contentColor = Color.white;
            return clicked;
        }

        private void EnsureStyles()
        {
            if (_btn != null) return;

            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                if (f != null && f.name.ToLower().Contains("minecraft")) { _font = f; break; }

            var normal = Resources.Load<Texture2D>("UI/button") ?? Tex(new Color(0.42f, 0.42f, 0.42f), new Color(0.1f, 0.1f, 0.1f));
            var hover = Resources.Load<Texture2D>("UI/button_hover") ?? Tex(new Color(0.52f, 0.52f, 0.65f), new Color(0.1f, 0.1f, 0.1f));
            normal.filterMode = FilterMode.Point;
            hover.filterMode = FilterMode.Point;
            if (Resources.Load<Texture2D>("UI/button") == null) Debug.LogError("[Settings] UI/button НЕ найден, рисую кодом");

            _tNormal = normal;
            _tHover = hover;

            _btn = new GUIStyle { alignment = TextAnchor.MiddleCenter, border = new RectOffset(3, 3, 3, 3) };
            _btn.normal.background = normal; _btn.hover.background = hover; _btn.active.background = hover;
            _btn.normal.textColor = Color.clear; _btn.hover.textColor = Color.clear;

            _btnText = new GUIStyle(_btn);
            _btnText.normal.background = null; _btnText.hover.background = null; _btnText.active.background = null;
            _btnText.normal.textColor = Color.white; _btnText.hover.textColor = Color.white; _btnText.active.textColor = Color.white;
            _btnShadow = new GUIStyle(_btnText);

            _title = new GUIStyle { alignment = TextAnchor.MiddleCenter };
            _title.normal.textColor = Color.white;

            _label = new GUIStyle { alignment = TextAnchor.MiddleLeft };
            _label.normal.textColor = Color.white;

            if (_font != null) { _btn.font = _font; _btnText.font = _font; _btnShadow.font = _font; _title.font = _font; _label.font = _font; }

            _track = McGui.Load("slider_track"); _trackHover = McGui.Load("slider_track_hover");
            _handle = McGui.Load("slider_handle"); _handleHover = McGui.Load("slider_handle_hover");
        }

        private static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private float Slider(Rect r, float v, float k)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;

            float over = Mathf.Max(2f, Mathf.Round(r.height * 0.18f));   // на сколько ползунок выступает сверху и снизу
            float hh = r.height + over * 2f;                       // высота ползунка
            float hw = Mathf.Round(hh * 8f / 20f);                 // ширина с пропорциями текстуры 8x20
            Rect hit = new Rect(r.x, r.y - over, r.width, hh);
            float Calc() => Mathf.Clamp01((e.mousePosition.x - r.x - hw / 2f) / (r.width - hw));

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && hit.Contains(e.mousePosition)) { GUIUtility.hotControl = id; v = Calc(); e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id) { v = Calc(); e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
                case EventType.Repaint:
                    bool hot = GUIUtility.hotControl == id || hit.Contains(e.mousePosition);
                    McGui.DrawSliced(r, hot ? _trackHover : _track, 2f);

                    float hx = r.x + (r.width - hw) * v;                  // левый край ползунка

                    GUI.DrawTexture(new Rect(hx, r.y - over, hw, hh), hot ? _handleHover : _handle);
                    break;
            }
            return v;
        }

        private static Rect SliderRect(float cx, float bw, float y, float k)
            => new Rect(cx - bw * 0.35f, y, bw * 0.7f, 24f * k);

        private void OnGUI()
        {
            if (!IsOpen) return;
            GUI.depth = -1000;
            EnsureStyles();
            Event e = Event.current;

            // ожидание клавиши для переназначения
            if (_rebinding.HasValue && e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                if (e.keyCode != KeyCode.Escape) _msg = GameSettings.TryRebind(_rebinding.Value, e.keyCode);
                _rebinding = null;
                e.Use();
                return;
            }

            float k = Mathf.Clamp(Screen.height / 720f, 1f, 2.5f);
            _btn.fontSize = Mathf.RoundToInt(18 * k);
            _label.fontSize = Mathf.RoundToInt(18 * k);
            _title.fontSize = Mathf.RoundToInt(32 * k);

            if (e.type == EventType.Repaint) Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.65f));

            float bw = Mathf.Min(400f, Screen.width * 0.6f), bh = 42f, gap = 8f * k;
            float cx = Screen.width / 2f;
            float y = Screen.height * (_page == Page.Controls ? 0.06f : 0.18f);

            Rect Row() { var r = new Rect(cx - bw / 2f, y, bw, bh); y += bh + gap; return r; }

            string title = _page == Page.Main ? "Настройки"
                : _page == Page.Audio ? "Музыка и звуки" : "Управление";
            GUI.Label(new Rect(0, y, Screen.width, 44f * k), title, _title);
            y += 56f * k;

            switch (_page)
            {
                case Page.Main:
                    string vt = "Дальность прорисовки: " + GameSettings.ViewNames[GameSettings.ViewPreset];
                    if (Btn(Row(), vt))
                        GameSettings.SetViewPreset((GameSettings.ViewPreset + 1) % GameSettings.ViewNames.Length);

                    if (Btn(Row(), "Покачивание камеры: " + (GameSettings.ViewBobbing ? "Вкл" : "Выкл")))
                        GameSettings.SetViewBobbing(!GameSettings.ViewBobbing);

                    if (Btn(Row(), "Музыка и звуки")) _page = Page.Audio;
                    if (Btn(Row(), "Управление")) _page = Page.Controls;
                    y += gap * 2f;
                    if (Btn(Row(), "Готово")) Back();
                    break;

                case Page.Audio:
                    AudioRow("Общая громкость", GameSettings.Master, GameSettings.SetMaster, cx, bw, bh, gap, k, ref y);
                    AudioRow("Блоки", GameSettings.Blocks, GameSettings.SetBlocks, cx, bw, bh, gap, k, ref y);
                    AudioRow("Музыка", GameSettings.Music, GameSettings.SetMusic, cx, bw, bh, gap, k, ref y);
                    y += gap;
                    if (Btn(Row(), "Назад")) Back();
                    break;

                case Page.Controls:
                    for (int i = 0; i < GameSettings.BindNames.Length; i++)
                    {
                        var b = (GameSettings.Bind)i;
                        Rect row = Row();
                        GUI.Label(new Rect(row.x, row.y, row.width * 0.5f, row.height), GameSettings.BindNames[i], _label);
                        string kt = _rebinding == b ? "> ... <" : GameSettings.Key(b).ToString();
                        if (Btn(new Rect(row.x + row.width * 0.5f, row.y, row.width * 0.5f, row.height), kt))
                        {
                            _rebinding = b;
                            _msg = "";
                        }
                    }

                    float sv = (GameSettings.Sensitivity - 0.25f) / 2.75f;
                    GUI.Label(new Rect(cx - bw * 0.35f, y, bw * 0.7f, bh * 0.7f),
                        "Чувствительность мыши: " + Mathf.RoundToInt(GameSettings.Sensitivity * 100f) + "%", _label);
                    y += bh * 0.7f;
                    float nv = Slider(SliderRect(cx, bw, y, k), sv, k);
                    if (nv != sv) GameSettings.SetSensitivity(0.25f + nv * 2.75f);
                    y += 24f * k + gap * 2f;

                    GUI.Label(new Rect(cx - bw / 2f, y, bw, bh * 0.8f),
                        _rebinding.HasValue ? "Нажмите клавишу (Esc: отмена)" : _msg, _label);
                    y += bh * 0.8f + gap;

                    if (Btn(Row(), "Сбросить клавиши")) { GameSettings.ResetKeys(); _msg = ""; }
                    if (Btn(Row(), "Назад")) Back();
                    break;
            }
        }

        private void AudioRow(string name, float value, Action<float> set,
                            float cx, float bw, float bh, float gap, float k, ref float y)
        {
            GUI.Label(new Rect(cx - bw * 0.35f, y, bw * 0.7f, bh * 0.7f), $"{name}: {Mathf.RoundToInt(value * 100f)}%", _label);
            y += bh * 0.7f;
            float nv = Slider(SliderRect(cx, bw, y, k), value, k);
            if (nv != value) set(nv);
            y += 24f * k + gap * 2.5f;
        }
    }
}