using UnityEngine;

namespace MinecraftClone.Core
{
    // Всё, что может получать урон: игрок сейчас, мобы позже.
    public interface IDamageable
    {
        float Health { get; }
        float MaxHealth { get; }

        // Урон наносится только там, где живёт логика (сервер или одиночная игра),
        // на чистом клиенте вызов ничего не делает.
        void TakeDamage(float amount, Vector3 sourcePosition);
    }
}