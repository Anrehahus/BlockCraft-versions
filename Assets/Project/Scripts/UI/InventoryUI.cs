using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.Player;
using MinecraftClone.ScriptableObjects;

namespace MinecraftClone.UI
{
    public class InventoryUI : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        public static int ClosedFrame { get; private set; } = -1;

        private const int Cols = 9, Rows = 6;
        private const float Pad = 7f, Cell = 18f, Gap = 8f;
        private const float PanelW = 176f;
        private const float PanelH = Pad + Rows * Cell + Gap + Cell + Pad;

        private readonly List<BlockType> _blocks = new List<BlockType>();
        private int _scrollRow;
        private bool _hasHeld;
        private BlockType _held;
        private GUIStyle _style;
        private Font _font;

        private static bool Allowed()
        {
            if (ChatUI.IsOpen || RuntimeLoadingScreen.IsShown) return false;
            if (Time.timeScale == 0f) return false;
            if (PauseMenuUI.Instance != null && PauseMenuUI.Instance.IsPaused) return false;
            if (WorldManager.Instance == null || !WorldManager.Instance.IsWorldReady) return false;
            return PlayerHotbarUI.Instance != null && PlayerHotbarUI.Instance.Player != null;
        }

        private void Update()
        {
            if (!IsOpen)
            {
                if (GameSettings.Pressed(GameSettings.Bind.Inventory) && Allowed()) Open();
                return;
            }

            if (GameSettings.Pressed(GameSettings.Bind.Inventory) || Input.GetKeyDown(KeyCode.Escape) || !Allowed())
            {
                Close();
                return;
            }

            int maxScroll = Mathf.Max(0, Mathf.CeilToInt(_blocks.Count / (float)Cols) - Rows);
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (wheel > 0f) _scrollRow = Mathf.Max(0, _scrollRow - 1);
            else if (wheel < 0f) _scrollRow = Mathf.Min(maxScroll, _scrollRow + 1);
        }

