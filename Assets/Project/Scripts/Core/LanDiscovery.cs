using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MinecraftClone.UI
{
    public class LanDiscovery : MonoBehaviour
    {
        public struct DiscoveredServer
        {
            public string ServerName;
            public string IpAddress;
            public int Port;
            public float LastSeenTime;
        }

        [Header("Настройки LAN Discovery")]
        [SerializeField] private int discoveryPort = 47777;
        [SerializeField] private float serverTimeout = 4f;

        private UdpClient _udpClient;
        private CancellationTokenSource _cts;

        private bool _isHosting = false;
        private bool _isListening = false;

        private readonly Dictionary<string, DiscoveredServer> _discoveredServers = new Dictionary<string, DiscoveredServer>();
        private readonly object _lock = new object();

        public IReadOnlyDictionary<string, DiscoveredServer> DiscoveredServers
        {
            get
            {
                lock (_lock)
                {
                    return new Dictionary<string, DiscoveredServer>(_discoveredServers);
                }
            }
        }

        private void Update()
        {
            if (!_isListening) return;

            float currentTime = Time.time;
            List<string> keysToRemove = new List<string>();

            lock (_lock)
            {
                foreach (var kvp in _discoveredServers)
                {
                    if (currentTime - kvp.Value.LastSeenTime > serverTimeout)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _discoveredServers.Remove(key);
                }
            }
        }

        public void StartBroadcasting(string serverName, int gamePort)
        {
            StopDiscovery();

            _isHosting = true;
            _cts = new CancellationTokenSource();

            try
            {
                _udpClient = new UdpClient();
                _udpClient.EnableBroadcast = true;
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, discoveryPort);
                byte[] bytes = Encoding.UTF8.GetBytes($"{serverName}|{gamePort}");

                CancellationToken token = _cts.Token;

                Task.Run(async () =>
                {
                    while (_isHosting && !token.IsCancellationRequested)
                    {
                        try
                        {
                            if (_udpClient != null)
                            {
                                await _udpClient.SendAsync(bytes, bytes.Length, endPoint);
                            }
                            await Task.Delay(1500, token);
                        }
                        catch (OperationCanceledException) { break; }
                        catch (ObjectDisposedException) { break; }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[LanDiscovery] Ошибка отправки бродкаста: {e.Message}");
                            break;
                        }
                    }
                }, token);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LanDiscovery] Ошибка запуска хоста: {e.Message}");
            }
        }

        public void StartListening()
        {
            if (_isListening) return;

            StopDiscovery();

            _isListening = true;
            _cts = new CancellationTokenSource();

            try
            {
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));

                CancellationToken token = _cts.Token;

                Task.Run(async () =>
                {
                    while (_isListening && !token.IsCancellationRequested)
                    {
                        try
                        {
                            UdpReceiveResult result = await _udpClient.ReceiveAsync();
                            string rawMessage = Encoding.UTF8.GetString(result.Buffer);
                            string[] parts = rawMessage.Split('|');

                            if (parts.Length == 2 && int.TryParse(parts[1], out int gamePort))
                            {
                                string serverName = parts[0];
                                string ip = result.RemoteEndPoint.Address.ToString();
                                string serverKey = $"{ip}:{gamePort}";

                                lock (_lock)
                                {
                                    _discoveredServers[serverKey] = new DiscoveredServer
                                    {
                                        ServerName = serverName,
                                        IpAddress = ip,
                                        Port = gamePort,
                                        LastSeenTime = Time.realtimeSinceStartup
                                    };
                                }
                            }
                        }
                        catch (ObjectDisposedException) { break; }
                        catch (OperationCanceledException) { break; }
                        catch (Exception e)
                        {
                            if (_isListening)
                            {
                                Debug.LogWarning($"[LanDiscovery] Ошибка приема пакета: {e.Message}");
                            }
                        }
                    }
                }, token);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LanDiscovery] Ошибка запуска прослушивания: {e.Message}");
            }
        }

        public void StopDiscovery()
        {
            _isHosting = false;
            _isListening = false;

            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            if (_udpClient != null)
            {
                try
                {
                    _udpClient.Close();
                    _udpClient.Dispose();
                }
                catch { }
                _udpClient = null;
            }

            lock (_lock)
            {
                _discoveredServers.Clear();
            }
        }

        private void OnDisable() => StopDiscovery();
        private void OnApplicationQuit() => StopDiscovery();
    }
}   