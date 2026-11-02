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

        public override void OnStartClient()
        {
            // поздно зашедшие игроки получают уже готовый скин
            if (_skinData != null && _skinData.Length > 0) Apply(_skinData);
        }

        public override void OnStartLocalPlayer()
        {
            byte[] bytes = LauncherSkin.GetBytes();
            if (bytes != null) CmdSetSkin(bytes);
        }

        [Command]
        private void CmdSetSkin(byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > LauncherSkin.MaxBytes) return;
            _skinData = data;
        }

        private void OnSkinChanged(byte[] oldValue, byte[] newValue) => Apply(newValue);

        private void Apply(byte[] data)
        {
            Texture2D tex = LauncherSkin.Decode(data);
            if (tex != null) _model.SetSkin(tex);
        }
    }
}