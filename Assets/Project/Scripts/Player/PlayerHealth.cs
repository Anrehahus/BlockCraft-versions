using System;
using UnityEngine;
using Mirror;
using MinecraftClone.Core;
using MinecraftClone.Networking;
using MinecraftClone.Management;

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
            Died?.Invoke();
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