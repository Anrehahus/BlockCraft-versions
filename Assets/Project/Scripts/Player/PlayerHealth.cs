using System;
using UnityEngine;
using Mirror;
using MinecraftClone.Core;

namespace MinecraftClone.Player
{
    // Здоровье игрока. 20 единиц = 10 сердец (одно сердце = 2 единицы).
    // Меняет значение только сервер (в одиночной игре без сети это сам игрок),
    // клиенты получают его через SyncVar и только показывают.
    // Компонент нужно добавить на префаб игрока.
    public class PlayerHealth : NetworkBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 20f;

        [Tooltip("Сколько секунд после удара игрок неуязвим (в Minecraft 0.5)")]
        [SerializeField] private float invulnerableSeconds = 0.5f;

        [SyncVar] private float _healthSync = 20f;   // что видят клиенты
        private float _healthLocal = 20f;            // что знает тот, кто управляет (сервер или одиночная игра)
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
            if (!RunsLogic || amount <= 0f) return;
            if (Time.time < _invulnerableUntil) return;

            _invulnerableUntil = Time.time + invulnerableSeconds;
            SetHealth(_healthLocal - amount);
        }

        public void Heal(float amount)
        {
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
            Died?.Invoke();

            // TODO: экран смерти и возрождение. Пока здоровье просто восстанавливается.
            Debug.Log("[PlayerHealth] Игрок погиб. Экрана смерти пока нет: здоровье восстановлено.");
            _invulnerableUntil = Time.time + 1f;
            SetHealth(maxHealth);
        }

        // Проверка без врагов: правый клик по компоненту в Inspector во время игры
        [ContextMenu("Test: damage 3 (1.5 сердца)")]
        private void TestDamage() => TakeDamage(3f, transform.position);

        [ContextMenu("Test: heal 3 (1.5 сердца)")]
        private void TestHeal() => Heal(3f);
    }
}