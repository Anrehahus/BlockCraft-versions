using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Mirror;
using MinecraftClone.Management;
using UnityEngine.SceneManagement;
using MinecraftClone.Core;
using MinecraftClone.UI;
using MinecraftClone.ScriptableObjects;
using MinecraftClone.Player;

namespace MinecraftClone.Networking
{
    public struct NetworkPlayerMessage : NetworkMessage
    {
        public string Nickname;
    }

    public struct RejectConnectionMessage : NetworkMessage
    {
        public string Reason;
    }

    public struct WorldSeedMessage : NetworkMessage
    {
        public int Seed;
    }

    public struct BlockChangeRequestMessage : NetworkMessage { public Vector3Int Pos; public BlockType Type; }
    public struct BlockChangeMessage : NetworkMessage { public Vector3Int Pos; public BlockType Type; }
    public struct ChunkRequestMessage : NetworkMessage { public int X; public int Z; }
    public struct ChunkDataMessage : NetworkMessage { public int X; public int Z; public int[] Rle; }
    public struct ChatSendMessage : NetworkMessage { public string Text; }
    public struct ChatMessage : NetworkMessage { public string Text; public byte Kind; }

    public class MinecraftNetworkManager : NetworkManager
    {
        public static MinecraftNetworkManager Instance => singleton as MinecraftNetworkManager;

        [Header("Настройки спавна игроков")]
        [SerializeField] private GameObject playerNetworkPrefab;
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        public static bool ExpectedDisconnect;
        public static string LastError = "";

        private readonly Dictionary<int, string> _connectedNicknames = new Dictionary<int, string>();
        private WorldManager _worldManager;

        [System.Serializable]
        public class BanData
        {
            public List<string> Names = new List<string>(); // ники в нижнем регистре
            public List<string> Ips = new List<string>();
        }

        private BanData _bans = new BanData();

        [System.Serializable]
        public class SpawnData
        {
            public bool IsSet;
            public float X, Y, Z;
        }

        private SpawnData _spawn = new SpawnData();

        private static string GetSpawnFilePath()
        {
            string dir = SaveSystem.Instance != null
                ? SaveSystem.Instance.GetSlotFolderPath(WorldSlotSelection.Slot)
                : Application.persistentDataPath;
            return Path.Combine(dir, "spawn.json");
        }

        private void LoadSpawn()
        {
            _spawn = new SpawnData();
            try
            {
                string path = GetSpawnFilePath();
                if (!File.Exists(path)) return;
                var loaded = JsonUtility.FromJson<SpawnData>(File.ReadAllText(path));
                if (loaded != null) _spawn = loaded;
            }
            catch (System.Exception ex) { Debug.LogWarning("[Spawn] Не удалось загрузить: " + ex.Message); }
        }

        private void SaveSpawn()
        {
            try
            {
                string path = GetSpawnFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(_spawn, true));
            }
            catch (System.Exception ex) { Debug.LogWarning("[Spawn] Не удалось сохранить: " + ex.Message); }
        }

        private static string GetBanFilePath()
        {
            string dir = SaveSystem.Instance != null
                ? SaveSystem.Instance.GetSlotFolderPath(WorldSlotSelection.Slot)
                : Application.persistentDataPath;
            return Path.Combine(dir, "banned.json");
        }

        private void LoadBans()
        {
            _bans = new BanData();
            try
            {
                string path = GetBanFilePath();
                if (!File.Exists(path)) return;
                var loaded = JsonUtility.FromJson<BanData>(File.ReadAllText(path));
                if (loaded == null) return;
                if (loaded.Names == null) loaded.Names = new List<string>();
                if (loaded.Ips == null) loaded.Ips = new List<string>();
                _bans = loaded;
            }
            catch (System.Exception ex) { Debug.LogWarning("[Bans] Не удалось загрузить: " + ex.Message); }
        }

