using System.Text.RegularExpressions;
using UnityEngine;

namespace MinecraftClone.Management
{
    /// <summary>Данные, переданные игре лаунчером через аргументы командной строки.</summary>
    public static class LauncherArgs
    {
        public static string Nickname { get; private set; } = "";
        public static bool HasNickname => !string.IsNullOrEmpty(Nickname);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            string[] args = System.Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-username") continue;

                string value = args[i + 1].Trim();
                if (Regex.IsMatch(value, "^[A-Za-z0-9_]{3,16}$"))
                {
                    Nickname = value;
                    PlayerPrefs.SetString("PlayerNickname", value);
                    PlayerPrefs.Save();
                    Debug.Log($"[Launcher] Ник из лаунчера: {value}");
                }
                break;
            }
        }
    }
}