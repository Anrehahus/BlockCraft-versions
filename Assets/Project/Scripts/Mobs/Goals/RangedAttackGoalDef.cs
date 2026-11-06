using UnityEngine;

namespace MinecraftClone.Mobs
{
    [CreateAssetMenu(fileName = "RangedAttackGoal", menuName = "Minecraft/Mobs/Goals/Ranged Attack")]
    public class RangedAttackGoalDef : GoalDef
    {
        [Tooltip("Дистанция, с которой моб останавливается и стреляет")]
        public float attackRange = 12f;

        [Tooltip("Скорость, с которой моб подходит к цели")]
        public float approachSpeed = 3f;

        [Tooltip("Сколько секунд моб целится перед первым выстрелом")]
        public float drawTime = 1f;

        [Tooltip("Секунд между выстрелами")]
        public float shootInterval = 2f;

        public override Goal Create(Mob mob) => new RangedAttackGoal(mob, this);

        [Header("Стрела")]
        public ProjectileDataSO projectile;

        [Tooltip("Скорость стрелы, блоков в секунду")]
        public float projectileSpeed = 30f;

        [Tooltip("Разброс: угол конуса вокруг линии прицеливания, градусы")]
        public float spreadDegrees = 3f;

        [Tooltip("Урон от попадания. Пока не используется: заработает вместе с системой здоровья")]
        public float damage = 2f;
    }
}