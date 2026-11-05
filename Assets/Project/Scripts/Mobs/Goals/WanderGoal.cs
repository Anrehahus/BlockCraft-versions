using UnityEngine;

namespace MinecraftClone.Mobs
{
    public class WanderGoal : Goal
    {
        private readonly WanderGoalDef _def;
        private Vector3 _dir;
        private float _timer;
        private float _blockedTime;

        public WanderGoal(Mob mob, WanderGoalDef def) : base(mob)
        {
            _def = def;
            Flags = GoalFlags.Move | GoalFlags.Look;
        }

        public override bool CanStart() => Random.value < Time.deltaTime / Mathf.Max(0.1f, _def.meanIdleSeconds);

        public override bool CanContinue() => _timer > 0f && _blockedTime < 0.6f;

        public override void Start()
        {
            float a = Random.value * Mathf.PI * 2f;
            _dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            _timer = Random.Range(_def.walkSeconds.x, _def.walkSeconds.y);
            _blockedTime = 0f;
        }

        public override void Tick(float dt)
        {
            _timer -= dt;
            // упёрлись в стену дольше 0.6 с (а не просто прыгаем на ступеньку) - выбираем другое направление
            _blockedTime = mob.Motor.HitHoriz ? _blockedTime + dt : 0f;
            mob.MoveToward(_dir, _def.speed);
        }
    }
}