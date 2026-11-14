using UnityEngine;

namespace MinecraftClone.Mobs
{
    // Моб прыгает вперёд на цель. Сам удар наносит MeleeAttackGoal, когда моб приземлится рядом.
    // Приоритет должен быть меньше (важнее), чем у MeleeAttackGoal и ChaseTargetGoal.
    public class LeapAttackGoal : Goal
    {
        private const float MinAirTime = 0.1f;   // пока Grounded ещё не успел стать false
        private const float MaxAirTime = 1.5f;   // страховка, если моб завис

        private readonly LeapAttackGoalDef _def;
        private float _nextLeap;
        private float _airTime;

        public LeapAttackGoal(Mob mob, LeapAttackGoalDef def) : base(mob)
        {
            _def = def;
            Flags = GoalFlags.Move | GoalFlags.Look;
        }

        public override bool CanStart()
        {
            if (mob.Target == null || mob.Motor == null || !mob.Motor.Grounded) return false;
            if (Time.time < _nextLeap) return false;

            Vector3 targetCenter = mob.Target.position + Vector3.up * 0.9f;
            float d = Vector3.Distance(mob.CenterPos, targetCenter);
            if (d < _def.minDistance || d > _def.maxDistance) return false;

            // небольшая случайность, чтобы прыжок не происходил в первый же кадр
            if (Random.value > Time.deltaTime * 4f) return false;

            return mob.Motor.LineClear(mob.CenterPos, targetCenter);   // через стену не прыгаем
        }

        // работаем, пока моб в воздухе
        public override bool CanContinue()
            => _airTime < MaxAirTime && (_airTime < MinAirTime || !mob.Motor.Grounded);

        public override void Start()
        {
            _airTime = 0f;
            _nextLeap = Time.time + _def.cooldown;

            Vector3 to = mob.Target.position - mob.transform.position;
            mob.Face(to);
            mob.ServerLeap(to, _def.horizontalSpeed, _def.verticalSpeed);
        }

        public override void Tick(float dt)
        {
            _airTime += dt;
            if (mob.Target != null) mob.Face(mob.Target.position - mob.transform.position);
        }
    }
}