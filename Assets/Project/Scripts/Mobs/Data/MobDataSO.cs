using System;
using System.Collections.Generic;
using UnityEngine;

namespace MinecraftClone.Mobs
{
    public enum Temperament { Passive, Neutral, Hostile }
    public enum TargetMode { None, NearestPlayer, Retaliate }
    public enum PartAnim { None, SwingA, SwingB }                 // A и B качаются в противофазе
    public enum ChargeEffect { None, SwellAndFlash }

    [Serializable]
    public class ModelPartDef
    {
        public string name = "Part";
        [Tooltip("Точка вращения в пикселях от ног моба")] public Vector3 pivotPx;
        [Tooltip("Левый верхний угол части на текстуре (пиксели)")] public Vector2Int uv;
        [Tooltip("Ширина, высота, глубина в пикселях")] public Vector3Int size = new Vector3Int(4, 4, 4);
        [Tooltip("Нижний угол коробки в пикселях относительно pivot")] public Vector3 minPx;
        public PartAnim anim = PartAnim.None;
        [Tooltip("Размах качания в радианах")] public float swingAmplitude = 1.4f;
        [Tooltip("Зеркалить текстуру (левая рука и нога в старом скине 64x32)")] public bool mirror;
        [Tooltip("При атаке поднимать и размахивать (руки зомби)")] public bool waveWhenAttacking;
        [Tooltip("Угол поднятой руки при атаке, градусы (-90 = вперёд)")] public float attackPitch = -90f;
        [Tooltip("При прицеливании (IsAttacking) принимать позу attackPitch. Руки скелета")] public bool poseWhenAttacking;
        [Tooltip("Поза покоя: наклон по X в градусах (-90 = рука вытянута вперёд)")] public float restPitch;
    }

    [Serializable]
    public class SpawnRule
    {
        public bool onSurface = true;
        public bool underground = false;
        [Tooltip("Относительная частота появления (0 = не спавнить)")] public int weight = 10;
        [Tooltip("Появляться только на траве")] public bool onlyOnGrass = false;
        public int groupMin = 1;
        public int groupMax = 1;
    }

    [CreateAssetMenu(fileName = "NewMob", menuName = "Minecraft/Mobs/Mob Data")]
    public class MobDataSO : ScriptableObject
    {
        [Header("Основное")]
        [Tooltip("Уникальный id, например creeper")] public string Id = "mob";
        public string DisplayName = "Mob";
        public Temperament Temperament = Temperament.Passive;
        public float MaxHealth = 20f;

        [Header("Тело и движение")]
        public float Width = 0.6f;
        public float Height = 1.7f;
        public float JumpHeight = 1.25f;

        [Header("Цель")]
        public TargetMode Targeting = TargetMode.None;
        public float FollowRange = 16f;

        [Header("Поведения (Goals)")]
        public List<GoalDef> Goals = new List<GoalDef>();

        [Header("Заряд (крипер и подобные). 0 = не используется")]
        public float ChargeTime = 0f;
        public ChargeEffect ChargeEffect = ChargeEffect.None;

        [Header("Модель")]
        public Texture2D Texture;
        public float PixelSize = 1f / 16f;
        public List<ModelPartDef> ModelParts = new List<ModelPartDef>();

        [Header("Спавн")]
        public SpawnRule Spawn = new SpawnRule();

#if UNITY_EDITOR
        // Правый клик по заголовку ассета в Inspector -> "Preset: ..."
        [ContextMenu("Preset: Creeper")]
        private void PresetCreeper()
        {
            Id = "creeper"; DisplayName = "Creeper";
            Temperament = Temperament.Hostile; MaxHealth = 20f;
            Width = 0.6f; Height = 1.7f; JumpHeight = 1.25f;
            Targeting = TargetMode.NearestPlayer; FollowRange = 16f;
            ChargeTime = 1.5f; ChargeEffect = ChargeEffect.SwellAndFlash;
            PixelSize = 1f / 16f;
            Spawn = new SpawnRule { onSurface = true, underground = true, weight = 100, groupMin = 1, groupMax = 1 };

            ModelParts = new List<ModelPartDef>
            {
                Part("Head", 0, 18, 0,   0, 0,   8, 8, 8,   -4, 0, -4, PartAnim.None),
                Part("Body", 0, 6, 0,    16, 16, 8, 12, 4,  -4, 0, -2, PartAnim.None),
                Part("Leg0", -2, 6, 4,   0, 16,  4, 6, 4,   -2, -6, -2, PartAnim.SwingA),
                Part("Leg1", 2, 6, 4,    0, 16,  4, 6, 4,   -2, -6, -2, PartAnim.SwingB),
                Part("Leg2", -2, 6, -4,  0, 16,  4, 6, 4,   -2, -6, -2, PartAnim.SwingB),
                Part("Leg3", 2, 6, -4,   0, 16,  4, 6, 4,   -2, -6, -2, PartAnim.SwingA),
            };
            UnityEditor.EditorUtility.SetDirty(this);
        }

        [ContextMenu("Preset: Zombie (скин 64x32)")]
        private void PresetZombieOld() => FillZombie(true);

        [ContextMenu("Preset: Zombie (скин 64x64)")]
        private void PresetZombieNew() => FillZombie(false);

