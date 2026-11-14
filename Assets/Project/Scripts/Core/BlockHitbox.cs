using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Management;

namespace MinecraftClone.Core
{
    // Настоящие границы блока внутри клетки: нужны для выделения и прицеливания.
    // Плита занимает нижнюю половину, растения только ту часть, где у текстуры есть непрозрачные пиксели.
    public static class BlockHitbox
    {
        private struct Box { public Vector3 Min, Max; }

        private static readonly Dictionary<BlockType, Box> _plants = new Dictionary<BlockType, Box>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _plants.Clear();

        // false = у блока обычная целая клетка
        public static bool TryGetBounds(BlockType t, out Vector3 min, out Vector3 max)
        {
            if (t == BlockType.StoneSlab)
            {
                min = Vector3.zero;
                max = new Vector3(1f, 0.5f, 1f);
                return true;
            }

            if (t.IsPlant())
            {
                if (!_plants.TryGetValue(t, out Box box))
                {
                    box = BuildPlantBox(t);
                    _plants[t] = box;
                }
                min = box.Min;
                max = box.Max;
                return true;
            }

            min = Vector3.zero;
            max = Vector3.one;
            return false;
        }

        // Коробка по непрозрачным пикселям текстуры. Крестовина растения идёт по диагоналям клетки,
        // поэтому ширина по X и по Z одинаковая: от первого до последнего непрозрачного столбца.
        private static Box BuildPlantBox(BlockType t)
        {
            Box fallback = FallbackBox(t);

            var wm = WorldManager.Instance;
            if (wm == null || wm.BlockDatabase == null) return fallback;
            if (!wm.BlockDatabase.TryGetValue(t, out var data) || data == null) return fallback;

            Texture2D tex = data.TopTexture != null ? data.TopTexture : data.SideTexture;
            if (tex == null || !tex.isReadable) return fallback;

            Color32[] px = tex.GetPixels32();   // строка 0 = низ текстуры
            int w = tex.width, h = tex.height;
            int x0 = w, x1 = -1, y0 = h, y1 = -1;

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a >= 128)
                    {
                        if (x < x0) x0 = x;
                        if (x > x1) x1 = x;
                        if (y < y0) y0 = y;
                        if (y > y1) y1 = y;
                    }

            if (x1 < x0 || y1 < y0) return fallback;

            float fx0 = x0 / (float)w, fx1 = (x1 + 1) / (float)w;
            float fy0 = y0 / (float)h, fy1 = (y1 + 1) / (float)h;

            // не даём коробке стать совсем тонкой, иначе в неё трудно попасть
            if (fx1 - fx0 < 0.2f) { float c = (fx0 + fx1) * 0.5f; fx0 = c - 0.1f; fx1 = c + 0.1f; }
            if (fy1 - fy0 < 0.2f) fy1 = fy0 + 0.2f;

            return new Box { Min = new Vector3(fx0, fy0, fx0), Max = new Vector3(fx1, fy1, fx1) };
        }

        // если текстуру прочитать нельзя (выключен Read/Write): размеры как в оригинале
        private static Box FallbackBox(BlockType t)
        {
            switch (t)
            {
                case BlockType.BrownMushroom:
                case BlockType.RedMushroom:
                    return new Box { Min = new Vector3(0.3f, 0f, 0.3f), Max = new Vector3(0.7f, 0.4f, 0.7f) };
                case BlockType.OakSapling:
                    return new Box { Min = new Vector3(0.1f, 0f, 0.1f), Max = new Vector3(0.9f, 0.8f, 0.9f) };
                default:   // цветы
                    return new Box { Min = new Vector3(0.3f, 0f, 0.3f), Max = new Vector3(0.7f, 0.6f, 0.7f) };
            }
        }
    }
}