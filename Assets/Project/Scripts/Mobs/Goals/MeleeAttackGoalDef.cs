using UnityEngine;

namespace MinecraftClone.Mobs
{
    [CreateAssetMenu(fileName = "MeleeAttackGoal", menuName = "Minecraft/Mobs/Goals/Melee Attack")]
    public class MeleeAttackGoalDef : GoalDef
    {
        [Tooltip("Расстояние, на котором моб начинает бить (от центра моба до центра игрока)")]
        public float attackRange = 2f;

        [Tooltip("Секунд между ударами")]
        public float cooldown = 1f;

        [Tooltip("Урон. Пока не используется: заработает вместе с системой здоровья")]
        public float damage = 3f;

        public override Goal Create(Mob mob) => new MeleeAttackGoal(mob, this);
    }
}