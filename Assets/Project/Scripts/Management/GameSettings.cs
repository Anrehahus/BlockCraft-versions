using UnityEngine;

namespace MinecraftClone.Management
{
    public static class GameSettings
    {
        public enum Bind { Forward, Back, Left, Right, Jump, Inventory }

        public static readonly string[] BindNames =
            { "Вперёд", "Назад", "Влево", "Вправо", "Прыжок", "Инвентарь" };

        private static readonly KeyCode[] Defaults =
            { KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.Space, KeyCode.B };

        // Дальняя / Средняя / Близкая / Очень близкая (в чанках)
        public static readonly string[] ViewNames = { "Дальняя", "Средняя", "Близкая", "Очень близкая" };
        public static readonly int[] ViewDistances = { 8, 5, 3, 2 };

        public static int ViewPreset { get; private set; } = 1;
        public static int ViewDistance => ViewDistances[ViewPreset];

        public static float Master { get; private set; } = 1f;
        public static float Blocks { get; private set; } = 1f;
        public static float Music { get; private set; } = 1f;
        public static float Sensitivity { get; private set; } = 1f; // множитель: 0.25 .. 3

        private static KeyCode[] _keys = (KeyCode[])Defaults.Clone();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Load()
        {
            ViewPreset = Mathf.Clamp(PlayerPrefs.GetInt("ViewPreset", 1), 0, ViewDistances.Length - 1);
            Master = PlayerPrefs.GetFloat("VolMaster", 1f);
            Blocks = PlayerPrefs.GetFloat("VolBlocks", 1f);
            Music = PlayerPrefs.GetFloat("VolMusic", 1f);
            Sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat("Sensitivity", 1f), 0.25f, 3f);

            for (int i = 0; i < _keys.Length; i++)
                _keys[i] = (KeyCode)PlayerPrefs.GetInt("Key_" + i, (int)Defaults[i]);

            AudioListener.volume = Master;
        }

        public static void SaveAll() => PlayerPrefs.Save();

        // ---------- Клавиши ----------
        public static KeyCode Key(Bind b) => _keys[(int)b];
        public static bool Held(Bind b) => Input.GetKey(_keys[(int)b]);
        public static bool Pressed(Bind b) => Input.GetKeyDown(_keys[(int)b]);

        private static bool IsReserved(KeyCode k)
        {
            if (k == KeyCode.Escape || k == KeyCode.T || k == KeyCode.Tab ||
                k == KeyCode.Slash || k == KeyCode.KeypadDivide) return true;
            return k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9; // слоты хотбара
        }

        /// <returns>"" если успешно, иначе текст ошибки</returns>
        public static string TryRebind(Bind b, KeyCode k)
        {
            if (IsReserved(k)) return "Эта клавиша зарезервирована игрой";

            // если клавиша занята другим действием, меняем их местами
            for (int i = 0; i < _keys.Length; i++)
            {
                if (i != (int)b && _keys[i] == k)
                {
                    _keys[i] = _keys[(int)b];
                    PlayerPrefs.SetInt("Key_" + i, (int)_keys[i]);
                    break;
                }
            }

            _keys[(int)b] = k;
            PlayerPrefs.SetInt("Key_" + (int)b, (int)k);
            return "";
        }

        public static void ResetKeys()
        {
            for (int i = 0; i < _keys.Length; i++)
            {
                _keys[i] = Defaults[i];
                PlayerPrefs.SetInt("Key_" + i, (int)Defaults[i]);
            }
        }

        // ---------- Остальное ----------
        public static void SetViewPreset(int i)
        {
            ViewPreset = Mathf.Clamp(i, 0, ViewDistances.Length - 1);
            PlayerPrefs.SetInt("ViewPreset", ViewPreset);
            if (WorldManager.Instance != null) WorldManager.Instance.SetViewDistance(ViewDistance);

            if (Mirror.NetworkServer.active)
            {
                Mirror.NetworkServer.SendToAll(new MinecraftClone.Networking.ViewDistanceMessage { Chunks = ViewDistance });
            }
        }

        public static void SetMaster(float v)
        {
            Master = Mathf.Clamp01(v);
            AudioListener.volume = Master;
            PlayerPrefs.SetFloat("VolMaster", Master);
        }

        public static void SetBlocks(float v) { Blocks = Mathf.Clamp01(v); PlayerPrefs.SetFloat("VolBlocks", Blocks); }
        public static void SetMusic(float v) { Music = Mathf.Clamp01(v); PlayerPrefs.SetFloat("VolMusic", Music); }
        public static void SetSensitivity(float v) { Sensitivity = Mathf.Clamp(v, 0.25f, 3f); PlayerPrefs.SetFloat("Sensitivity", Sensitivity); }
    }
}