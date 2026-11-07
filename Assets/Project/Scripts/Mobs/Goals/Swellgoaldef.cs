using UnityEngine;

namespace MinecraftClone.Mobs
{
    // Настройки поведения крипера: подошёл к игроку, надулся, взорвался.
    // Время запала берётся из MobDataSO.ChargeTime.
    [CreateAssetMenu(fileName = "SwellGoal", menuName = "Minecraft/Mobs/Goals/Swell And Explode")]
    public class SwellGoalDef : GoalDef
    {
        public float igniteDistance = 3f;
        [Tooltip("Если игрок отбежал дальше, крипер сдувается")]
        public float cancelDistance = 7f;
        public float explosionRadius = 3f;

        public override Goal Create(Mob mob) => new SwellGoal(mob, this);

        [Header("Урон игроку")]
        [Tooltip("Дальше этого расстояния от центра взрыва урона нет")]
        public float damageRadius = 4f;
        public float minDamage = 2f;   // на краю зоны
        public float maxDamage = 6f;   // в центре
    }
}