using UnityEngine;

namespace MinecraftClone.Mobs
{
    // Дальний бой: моб подходит к цели на дистанцию выстрела, останавливается, целится и стреляет.
    // Пока цель не видна (за стеной) или далеко, он идёт к ней.
    // Анимацию прицеливания клиенты берут из Mob.IsAttacking.
    public class RangedAttackGoal : Goal
    {
        private readonly RangedAttackGoalDef _def;
        private bool _shooting;   // стоим и целимся
        private float _timer;     // секунд до выстрела

        public RangedAttackGoal(Mob mob, RangedAttackGoalDef def) : base(mob)
        {
            _def = def;
            Flags = GoalFlags.Move | GoalFlags.Look;
        }

        public override bool CanStart() => mob.Target != null;

        public override void Start() => _shooting = false;

        public override void Stop()
        {
            _shooting = false;
            mob.ServerSetAttacking(false);
        }

        public override void Tick(float dt)
        {
            Vector3 toTarget = mob.Target.position - mob.transform.position;
            mob.Face(toTarget);

            Vector3 targetEye = mob.Target.position + Vector3.up * 0.9f;
            Vector3 muzzle = mob.transform.position + Vector3.up * (mob.Data.Height * 0.85f);   // уровень головы
            float dist = Vector3.Distance(muzzle, targetEye);
            bool visible = mob.Motor.LineClear(muzzle, targetEye);

            // небольшой запас, чтобы моб не дёргался на границе дистанции
            float range = _shooting ? _def.attackRange + 1f : _def.attackRange;

            if (dist < range && visible)
            {
                if (!_shooting)
                {
                    _shooting = true;
                    _timer = _def.drawTime;
                    mob.ServerSetAttacking(true);
                }

                _timer -= dt;
                if (_timer <= 0f)
                {
                    _timer = _def.shootInterval;
                    Shoot(muzzle, targetEye);
                }
            }
            else
            {
                if (_shooting)
                {
                    _shooting = false;
                    mob.ServerSetAttacking(false);
                }
                mob.MoveToward(toTarget, _def.approachSpeed);
            }
        }

        private void Shoot(Vector3 from, Vector3 to)
        {
            // TODO (следующий шаг): создать стрелу в точке from и запустить её в сторону to.
            Debug.Log($"[RangedAttack] {mob.Data.Id}: выстрел (стрелы пока нет)");
        }
    }
}