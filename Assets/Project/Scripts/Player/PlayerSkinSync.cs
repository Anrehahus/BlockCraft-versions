using Mirror;
using UnityEngine;

namespace MinecraftClone.Player
{
    [RequireComponent(typeof(PlayerModel))]
    public class PlayerSkinSync : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnSkinChanged))] private byte[] _skinData;

        private PlayerModel _model;

        private void Awake() { _model = GetComponent<PlayerModel>(); }

        private void Start()
        {
            // Одиночная игра: сети нет, OnStartLocalPlayer не вызывается, поэтому применяем скин лаунчера напрямую.
            // В сети скин приходит через SyncVar, там этот код не нужен.
            if (NetworkServer.active || NetworkClient.active) return;

            byte[] bytes = LauncherSkin.GetBytes();
            if (bytes != null) Apply(bytes);
        }

        private void OnSkinChanged(byte[] oldData, byte[] newData)
        {
            Apply(newData);
        }

        public override void OnStartClient()
        {
            // поздно зашедшие игроки получают уже готовый скин
            if (_skinData != null && _skinData.Length > 0) Apply(_skinData);
        }

        public override void OnStartLocalPlayer()
        {
            byte[] bytes = LauncherSkin.GetBytes();
            Debug.Log("[Skin] LocalPlayer start, bytes=" + (bytes?.Length ?? -1));
            if (bytes != null) CmdSetSkin(bytes);
        }

        [Command]
        private void CmdSetSkin(byte[] data)
        {
            Debug.Log("[Skin] Cmd received on server, bytes=" + (data?.Length ?? -1));
            if (data == null || data.Length == 0 || data.Length > LauncherSkin.MaxBytes) return;
            _skinData = data;
        }

        private void Apply(byte[] data)
        {
            Texture2D tex = LauncherSkin.Decode(data);
            Debug.Log("[Skin] Apply, bytes=" + (data?.Length ?? -1) + ", decoded=" + (tex != null));
            if (tex != null) _model.SetSkin(tex);
        }
    }
}