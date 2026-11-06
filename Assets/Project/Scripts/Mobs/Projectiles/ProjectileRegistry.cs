using System.Collections.Generic;
using UnityEngine;

namespace MinecraftClone.Mobs
{
    // Находит все ProjectileDataSO из папки Resources/Projectiles
    public static class ProjectileRegistry
    {
        private static Dictionary<string, ProjectileDataSO> _byId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _byId = null; }

        private static void Load()
        {
            if (_byId != null) return;
            _byId = new Dictionary<string, ProjectileDataSO>();

            foreach (var d in Resources.LoadAll<ProjectileDataSO>("Projectiles"))
            {
                if (d == null || string.IsNullOrEmpty(d.Id)) continue;
                if (_byId.ContainsKey(d.Id))
                {
                    Debug.LogWarning($"[ProjectileRegistry] Повторяющийся id '{d.Id}' ({d.name}), пропущен");
                    continue;
                }
                _byId[d.Id] = d;
            }
        }

        public static ProjectileDataSO Get(string id)
        {
            Load();
            if (string.IsNullOrEmpty(id)) return null;
            _byId.TryGetValue(id, out ProjectileDataSO d);
            return d;
        }
    }
}