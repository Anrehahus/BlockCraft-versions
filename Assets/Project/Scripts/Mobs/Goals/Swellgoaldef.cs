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
    }
}