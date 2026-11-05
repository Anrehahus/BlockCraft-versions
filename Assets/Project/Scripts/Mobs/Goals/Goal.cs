using UnityEngine;

namespace MinecraftClone.Mobs
{
    // Какие "ресурсы" занимает поведение. Два поведения с общим флагом одновременно не работают:
    // побеждает то, у которого число priority меньше.
    [System.Flags]
    public enum GoalFlags { None = 0, Move = 1, Look = 2 }

    // Рабочий экземпляр поведения у конкретного моба. Выполняется только на сервере.
    public abstract class Goal
    {
        protected readonly Mob mob;
        public GoalFlags Flags { get; protected set; }

        protected Goal(Mob mob) { this.mob = mob; }

        public abstract bool CanStart();
        public virtual bool CanContinue() => CanStart();
        public virtual void Start() { }
        public virtual void Stop() { }
        public virtual void Tick(float dt) { }
    }
}