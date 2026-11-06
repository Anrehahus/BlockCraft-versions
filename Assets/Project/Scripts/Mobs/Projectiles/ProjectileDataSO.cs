using UnityEngine;

namespace MinecraftClone.Mobs
{
    // Описание снаряда (стрела и т.п.). Ассеты лежат в Assets/Resources/Projectiles/
    [CreateAssetMenu(fileName = "NewProjectile", menuName = "Minecraft/Mobs/Projectile Data")]
    public class ProjectileDataSO : ScriptableObject
    {
        [Tooltip("Уникальный id, например arrow")] public string Id = "arrow";
        [Tooltip("Текстура 32x32 в раскладке стрелы Minecraft")] public Texture2D Texture;

        [Tooltip("Ускорение вниз, блоков/с² (у стрелы в Minecraft около -20)")]
        public float Gravity = -20f;

        [Tooltip("Сколько секунд стрела летит, пока не исчезнет")]
        public float MaxLifetime = 10f;

        [Tooltip("Сколько секунд воткнутая в блок стрела остаётся на месте")]
        public float StuckLifetime = 30f;
    }
}