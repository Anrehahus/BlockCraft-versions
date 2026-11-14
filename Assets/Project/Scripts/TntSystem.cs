using System.Collections.Generic;
using UnityEngine;
using Mirror;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.Items;
using MinecraftClone.Mobs;

namespace MinecraftClone.Physics
{
    public struct TntIgniteRequestMessage : NetworkMessage { public Vector3Int Pos; }
    public struct TntDefuseRequestMessage : NetworkMessage { public int Id; }
    public struct TntPrimedMessage : NetworkMessage { public int Id; public Vector3 Pos; public float Fuse; }
    public struct TntRemoveMessage : NetworkMessage { public int Id; }

    // Зажжённый ТНТ. Создаётся сам при старте игры, в сцену ничего добавлять не нужно.
    // Сервер / одиночная игра решают всё (поджог, снятие, взрыв), клиенты только показывают сущность.
    public class TntSystem : MonoBehaviour
    {
        public const float FuseSeconds = 4f;        // время до взрыва (в оригинале 80 тиков = 4 с)
        private const float ExplosionRadius = 3.5f;   // радиус разрушения блоков
        private const float DamageRadius = 8f;      // радиус урона игрокам и мобам
        private const float MinDamage = 1f;
        private const float MaxDamage = 20f;
        private const float ReachSq = 64f;          // игрок не дальше 8 блоков

        public static TntSystem Instance { get; private set; }
        public static bool IsAuthority => NetworkServer.active || !NetworkClient.active;

        private readonly Dictionary<int, PrimedTntEntity> _tnt = new Dictionary<int, PrimedTntEntity>();
        private int _nextId = 1;
        private bool _srvReg, _cliReg;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("[TntSystem]");
            DontDestroyOnLoad(go);
            go.AddComponent<TntSystem>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        // Обработчики сообщений регистрируем здесь, чтобы не трогать NetworkManager.
        // Mirror очищает их при остановке сети, поэтому следим за состоянием каждый кадр.
        private void Update()
        {
            if (NetworkServer.active)
            {
                if (!_srvReg)
                {
                    _srvReg = true;
                    NetworkServer.RegisterHandler<TntIgniteRequestMessage>(OnIgniteRequest);
                    NetworkServer.RegisterHandler<TntDefuseRequestMessage>(OnDefuseRequest);
                }
            }
            else _srvReg = false;

            if (NetworkClient.active)
            {
                if (!_cliReg)
                {
                    _cliReg = true;
                    NetworkClient.RegisterHandler<TntPrimedMessage>(OnClientPrimed);
                    NetworkClient.RegisterHandler<TntRemoveMessage>(OnClientRemove);
                }
            }
            else _cliReg = false;
        }

        // ---------- Вызывается из PlayerBlockInteraction ----------

        public static void RequestIgnite(Vector3Int pos)
        {
            if (Instance == null) return;
            if (IsAuthority) Instance.ServerIgnite(pos, FuseSeconds);
            else NetworkClient.Send(new TntIgniteRequestMessage { Pos = pos });
        }

        public static void RequestDefuse(int id)
        {
            if (Instance == null) return;
            if (IsAuthority)
            {
                if (Instance._tnt.TryGetValue(id, out PrimedTntEntity e) && e != null) Instance.ServerDefuse(e);
            }
            else NetworkClient.Send(new TntDefuseRequestMessage { Id = id });
        }

        // ---------- Сервер ----------

        private void OnIgniteRequest(NetworkConnectionToClient conn, TntIgniteRequestMessage msg)
        {
            if (conn.identity == null) return;
            Vector3 c = new Vector3(msg.Pos.x + 0.5f, msg.Pos.y + 0.5f, msg.Pos.z + 0.5f);
            if ((conn.identity.transform.position - c).sqrMagnitude > ReachSq) return;
            ServerIgnite(msg.Pos, FuseSeconds);
        }

        private void OnDefuseRequest(NetworkConnectionToClient conn, TntDefuseRequestMessage msg)
        {
            if (conn.identity == null) return;
            if (!_tnt.TryGetValue(msg.Id, out PrimedTntEntity e) || e == null) return;
            if ((conn.identity.transform.position - e.transform.position).sqrMagnitude > ReachSq) return;
            ServerDefuse(e);
        }

