using UnityEngine;

namespace MinecraftClone.Mobs
{
    // Прыжок на цель перед ударом (паук). Создать ассет: Create -> Minecraft -> Mobs -> Goals -> Leap Attack
    [CreateAssetMenu(fileName = "LeapAttackGoal", menuName = "Minecraft/Mobs/Goals/Leap Attack")]
    public class LeapAttackGoalDef : GoalDef
    {
        [Tooltip("Прыгает, если до цели не меньше этого расстояния (от центра до центра)")]
        public float minDistance = 2.6f;

        [Tooltip("...и не больше этого")]
        public float maxDistance = 4.5f;

        [Tooltip("Скорость вперёд во время прыжка, блоков/с")]
        public float horizontalSpeed = 6f;

        [Tooltip("Начальная скорость вверх, блоков/с (6 = примерно блок высоты)")]
        public float verticalSpeed = 6f;

        [Tooltip("Секунд между прыжками")]
        public float cooldown = 1.5f;

        public override Goal Create(Mob mob) => new LeapAttackGoal(mob, this);
    }
}