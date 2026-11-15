using UnityEngine;

namespace MinecraftClone.Mobs
{
    [CreateAssetMenu(fileName = "EatGrassGoal", menuName = "Minecraft/Mobs/Goals/Eat Grass")]
    public class EatGrassGoalDef : GoalDef
    {
        [Tooltip("В среднем раз в сколько секунд овца решает пощипать траву")]
        public float meanIdleSeconds = 20f;
        public float eatSeconds = 2.5f;
        [Tooltip("Есть траву только если овца без шерсти")]
        public float raiseSeconds = 0.5f;
        public bool onlyWhenSheared = true;

        public override Goal Create(Mob mob) => new EatGrassGoal(mob, this);
    }
}