        public void ServerIgnite(Vector3Int pos, float fuse)
        {
            var wm = WorldManager.Instance;
            if (wm == null || wm.GetBlockAtWorldPosition(pos.x, pos.y, pos.z) != BlockType.Tnt) return;

            // блок исчезает без частиц и звука поломки, вместо него появляется сущность
            wm.ApplyBlockChange(pos.x, pos.y, pos.z, BlockType.Air, rebuildMesh: true, silent: true);
            ServerSpawn(new Vector3(pos.x + 0.5f, pos.y + 0.5f, pos.z + 0.5f), fuse);
        }

        private void ServerSpawn(Vector3 center, float fuse)
        {
            int id = _nextId++;
            CreateEntity(id, center, fuse, true);
            if (NetworkServer.active)
                NetworkServer.SendToAll(new TntPrimedMessage { Id = id, Pos = center, Fuse = fuse });
        }

        private void ServerDefuse(PrimedTntEntity e)
        {
            Vector3 pos = e.transform.position;
            RemoveEntity(e.Id);

            int itemId = ItemRegistry.IdForBlock(BlockType.Tnt);
            if (itemId > 0 && ItemDropSystem.Instance != null)
                ItemDropSystem.Instance.ServerDropStack(itemId, 1, pos);
        }

        public void ServerExplode(PrimedTntEntity e)
        {
            Vector3 c = e.transform.position;
            RemoveEntity(e.Id);

            ChainIgnite(c);
            MobExplosion.Explode(c, ExplosionRadius);
            MobExplosion.DamagePlayers(c, DamageRadius, MinDamage, MaxDamage);
            MobExplosion.DamageMobs(c, DamageRadius, MinDamage, MaxDamage);
        }

        // другие ТНТ рядом не ломаются, а загораются с короткой задержкой (как в оригинале)
        private void ChainIgnite(Vector3 center)
        {
            var wm = WorldManager.Instance;
            if (wm == null) return;

            Vector3Int ci = Vector3Int.FloorToInt(center);
            int r = Mathf.CeilToInt(ExplosionRadius);
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (dx * dx + dy * dy + dz * dz > r * r) continue;
                        Vector3Int p = ci + new Vector3Int(dx, dy, dz);
                        if (wm.GetBlockAtWorldPosition(p.x, p.y, p.z) != BlockType.Tnt) continue;

                        wm.ApplyBlockChange(p.x, p.y, p.z, BlockType.Air, rebuildMesh: true, silent: true);
                        ServerSpawn(new Vector3(p.x + 0.5f, p.y + 0.5f, p.z + 0.5f), Random.Range(0.5f, 1.5f));
                    }
        }

        // ---------- Клиент ----------

        private void OnClientPrimed(TntPrimedMessage m)
        {
            if (NetworkServer.active) return;   // у хоста сущность уже создана сервером
            CreateEntity(m.Id, m.Pos, m.Fuse, false);
        }

        private void OnClientRemove(TntRemoveMessage m)
        {
            if (NetworkServer.active) return;
            DestroyEntity(m.Id);
        }

        // ---------- Общее ----------

        private void CreateEntity(int id, Vector3 center, float fuse, bool authoritative)
        {
            var go = new GameObject("PrimedTnt_" + id);
            go.transform.position = center;
            var e = go.AddComponent<PrimedTntEntity>();
            e.Init(this, id, fuse, authoritative);
            _tnt[id] = e;
        }

        private void RemoveEntity(int id)
        {
            if (NetworkServer.active) NetworkServer.SendToAll(new TntRemoveMessage { Id = id });
            DestroyEntity(id);
        }

        private void DestroyEntity(int id)
        {
            if (_tnt.TryGetValue(id, out PrimedTntEntity e))
            {
                _tnt.Remove(id);
                if (e != null) Destroy(e.gameObject);
            }
        }

