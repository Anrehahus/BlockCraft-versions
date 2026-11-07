using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;
using MinecraftClone.Management;
using MinecraftClone.Networking;

namespace MinecraftClone.UI
{
    public class ChatUI : MonoBehaviour
    {
        public static ChatUI Instance { get; private set; }
        public static bool IsOpen { get; private set; }

        [SerializeField] private string mainMenuSceneName = "MainMenu";
        [SerializeField] private int fontSize = 16;

        private const int MaxLength = 100;
        private const float VisibleSeconds = 10f;

        private struct Line { public string Text; public float Time; public Color Color; }

        private readonly List<Line> _lines = new List<Line>();
        private readonly List<string> _history = new List<string>();
        private int _historyIndex = -1;
        private string _input = "";
        private int _openedFrame = -1;

        private string _tabHead = "";
        private string _tabLast = null;
        private readonly List<string> _tabNames = new List<string>();
        private int _tabIndex = -1;

        private GUIStyle _style;
        private Font _font;

        private static Color KindColor(byte kind)
        {
            switch (kind)
            {
                case 1: return new Color(1f, 1f, 0.33f);      // жёлтый
                case 2: return new Color(1f, 0.33f, 0.33f);   // красный
                case 3: return new Color(0.67f, 0.67f, 0.67f);// серый
                default: return Color.white;
            }
        }

        public static void AddMessage(string text, byte kind = 0)
        {
            if (Instance == null) new GameObject("[Chat]").AddComponent<ChatUI>();
            Instance.Push(text, kind);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) { Instance = null; IsOpen = false; }
        }

        private void OnDisable()
        {
            if (Instance == this) IsOpen = false;
        }

        private void Push(string text, byte kind = 0)
        {
            _lines.Add(new Line { Text = text, Time = Time.unscaledTime, Color = KindColor(kind) });
            if (_lines.Count > 100) _lines.RemoveAt(0);
        }

        private bool CanOpen()
        {
            if (InventoryUI.IsOpen) return false;
            if (DeathScreenUI.IsShown) return false;
            if (RuntimeLoadingScreen.IsShown) return false;
            if (Time.timeScale == 0f) return false;
            if (PauseMenuUI.Instance != null && PauseMenuUI.Instance.IsPaused) return false;
            return WorldManager.Instance != null && WorldManager.Instance.IsWorldReady;
        }

        private void Open(string prefix)
        {
            IsOpen = true;
            _input = prefix;
            _historyIndex = -1;
            _openedFrame = Time.frameCount;
            _tabLast = null;
        }

        private void Close()
        {
            IsOpen = false;
            _input = "";
            _historyIndex = -1;
            _tabLast = null;
        }

        private void Update()
        {
            if (SceneManager.GetActiveScene().name == mainMenuSceneName) return;

            if (!IsOpen && Input.GetKeyDown(KeyCode.T) && CanOpen())
            {
                Open("");
            }

            // Если во время набора появилась пауза или экран загрузки, закрываем чат
            if (IsOpen && !CanOpen()) Close();
        }

        private void Submit()
        {
            string text = _input.Trim();
            Close();
            if (text.Length == 0) return;

            _history.Add(text);
            if (_history.Count > 50) _history.RemoveAt(0);

            if (text.StartsWith("/"))
            {
                if (NetworkClient.active && NetworkClient.isConnected)
                    NetworkClient.Send(new ChatSendMessage { Text = text });
                else
                    Push("Команды доступны только в сетевой игре", 2);
                return;
            }

            if (NetworkClient.active && NetworkClient.isConnected)
            {
                NetworkClient.Send(new ChatSendMessage { Text = text });
            }
            else
            {
                Push($"<{PlayerPrefs.GetString("PlayerNickname", "Player")}> {text}");
            }
        }

        private void BrowseHistory(int dir)
        {
            if (_history.Count == 0) return;

            if (_historyIndex == -1)
            {
                if (dir > 0) return;
                _historyIndex = _history.Count;
            }

            _historyIndex += dir;
            if (_historyIndex < 0) _historyIndex = 0;

            if (_historyIndex >= _history.Count)
            {
                _historyIndex = -1;
                _input = "";
                return;
            }

            _input = _history[_historyIndex];
            _tabLast = null;
        }

