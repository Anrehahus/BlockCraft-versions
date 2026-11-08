using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using Mirror;

namespace MinecraftClone.Effects
{
    [RequireComponent(typeof(Camera))]
    public class UnderwaterEffect : MonoBehaviour
    {
        [Header("World Reference")]
        [SerializeField] private WorldManager worldManager;

        [Header("UI Overlays")]
        [SerializeField] private GameObject underwaterUIOverlay;
        [SerializeField] private GameObject lavaUIOverlay;

        [Header("Underwater Fog Settings")]
        [SerializeField] private Color underwaterFogColor = new Color(0.05f, 0.25f, 0.55f, 1f);
        [SerializeField] private float underwaterFogStart = -1.0f;
        [SerializeField] private float underwaterFogEnd = 12.0f;

        [Header("Lava Fog Settings")]
        // Яркий оранжево-красный цвет лавового тумана
        [SerializeField] private Color lavaFogColor = new Color(0.95f, 0.4f, 0.05f, 1f);
        [SerializeField] private float lavaFogStart = -1.5f; // Начинается сзади камеры, создавая глубокий объем
        [SerializeField] private float lavaFogEnd = 3.5f;    // Задает видимость блоков в пределах 3.5 - 4 блоков

        private enum LiquidState { None, Water, Lava }
        private LiquidState _currentState = LiquidState.None;
        private Camera _cam;

        private NetworkIdentity _identity;

        // Сохранение настроек главного тумана игрового мира
        private Color _defaultFogColor;
        private float _defaultFogStart;
        private float _defaultFogEnd;

        private void Start()
        {
            _cam = GetComponent<Camera>();
            _identity = GetComponentInParent<NetworkIdentity>();

            // Чужая копия игрока в сети: этот эффект ей не нужен и не должен трогать общий туман сцены
            if (_identity != null && _identity.netId != 0 && !_identity.isLocalPlayer)
            {
                enabled = false;
                return;
            }

            if (worldManager == null)
            {
                worldManager = FindFirstObjectByType<WorldManager>();
            }

            _defaultFogColor = WorldManager.FogColor;
            _cam.clearFlags = CameraClearFlags.SolidColor;   // сплошное синее небо вместо серого Skybox Unity
            if (GetComponent<HorizonHaze>() == null) gameObject.AddComponent<HorizonHaze>();
            _defaultFogStart = RenderSettings.fogStartDistance;
            _defaultFogEnd = RenderSettings.fogEndDistance;

            DisableAllOverlays();

            RenderSettings.fog = true;
            ApplyFogState(LiquidState.None);
        }

        private void Update()
        {
            CheckLiquidStatus();
        }

        private void CheckLiquidStatus()
        {
            if (worldManager == null) return;

            Vector3 eyePos = transform.position;
            Vector3Int blockPos = new Vector3Int(
                Mathf.FloorToInt(eyePos.x),
                Mathf.FloorToInt(eyePos.y),
                Mathf.FloorToInt(eyePos.z)
            );

            BlockType blockAtHead = worldManager.GetBlockAtWorldPosition(blockPos.x, blockPos.y, blockPos.z);

            LiquidState newState = LiquidState.None;
            if (blockAtHead == BlockType.Water) newState = LiquidState.Water;
            else if (blockAtHead == BlockType.Lava) newState = LiquidState.Lava;

            if (newState != _currentState)
            {
                ApplyFogState(newState);
            }
        }

        private void ApplyFogState(LiquidState state)
        {
            _currentState = state;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;

            DisableAllOverlays();

            switch (state)
            {
                case LiquidState.Water:
                    if (underwaterUIOverlay != null) underwaterUIOverlay.SetActive(true);

                    RenderSettings.fogColor = underwaterFogColor;
                    RenderSettings.fogStartDistance = underwaterFogStart;
                    RenderSettings.fogEndDistance = underwaterFogEnd;
                    _cam.backgroundColor = underwaterFogColor;
                    break;

                case LiquidState.Lava:
                    if (lavaUIOverlay != null) lavaUIOverlay.SetActive(true);

                    RenderSettings.fogColor = lavaFogColor;
                    RenderSettings.fogStartDistance = lavaFogStart;
                    RenderSettings.fogEndDistance = lavaFogEnd;
                    _cam.backgroundColor = lavaFogColor;
                    break;

                case LiquidState.None:
                default:
                    if (worldManager != null) { _defaultFogStart = worldManager.FogStart; _defaultFogEnd = worldManager.FogEnd; }
                    RenderSettings.fogColor = _defaultFogColor;
                    RenderSettings.fogStartDistance = _defaultFogStart;
                    RenderSettings.fogEndDistance = _defaultFogEnd;
                    _cam.backgroundColor = WorldManager.SkyColor;
                    break;
            }
        }

        private void DisableAllOverlays()
        {
            if (underwaterUIOverlay != null) underwaterUIOverlay.SetActive(false);
            if (lavaUIOverlay != null) lavaUIOverlay.SetActive(false);
        }

    }
}