        internal void Unregister(int id, PrimedTntEntity e)
        {
            if (_tnt.TryGetValue(id, out PrimedTntEntity cur) && cur == e) _tnt.Remove(id);
        }
    }

    // Один зажжённый ТНТ: мигает белым и раздувается перед взрывом
    public class PrimedTntEntity : MonoBehaviour
    {
        private static Material _flashBase;

        public int Id { get; private set; }

        private TntSystem _owner;
        private float _left;
        private bool _auth;
        private Material _flashMat;

        private const float Gravity = 16f;
        private const float Drag = 0.98f;
        private const float Terminal = -39.2f;
        private float _vy;

        public void Init(TntSystem owner, int id, float fuse, bool authoritative)
        {
            _owner = owner; Id = id; _left = fuse; _auth = authoritative;

            var wm = WorldManager.Instance;
            Mesh mesh = GravityBlockSystem.Instance != null ? GravityBlockSystem.Instance.GetBlockMesh(BlockType.Tnt) : null;

            if (mesh != null && wm != null)
            {
                // меш рисуется от центра низа блока, а сущность стоит в центре клетки: сдвигаем вниз на 0.5
                var body = new GameObject("Body");
                body.transform.SetParent(transform, false);
                body.transform.localPosition = new Vector3(0f, -0.5f, 0f);
                body.AddComponent<MeshFilter>().sharedMesh = mesh;
                body.AddComponent<MeshRenderer>().sharedMaterial = wm.BlocksMaterial;

                Material baseMat = GetFlashBase();
                if (baseMat != null)
                {
                    _flashMat = new Material(baseMat);
                    var flash = new GameObject("Flash");
                    flash.transform.SetParent(transform, false);
                    flash.transform.localScale = Vector3.one * 1.01f;
                    flash.transform.localPosition = new Vector3(0f, -0.505f, 0f);
                    flash.AddComponent<MeshFilter>().sharedMesh = mesh;
                    flash.AddComponent<MeshRenderer>().sharedMaterial = _flashMat;
                }
            }

            // по триггеру целимся лучом (PlayerBlockInteraction.TryAttackMob)
            var col = gameObject.AddComponent<BoxCollider>();
            col.size = Vector3.one;
            col.isTrigger = true;
        }

        // Белая полупрозрачная плёнка. Если белый не виден, добавьте Sprites/Default
        // в Project Settings -> Graphics -> Always Included Shaders.
        private static Material GetFlashBase()
        {
            if (_flashBase != null) return _flashBase;

            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) return null;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();

            _flashBase = new Material(sh) { mainTexture = tex, color = new Color(1f, 1f, 1f, 0f) };
            return _flashBase;
        }

        private void Update()
        {
            Fall();
            _left -= Time.deltaTime;

            // в последние 0.5 с раздувается до +30% (как в оригинале)
            float f = Mathf.Clamp01(1f - (_left * 20f + 1f) / 10f);
            f *= f; f *= f;
            transform.localScale = Vector3.one * (1f + f * 0.3f);

            if (_flashMat != null)
            {
                bool white = _left > 0f && Mathf.FloorToInt(_left / 0.25f) % 2 == 0;
                _flashMat.color = new Color(1f, 1f, 1f, white ? 0.85f : 0f);
            }

            if (_left <= 0f)
            {
                if (_auth) _owner.ServerExplode(this);
                else if (_left < -3f) Destroy(gameObject);   // сообщение от сервера потерялось
            }
        }

        private void OnDestroy()
        {
            if (_owner != null) _owner.Unregister(Id, this);
            if (_flashMat != null) Destroy(_flashMat);
        }

        private void Fall()
        {
            var gs = GravityBlockSystem.Instance;
            if (gs == null) return;

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            Vector3 p = transform.position;
            float bottom = p.y - 0.5f;                       // сущность стоит в центре, низ на 0.5 ниже
            int cx = Mathf.FloorToInt(p.x), cz = Mathf.FloorToInt(p.z);

            _vy = Mathf.Max((_vy - Gravity * dt) * Mathf.Pow(Drag, dt * 20f), Terminal);
            float newBottom = bottom + _vy * dt;

            int fromCell = Mathf.FloorToInt(bottom);
            int toCell = Mathf.FloorToInt(newBottom);
            for (int c = fromCell - 1; c >= toCell; c--)
            {
                if (!gs.CanFallThrough(new Vector3Int(cx, c, cz)))
                {
                    _vy = 0f;
                    transform.position = new Vector3(p.x, c + 1 + 0.5f, p.z);   // встаёт на блок
                    return;
                }
            }
            transform.position = new Vector3(p.x, newBottom + 0.5f, p.z);
        }
    }
}