        private void FillZombie(bool oldSkin)
        {
            Id = "zombie"; DisplayName = "Zombie";
            Temperament = Temperament.Hostile; MaxHealth = 20f;
            Width = 0.6f; Height = 1.95f; JumpHeight = 1.25f;
            Targeting = TargetMode.NearestPlayer; FollowRange = 35f;
            ChargeTime = 0f; ChargeEffect = ChargeEffect.None;
            PixelSize = 1.95f / 32f;
            Spawn = new SpawnRule { onSurface = true, underground = true, weight = 100, groupMin = 1, groupMax = 3 };

            ModelParts = new List<ModelPartDef>
            {
                Part("Head",     0, 24, 0,  0, 0,    8, 8, 8,   -4, 0, -4, PartAnim.None),
                Part("Body",     0, 24, 0,  16, 16,  8, 12, 4,  -4, -12, -2, PartAnim.None),
                Part("RightArm", 6, 22, 0,  40, 16,  4, 12, 4,  -2, -10, -2, PartAnim.SwingB, 0f, false, true, -90f),
                Part("LeftArm", -6, 22, 0,  oldSkin ? 40 : 32, oldSkin ? 16 : 48, 4, 12, 4, -2, -10, -2, PartAnim.SwingA, 0f, oldSkin, true, -90f),
                Part("RightLeg", 2, 12, 0,  0, 16,   4, 12, 4,  -2, -12, -2, PartAnim.SwingA),
                Part("LeftLeg", -2, 12, 0,  oldSkin ? 0 : 16, oldSkin ? 16 : 48, 4, 12, 4, -2, -12, -2, PartAnim.SwingB, 1.4f, oldSkin, false),
            };
            UnityEditor.EditorUtility.SetDirty(this);
        }

                [ContextMenu("Preset: Pig")]
        private void PresetPig()
        {
            Id = "pig"; DisplayName = "Pig";
            Temperament = Temperament.Passive; MaxHealth = 10f;
            Width = 0.9f; Height = 0.9f; JumpHeight = 1.25f;
            Targeting = TargetMode.None; FollowRange = 16f;
            ChargeTime = 0f; ChargeEffect = ChargeEffect.None;
            PixelSize = 1f / 16f;
            Spawn = new SpawnRule { onSurface = true, underground = false, weight = 100, groupMin = 2, groupMax = 4, onlyOnGrass = true };

            ModelParts = new List<ModelPartDef>
            {
                Part("Head",  0, 12, 6,   0, 0,   8, 8, 8,    -4, -4, 0, PartAnim.None),
                Part("Snout", 0, 12, 6,   16, 16, 4, 3, 1,    -2, -3, 8, PartAnim.None),
                // в оригинале коробка тела повёрнута на 90°, поэтому у неё поза покоя 90 градусов
                Part("Body",  0, 13, -2,  28, 8,  10, 16, 8,  -5, -6, -1, PartAnim.SwingA, 0f, false, false, 90f),
                Part("LegRearLeft",   -3, 6, -7,  0, 16, 4, 6, 4,  -2, -6, -2, PartAnim.SwingA),
                Part("LegRearRight",   3, 6, -7,  0, 16, 4, 6, 4,  -2, -6, -2, PartAnim.SwingB),
                Part("LegFrontLeft",  -3, 6,  5,  0, 16, 4, 6, 4,  -2, -6, -2, PartAnim.SwingB),
                Part("LegFrontRight",  3, 6,  5,  0, 16, 4, 6, 4,  -2, -6, -2, PartAnim.SwingA),
            };
            UnityEditor.EditorUtility.SetDirty(this);
        }   

                [ContextMenu("Preset: Skeleton")]
        private void PresetSkeleton()
        {
            Id = "skeleton"; DisplayName = "Skeleton";
            Temperament = Temperament.Hostile; MaxHealth = 20f;
            Width = 0.6f; Height = 1.99f; JumpHeight = 1.25f;
            Targeting = TargetMode.NearestPlayer; FollowRange = 20f;
            ChargeTime = 0f; ChargeEffect = ChargeEffect.None;
            PixelSize = 1.99f / 32f;
            Spawn = new SpawnRule { onSurface = false, underground = true, weight = 100, groupMin = 1, groupMax = 2 };

            ModelParts = new List<ModelPartDef>
            {
                Part("Head",     0, 24, 0,  0, 0,    8, 8, 8,    -4, 0, -4, PartAnim.None),
                Part("Body",     0, 24, 0,  16, 16,  8, 12, 4,   -4, -12, -2, PartAnim.None),
                Part("RightArm", 5, 22, 0,  40, 16,  2, 12, 2,   -1, -10, -1, PartAnim.SwingB, 1f, false, false, 0f, true),
                Part("LeftArm", -5, 22, 0,  40, 16,  2, 12, 2,   -1, -10, -1, PartAnim.SwingA, 1f, true, false, 0f, true),
                Part("RightLeg", 2, 12, 0,  0, 16,   2, 12, 2,   -1, -12, -1, PartAnim.SwingA),
                Part("LeftLeg", -2, 12, 0,  0, 16,   2, 12, 2,   -1, -12, -1, PartAnim.SwingB, 1.4f, true, false),
            };
            UnityEditor.EditorUtility.SetDirty(this);
        }

                private static ModelPartDef Part(string n, float px, float py, float pz, int u, int v,
                                         int w, int h, int d, float mx, float my, float mz, PartAnim a,
                                         float amp = 1.4f, bool mirror = false, bool wave = false, float rest = 0f,
                                         bool pose = false)
        {
            return new ModelPartDef
            {
                name = n, pivotPx = new Vector3(px, py, pz), uv = new Vector2Int(u, v),
                size = new Vector3Int(w, h, d), minPx = new Vector3(mx, my, mz), anim = a,
                swingAmplitude = amp, mirror = mirror, waveWhenAttacking = wave, restPitch = rest,
                poseWhenAttacking = pose
            };
        }
#endif
    }
}