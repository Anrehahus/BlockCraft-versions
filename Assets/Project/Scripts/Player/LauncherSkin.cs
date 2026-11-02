using System;
using System.IO;
using UnityEngine;

namespace MinecraftClone.Player
{
    public static class LauncherSkin
    {
        public const int MaxBytes = 32 * 1024;
        private static byte[] _cached;
        private static bool _loaded;

        // Байты скина, переданного лаунчером (null, если не передан)
        public static byte[] GetBytes()
        {
            if (_loaded) return _cached;
            _loaded = true;
            try
            {
                string[] args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (!string.Equals(args[i], "-skin", StringComparison.OrdinalIgnoreCase)) continue;
                    string path = args[i + 1];
                    if (!File.Exists(path)) break;
                    byte[] b = File.ReadAllBytes(path);
                    if (b.Length <= MaxBytes) _cached = b;
                    break;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Skin] " + e.Message); }
            return _cached;
        }

        // PNG -> Texture2D. Возвращает null, если файл не PNG или размер не 64x64 / 64x32
        public static Texture2D Decode(byte[] png)
        {
            if (png == null || png.Length < 24 || png.Length > MaxBytes) return null;
            if (png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47) return null;

            // размер читаем из заголовка до декодирования
            int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            if (w != 64 || (h != 64 && h != 32)) return null;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(png, false)) { UnityEngine.Object.Destroy(tex); return null; }
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }
}