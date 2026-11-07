using UnityEngine;

namespace MinecraftClone.Mobs
{
    public class SwellGoal : Goal
    {
        private readonly SwellGoalDef _def;

        public SwellGoal(Mob mob, SwellGoalDef def) : base(mob)
        {
            _def = def;
            Flags = GoalFlags.Move | GoalFlags.Look;
        }

        private Vector3 TargetEye => mob.Target.position + Vector3.up * 0.9f;

        private bool Check(float maxDist)
        {
            if (mob.Target == null) return false;
            Vector3 eye = TargetEye;
            Vector3 c = mob.CenterPos;
            if ((eye - c).sqrMagnitude >= maxDist * maxDist) return false;
            return mob.Motor.LineClear(c, eye);   // через стену не взрываемся
        }

        public override bool CanStart() => Check(_def.igniteDistance);
        public override bool CanContinue() => Check(_def.cancelDistance);

        public override void Start() => mob.ServerSetChargeDir(1);
        public override void Stop() => mob.ServerSetChargeDir(-1);

        public override void Tick(float dt)
        {
            mob.Face(mob.Target.position - mob.transform.position);

            if (mob.Charge >= mob.Data.ChargeTime)
            {
                Vector3 center = mob.CenterPos;
                MobExplosion.Explode(center, _def.explosionRadius);
                MobExplosion.DamagePlayers(center, _def.damageRadius, _def.minDamage, _def.maxDamage);
                mob.ServerDespawn();
            }
        }
    }
}