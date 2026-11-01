using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MinecraftClone.Rendering
{
    public class TextureStitcher
    {
        private readonly int textureSize;
        public Texture2D AtlasTexture { get; private set; }
        public Dictionary<string, Rect> UVMap { get; private set; }

        public TextureStitcher(int textureSize = 16)
        {
            this.textureSize = textureSize;
            UVMap = new Dictionary<string, Rect>();
        }

        public void StitchTextures(Texture2D[] textures, Material targetMaterial)
        {
            if (textures == null || textures.Length == 0)
            {
                Debug.LogError("TextureStitcher: Нет текстур для упаковки!");
                return;
            }

            List<Texture2D> uniqueSortedTextures = textures
                .Where(t => t != null)
                .GroupBy(t => t.name)
                .Select(g => g.First())
                .OrderBy(t => t.name)
                .ToList();

            // Отключаем мипмапы (false) для идеальной пиксельной графики
            AtlasTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            AtlasTexture.filterMode = FilterMode.Point;
            AtlasTexture.wrapMode = TextureWrapMode.Clamp;

            // ВАЖНО: Добавлен padding = 4 (второй аргумент)
            Rect[] uvs = AtlasTexture.PackTextures(uniqueSortedTextures.ToArray(), 4, 8192, false);

            UVMap.Clear();
            for (int i = 0; i < uniqueSortedTextures.Count; i++)
            {
                string textureName = uniqueSortedTextures[i].name;
                if (!UVMap.ContainsKey(textureName))
                {
                    UVMap.Add(textureName, uvs[i]);
                }
            }

            if (targetMaterial != null)
            {
                targetMaterial.mainTexture = AtlasTexture;
            }

            Debug.Log($"[TextureStitcher] Атлас успешно сшит! Упаковано уникальных текстур: {uniqueSortedTextures.Count}");
        }
    }
}