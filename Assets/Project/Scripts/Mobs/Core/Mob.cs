using System.Collections.Generic;
using UnityEngine;
using Mirror;
using MinecraftClone.Management;
using MinecraftClone.Networking;

namespace MinecraftClone.Mobs
{
    // Единственный класс для всех мобов. Чем моб отличается от других, определяет MobDataSO.
    // Логика (цель, поведения, физика) выполняется только на сервере (в одиночной игре это хост).
    [RequireComponent(typeof(NetworkIdentity))]
    public class Mob : NetworkBehaviour
    {
        public static readonly List<Mob> All = new List<Mob>();

        [SyncVar] private string _mobId;
        [SyncVar] private int _chargeDir;           // +1 заряжается, -1 (или 0) разряжается

        public MobDataSO Data { get; private set; }
        public Transform Target { get; private set; }
        public MobMotor Motor { get; private set; }

        public float Charge { get; private set; }   // секунды, 0..Data.ChargeTime (считается и на клиентах)
        public float Charge01 => (Data != null && Data.ChargeTime > 0f) ? Mathf.Clamp01(Charge / Data.ChargeTime) : 0f;
        public Vector3 CenterPos => transform.position + Vector3.up * (Data != null ? Data.Height * 0.5f : 0.8f);

        private const float TurnSpeed = 360f;

        private BoxCollider _box;
        private GoalSelector _goals;
        private Vector3 _wishDir, _faceDir;
        private float _wishSpeed, _targetTimer;
        private bool _dead;

        // ---------- Кэш игроков ----------

        private static NetworkPlayer[] _players = new NetworkPlayer[0];
        private static float _playersTime = -10f;

        public static NetworkPlayer[] GetPlayers()
        {
            if (Time.unscaledTime - _playersTime > 0.5f)
            {
                _players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
                _playersTime = Time.unscaledTime;
            }
            return _players;
        }

        // ---------- Жизненный цикл ----------

        private void Awake()
        {
            _box = GetComponent<BoxCollider>();
            if (_box == null) _box = gameObject.AddComponent<BoxCollider>();

            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Вызывает спавнер на сервере ДО NetworkServer.Spawn
        [Server]
        public void ServerInit(MobDataSO data)
        {
            _mobId = data.Id;
            ApplyData(data);
        }

        private void ApplyData(MobDataSO d)
        {
            if (d == null)
            {
                Debug.LogError($"[Mob] Не найден MobDataSO с id '{_mobId}'. Лежит ли он в Resources/MobData?");
                return;
            }

            Data = d;
            name = d.Id;
            _box.size = new Vector3(d.Width, d.Height, d.Width);
            _box.center = new Vector3(0f, d.Height * 0.5f, 0f);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            All.Add(this);

            if (Data == null) { Debug.LogError("[Mob] ServerInit не вызван до Spawn"); return; }

            Motor = new MobMotor(Data);
            _goals = new GoalSelector();
            foreach (var def in Data.Goals)
                if (def != null) _goals.Add(def.priority, def.Create(this));
        }

        public override void OnStopServer()
        {
            All.Remove(this);
            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (Data == null) ApplyData(MobRegistry.Get(_mobId));
            if (Data != null && GetComponent<MobModel>() == null)
                gameObject.AddComponent<MobModel>().Build(this);
        }

        private void Update()
        {
            if (Data == null) return;
            float dt = Time.deltaTime;

            if (Data.ChargeTime > 0f)
                Charge = Mathf.Clamp(Charge + (_chargeDir > 0 ? dt : -dt), 0f, Data.ChargeTime);

            if (!isServer || _dead || Motor == null) return;
            if (WorldManager.Instance == null) return;

            ServerTick(Mathf.Min(dt, 0.05f));
        }

        // ---------- Серверная логика ----------

        private void ServerTick(float dt)
        {
            if (transform.position.y < -10f) { ServerDespawn(); return; }

            UpdateTarget(dt);

            _wishDir = Vector3.zero;
            _wishSpeed = 0f;
            _faceDir = Vector3.zero;

            _goals.Tick(dt);
            if (_dead) return;

            transform.position = Motor.Move(transform.position, _wishDir * _wishSpeed, dt);

            Vector3 face = _faceDir.sqrMagnitude > 0.001f ? _faceDir : _wishDir;
            if (face.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, Quaternion.LookRotation(face), TurnSpeed * dt);
            }
        }

        private void UpdateTarget(float dt)
        {
            _targetTimer -= dt;
            if (_targetTimer > 0f) return;
            _targetTimer = 0.25f;

            switch (Data.Targeting)
            {
                case TargetMode.None:
                    Target = null;
                    break;

                case TargetMode.NearestPlayer:
                    {
                        Transform best = null;
                        float bestSq = Data.FollowRange * Data.FollowRange;
                        Vector3 c = CenterPos;
                        foreach (var p in GetPlayers())
                        {
                            if (p == null) continue;
                            float sq = (p.transform.position + Vector3.up * 0.9f - c).sqrMagnitude;
                            if (sq < bestSq) { bestSq = sq; best = p.transform; }
                        }
                        Target = best;
                        break;
                    }

                case TargetMode.Retaliate:
                    // цель назначается извне (ServerSetTarget), здесь только забываем далёкую
                    if (Target != null)
                    {
                        float lim = Data.FollowRange * 1.5f;
                        if ((Target.position - transform.position).sqrMagnitude > lim * lim) Target = null;
                    }
                    break;
            }
        }

        // ---------- API для поведений (Goals) ----------

        public void MoveToward(Vector3 dir, float speed)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            _wishDir = dir.normalized;
            _wishSpeed = speed;
        }

        public void Face(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            _faceDir = dir.normalized;
        }

        [Server]
        public void ServerSetTarget(Transform t) => Target = t;

        [Server]
        public void ServerSetChargeDir(int dir) { if (_chargeDir != dir) _chargeDir = dir; }

        [Server]
        public void ServerDespawn()
        {
            if (_dead) return;
            _dead = true;
            NetworkServer.Destroy(gameObject);
        }
    }
}