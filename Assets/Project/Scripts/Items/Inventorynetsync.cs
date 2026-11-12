using UnityEngine;
using Mirror;
using MinecraftClone.Networking;
using MinecraftClone.Player;

namespace MinecraftClone.Items
{
    // Чистый клиент: после любого изменения склада (с небольшой задержкой) шлёт серверу снимок.
    // Сервер хранит копию по нику, сохраняет её в файл и раздаёт при смерти.
    public class InventoryNetSync : MonoBehaviour
    {
        private const float Debounce = 0.2f;   // ждём, пока изменения «устаканятся»

        private bool _dirty;
        private float _lastChange;

        private void OnEnable() { PlayerInventory.Changed += OnChanged; }
        private void OnDisable() { PlayerInventory.Changed -= OnChanged; }

        private void OnChanged()
        {
            _dirty = true;
            _lastChange = Time.unscaledTime;
        }

        private void Update()
        {
            if (!_dirty) return;

            bool pureClient = NetworkClient.active && !NetworkServer.active;
            if (!pureClient) { _dirty = false; return; }                      // у хоста и в одиночной игре сервер не нужен
            if (!NetworkClient.isConnected || !PlayerInventory.NetSynced) return;   // ждём ответа сервера
            if (Time.unscaledTime - _lastChange < Debounce) return;

            PlayerInventory.Encode(PlayerInventory.GetAll(), out int[] ids, out int[] counts);
            NetworkClient.Send(new InventorySyncMessage { Ids = ids, Counts = counts });
            _dirty = false;
        }
    }
}