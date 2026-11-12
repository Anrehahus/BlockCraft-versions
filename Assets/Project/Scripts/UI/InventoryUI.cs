using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.Items;
using MinecraftClone.Player;

namespace MinecraftClone.UI
{
    // Инвентарь-склад: 27 слотов + хотбар. Креатив (PlayerBlockInteraction.CreativeInventory = true)
    // возвращает старую палитру всех блоков вместо склада.
    public class InventoryUI : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        public static int ClosedFrame { get; private set; } = -1;

        private const int Cols = 9;
        private const float Pad = 7f, Cell = 18f, Gap = 8f;
        private const float PanelW = 176f;

        private static bool Creative => PlayerBlockInteraction.CreativeInventory;
        private static int Rows => Creative ? 6 : PlayerInventory.MainSize / Cols;
        private static float PanelH => Pad + Rows * Cell + Gap + Cell + Pad;

        private readonly List<int> _palette = new List<int>();   // палитра креатива: номера предметов
        private int _scrollRow;
        private ItemStack _cursor;                                           // стак «в руке» у курсора
        private GUIStyle _style;
        private Font _font;

        private static bool Allowed()
        {
            if (DeathScreenUI.IsShown) return false;
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

            if (Creative)
            {
                int maxScroll = Mathf.Max(0, Mathf.CeilToInt(_palette.Count / (float)Cols) - Rows);
                float wheel = Input.GetAxis("Mouse ScrollWheel");
                if (wheel > 0f) _scrollRow = Mathf.Max(0, _scrollRow - 1);
                else if (wheel < 0f) _scrollRow = Mathf.Min(maxScroll, _scrollRow + 1);
            }
        }

        private void Open()
        {
            _palette.Clear();
            if (Creative)
            {
                // все предметы из реестра: блоки и остальные
                foreach (ItemDef def in ItemRegistry.All) _palette.Add(def.Id);
            }

            _scrollRow = 0;
            _cursor = default;
            IsOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Close()
        {
            ReturnCursor();
            IsOpen = false;
            ClosedFrame = Time.frameCount;
        }

        // Стак из руки возвращается на склад (при закрытии инвентаря)
        private void ReturnCursor()
        {
            if (!_cursor.IsEmpty && !Creative) PlayerInventory.Add(_cursor.Id, _cursor.Count);
            _cursor = default;
        }

        private void OnDisable() { if (IsOpen) ReturnCursor(); IsOpen = false; }
        private void OnDestroy() { if (IsOpen) ReturnCursor(); IsOpen = false; }

        // ---------- Клики по слотам ----------

        private void LeftClick(int i)
        {
            if (Creative)
            {
                if (!_cursor.IsEmpty) { PlayerInventory.Set(i, _cursor); _cursor = default; }
                return;
            }

            ItemStack s = PlayerInventory.Get(i);

            if (_cursor.IsEmpty)
            {
                if (s.IsEmpty) return;
                _cursor = s;                                   // взять весь стак
                PlayerInventory.Set(i, default);
            }
            else if (s.IsEmpty)
            {
                PlayerInventory.Set(i, _cursor);               // положить
                _cursor = default;
            }
            else if (s.Id == _cursor.Id)
            {
                int move = Mathf.Min(_cursor.Count, ItemRegistry.MaxStackOf(s.Id) - s.Count);   // слить в стак
                s.Count += move;
                _cursor.Count -= move;
                PlayerInventory.Set(i, s);
                if (_cursor.Count <= 0) _cursor = default;
            }
            else
            {
                PlayerInventory.Set(i, _cursor);               // поменять местами
                _cursor = s;
            }
        }

        private void RightClick(int i)
        {
            if (Creative)
            {
                PlayerInventory.Set(i, default);
                return;
            }

            ItemStack s = PlayerInventory.Get(i);

            if (_cursor.IsEmpty)
            {
                if (s.IsEmpty) return;
                int take = (s.Count + 1) / 2;                  // взять половину
                _cursor = new ItemStack(s.Id, take);
                s.Count -= take;
                PlayerInventory.Set(i, s);
            }
            else if (s.IsEmpty)
            {
                PlayerInventory.Set(i, new ItemStack(_cursor.Id, 1));   // положить один
                _cursor.Count--;
                if (_cursor.Count <= 0) _cursor = default;
            }
            else if (s.Id == _cursor.Id)
            {
                if (s.Count >= ItemRegistry.MaxStackOf(s.Id)) return;
                s.Count++;
                PlayerInventory.Set(i, s);
                _cursor.Count--;
                if (_cursor.Count <= 0) _cursor = default;
            }
            else
            {
                PlayerInventory.Set(i, _cursor);               // другой предмет: поменять местами
                _cursor = s;
            }
        }

        // ---------- Рисование ----------

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

        private static void DrawSlot(Rect r, float s)
        {
            Fill(r, new Color(0.55f, 0.55f, 0.55f, 1f));
            Fill(new Rect(r.x, r.y, r.width, s), new Color(0.22f, 0.22f, 0.22f, 1f));
            Fill(new Rect(r.x, r.y, s, r.height), new Color(0.22f, 0.22f, 0.22f, 1f));
            Fill(new Rect(r.x, r.yMax - s, r.width, s), Color.white);
            Fill(new Rect(r.xMax - s, r.y, s, r.height), Color.white);
        }

        private static Rect CellRect(float x0, float y0, float s, int col, float yUnits)
        {
            return new Rect(x0 + (Pad + Cell * col) * s, y0 + yUnits * s, Cell * s, Cell * s);
        }

        // Что под курсором: слот склада/хотбара или предмет палитры (креатив)
        private void FindHover(Vector2 m, float x0, float y0, float s, out int slot, out int palette)
        {
            slot = -1; palette = -1;
            float hotbarY = Pad + Rows * Cell + Gap;

            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    if (!CellRect(x0, y0, s, c, Pad + Cell * r).Contains(m)) continue;

                    if (Creative)
                    {
                        int idx = (r + _scrollRow) * Cols + c;
                        if (idx < _palette.Count) palette = idx;
                    }
                    else slot = PlayerInventory.HotbarSize + r * Cols + c;
                    return;
                }

            for (int c = 0; c < PlayerInventory.HotbarSize; c++)
                if (CellRect(x0, y0, s, c, hotbarY).Contains(m)) { slot = c; return; }
        }