        private void SaveBans()
        {
            try
            {
                string path = GetBanFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(_bans, true));
            }
            catch (System.Exception ex) { Debug.LogWarning("[Bans] Не удалось сохранить: " + ex.Message); }
        }

        private static string NormalizeIp(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return "";
            ip = ip.Trim();
            if (ip.StartsWith("::ffff:")) ip = ip.Substring(7);
            return ip;
        }

        public override void Start()
        {
            base.Start();
            _worldManager = WorldManager.Instance != null ? WorldManager.Instance : Object.FindFirstObjectByType<WorldManager>();
        }

        // --- ЛОГИКА СЕРВЕРА ---

        public override void OnStartServer()
        {
            base.OnStartServer();
            LoadBans();
            LoadSpawn();
            NetworkServer.RegisterHandler<NetworkPlayerMessage>(OnServerReceivePlayerInfo);
            NetworkServer.RegisterHandler<BlockChangeRequestMessage>(OnServerBlockRequest);
            NetworkServer.RegisterHandler<ChunkRequestMessage>(OnServerChunkRequest);
            NetworkServer.RegisterHandler<ChatSendMessage>(OnServerChatSend);
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);
            if (conn == NetworkServer.localConnection) return;
            if (_bans.Ips.Contains(NormalizeIp(conn.address)))
                KickConnection(conn, "Ваш IP-адрес забанен на этом сервере");
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            // Ручной спавн игроков управляется через OnServerReceivePlayerInfo
        }

        private void OnServerBlockRequest(NetworkConnectionToClient conn, BlockChangeRequestMessage msg)
        {
            var wm = WorldManager.Instance;
            if (conn.identity == null || wm == null) return;

            // игрок не может менять блоки дальше ~8 блоков от себя
            Vector3 center = new Vector3(msg.Pos.x + 0.5f, msg.Pos.y + 0.5f, msg.Pos.z + 0.5f);
            if ((conn.identity.transform.position - center).sqrMagnitude > 64f) return;

            if (msg.Type == BlockType.Air)
            {
                BlockType cur = wm.GetBlockAtWorldPosition(msg.Pos.x, msg.Pos.y, msg.Pos.z);
                if (cur == BlockType.Air) return;
                if (wm.BlockDatabase != null && wm.BlockDatabase.TryGetValue(cur, out BlockDataSO d) && d != null && !d.IsBreakable) return;
            }

            if (msg.Type != BlockType.Air
                && wm.BlockDatabase != null
                && wm.BlockDatabase.TryGetValue(msg.Type, out BlockDataSO placed)
                && placed != null && placed.IsSolid
                && NetworkPlayer.IsBlockOverlappingAnyPlayer(msg.Pos))
            {
                return;
            }

            // проверки установки внутри + применение + рассылка всем
            wm.SetBlockAtWorldPosition(msg.Pos.x, msg.Pos.y, msg.Pos.z, msg.Type);
        }

        private void OnServerChunkRequest(NetworkConnectionToClient conn, ChunkRequestMessage msg)
        {
            if (WorldManager.Instance == null || SaveSystem.Instance == null) return;
            ChunkData chunk = WorldManager.Instance.GetOrGenerateChunk(new Vector2Int(msg.X, msg.Z));
            conn.Send(new ChunkDataMessage { X = msg.X, Z = msg.Z, Rle = SaveSystem.Instance.CompressBlocksRLE(chunk) });
        }

        private void OnServerChatSend(NetworkConnectionToClient conn, ChatSendMessage msg)
        {
            // ник берём у сервера, подделать его клиент не может
            if (!_connectedNicknames.TryGetValue(conn.connectionId, out string nick)) return;

            string text = (msg.Text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > 100) text = text.Substring(0, 100);

            if (text.StartsWith("/")) { HandleCommand(conn, text); return; }

            NetworkServer.SendToAll(new ChatMessage { Text = $"<{nick}> {text}", Kind = 0 });
        }

        private void OnServerReceivePlayerInfo(NetworkConnectionToClient conn, NetworkPlayerMessage msg)
        {
            string cleanNick = string.IsNullOrEmpty(msg.Nickname) ? "Player" : msg.Nickname.Trim();

            if (conn != NetworkServer.localConnection &&
                (_bans.Names.Contains(cleanNick.ToLowerInvariant()) || _bans.Ips.Contains(NormalizeIp(conn.address))))
            {
                KickConnection(conn, "Вы забанены на этом сервере");
                return;
            }

            if (_worldManager == null) _worldManager = WorldManager.Instance != null ? WorldManager.Instance : Object.FindFirstObjectByType<WorldManager>();

            // Если это локальный хост (игрок уже существует на сервере)
            if (conn.identity != null)
            {
                _connectedNicknames[conn.connectionId] = cleanNick;
                var netPlayer = conn.identity.GetComponent<NetworkPlayer>();
                if (netPlayer != null)
                {
                    netPlayer.ServerSetNickname(cleanNick);
                }

                if (_worldManager != null && _worldManager.Config != null)
                {
                    conn.Send(new WorldSeedMessage { Seed = _worldManager.Config.Seed });
                }
                return;
            }

            // Проверка уникальности никнейма без учёта регистра
            bool nickTaken = false;
            foreach (var n in _connectedNicknames.Values)
                if (string.Equals(n, cleanNick, System.StringComparison.OrdinalIgnoreCase)) { nickTaken = true; break; }
            if (nickTaken)
            {
                conn.Send(new RejectConnectionMessage { Reason = "Игрок с таким ником уже в игре!" });
                StartCoroutine(DisconnectWithDelay(conn));
                return;
            }

            _connectedNicknames[conn.connectionId] = cleanNick;
            if (conn != NetworkServer.localConnection)
                NetworkServer.SendToAll(new ChatMessage { Text = $"{cleanNick} присоединился к игре", Kind = 1 });

            // 1. Отправляем Seed мира подключившемуся клиенту
            if (_worldManager != null && _worldManager.Config != null)
            {
                conn.Send(new WorldSeedMessage { Seed = _worldManager.Config.Seed });
            }

            // 2. Загружаем или спавним игрока
            Vector3 spawnPosition = _worldManager != null ? _worldManager.GetValidSpawnPosition(Vector2Int.zero) : Vector3.up * 80f;
            Vector2 spawnRotation = Vector2.zero;
            if (_spawn.IsSet) spawnPosition = new Vector3(_spawn.X, _spawn.Y, _spawn.Z);

            bool isHostPlayer = conn == NetworkServer.localConnection;
            if (isHostPlayer)
            {
                // хост остаётся там, где стоял в одиночной игре
                foreach (var p in GameObject.FindGameObjectsWithTag("Player"))
                {
                    var pid = p.GetComponent<NetworkIdentity>();
                    if (pid == null || pid.netId == 0) { spawnPosition = p.transform.position; break; }
                }
            }

            string playerDataDir = GetPlayerDataDir();
            string playerFilePath = Path.Combine(playerDataDir, $"{cleanNick}.json");

            if (!isHostPlayer && File.Exists(playerFilePath))
            {
                try
                {
                    string json = File.ReadAllText(playerFilePath);
                    PlayerSaveData data = JsonUtility.FromJson<PlayerSaveData>(json);
                    if (data != null)
                    {
                        spawnPosition = new Vector3(data.posX, data.posY, data.posZ);
                        spawnRotation = new Vector2(data.rotX, data.rotY);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[NetworkManager] Не удалось загрузить данные игрока {cleanNick}: {ex.Message}");
                }
            }

            GameObject prefabToSpawn = playerNetworkPrefab != null ? playerNetworkPrefab : playerPrefab;

            if (prefabToSpawn == null)
            {
                Debug.LogError("[NetworkManager] Префаб игрока не назначен в NetworkManager!");
                return;
            }

            GameObject playerObj = Instantiate(prefabToSpawn, spawnPosition, Quaternion.Euler(spawnRotation.x, spawnRotation.y, 0));

            var playerNetwork = playerObj.GetComponent<NetworkPlayer>();
            if (playerNetwork != null)
            {
                playerNetwork.ServerSetNickname(cleanNick);
            }

            NetworkServer.AddPlayerForConnection(conn, playerObj);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (_connectedNicknames.TryGetValue(conn.connectionId, out string nickname))
            {
                if (conn.identity != null)
                {
                    SavePlayerData(nickname, conn.identity.transform.position, conn.identity.transform.eulerAngles);
                }
                _connectedNicknames.Remove(conn.connectionId);
                if (conn != NetworkServer.localConnection)
                    NetworkServer.SendToAll(new ChatMessage { Text = $"{nickname} покинул игру", Kind = 1 });
            }

            base.OnServerDisconnect(conn);
        }

        private static string GetPlayerDataDir()
        {
            string slotDir = SaveSystem.Instance != null
                ? SaveSystem.Instance.GetSlotFolderPath(WorldSlotSelection.Slot)
                : Application.persistentDataPath;
            return Path.Combine(slotDir, "playerdata");
        }

        public void SavePlayerData(string nickname, Vector3 pos, Vector3 rot)
        {
            string playerDataDir = GetPlayerDataDir();
            if (!Directory.Exists(playerDataDir)) Directory.CreateDirectory(playerDataDir);

            PlayerSaveData data = new PlayerSaveData
            {
                posX = pos.x,
                posY = pos.y,
                posZ = pos.z,
                rotX = rot.x,
                rotY = rot.y
            };

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(Path.Combine(playerDataDir, $"{nickname}.json"), json);
        }

        public void SaveAllPlayers()
        {
            foreach (var kvp in NetworkServer.connections)
            {
                if (kvp.Value.identity != null && _connectedNicknames.TryGetValue(kvp.Key, out string nick))
                    SavePlayerData(nick, kvp.Value.identity.transform.position, kvp.Value.identity.transform.eulerAngles);
            }
        }

        private IEnumerator DisconnectWithDelay(NetworkConnectionToClient conn)
        {
            yield return new WaitForSeconds(0.1f);
            conn.Disconnect();
        }

        private void Reply(NetworkConnectionToClient conn, string text, byte kind = 0)
        {
            conn.Send(new ChatMessage { Text = text, Kind = kind });
        }

        private NetworkConnectionToClient FindConnByNick(string nick)
        {
            foreach (var kvp in _connectedNicknames)
            {
                if (string.Equals(kvp.Value, nick, System.StringComparison.OrdinalIgnoreCase)
                    && NetworkServer.connections.TryGetValue(kvp.Key, out NetworkConnectionToClient c))
                    return c;
            }
            return null;
        }

        private void KickConnection(NetworkConnectionToClient c, string reason)
        {
            c.Send(new RejectConnectionMessage { Reason = reason });
            StartCoroutine(DisconnectWithDelay(c));
        }

        private void HandleSay(NetworkConnectionToClient conn, string targetNick, string message)
        {
            if (targetNick == "" || message == "")
            {
                Reply(conn, "Использование: /say <ник> <текст>", 2);
                return;
            }

            var target = FindConnByNick(targetNick);
            if (target == null)
            {
                Reply(conn, $"Игрок {targetNick} не найден", 2);
                return;
            }

            if (target == conn)
            {
                Reply(conn, "Нельзя написать самому себе", 2);
                return;
            }

            // ник отправителя и получателя берём у сервера, подделать их нельзя
            string senderNick = _connectedNicknames.TryGetValue(conn.connectionId, out string sn) ? sn : "Игрок";
            string targetRealNick = _connectedNicknames.TryGetValue(target.connectionId, out string tn) ? tn : targetNick;

            target.Send(new ChatMessage { Text = $"{senderNick} шепчет вам: {message}", Kind = 3 });
            Reply(conn, $"Вы шепчете {targetRealNick}: {message}", 3);
        }

        private string GetNick(NetworkConnectionToClient c)
            => _connectedNicknames.TryGetValue(c.connectionId, out string n) ? n : "Игрок";

        private static NetworkPlayer GetNetPlayer(NetworkConnectionToClient c)
            => c != null && c.identity != null ? c.identity.GetComponent<NetworkPlayer>() : null;

        private void HandleSetSpawn(NetworkConnectionToClient conn)
        {
            if (conn.identity == null) { Reply(conn, "Ваш игрок ещё не создан", 2); return; }

            Vector3 p = conn.identity.transform.position;
            _spawn = new SpawnData { IsSet = true, X = Mathf.Floor(p.x) + 0.5f, Y = p.y, Z = Mathf.Floor(p.z) + 0.5f };
            SaveSpawn();

            Reply(conn, $"Точка возрождения установлена: {_spawn.X:F1}, {_spawn.Y:F1}, {_spawn.Z:F1}");
        }

        private const string TeleportUsage =
            "Использование: /tp <цель>  |  /tp <ник> <цель>  |  /tp <x> <y> <z>  |  /tp <ник> <x> <y> <z>";

        private void HandleTeleport(NetworkConnectionToClient conn, string text)
        {
            // a[0] — имя команды, дальше аргументы
            string[] a = text.Substring(1).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            int argc = a.Length - 1;

            NetworkConnectionToClient who = conn;   // кого телепортируем
            string destNick = null;                   // к какому игроку
            int coordIndex = -1;                      // с какого аргумента идут координаты

            switch (argc)
            {
                case 1:                       // /tp <цель>
                    destNick = a[1];
                    break;
                case 2:                       // /tp <ник> <цель>
                    who = FindConnByNick(a[1]);
                    if (who == null) { Reply(conn, $"Игрок {a[1]} не найден", 2); return; }
                    destNick = a[2];
                    break;
                case 3:                       // /tp <x> <y> <z>
                    coordIndex = 1;
                    break;
                case 4:                       // /tp <ник> <x> <y> <z>
                    who = FindConnByNick(a[1]);
                    if (who == null) { Reply(conn, $"Игрок {a[1]} не найден", 2); return; }
                    coordIndex = 2;
                    break;
                default:
                    Reply(conn, TeleportUsage, 2);
                    return;
            }

            NetworkPlayer whoPlayer = GetNetPlayer(who);
            if (whoPlayer == null) { Reply(conn, "Игрок ещё не загрузился", 2); return; }

            Vector3 dest;
            string whereText;

            if (destNick != null)
            {
                NetworkConnectionToClient destConn = FindConnByNick(destNick);
                NetworkPlayer destPlayer = GetNetPlayer(destConn);
                if (destPlayer == null) { Reply(conn, $"Игрок {destNick} не найден", 2); return; }
                if (destConn == who) { Reply(conn, "Нельзя телепортировать игрока к самому себе", 2); return; }

                dest = destPlayer.transform.position;
                whereText = $"к {GetNick(destConn)}";
            }
            else
            {
                Vector3 cur = whoPlayer.transform.position;
                if (!TryParseCoord(a[coordIndex], cur.x, true, out float x) ||
                    !TryParseCoord(a[coordIndex + 1], cur.y, false, out float y) ||
                    !TryParseCoord(a[coordIndex + 2], cur.z, true, out float z))
                {
                    Reply(conn, TeleportUsage, 2);
                    return;
                }

                WorldManager wm = WorldManager.Instance;
                if (wm == null || !wm.IsInsideWorld(x, z)) { Reply(conn, "Координаты за пределами мира", 2); return; }
                if (y < 0f || y > 255f) { Reply(conn, "Y должен быть от 0 до 255", 2); return; }

                dest = new Vector3(x, y, z);
                whereText = $"на {x:F1}, {y:F1}, {z:F1}";
            }

            whoPlayer.ServerTeleport(dest);

            Reply(conn, $"{GetNick(who)} телепортирован {whereText}");
            if (who != conn) Reply(who, $"Вас телепортировали {whereText}", 1);
        }

        // "10" / "10.5" / "~" / "~5" / "~-3". Целые абсолютные X и Z центрируются в блоке (+0.5), как в современной игре.
        private static bool TryParseCoord(string s, float current, bool centerInteger, out float value)
        {
            value = current;
            bool relative = s.StartsWith("~");
            string num = (relative ? s.Substring(1) : s).Replace(',', '.');

            float parsed = 0f;
            if (num.Length > 0 &&
                !float.TryParse(num, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out parsed))
                return false;

            if (relative) value = current + parsed;
            else
            {
                value = parsed;
                if (centerInteger && !num.Contains(".")) value += 0.5f;
            }

            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void HandleCommand(NetworkConnectionToClient conn, string text)
        {
            string[] p = text.Substring(1).Split(new[] { ' ' }, 3, System.StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) return;

            string cmd = p[0].ToLowerInvariant();
            string arg1 = p.Length > 1 ? p[1] : "";
            string rest = p.Length > 2 ? p[2] : "";

            // /say доступна всем игрокам
            if (cmd == "say")
            {
                HandleSay(conn, arg1, rest);
                return;
            }

            // /help доступна всем игрокам
            if (cmd == "help")
            {
                Reply(conn, "Для всех игроков: /say <ник> <текст>");
                if (conn == NetworkServer.localConnection)
                {
                    Reply(conn, "Только для хоста: /kick <ник> [причина]    /ban <ник> [причина]    /banip <ник или ip>");
                    Reply(conn, "Только для хоста: /unban <ник>    /unbanip <ip>    /banlist");
                    Reply(conn, "Только для хоста: /setspawn    /tp <цель>    /tp <ник> <цель>");
                    Reply(conn, "Только для хоста: /tp <x> <y> <z>    /tp <ник> <x> <y> <z>    (~ = относительные координаты)");
                }
                return;
            }

            // остальные команды только для хоста
            if (conn != NetworkServer.localConnection)
            {
                Reply(conn, "Эта команда доступна только хосту", 2);
                return;
            }

            switch (cmd)
            {
                case "kick":
                    {
                        if (arg1 == "") { Reply(conn, "Использование: /kick <ник> [причина]", 2); return; }
                        var t = FindConnByNick(arg1);
                        if (t == null) { Reply(conn, $"Игрок {arg1} не найден", 2); return; }
                        if (t == NetworkServer.localConnection) { Reply(conn, "Нельзя кикнуть самого себя", 2); return; }

                        KickConnection(t, rest == "" ? "Вы были кикнуты с сервера" : "Вы кикнуты: " + rest);
                        Reply(conn, $"Игрок {arg1} кикнут");
                        return;
                    }

                case "ban":
                    {
                        if (arg1 == "") { Reply(conn, "Использование: /ban <ник> [причина]", 2); return; }
                        var t = FindConnByNick(arg1);
                        if (t == NetworkServer.localConnection) { Reply(conn, "Нельзя забанить самого себя", 2); return; }

                        string key = arg1.ToLowerInvariant();
                        if (!_bans.Names.Contains(key)) _bans.Names.Add(key);
                        SaveBans();

                        if (t != null)
                            KickConnection(t, rest == "" ? "Вы забанены на этом сервере" : "Вы забанены: " + rest);

                        Reply(conn, $"Игрок {arg1} забанен");
                        return;
                    }

                case "banip":
                    {
                        if (arg1 == "") { Reply(conn, "Использование: /banip <ник или ip>", 2); return; }

                        string ip;
                        var t = FindConnByNick(arg1);
                        if (t != null)
                        {
                            if (t == NetworkServer.localConnection) { Reply(conn, "Нельзя забанить самого себя", 2); return; }
                            ip = NormalizeIp(t.address);
                        }
                        else if ((arg1.Contains(".") || arg1.Contains(":")) && System.Net.IPAddress.TryParse(arg1, out _))
                        {
                            ip = NormalizeIp(arg1);
                        }
                        else
                        {
                            Reply(conn, $"Игрок или IP «{arg1}» не найден", 2);
                            return;
                        }

                        if (!_bans.Ips.Contains(ip)) _bans.Ips.Add(ip);
                        SaveBans();

                        var toKick = new List<NetworkConnectionToClient>();
                        foreach (var c in NetworkServer.connections.Values)
                            if (c != NetworkServer.localConnection && NormalizeIp(c.address) == ip) toKick.Add(c);
                        foreach (var c in toKick) KickConnection(c, "Ваш IP-адрес забанен на этом сервере");

                        Reply(conn, $"IP {ip} забанен");
                        return;
                    }

                case "unban":
                    {
                        if (arg1 == "") { Reply(conn, "Использование: /unban <ник>", 2); return; }
                        if (_bans.Names.Remove(arg1.ToLowerInvariant())) { SaveBans(); Reply(conn, $"Игрок {arg1} разбанен"); }
                        else Reply(conn, $"Игрок {arg1} не в списке банов", 2);
                        return;
                    }

                case "unbanip":
                    {
                        if (arg1 == "") { Reply(conn, "Использование: /unbanip <ip>", 2); return; }
                        if (_bans.Ips.Remove(NormalizeIp(arg1))) { SaveBans(); Reply(conn, $"IP {arg1} разбанен"); }
                        else Reply(conn, $"IP {arg1} не в списке банов", 2);
                        return;
                    }

                case "banlist":
                    {
                        Reply(conn, "Ники: " + (_bans.Names.Count == 0 ? "нет" : string.Join(", ", _bans.Names)));
                        Reply(conn, "IP: " + (_bans.Ips.Count == 0 ? "нет" : string.Join(", ", _bans.Ips)));
                        return;
                    }

                case "setspawn":
                    HandleSetSpawn(conn);
                    return;

                case "teleport":
                case "tp":
                    HandleTeleport(conn, text);
                    return;

                default:
                    Reply(conn, $"Неизвестная команда: /{cmd}. Список: /help", 2);
                    return;
            }
        }

        // --- ЛОГИКА КЛИЕНТА ---

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Регистрируем сетевые хэндлеры ДО момента подключения к серверу,
            // чтобы сообщения не терялись при получении
            NetworkClient.RegisterHandler<RejectConnectionMessage>(OnClientRejected);
            NetworkClient.RegisterHandler<WorldSeedMessage>(OnClientReceiveSeed);
            NetworkClient.RegisterHandler<BlockChangeMessage>(OnClientBlockChange);
            NetworkClient.RegisterHandler<ChunkDataMessage>(OnClientChunkData);
            NetworkClient.RegisterHandler<ChatMessage>(OnClientChat);
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            StartCoroutine(JoinSequence());
        }

        private IEnumerator JoinSequence()
        {
            // чистый клиент сидит в главном меню: грузим игровую сцену с экраном загрузки
            if (!NetworkServer.active && SceneManager.GetActiveScene().name != gameSceneName)
                RuntimeLoadingScreen.LoadGameScene(gameSceneName);

            while (WorldManager.Instance == null || SceneManager.GetActiveScene().name != gameSceneName)
            {
                if (!NetworkClient.isConnected) yield break;
                yield return null;
            }
            yield return null; // даём отработать Start у WorldManager

            if (!NetworkServer.active) WorldManager.Instance.ClearWorld();

            string nick = LauncherArgs.HasNickname ? LauncherArgs.Nickname : PlayerPrefs.GetString("PlayerNickname", "Player");
            NetworkClient.Send(new NetworkPlayerMessage { Nickname = nick });
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();

            bool expected = ExpectedDisconnect;
            ExpectedDisconnect = false;
            if (NetworkServer.active) return; // хост: выходом занимается меню паузы

            if (!expected && string.IsNullOrEmpty(LastError)) LastError = "Соединение с сервером потеряно";

            RuntimeLoadingScreen.Hide();
            if (WorldManager.Instance != null) WorldManager.Instance.ClearWorld();

            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (!expected && SceneManager.GetActiveScene().name != mainMenuSceneName)
                SceneManager.LoadScene(mainMenuSceneName);
        }

        private void OnClientReceiveSeed(WorldSeedMessage msg)
        {
            if (NetworkServer.active) return; // Хост уже сгенерировал свой мир

            if (_worldManager == null) _worldManager = WorldManager.Instance != null ? WorldManager.Instance : Object.FindFirstObjectByType<WorldManager>();
            if (_worldManager != null)
            {
                _worldManager.GenerateNewWorld(msg.Seed);
            }
        }

        private void OnClientRejected(RejectConnectionMessage msg)
        {
            LastError = msg.Reason;
            Debug.LogError($"[NetworkManager] Подключение отклонено сервером: {msg.Reason}");
        }

        private void OnClientBlockChange(BlockChangeMessage msg)
        {
            if (NetworkServer.active) return; // хост уже применил изменение сам
            if (WorldManager.Instance != null)
                WorldManager.Instance.ApplyBlockChange(msg.Pos.x, msg.Pos.y, msg.Pos.z, msg.Type, true);
        }

        private void OnClientChunkData(ChunkDataMessage msg)
        {
            if (NetworkServer.active) return;
            if (WorldManager.Instance != null)
                WorldManager.Instance.ApplyChunkFromServer(msg.X, msg.Z, msg.Rle);
        }

        private void OnClientChat(ChatMessage msg)
        {
            ChatUI.AddMessage(msg.Text, msg.Kind); // хост тоже получает своё сообщение через SendToAll
        }

        public override void OnStopHost() { base.OnStopHost(); ResetWorldAndCursor(); }
        public override void OnStopClient() { base.OnStopClient(); ResetWorldAndCursor(); }
        public override void OnStopServer()
        {
            base.OnStopServer();
            _connectedNicknames.Clear();
            ResetWorldAndCursor();
        }

        private void ResetWorldAndCursor()
        {
            if (WorldManager.Instance != null) WorldManager.Instance.OnHostOrClientStopped();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}