using UnityEngine;

namespace MinecraftClone.Mobs
{
    // Ассет с настройками поведения. Его кладут в список Goals у MobDataSO.
    public abstract class GoalDef : ScriptableObject
    {
        [Tooltip("Чем меньше число, тем важнее поведение")]
        public int priority = 5;

        public abstract Goal Create(Mob mob);
    }
}