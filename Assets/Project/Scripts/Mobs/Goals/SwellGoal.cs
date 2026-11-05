using UnityEngine;

namespace MinecraftClone.Mobs
{
    // Поведение крипера: подошёл к игроку, надулся, взорвался.
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
                MobExplosion.Explode(mob.CenterPos, _def.explosionRadius);
                mob.ServerDespawn();
            }
        }
    }
}