        private void CompleteNick()
        {
            // новый цикл, если текст изменили после прошлого Tab
            if (_tabLast == null || _input != _tabLast)
            {
                int sp = _input.LastIndexOf(' ');
                _tabHead = _input.Substring(0, sp + 1);
                string partial = _input.Substring(sp + 1);

                _tabNames.Clear();
                if (NetworkClient.active && NetworkClient.isConnected)
                {
                    foreach (NetworkIdentity ni in NetworkClient.spawned.Values)
                    {
                        if (ni == null) continue;
                        var p = ni.GetComponent<NetworkPlayer>();
                        if (p != null && !string.IsNullOrEmpty(p.Nickname)
                            && p.Nickname.StartsWith(partial, System.StringComparison.OrdinalIgnoreCase))
                            _tabNames.Add(p.Nickname);
                    }
                    _tabNames.Sort(System.StringComparer.OrdinalIgnoreCase);
                }
                _tabIndex = -1;
            }

            if (_tabNames.Count == 0) return;

            _tabIndex = (_tabIndex + 1) % _tabNames.Count;
            string res = _tabHead + _tabNames[_tabIndex];
            if (res.Length > MaxLength) return;
            _input = res;
            _tabLast = res;
        }

        private void HandleKeys(Event e)
        {
            if (e.type != EventType.KeyDown) return;

            // События той же клавиши, которой открыли чат (T), игнорируем
            if (Time.frameCount <= _openedFrame) { e.Use(); return; }

            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Submit(); e.Use(); return;
                case KeyCode.Escape:
                    Close(); e.Use(); return;
                case KeyCode.Backspace:
                    if (_input.Length > 0) _input = _input.Substring(0, _input.Length - 1);
                    e.Use(); return;
                case KeyCode.Tab:
                    CompleteNick(); e.Use(); return;
                case KeyCode.UpArrow:
                    BrowseHistory(-1); e.Use(); return;
                case KeyCode.DownArrow:
                    BrowseHistory(1); e.Use(); return;
            }

            char c = e.character;
            if (c >= ' ' && c != (char)127 && _input.Length < MaxLength)
            {
                _input += c;
                _tabLast = null;
                e.Use();
            }
        }

        private void EnsureStyle()
        {
            if (_style != null) return;

            if (_font == null)
            {
                foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                {
                    if (f != null && f.name.ToLower().Contains("minecraft")) { _font = f; break; }
                }
            }

            _style = new GUIStyle
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                wordWrap = false
            };
            _style.normal.textColor = Color.white;
            if (_font != null) _style.font = _font;
        }

        private void OnGUI()
        {
            if (SceneManager.GetActiveScene().name == mainMenuSceneName) return;
            if (RuntimeLoadingScreen.IsShown) return;

            EnsureStyle();
            Event e = Event.current;

            if (IsOpen)
            {
                HandleKeys(e);
            }
            else if (e.type == EventType.KeyDown && e.character == '/' && CanOpen())
            {
                Open("/");
                e.Use();
            }

            if (e.type != EventType.Repaint) return;

            DrawLines();
            if (IsOpen) DrawInput();
        }

        private void DrawLines()
        {
            float now = Time.unscaledTime;
            int maxLines = IsOpen ? 20 : 10;
            float lineH = fontSize + 6f;
            float width = Mathf.Min(Screen.width - 12f, 960f);

            float inputH = fontSize + 8f;
            float baseY = IsOpen ? Screen.height - inputH - 8f
                                 : Screen.height - PlayerHotbarUI.BarHeight - 12f;
            int drawn = 0;

            for (int i = _lines.Count - 1; i >= 0 && drawn < maxLines; i--)
            {
                float age = now - _lines[i].Time;
                float alpha = 1f;

                if (!IsOpen)
                {
                    if (age > VisibleSeconds) break;
                    alpha = Mathf.Clamp01(VisibleSeconds - age); // гаснет в последнюю секунду
                }

                float y = baseY - (drawn + 1) * lineH;
                Rect r = new Rect(4f, y, width, lineH);

                GUI.color = new Color(0f, 0f, 0f, 0.45f * alpha);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;

                GUI.contentColor = new Color(0f, 0f, 0f, alpha);
                GUI.Label(new Rect(r.x + 5f, r.y + 1f, r.width, r.height), _lines[i].Text, _style);

                Color lc = _lines[i].Color;
                GUI.contentColor = new Color(lc.r, lc.g, lc.b, alpha);
                GUI.Label(new Rect(r.x + 4f, r.y, r.width, r.height), _lines[i].Text, _style);

                drawn++;
            }

            GUI.contentColor = Color.white;
            GUI.color = Color.white;
        }

        private void DrawInput()
        {
            float h = fontSize + 8f;
            Rect r = new Rect(2f, Screen.height - h - 4f, Screen.width - 4f, h);

            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;

            bool blink = ((int)(Time.unscaledTime * 2f)) % 2 == 0;
            string shown = _input + (blink ? "_" : "");

            GUI.contentColor = Color.black;
            GUI.Label(new Rect(r.x + 5f, r.y + 1f, r.width, r.height), shown, _style);
            GUI.contentColor = Color.white;
            GUI.Label(new Rect(r.x + 4f, r.y, r.width, r.height), shown, _style);
        }
    }
}