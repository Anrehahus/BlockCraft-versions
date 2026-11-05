using UnityEngine;

namespace MinecraftClone.Mobs
{
    [CreateAssetMenu(fileName = "WanderGoal", menuName = "Minecraft/Mobs/Goals/Wander")]
    public class WanderGoalDef : GoalDef
    {
        public float speed = 2f;
        [Tooltip("В среднем раз в сколько секунд моб решает куда-то пойти")]
        public float meanIdleSeconds = 3f;
        public Vector2 walkSeconds = new Vector2(2f, 5f);

        public override Goal Create(Mob mob) => new WanderGoal(mob, this);
    }
}