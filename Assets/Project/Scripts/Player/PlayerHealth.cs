using System;
using UnityEngine;
using Mirror;
using MinecraftClone.Core;
using MinecraftClone.Networking;
using MinecraftClone.Management;
using MinecraftClone.Items;
using System.Collections.Generic;

namespace MinecraftClone.Player
{
    // Здоровье игрока. 20 единиц = 10 сердец (одно сердце = 2 единицы).
    // Меняет значение только сервер (в одиночной игре без сети это сам игрок),
    // клиенты получают его через SyncVar и только показывают.
    // Компонент нужно добавить на префаб игрока.
    public class PlayerHealth : NetworkBehaviour, IDamageable
    {
        // Локальный игрок (в одиночной игре, у хоста и у клиента); нужен сохранению мира
        public static PlayerHealth Local { get; private set; }

        private void Start()
        {
            if (isLocalPlayer || (!NetworkServer.active && !NetworkClient.active)) Local = this;
        }

        // Состояние из сохранения: мёртвый игрок начинает с экрана смерти, счёт сохраняется
        public void ApplyLoadedState(bool dead, int score)
        {
            if (!RunsLogic) return;

            _scoreLocal = Mathf.Max(0, score);
            if (isServer) _scoreSync = _scoreLocal;

            _deadLocal = dead;
            if (isServer) _deadSync = dead;

            _healthLocal = dead ? 0f : maxHealth;
            if (isServer) _healthSync = _healthLocal;
            _invulnerableUntil = Time.time + 1f;
        }

        [SerializeField] private float maxHealth = 20f;
        [SyncVar] private int _hurtSync;
        private int _hurtLocal;
        public int HurtCount => RunsLogic ? _hurtLocal : _hurtSync;

        private const float KnockbackSpeed = 6f;
        private const float HopSpeed = 3.5f;

        [Tooltip("Сколько секунд после удара игрок неуязвим (в Minecraft 0.5)")]
        [SerializeField] private float invulnerableSeconds = 0.5f;

        [SyncVar] private float _healthSync = 20f;   // что видят клиенты
        private float _healthLocal = 20f;            // что знает тот, кто управляет (сервер или одиночная игра)
        [SyncVar] private bool _deadSync;
        private bool _deadLocal;
        [SyncVar] private int _scoreSync;
        private int _scoreLocal;

        public bool IsDead => RunsLogic ? _deadLocal : _deadSync;
        public int Score => RunsLogic ? _scoreLocal : _scoreSync;
        private float _invulnerableUntil;

        public event Action Died;

        // true на сервере и в одиночной игре без Mirror
        private bool RunsLogic => isServer || (!NetworkServer.active && !NetworkClient.active);

        public float MaxHealth => maxHealth;
        public float Health => RunsLogic ? _healthLocal : _healthSync;

        private void Awake()
        {
            _healthLocal = maxHealth;
            _healthSync = maxHealth;
        }

        public void TakeDamage(float amount, Vector3 sourcePosition)
        {
            if (_deadLocal) return;
            if (!RunsLogic || amount <= 0f) return;
            if (Time.time < _invulnerableUntil) return;

            _invulnerableUntil = Time.time + invulnerableSeconds;

            _hurtLocal++;
            if (isServer) _hurtSync = _hurtLocal;

            SetHealth(_healthLocal - amount);
            if (!_deadLocal) SendKnockback(sourcePosition);
        }

        public void Heal(float amount)
        {
            if (_deadLocal) return;
            if (!RunsLogic || amount <= 0f) return;
            SetHealth(_healthLocal + amount);
        }

        private void SetHealth(float value)
        {
            _healthLocal = Mathf.Clamp(value, 0f, maxHealth);
            if (isServer) _healthSync = _healthLocal;

            if (_healthLocal <= 0f) Die();
        }

        private void Die()
        {
            if (_deadLocal) return;
            _deadLocal = true;
            if (isServer) _deadSync = true;
            DropInventory();
            Died?.Invoke();
        }

        private void DropInventory()
        {
            if (PlayerBlockInteraction.CreativeInventory) return;
            var sys = MinecraftClone.Items.ItemDropSystem.Instance;
            if (sys == null) return;

            List<ItemStack> stacks;
            bool remote = isServer && !isLocalPlayer && connectionToClient != null;
            if (remote)
            {
                // игрок на удалённом клиенте: его склад хранит сервер
                var mnm = NetworkManager.singleton as MinecraftNetworkManager;
                stacks = mnm != null ? mnm.ServerTakeInventory(connectionToClient) : null;
            }
            else stacks = PlayerInventory.TakeAll();   // одиночная игра или хост

            if (stacks == null) return;
            Vector3 pos = transform.position + Vector3.up * 0.9f;
            foreach (var st in stacks) sys.ServerDropStack(st.Id, st.Count, pos);
        }

        // ---------- Возрождение ----------

        // Вызывает кнопка «Возродиться» у владельца игрока
        public void RequestRespawn()
        {
            if (!IsDead) return;
            if (NetworkClient.active && !NetworkServer.active) CmdRespawn();   // чистый клиент просит сервер
            else if (RunsLogic) ServerRespawn();                               // хост или одиночная игра
        }

        [Command]
        private void CmdRespawn() => ServerRespawn();

        private void ServerRespawn()
        {
            if (!_deadLocal) return;

            Vector3 pos = GetRespawnPosition();

            _deadLocal = false;
            _airTimer = 0f; _drownTimer = 0f;

            if (isServer) _deadSync = false;
            _scoreLocal = 0;
            if (isServer) _scoreSync = 0;

            _invulnerableUntil = Time.time + 2f;
            SetHealth(maxHealth);

            NetworkPlayer np = GetComponent<NetworkPlayer>();
            if (np != null)
            {
                if (isServer) np.ServerTeleport(pos);
                else np.TeleportLocal(pos);
            }
        }

