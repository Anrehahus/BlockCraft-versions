using UnityEngine;

namespace MinecraftClone.Mobs
{
    public class ChaseTargetGoal : Goal
    {
        private readonly ChaseTargetGoalDef _def;

        public ChaseTargetGoal(Mob mob, ChaseTargetGoalDef def) : base(mob)
        {
            _def = def;
            Flags = GoalFlags.Move | GoalFlags.Look;
        }

        public override bool CanStart() => mob.Target != null;

        public override void Tick(float dt)
        {
            Vector3 to = mob.Target.position - mob.transform.position;
            to.y = 0f;

            mob.Face(to);
            if (to.magnitude > _def.stopDistance) mob.MoveToward(to, _def.speed);
        }
    }
}