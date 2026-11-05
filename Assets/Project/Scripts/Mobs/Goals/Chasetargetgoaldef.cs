using UnityEngine;

namespace MinecraftClone.Mobs
{
    [CreateAssetMenu(fileName = "ChaseTargetGoal", menuName = "Minecraft/Mobs/Goals/Chase Target")]
    public class ChaseTargetGoalDef : GoalDef
    {
        public float speed = 4f;
        [Tooltip("Ближе этого расстояния моб останавливается")]
        public float stopDistance = 0f;

        public override Goal Create(Mob mob) => new ChaseTargetGoal(mob, this);
    }
}