        private static Vector3 GetRespawnPosition()
        {
            if (NetworkServer.active && NetworkManager.singleton is MinecraftNetworkManager mnm)
                return mnm.GetRespawnPosition();

            WorldManager wm = WorldManager.Instance;
            return wm != null ? wm.GetValidSpawnPosition(Vector2Int.zero) : new Vector3(0f, 80f, 0f);
        }

        // ---------- Счёт ----------

        public static int ScoreForMob(string mobId)
        {
            switch (mobId)
            {
                case "zombie": return 100;
                case "skeleton": return 100;
                case "creeper": return 250;
                case "pig": return 10;
                case "spider": return 10;
                default: return 0;
            }
        }

        // Вызвать на сервере, когда этот игрок убил моба: health.AddKill(mob.Data.Id)
        public void AddKill(string mobId) => AddScore(ScoreForMob(mobId));

        public void AddScore(int points)
        {
            if (!RunsLogic || points <= 0 || _deadLocal) return;
            _scoreLocal += points;
            if (isServer) _scoreSync = _scoreLocal;
        }

        private void SendKnockback(Vector3 source)
        {
            Vector3 dir = transform.position - source;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : -transform.forward;
            Vector3 kv = dir * KnockbackSpeed;

            // позицией управляет владелец, поэтому толчок отправляем ему
            if (isServer && !isLocalPlayer && connectionToClient != null) TargetKnockback(connectionToClient, kv);
            else ApplyKnockback(kv);   // хост или одиночная игра
        }

        // Еда: хост и одиночная игра лечат сами, чистый клиент просит сервер
        public void RequestEat(int itemId)
        {
            float heal = ItemRegistry.FoodHealOf(itemId);
            if (IsDead || heal <= 0f) return;
            if (NetworkClient.active && !NetworkServer.active) CmdEat(itemId);
            else if (RunsLogic) Heal(heal);
        }

        [Command]
        private void CmdEat(int itemId)
        {
            float heal = ItemRegistry.FoodHealOf(itemId);
            if (heal <= 0f) return;
            Heal(heal);
        }

        // Урон среды (вода, лава): как TakeDamage, но без отбрасывания
        public void TakeEnvironmentalDamage(float amount)
        {
            if (_deadLocal || !RunsLogic || amount <= 0f) return;
            if (Time.time < _invulnerableUntil) return;

            _invulnerableUntil = Time.time + invulnerableSeconds;
            _hurtLocal++;
            if (isServer) _hurtSync = _hurtLocal;
            SetHealth(_healthLocal - amount);
        }

        // ---------- Вода и лава (считает только сервер или одиночная игра) ----------

        private const float EyeHeight = 1.62f;
        private const float DrownDelay = 7f;      // секунд с головой под водой до первого урона
        private const float DrownDamage = 2f;     // 1 сердце
        private const float DrownInterval = 1f;   // потом каждую секунду
        private const float LavaDamage = 4f;      // 2 сердца за каждые 0.5 с
        private float _airTimer;
        private float _drownTimer;

        private void Update()
        {
            if (!RunsLogic || _deadLocal) return;
            var wm = WorldManager.Instance;
            if (wm == null || !wm.IsWorldReady) return;

            Vector3 p = transform.position;   // pivot у ног

            // лава: достаточно, чтобы она была у ног, тела или головы
            if (BlockAt(wm, p + Vector3.up * 0.1f) == BlockType.Lava ||
                BlockAt(wm, p + Vector3.up * 0.9f) == BlockType.Lava ||
                BlockAt(wm, p + Vector3.up * EyeHeight) == BlockType.Lava)
                TakeEnvironmentalDamage(LavaDamage);

            // вода: считается только с головой
            if (BlockAt(wm, p + Vector3.up * EyeHeight) == BlockType.Water)
            {
                _airTimer += Time.deltaTime;
                if (_airTimer >= DrownDelay)
                {
                    _drownTimer -= Time.deltaTime;
                    if (_drownTimer <= 0f)
                    {
                        _drownTimer = DrownInterval;
                        TakeEnvironmentalDamage(DrownDamage);
                    }
                }
            }
            else
            {
                _airTimer = Mathf.Max(0f, _airTimer - Time.deltaTime * 4f);   // воздух набирается быстрее, чем тратится
                _drownTimer = 0f;
            }
        }

        private static BlockType BlockAt(WorldManager wm, Vector3 p)
        {
            return wm.GetBlockAtWorldPosition(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
        }

        [TargetRpc]
        private void TargetKnockback(NetworkConnection target, Vector3 kv) => ApplyKnockback(kv);

        private void ApplyKnockback(Vector3 kv)
        {
            PlayerController pc = GetComponent<PlayerController>();
            if (pc != null) pc.AddKnockback(kv, HopSpeed);
        }

        [ContextMenu("Test: die")]
        private void TestDie() => TakeDamage(999f, transform.position);

        [ContextMenu("Test: +250 очков")]
        private void TestScore() => AddScore(250);

        // Проверка без врагов: правый клик по компоненту в Inspector во время игры
        [ContextMenu("Test: damage 3 (1.5 сердца)")]
        private void TestDamage() => TakeDamage(3f, transform.position);

        [ContextMenu("Test: heal 3 (1.5 сердца)")]
        private void TestHeal() => Heal(3f);
    }
}