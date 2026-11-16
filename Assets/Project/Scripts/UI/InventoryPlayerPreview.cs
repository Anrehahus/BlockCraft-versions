using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Player;
using MinecraftClone.Rendering;

namespace MinecraftClone.UI
{
    // Превью игрока для инвентаря: копия модели в стороне от мира, снимается камерой в RenderTexture
    public class InventoryPlayerPreview : MonoBehaviour
    {
        private const float CamHeight = 0.9f;    // центр кадра (модель высотой 1.8 м)
        private const float OrthoSize = 1.05f;   // половина высоты кадра, м

        private Camera _cam;
        private RenderTexture _rt;
        private Transform _model, _head;
        private readonly List<Material> _mats = new List<Material>();
        private float _yaw, _pitch;

        private void Awake()
        {
            transform.position = new Vector3(0f, 5000f, 0f);
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0f, CamHeight, 3f);
            camGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = OrthoSize;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 10f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.depth = -50f;
            _cam.enabled = false;
        }

        public void Show(PlayerModel pm)
        {
            Hide();
            Transform src = pm != null ? pm.transform.Find("Model") : null;
            if (src == null) return;

            GameObject go = Instantiate(src.gameObject, transform);
            go.name = "PreviewModel";
            go.SetActive(true);
            _model = go.transform;
            _model.localPosition = Vector3.zero;
            _model.localRotation = Quaternion.identity;
            _model.localScale = Vector3.one;
            _head = _model.Find("Head");
            foreach (Transform part in _model) part.localRotation = Quaternion.identity;   // убрать позу ходьбы/удара

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material m = new Material(r.sharedMaterial);
                LightTint.SetColor(m, Color.white);     // без затемнения от света в пещере
                _mats.Add(m);
                r.sharedMaterial = m;
                r.enabled = true;
            }

            _yaw = _pitch = 0f;
            _cam.enabled = true;
        }

        public void Hide()
        {
            if (_cam != null) { _cam.enabled = false; _cam.targetTexture = null; }
            if (_model != null) Destroy(_model.gameObject);
            _model = null; _head = null;
            foreach (var m in _mats) if (m != null) Destroy(m);
            _mats.Clear();
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
        }

        // Вызывать из OnGUI (только Repaint). box — чёрный блок на экране, headScreen — где на экране голова.
        public void Draw(Rect box, Vector2 headScreen, Vector2 mouse, float s)
        {
            if (_model == null) return;

            int w = Mathf.RoundToInt(box.width), h = Mathf.RoundToInt(box.height);
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                if (_rt != null) { _cam.targetTexture = null; _rt.Release(); Destroy(_rt); }
                _rt = new RenderTexture(w, h, 16) { filterMode = FilterMode.Point };
                _cam.targetTexture = _rt;
            }

            // как в Minecraft: голова и тело поворачиваются за курсором
            float dx = (mouse.x - headScreen.x) / (40f * s);
            float dy = (mouse.y - headScreen.y) / (40f * s);
            float targetYaw = Mathf.Clamp(-Mathf.Atan(dx) * 40f, -60f, 60f);
            float targetPitch = Mathf.Clamp(Mathf.Atan(dy) * 20f, -30f, 30f);

            float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            _yaw = Mathf.Lerp(_yaw, targetYaw, k);
            _pitch = Mathf.Lerp(_pitch, targetPitch, k);

            _model.localRotation = Quaternion.Euler(0f, _yaw * 0.5f, 0f);
            if (_head != null) _head.localRotation = Quaternion.Euler(_pitch, _yaw * 0.5f, 0f);

            GUI.DrawTexture(box, _rt, ScaleMode.StretchToFill, false);
        }

        private void OnDestroy() { Hide(); }
    }
}