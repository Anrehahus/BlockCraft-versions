using UnityEngine;
using Mirror;
using MinecraftClone.Management;

namespace MinecraftClone.Player
{
    // Вид от первого лица: рука справа внизу, покачивание при ходьбе и взмах при ударе.
    // Добавляется автоматически вместе с PlayerModel.
    public class FirstPersonView : MonoBehaviour
    {
        [Header("Рука (подгоните под вкус)")]
        [SerializeField] private Vector3 armPosition = new Vector3(0.22f, -0.48f, 0.28f);
        [SerializeField] private Vector3 armRotation = new Vector3(-150f, -15f, 0f);
        [SerializeField] private float armScale = 1f;

        [Header("Покачивание при ходьбе")]
        [SerializeField] private float cameraBobX = 0.02f;
        [SerializeField] private float cameraBobY = 0.025f;
        [SerializeField] private float cameraRoll = 0.5f;      // градусы
        [SerializeField] private float armBobX = 0.02f;
        [SerializeField] private float armBobY = 0.025f;

        private const float SwingDuration = 0.3f;

        private PlayerModel _model;
        private PlayerHealth _health;
        private NetworkIdentity _identity;
        private CharacterController _controller;
        private Camera _cam;

        private Vector3 _camBasePos;
        private bool _hasBase;

        private Transform _holder;
        private Mesh _srcMesh;
        private Material _mat;
        private bool _shaderMissing;

        private Vector3 _lastPos;
        private float _phase, _amount;
        private float _swing = -1f;

        public void Swing() { _swing = 0f; }

        private bool IsOwn => _identity == null || _identity.netId == 0 || _identity.isLocalPlayer;

        private void Start()
        {
            _model = GetComponent<PlayerModel>();
            _health = GetComponent<PlayerHealth>();
            _identity = GetComponent<NetworkIdentity>();
            _controller = GetComponent<CharacterController>();
            _cam = GetComponentInChildren<Camera>(true);
            _lastPos = transform.position;
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }

        private void LateUpdate()
        {
            if (!IsOwn || _cam == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (!_hasBase) { _camBasePos = _cam.transform.localPosition; _hasBase = true; }

            bool dead = _health != null && _health.IsDead;

            // ходьба
            Vector3 delta = transform.position - _lastPos;
            _lastPos = transform.position;
            delta.y = 0f;
            float dist = delta.magnitude;
            float speed = dist / dt;
            if (speed > 20f) { dist = 0f; speed = 0f; }   // телепорт, а не ходьба

            bool walking = !dead && _controller != null && _controller.enabled && _controller.isGrounded && speed > 0.3f;
            float target = walking ? Mathf.Clamp01(speed / 4.3f) : 0f;
            _amount = Mathf.Lerp(_amount, target, 10f * dt);
            if (walking) _phase += dist * 2.8f;

            float s = Mathf.Sin(_phase), c = Mathf.Cos(_phase);

            // камера
            if (!dead)
            {
                float camAmount = GameSettings.ViewBobbing ? _amount : 0f;   // настройка влияет только на камеру
                _cam.transform.localPosition = _camBasePos + new Vector3(s * cameraBobX, -Mathf.Abs(c) * cameraBobY, 0f) * camAmount;
                float px = Mathf.DeltaAngle(0f, _cam.transform.localEulerAngles.x);
                _cam.transform.localRotation = Quaternion.Euler(px, 0f, s * cameraRoll * camAmount);
            }

            // рука
            EnsureArm();
            if (_holder == null) return;
            _holder.gameObject.SetActive(!dead);

            Vector3 pos = armPosition + new Vector3(s * armBobX, -Mathf.Abs(c) * armBobY, 0f) * _amount;
            Vector3 rot = armRotation;

            if (_swing >= 0f)
            {
                _swing += dt;
                float p = _swing / SwingDuration;
                if (p >= 1f) _swing = -1f;
                else
                {
                    float k = Mathf.Sin(p * Mathf.PI);
                    rot.x += k * 50f;                                          // рука подаётся вперёд, как удар
                    pos += new Vector3(-0.06f * k, -0.04f * k, 0.12f * k);     // и немного вниз-влево
                }
            }

            _holder.localPosition = pos;
            _holder.localRotation = Quaternion.Euler(rot);
        }

        // Рука = копия правой руки модели с отдельным материалом, который рисуется поверх мира
        private void EnsureArm()
        {
            if (_model == null || _shaderMissing) return;

            Transform src = _model.RightArm;
            if (src == null) return;
            MeshFilter smf = src.GetComponent<MeshFilter>();
            if (smf == null || smf.sharedMesh == null) return;
            if (_holder != null && smf.sharedMesh == _srcMesh) return;   // уже построена и скин не менялся

            if (_holder != null) Destroy(_holder.gameObject);
            if (_mat != null) Destroy(_mat);

            Shader sh = Resources.Load<Shader>("Shaders/ViewModel");
            if (sh == null) sh = Shader.Find("Minecraft/ViewModel");
            if (sh == null)
            {
                _shaderMissing = true;
                Debug.LogWarning("[FirstPersonView] Не найден шейдер Resources/Shaders/ViewModel.shader");
                return;
            }

            _mat = new Material(sh);
            Texture2D skin = _model.Skin;
            if (skin != null) { skin.filterMode = FilterMode.Point; _mat.mainTexture = skin; }

            _srcMesh = smf.sharedMesh;

            GameObject go = new GameObject("ViewArm");
            _holder = go.transform;
            _holder.SetParent(_cam.transform, false);
            _holder.localScale = Vector3.one * armScale;

            go.AddComponent<MeshFilter>().sharedMesh = _srcMesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }
}