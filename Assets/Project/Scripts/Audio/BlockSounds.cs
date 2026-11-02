using UnityEngine;
using MinecraftClone.Core;

namespace MinecraftClone.Audio
{
    public enum SoundType { None, Grass, Gravel, Stone, Wood, HighStone }

    public static class BlockSounds
    {
        private const int PoolSize = 16;

        private static AudioClip[][] _clips;
        private static AudioSource[] _pool;
        private static int _next;

        public static SoundType GetSoundType(BlockType t)
        {
            switch (t)
            {
                case BlockType.Dirt:
                case BlockType.Grass:
                case BlockType.OakLeaves:
                case BlockType.Sponge:
                case BlockType.WhiteWool:
                case BlockType.OrangeWool:
                case BlockType.MagentaWool:
                case BlockType.LightBlueWool:
                case BlockType.YellowWool:
                case BlockType.LimeWool:
                case BlockType.PinkWool:
                case BlockType.GrayWool:
                case BlockType.LightGrayWool:
                case BlockType.CyanWool:
                case BlockType.PurpleWool:
                case BlockType.BlueWool:
                case BlockType.BrownWool:
                case BlockType.GreenWool:
                case BlockType.RedWool:
                case BlockType.BlackWool:
                    return SoundType.Grass;

                case BlockType.Gravel:
                case BlockType.Sand:
                    return SoundType.Gravel;

                case BlockType.Stone:
                case BlockType.Cobblestone:
                case BlockType.IronOre:
                case BlockType.GoldOre:
                case BlockType.CoalOre:
                case BlockType.Bedrock:
                    return SoundType.Stone;

                case BlockType.Glass:
                case BlockType.GoldBlock:
                    return SoundType.HighStone;

                case BlockType.OakPlanks:
                case BlockType.OakLog:
                    return SoundType.Wood;

                default: // воздух, вода, лава, саженец, цветы, грибы: без звука
                    return SoundType.None;
            }
        }

        private static void Init()
        {
            if (_pool != null && _pool[0] != null) return;

            string[] names = { "", "grass", "gravel", "stone", "wood" };
            _clips = new AudioClip[names.Length][];
            for (int i = 1; i < names.Length; i++)
            {
                var list = new System.Collections.Generic.List<AudioClip>();
                for (int n = 1; n <= 4; n++)
                {
                    var c = Resources.Load<AudioClip>("Sounds/" + names[i] + n);
                    if (c != null) list.Add(c);
                    else Debug.LogWarning($"[BlockSounds] Не найден Resources/Sounds/{names[i]}{n}");
                }
                _clips[i] = list.ToArray();
            }

            var host = new GameObject("[BlockSounds]");
            Object.DontDestroyOnLoad(host);
            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var s = host.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 2f;
                s.maxDistance = 24f;
                _pool[i] = s;
            }
        }

        public static void Play(BlockType block, Vector3 position, float volume, float pitch)
        {
            SoundType st = GetSoundType(block);
            if (st == SoundType.None) return;

            Init();

            int idx;
            if (st == SoundType.HighStone) { idx = 3; pitch *= 1.5f; }
            else idx = (int)st;

            AudioClip[] arr = _clips[idx];
            if (arr == null || arr.Length == 0) return;

            AudioSource src = _pool[_next];
            _next = (_next + 1) % PoolSize;

            src.transform.position = position;
            src.pitch = pitch * Random.Range(0.92f, 1.08f);
            src.volume = volume;
            src.clip = arr[Random.Range(0, arr.Length)];
            src.Play();
        }
    }
}