        private void DrawStack(PlayerHotbarUI hb, WorldManager wm, Rect cell, float s, int itemId, int count)
        {
            Texture2D icon = hb.GetItemIcon(itemId, wm);
            Rect ir = new Rect(cell.x + s, cell.y + s, 16f * s, 16f * s);
            if (icon != null) GUI.DrawTexture(ir, icon, ScaleMode.StretchToFill, true);
            PlayerHotbarUI.DrawStackCount(ir, count, s);
        }

        private void OnGUI()
        {
            if (!IsOpen) return;

            var hb = PlayerHotbarUI.Instance;
            var wm = WorldManager.Instance;
            if (hb == null || hb.Player == null || wm == null || !ItemRegistry.Ready) return;

            GUI.depth = 5;
            Event e = Event.current;

            float s = Mathf.Clamp(Mathf.FloorToInt(Screen.height / 200f), 2, 8);
            float x0 = Mathf.Round((Screen.width - PanelW * s) / 2f);
            float y0 = Mathf.Round((Screen.height - PanelH * s) / 2f);
            float hotbarY = Pad + Rows * Cell + Gap;

            // ---------- Клики ----------
            if (e.type == EventType.MouseDown)
            {
                FindHover(e.mousePosition, x0, y0, s, out int slot, out int palette);

                if (palette >= 0 && e.button == 0) _cursor = new ItemStack(_palette[palette], 1);
                else if (slot >= 0)
                {
                    if (e.button == 0) LeftClick(slot);
                    else if (e.button == 1) RightClick(slot);
                }
                else if (Creative && e.button == 1) _cursor = default;   // ПКМ мимо слотов: убрать «призрак»

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
            FindHover(mouse, x0, y0, s, out int hoverSlot, out int hoverPalette);

            int hoverId = 0;

            // верхняя часть: склад (27 слотов) или палитра креатива
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    Rect cell = CellRect(x0, y0, s, c, Pad + Cell * r);
                    DrawSlot(cell, s);

                    int itemId = 0;
                    int count = 1;
                    bool hovered = false;

                    if (Creative)
                    {
                        int idx = (r + _scrollRow) * Cols + c;
                        if (idx < _palette.Count) { itemId = _palette[idx]; hovered = hoverPalette == idx; }
                    }
                    else
                    {
                        int si = PlayerInventory.HotbarSize + r * Cols + c;
                        ItemStack st = PlayerInventory.Get(si);
                        if (!st.IsEmpty) { itemId = st.Id; count = st.Count; }
                        hovered = hoverSlot == si;
                    }

                    if (itemId > 0) DrawStack(hb, wm, cell, s, itemId, count);

                    if (hovered)
                    {
                        Fill(new Rect(cell.x + s, cell.y + s, cell.width - 2 * s, cell.height - 2 * s), new Color(1f, 1f, 1f, 0.35f));
                        if (itemId > 0) hoverId = itemId;
                    }
                }

            // хотбар
            for (int c = 0; c < PlayerInventory.HotbarSize; c++)
            {
                Rect cell = CellRect(x0, y0, s, c, hotbarY);
                DrawSlot(cell, s);

                ItemStack st = PlayerInventory.Get(c);
                if (!st.IsEmpty) DrawStack(hb, wm, cell, s, st.Id, st.Count);

                if (hoverSlot == c)
                {
                    Fill(new Rect(cell.x + s, cell.y + s, cell.width - 2 * s, cell.height - 2 * s), new Color(1f, 1f, 1f, 0.35f));
                    if (!st.IsEmpty) hoverId = st.Id;
                }
            }

            // стак у курсора
            if (!_cursor.IsEmpty)
            {
                Texture2D icon = hb.GetItemIcon(_cursor.Id, wm);
                Rect ir = new Rect(mouse.x - 8f * s, mouse.y - 8f * s, 16f * s, 16f * s);
                if (icon != null)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.9f);
                    GUI.DrawTexture(ir, icon, ScaleMode.StretchToFill, true);
                    GUI.color = Color.white;
                }
                PlayerHotbarUI.DrawStackCount(ir, _cursor.Count, s);
            }
            else if (hoverId > 0 && ItemRegistry.Get(hoverId) != null)
            {
                EnsureStyle();
                string name = ItemRegistry.Get(hoverId).Name;
                Vector2 size = _style.CalcSize(new GUIContent(name));
                Rect tip = new Rect(mouse.x + 14f, mouse.y - 8f, size.x + 12f, size.y + 6f);
                Fill(tip, new Color(0.1f, 0f, 0.1f, 0.9f));
                GUI.Label(new Rect(tip.x + 6f, tip.y + 3f, size.x, size.y), name, _style);
            }
        }
    }
}