        private void Open()
        {
            _blocks.Clear();
            var db = WorldManager.Instance.BlockDatabase;
            foreach (BlockType t in System.Enum.GetValues(typeof(BlockType)))
            {
                if (t == BlockType.Air || t == BlockType.Bedrock || t == BlockType.Water || t == BlockType.Lava) continue;
                if (db != null && db.ContainsKey(t)) _blocks.Add(t);
            }

            _scrollRow = 0;
            _hasHeld = false;
            IsOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Close()
        {
            IsOpen = false;
            _hasHeld = false;
            ClosedFrame = Time.frameCount;
        }

        private void OnDisable() { IsOpen = false; }
        private void OnDestroy() { IsOpen = false; }

        private static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private void EnsureStyle()
        {
            if (_style != null) return;
            if (_font == null)
                foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                    if (f != null && f.name.ToLower().Contains("minecraft")) { _font = f; break; }

            _style = new GUIStyle { fontSize = 16, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _style.normal.textColor = Color.white;
            if (_font != null) _style.font = _font;
        }

        private void DrawSlot(Rect r, float s)
        {
            Fill(r, new Color(0.55f, 0.55f, 0.55f, 1f));
            Fill(new Rect(r.x, r.y, r.width, s), new Color(0.22f, 0.22f, 0.22f, 1f));
            Fill(new Rect(r.x, r.y, s, r.height), new Color(0.22f, 0.22f, 0.22f, 1f));
            Fill(new Rect(r.x, r.yMax - s, r.width, s), Color.white);
            Fill(new Rect(r.xMax - s, r.y, s, r.height), Color.white);
        }

        private void OnGUI()
        {
            if (!IsOpen) return;

            var hb = PlayerHotbarUI.Instance;
            var wm = WorldManager.Instance;
            if (hb == null || hb.Player == null || wm == null || wm.BlockDatabase == null) return;

            GUI.depth = 5;
            Event e = Event.current;

            float s = Mathf.Clamp(Mathf.FloorToInt(Screen.height / 200f), 2, 8);
            float x0 = Mathf.Round((Screen.width - PanelW * s) / 2f);
            float y0 = Mathf.Round((Screen.height - PanelH * s) / 2f);
            float hotbarY = Pad + Rows * Cell + Gap;

            Rect GridRect(int c, int r) => new Rect(x0 + (Pad + Cell * c) * s, y0 + (Pad + Cell * r) * s, Cell * s, Cell * s);
            Rect HotRect(int c) => new Rect(x0 + (Pad + Cell * c) * s, y0 + hotbarY * s, Cell * s, Cell * s);

            // ---------- Клики ----------
            if (e.type == EventType.MouseDown)
            {
                bool handled = false;

                if (e.button == 0)
                {
                    for (int r = 0; r < Rows && !handled; r++)
                        for (int c = 0; c < Cols; c++)
                        {
                            int idx = (r + _scrollRow) * Cols + c;
                            if (idx < _blocks.Count && GridRect(c, r).Contains(e.mousePosition))
                            {
                                _held = _blocks[idx];
                                _hasHeld = true;
                                handled = true;
                                break;
                            }
                        }

                    if (!handled && _hasHeld)
                    {
                        for (int c = 0; c < PlayerBlockInteraction.HotbarBlocks.Length; c++)
                            if (HotRect(c).Contains(e.mousePosition))
                            {
                                PlayerBlockInteraction.HotbarBlocks[c] = _held;
                                _hasHeld = false;
                                handled = true;
                                break;
                            }
                    }
                }

                if (!handled) _hasHeld = false; // клик мимо слота или ПКМ: убрать "призрак"
                e.Use();
                return;
            }

            if (e.type != EventType.Repaint) return;

            // ---------- Отрисовка ----------
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.5f));

            Rect panel = new Rect(x0, y0, PanelW * s, PanelH * s);
            Fill(panel, Color.black);
            Fill(new Rect(panel.x + s, panel.y + s, panel.width - 2 * s, panel.height - 2 * s), new Color(0.78f, 0.78f, 0.78f, 1f));
            Fill(new Rect(panel.x + s, panel.y + s, panel.width - 2 * s, s), Color.white);
            Fill(new Rect(panel.x + s, panel.y + s, s, panel.height - 2 * s), Color.white);
            Fill(new Rect(panel.x + s, panel.yMax - 2 * s, panel.width - 2 * s, s), new Color(0.35f, 0.35f, 0.35f, 1f));
            Fill(new Rect(panel.xMax - 2 * s, panel.y + s, s, panel.height - 2 * s), new Color(0.35f, 0.35f, 0.35f, 1f));

            Vector2 mouse = e.mousePosition;
            bool hoverFound = false;
            BlockType hoverType = BlockType.Air;

            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    Rect cell = GridRect(c, r);
                    DrawSlot(cell, s);

                    int idx = (r + _scrollRow) * Cols + c;
                    if (idx >= _blocks.Count) continue;

                    Texture2D icon = hb.GetIcon(_blocks[idx], wm);
                    if (icon != null)
                        GUI.DrawTexture(new Rect(cell.x + s, cell.y + s, 16f * s, 16f * s), icon, ScaleMode.StretchToFill, true);

                    if (cell.Contains(mouse))
                    {
                        Fill(new Rect(cell.x + s, cell.y + s, cell.width - 2 * s, cell.height - 2 * s), new Color(1f, 1f, 1f, 0.35f));
                        hoverFound = true;
                        hoverType = _blocks[idx];
                    }
                }

            for (int c = 0; c < PlayerBlockInteraction.HotbarBlocks.Length; c++)
            {
                Rect cell = HotRect(c);
                DrawSlot(cell, s);

                Texture2D icon = hb.GetIcon(PlayerBlockInteraction.HotbarBlocks[c], wm);
                if (icon != null)
                    GUI.DrawTexture(new Rect(cell.x + s, cell.y + s, 16f * s, 16f * s), icon, ScaleMode.StretchToFill, true);

                if (_hasHeld && cell.Contains(mouse))
                    Fill(new Rect(cell.x + s, cell.y + s, cell.width - 2 * s, cell.height - 2 * s), new Color(1f, 1f, 1f, 0.35f));
            }

            // "призрак" блока у курсора
            if (_hasHeld)
            {
                Texture2D icon = hb.GetIcon(_held, wm);
                if (icon != null)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.85f);
                    GUI.DrawTexture(new Rect(mouse.x - 8f * s, mouse.y - 8f * s, 16f * s, 16f * s), icon, ScaleMode.StretchToFill, true);
                    GUI.color = Color.white;
                }
            }
            else if (hoverFound && wm.BlockDatabase.TryGetValue(hoverType, out BlockDataSO d) && d != null)
            {
                EnsureStyle();
                string name = string.IsNullOrEmpty(d.BlockName) ? hoverType.ToString() : d.BlockName;
                Vector2 size = _style.CalcSize(new GUIContent(name));
                Rect tip = new Rect(mouse.x + 14f, mouse.y - 8f, size.x + 12f, size.y + 6f);
                Fill(tip, new Color(0.1f, 0f, 0.1f, 0.9f));
                GUI.Label(new Rect(tip.x + 6f, tip.y + 3f, size.x, size.y), name, _style);
            }
        }
    }
}