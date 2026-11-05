using System.Collections.Generic;
using UnityEngine;

namespace MinecraftClone.Mobs
{
    // Находит все MobDataSO из папки Resources/MobData
    public static class MobRegistry
    {
        private static Dictionary<string, MobDataSO> _byId;
        private static List<MobDataSO> _all;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _byId = null; _all = null; }

        private static void Load()
        {
            if (_byId != null) return;
            _byId = new Dictionary<string, MobDataSO>();
            _all = new List<MobDataSO>();

            foreach (var d in Resources.LoadAll<MobDataSO>("MobData"))
            {
                if (d == null || string.IsNullOrEmpty(d.Id)) continue;
                if (_byId.ContainsKey(d.Id))
                {
                    Debug.LogWarning($"[MobRegistry] Повторяющийся id '{d.Id}' ({d.name}), пропущен");
                    continue;
                }
                _byId[d.Id] = d;
                _all.Add(d);
            }
        }

        public static MobDataSO Get(string id)
        {
            Load();
            if (string.IsNullOrEmpty(id)) return null;
            _byId.TryGetValue(id, out MobDataSO d);
            return d;
        }

        public static IReadOnlyList<MobDataSO> All { get { Load(); return _all; } }
    }
}