using UnityEngine;

namespace MinecraftClone.ScriptableObjects
{
    [CreateAssetMenu(fileName = "WorldConfig", menuName = "Minecraft/World Config")]
    public class WorldConfigSO : ScriptableObject
    {
        [Header("Размеры чанка")]
        public int ChunkWidth = 16;
        public int ChunkHeight = 128;

        [Header("Генерация")]
        public int Seed = 1337;
        public float NoiseScale = 0.015f;
    }
}