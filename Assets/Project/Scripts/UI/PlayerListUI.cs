using System.Collections.Generic;
using UnityEngine;
using Mirror;
using MinecraftClone.Management;
using MinecraftClone.Networking;

namespace MinecraftClone.UI
{
    public class PlayerListUI : MonoBehaviour
    {
        [SerializeField] private int fontSize = 16;

        private const int RowsPerColumn = 20;
        private const float RefreshInterval = 0.25f;

        private readonly List<string> _names = new List<string>();
        private bool _visible;
        private float _refreshTimer;
        private GUIStyle _style;
        private Font _font;

        private void Update()
        {
            bool canShow = Input.GetKey(KeyCode.Tab)
                && (WorldManager.Instance == null || WorldManager.Instance.GameMode != WorldGameMode.Classic024SurvivalTest)
                && NetworkClient.active && NetworkClient.isConnected
                && !ChatUI.IsOpen
                && !RuntimeLoadingScreen.IsShown
                && (PauseMenuUI.Instance == null || !PauseMenuUI.Instance.IsPaused)
                && WorldManager.Instance != null && WorldManager.Instance.IsWorldReady;

            if (!canShow) { _visible = false; return; }

            if (!_visible) { _visible = true; _refreshTimer = RefreshInterval; } // обновить сразу
            _refreshTimer += Time.unscaledDeltaTime;
            if (_refreshTimer >= RefreshInterval)
            {
                _refreshTimer = 0f;
                Refresh();
            }
        }

        private void Refresh()
        {
            _names.Clear();
            foreach (NetworkIdentity ni in NetworkClient.spawned.Values)
            {
                if (ni == null) continue;
                NetworkPlayer p = ni.GetComponent<NetworkPlayer>();
                if (p != null && !string.IsNullOrEmpty(p.Nickname)) _names.Add(p.Nickname);
            }
            _names.Sort(System.StringComparer.OrdinalIgnoreCase);
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

            _style = new GUIStyle { fontSize = fontSize, clipping = TextClipping.Clip, wordWrap = false };
            _style.normal.textColor = Color.white;
            if (_font != null) _style.font = _font;
        }

        private void DrawText(Rect r, string text, TextAnchor anchor)
        {
            _style.alignment = anchor;
            GUI.contentColor = Color.black;
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _style);
            GUI.contentColor = Color.white;
            GUI.Label(r, text, _style);
        }

        private void OnGUI()
        {
            if (!_visible || _names.Count == 0 || Event.current.type != EventType.Repaint) return;
            EnsureStyle();

            int columns = Mathf.CeilToInt(_names.Count / (float)RowsPerColumn);
            int rows = Mathf.Min(_names.Count, RowsPerColumn);
            float rowH = fontSize + 8f;
            float colW = 220f;
            float titleH = fontSize + 12f;
            float width = columns * colW + 16f;
            float height = titleH + rows * rowH + 12f;
            float x0 = (Screen.width - width) / 2f;
            float y0 = 20f;

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(x0, y0, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            DrawText(new Rect(x0, y0 + 4f, width, titleH), $"Игроки в сети: {_names.Count}", TextAnchor.MiddleCenter);

            for (int i = 0; i < _names.Count; i++)
            {
                int col = i / RowsPerColumn;
                int row = i % RowsPerColumn;
                Rect r = new Rect(x0 + 8f + col * colW, y0 + titleH + 4f + row * rowH, colW - 8f, rowH);
                DrawText(r, _names[i], TextAnchor.MiddleCenter);
            }

            GUI.contentColor = Color.white;
        }
    }
}
