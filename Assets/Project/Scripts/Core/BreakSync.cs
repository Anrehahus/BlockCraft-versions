using System.Collections.Generic;
using UnityEngine;
using Mirror;
using MinecraftClone.Core;
using MinecraftClone.Management;

namespace MinecraftClone.Networking
{
    public struct BreakProgressRequestMessage : NetworkMessage { public Vector3Int Pos; public int Stage; }   // клиент -> сервер (-1 = убрать)
    public struct BreakProgressMessage : NetworkMessage { public int Who; public Vector3Int Pos; public int Stage; } // сервер -> клиенты

    public static class BreakSync
    {
        public static void RegisterServer() => NetworkServer.RegisterHandler<BreakProgressRequestMessage>(OnServerRequest);
        public static void RegisterClient() => NetworkClient.RegisterHandler<BreakProgressMessage>(m => RemoteBreakOverlays.Set(m.Who, m.Pos, m.Stage));

        // Своя стадия ломания (stage < 0 = перестал ломать)
        public static void Send(Vector3Int pos, int stage)
        {
            if (NetworkServer.active) Broadcast(0, pos, stage, NetworkServer.localConnection);   // хост, у него id 0
            else if (NetworkClient.active) NetworkClient.Send(new BreakProgressRequestMessage { Pos = pos, Stage = stage });
        }

        private static void OnServerRequest(NetworkConnectionToClient conn, BreakProgressRequestMessage m)
            => Broadcast(conn.connectionId, m.Pos, m.Stage, conn);

        private static void Broadcast(int who, Vector3Int pos, int stage, NetworkConnection except)
        {
            var msg = new BreakProgressMessage { Who = who, Pos = pos, Stage = Mathf.Clamp(stage, -1, 9) };
            foreach (var c in NetworkServer.connections.Values)
                if (c != except && c.isReady) c.Send(msg);   // отправителю его же трещины не нужны
        }
    }

    // Трещины других игроков: по одному оверлею на игрока, пропадают через 2.5 с без обновлений
    public class RemoteBreakOverlays : MonoBehaviour
    {
        private class Entry { public BlockBreakOverlay Ov; public Vector3Int Pos; public int Stage; public float Expire; }

        private static RemoteBreakOverlays _inst;
        private readonly Dictionary<int, Entry> _e = new Dictionary<int, Entry>();
        private readonly List<int> _rm = new List<int>();

        public static void Set(int who, Vector3Int pos, int stage)
        {
            if (_inst == null) _inst = new GameObject("RemoteBreakOverlays").AddComponent<RemoteBreakOverlays>();
            _inst.Apply(who, pos, stage);
        }

        private void Apply(int who, Vector3Int pos, int stage)
        {
            if (stage < 0) { Remove(who); return; }
            if (!_e.TryGetValue(who, out Entry e))
            {
                e = new Entry { Ov = new GameObject("BreakOverlay_" + who).AddComponent<BlockBreakOverlay>() };
                e.Ov.transform.SetParent(transform, false);
                _e[who] = e;
            }
            e.Pos = pos; e.Stage = Mathf.Clamp(stage, 0, 9); e.Expire = Time.time + 2.5f;
            Refresh(e);
        }

        private void Update()
        {
            _rm.Clear();
            foreach (var kv in _e)
            {
                if (Time.time > kv.Value.Expire) _rm.Add(kv.Key);
                else Refresh(kv.Value);
            }
            foreach (int k in _rm) Remove(k);
        }

        private static void Refresh(Entry e)
        {
            var wm = WorldManager.Instance;
            BlockType t = wm != null ? wm.GetBlockAtWorldPosition(e.Pos.x, e.Pos.y, e.Pos.z) : BlockType.Air;
            if (t == BlockType.Air) { e.Ov.Hide(); return; }   // блок уже сломан
            if (!BlockHitbox.TryGetBounds(t, out Vector3 min, out Vector3 max)) { min = Vector3.zero; max = Vector3.one; }
            e.Ov.Show(e.Pos, min, max, (e.Stage + 0.5f) / 10f);
        }

        private void Remove(int who)
        {
            if (_e.TryGetValue(who, out Entry e)) { if (e.Ov != null) Destroy(e.Ov.gameObject); _e.Remove(who); }
        }
    }
}