using UnityEngine;

namespace MinecraftClone.Mobs
{
    // Ближний бой: моб останавливается рядом с целью, поднимает руки и бьёт раз в cooldown секунд.
    // Анимацию (поднятые и машущие руки) клиенты берут из Mob.IsAttacking.
    public class MeleeAttackGoal : Goal
    {
        private readonly MeleeAttackGoalDef _def;
        private float _cooldown;

        public MeleeAttackGoal(Mob mob, MeleeAttackGoalDef def) : base(mob)
        {
            _def = def;
            Flags = GoalFlags.Move | GoalFlags.Look;
        }

        private bool InRange(float range)
        {
            if (mob.Target == null) return false;

            Vector3 targetCenter = mob.Target.position + Vector3.up * 0.9f;
            Vector3 c = mob.CenterPos;
            if ((targetCenter - c).sqrMagnitude > range * range) return false;

            return mob.Motor.LineClear(c, targetCenter);   // через стену не бьём
        }

        public override bool CanStart() => InRange(_def.attackRange);

        // небольшой запас, чтобы моб не дёргался на границе дальности
        public override bool CanContinue() => InRange(_def.attackRange + 0.5f);

        public override void Start()
        {
            mob.ServerSetAttacking(true);
            _cooldown = 0.4f;   // первый удар чуть позже, чтобы успеть поднять руки
        }

        public override void Stop() => mob.ServerSetAttacking(false);

        public override void Tick(float dt)
        {
            mob.Face(mob.Target.position - mob.transform.position);

            _cooldown -= dt;
            if (_cooldown <= 0f)
            {
                _cooldown = _def.cooldown;
                Hit();
            }
        }

        private void Hit()
        {
            mob.ServerRegisterHit();
            // TODO (когда появится система здоровья): нанести _def.damage игроку mob.Target
            // и отбросить его. Пока удар только визуальный.
        }
    }
}