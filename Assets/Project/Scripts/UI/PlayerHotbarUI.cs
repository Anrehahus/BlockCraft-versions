using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.Player;
using MinecraftClone.ScriptableObjects;

namespace MinecraftClone.UI
{
    public class PlayerHotbarUI : MonoBehaviour
    {
        private const int IconSize = 64;

        public static PlayerHotbarUI Instance { get; private set; }
        public PlayerBlockInteraction Player => _player;

        public static int Scale => Mathf.Clamp(Mathf.FloorToInt(Screen.height / 200f), 2, 10);
        public static float BarHeight => 22f * Scale;

        private readonly Dictionary<BlockType, Texture2D> _icons = new Dictionary<BlockType, Texture2D>();
        private PlayerBlockInteraction _player;
        private float _findTimer;

        private Texture2D _bar, _sel;

        private void Awake()
        {
            Instance = this;

            _bar = Resources.Load<Texture2D>("UI/hotbar");
            _sel = Resources.Load<Texture2D>("UI/hotbar_selection");
            if (_bar != null) _bar.filterMode = FilterMode.Point;
            if (_sel != null) _sel.filterMode = FilterMode.Point;
        }

        private void Update()
        {
            if (_player != null && _player.enabled) return;

            _findTimer -= Time.unscaledDeltaTime;
            if (_findTimer > 0f) return;
            _findTimer = 0.5f;

            // чужие копии игроков в сети имеют выключенный компонент, берём включённый
            _player = null;
            foreach (var p in FindObjectsByType<PlayerBlockInteraction>(FindObjectsSortMode.None))
            {
                if (p.enabled) { _player = p; break; }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            foreach (var t in _icons.Values) if (t != null) Destroy(t);
            _icons.Clear();
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (InventoryUI.IsOpen) return; // в инвентаре рисуется свой хотбар
            if (_player == null || RuntimeLoadingScreen.IsShown) return;

            WorldManager wm = WorldManager.Instance;
            if (wm == null || wm.BlockDatabase == null || !wm.IsWorldReady) return;

            GUI.depth = 10; // под меню паузы и чатом

            if (_bar == null || _sel == null) return; // нет текстур в Resources/UI

            float u = Scale;                                // один "пиксель" оригинала на экране
            float barH = 22f * u;
            float barW = barH * _bar.width / _bar.height;      // ширина берётся из пропорций вашей текстуры
            float k = barW / 182f;                           // горизонтальный шаг
            float x0 = (Screen.width - barW) / 2f;
            float y0 = Screen.height - barH;

            GUI.DrawTexture(new Rect(x0, y0, barW, barH), _bar, ScaleMode.StretchToFill, true);

            for (int i = 0; i < PlayerBlockInteraction.HotbarBlocks.Length; i++)
            {
                Texture2D icon = GetIcon(PlayerBlockInteraction.HotbarBlocks[i], wm);
                if (icon == null) continue;
                float cx = x0 + (3f + 20f * i + 8f) * k;        // центр слота
                float cy = y0 + 11f * u;
                float s = 14f * u;                               // иконка чуть меньше слота
                GUI.DrawTexture(new Rect(cx - s / 2f, cy - s / 2f, s, s), icon, ScaleMode.StretchToFill, true);
            }

            int sel = _player.SelectedSlot;
            float selX = x0 + (3f + 20f * sel + 8f) * k;
            GUI.DrawTexture(new Rect(selX - 12f * u, y0 - u, 24f * u, 24f * u), _sel, ScaleMode.StretchToFill, true);
        }

        // ---------- Иконки ----------

        public Texture2D GetIcon(BlockType type, WorldManager wm)
        {
            if (_icons.TryGetValue(type, out Texture2D cached)) return cached;

            Texture2D icon = null;
            if (wm.BlockDatabase.TryGetValue(type, out BlockDataSO d) && d != null)
            {
                Texture2D top = d.TopTexture != null ? d.TopTexture : d.SideTexture;
                Texture2D side = d.SideTexture != null ? d.SideTexture : d.TopTexture;
                if (top != null && side != null)
                {
                    try { icon = BuildIcon(type, top, side); }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[Hotbar] Иконка {type}: включите Read/Write у текстуры. {e.Message}");
                    }
                }
            }

            _icons[type] = icon;
            return icon;
        }

        private static Texture2D BuildIcon(BlockType type, Texture2D top, Texture2D side)
        {
            Color32[] px = new Color32[IconSize * IconSize];

            if (type.IsPlant()) DrawFlat(px, top);
            else DrawCube(px, top, side);

            Texture2D tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        private static void DrawFlat(Color32[] px, Texture2D src)
        {
            Color32[] p = src.GetPixels32();
            int w = src.width, h = src.height;
            for (int y = 0; y < IconSize; y++)
                for (int x = 0; x < IconSize; x++)
                {
                    int tx = Mathf.Min((int)((x + 0.5f) / IconSize * w), w - 1);
                    int ty = Mathf.Min((int)((y + 0.5f) / IconSize * h), h - 1);
                    Color32 c = p[ty * w + tx];
                    if (c.a >= 128) px[y * IconSize + x] = c;
                }
        }

        private static void DrawCube(Color32[] px, Texture2D top, Texture2D side)
        {
            Color32[] tp = top.GetPixels32();
            Color32[] sp = side.GetPixels32();

            const float H = 1.059f;  // высота куба при ширине 1: вертикальное ребро равно ребру ромба
            const float k = 0.82f;    // размер куба относительно ячейки

            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    float cx = ((x + 0.5f) / IconSize - 0.5f) * H / k + 0.5f;
                    float cy = ((y + 0.5f) / IconSize - 0.5f) * H / k + H * 0.5f;
                    float u, v;
                    Color32 c;

                    if (Hit(cx, cy, 0f, H - 0.25f, 0.5f, 0.25f, 0.5f, -0.25f, out u, out v))
                        c = Sample(tp, top.width, top.height, u, v, 1.0f);       // верх
                    else if (Hit(cx, cy, 0f, H - 0.25f, 0.5f, -0.25f, 0f, -0.559f, out u, out v))
                        c = Sample(sp, side.width, side.height, u, v, 0.8f);      // левая грань
                    else if (Hit(cx, cy, 0.5f, H - 0.5f, 0.5f, 0.25f, 0f, -0.559f, out u, out v))
                        c = Sample(sp, side.width, side.height, u, v, 0.6f);      // правая грань
                    else continue;

                    if (c.a >= 128) px[y * IconSize + x] = c;
                }
            }
        }

        // точка P = A + u*e1 + v*e2, возвращает u,v и попадание в параллелограмм
        private static bool Hit(float px, float py, float ax, float ay, float e1x, float e1y, float e2x, float e2y,
                                out float u, out float v)
        {
            float dx = px - ax, dy = py - ay;
            float det = e1x * e2y - e1y * e2x;
            u = (dx * e2y - dy * e2x) / det;
            v = (e1x * dy - e1y * dx) / det;
            return u >= 0f && u < 1f && v >= 0f && v < 1f;
        }

        private static Color32 Sample(Color32[] p, int w, int h, float u, float v, float shade)
        {
            int tx = Mathf.Min((int)(u * w), w - 1);
            int ty = Mathf.Min((int)((1f - v) * h), h - 1);
            Color32 c = p[ty * w + tx];
            c.r = (byte)(c.r * shade);
            c.g = (byte)(c.g * shade);
            c.b = (byte)(c.b * shade);
            return c;
        }
    }
}