using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;

namespace MinecraftClone.Mobs
{
    public class EatGrassGoal : Goal
    {
        private readonly EatGrassGoalDef _def;
        private float _timer;
        private int _hurtAtStart;
        private bool _done;

        public EatGrassGoal(Mob mob, EatGrassGoalDef def) : base(mob)
        {
            _def = def;
            Flags = GoalFlags.Move | GoalFlags.Look;   // пока ест, стоит на месте
        }

        public override bool CanStart()
        {
            if (_def.onlyWhenSheared && !mob.IsSheared) return false;
            if (Random.value >= Time.deltaTime / Mathf.Max(1f, _def.meanIdleSeconds)) return false;
            return mob.Motor != null && mob.Motor.Grounded && GrassBelow(out _, out _, out _);
        }

        // удар по овце прерывает еду
        public override bool CanContinue()
    => _timer > -_def.raiseSeconds && mob.HurtCount == _hurtAtStart;

        public override void Start()
        {
            _timer = _def.eatSeconds;
            _done = false;
            _hurtAtStart = mob.HurtCount;
            mob.ServerSetEating(true);
        }

        public override void Stop() => mob.ServerSetEating(false);

        public override void Tick(float dt)
        {
            _timer -= dt;
            if (_done || _timer > 0f) return;

            // доели: голова начинает подниматься, трава -> земля, шерсть отрастает
            _done = true;
            mob.ServerSetEating(false);
            if (GrassBelow(out int x, out int y, out int z))
            {
                WorldManager.Instance.ApplyBlockChange(x, y, z, BlockType.Dirt);
                mob.ServerRegrowWool();
            }
        }

        private bool GrassBelow(out int x, out int y, out int z)
        {
            Vector3 p = mob.transform.position;
            x = Mathf.FloorToInt(p.x);
            y = Mathf.FloorToInt(p.y - 0.05f);
            z = Mathf.FloorToInt(p.z);
            WorldManager wm = WorldManager.Instance;
            return wm != null && wm.GetBlockAtWorldPosition(x, y, z) == BlockType.Grass;
        }
    }
}