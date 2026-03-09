using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using System.Collections.Generic;

public class NamedSessionManager : MonoBehaviour
{
    //References
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private UnityTransport unityTransport;

    //Network settings
    [Header("Settings")]
    [SerializeField] private int maxPlayers = 2;
    [SerializeField] private float connectTimeoutSeconds = 30f;

    private string joinedLobbyId;
    private const string JoinCodeKey = "joinCode";

    // Host heartbeat
    private CancellationTokenSource heartbeatCts;

    private void Reset()
    {
        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
        if (!unityTransport && networkManager) unityTransport = networkManager.GetComponent<UnityTransport>();
    }

    private void Start()
    {
        ResolveRefs();

        // debug events
        networkManager.OnClientConnectedCallback += id => Debug.Log($"[NGO] CONNECTED: {id}");
        networkManager.OnClientDisconnectCallback += id => Debug.Log($"[NGO] DISCONNECTED: {id}");
        networkManager.OnTransportFailure += () => Debug.Log("[NGO] TRANSPORT FAILURE");

        // Helps prevent disconect
        Application.runInBackground = true;
    }

    // Resolves references and checks for common setup errors
    private void ResolveRefs()
    {
        if (!networkManager) networkManager = NetworkManager.Singleton;

        if (!unityTransport)
            unityTransport = networkManager.NetworkConfig.NetworkTransport as UnityTransport;

        if (!networkManager) throw new Exception("NetworkManager not found.");
        if (!unityTransport) throw new Exception("NetworkTransport is not UnityTransport (check NetworkManager).");
    }

    
    private bool servicesReady = false;
    // Ensures Unity Services are initialized and player is authenticated
    private async Task EnsureServicesReady()
    {
        if (servicesReady) return;

        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        // Choose ONE valid profile per process run
        string profile = "p_" + Guid.NewGuid().ToString("N").Substring(0, 24);
        AuthenticationService.Instance.SwitchProfile(profile);

        await AuthenticationService.Instance.SignInAnonymouslyAsync();

        servicesReady = true;
    }

    // Host creates lobby and relay allocation, then starts hosting
    public async Task<(bool ok, string error)> HostCreateSession(string sessionName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionName))
                return (false, "Session name is empty.");

            ResolveRefs();
            await EnsureServicesReady();

            Allocation alloc = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
            Debug.Log($"[HOST] Relay joinCode={joinCode}");
            var createOptions = new CreateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { JoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, joinCode) }
                }
            };

            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(sessionName, maxPlayers, createOptions);
            joinedLobbyId = lobby.Id;

            bool useSecure = true;
            var endpoint = alloc.ServerEndpoints.First(e => e.ConnectionType == (useSecure ? "dtls" : "udp"));

            Debug.Log($"[HOST] Relay endpoint {endpoint.ConnectionType} {endpoint.Host}:{endpoint.Port}");

            unityTransport.SetRelayServerData(
                endpoint.Host,
                (ushort)endpoint.Port,
                alloc.AllocationIdBytes,
                alloc.Key,
                alloc.ConnectionData,
                alloc.ConnectionData,
                useSecure
            );

            if (!networkManager.StartHost())
                return (false, "StartHost() failed.");


            StartLobbyHeartbeat();

            return (true, $"Hosting '{sessionName}'.");
        }
        catch (LobbyServiceException e)
        {
            return (false, $"Lobby error: {e.Reason} ({e.Message})");
        }
        catch (RelayServiceException e)
        {
            return (false, $"Relay error: {e.Reason} ({e.Message})");
        }
        catch (Exception e)
        {
            return (false, $"Host error: {e.Message}");
        }
    }

    // Server list 
    public struct SessionRow
    {
        public string LobbyId;
        public string Name;
        public int Players;
        public int MaxPlayers;
    }

    //Lists current sessions
    public async Task<(bool ok, string error, List<SessionRow> sessions)> ListSessionsAsync()
    {
        try
        {
            await EnsureServicesReady();

            var result = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions { Count = 25 });

            var list = new List<SessionRow>();

            foreach (var l in result.Results)
            {
                // Filter OUT stale/invalid sessions
                if (l.Data == null || !l.Data.TryGetValue(JoinCodeKey, out var codeObj)) continue;
                if (string.IsNullOrWhiteSpace(codeObj.Value)) continue;

                int players = l.Players?.Count ?? 0;

                // Hide full sessions
                if (players >= l.MaxPlayers) continue;

                list.Add(new SessionRow
                {
                    LobbyId = l.Id,
                    Name = l.Name,
                    Players = players,
                    MaxPlayers = l.MaxPlayers
                });
            }

            return (true, null, list);
        }
        catch (Exception e)
        {
            return (false, e.Message, new List<SessionRow>());
        }
    }

    // Client joins lobby and relay allocation, then starts client
    public async Task<(bool ok, string error)> ClientJoinSessionByLobbyId(string lobbyId)
    {
        bool everConnected = false;

        try
        {
            ResolveRefs();
            await EnsureServicesReady();

            Lobby joined = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);
            joinedLobbyId = joined.Id;

            if (!joined.Data.TryGetValue(JoinCodeKey, out var joinCodeObj))
                return (false, "Session has no relay join code stored.");

            string joinCode = joinCodeObj.Value;
            Debug.Log($"[CLIENT] Lobby joinCode={joinCode}");
            JoinAllocation joinAlloc = await RelayService.Instance.JoinAllocationAsync(joinCode);

            Debug.Log($"Relay join OK. ip={joinAlloc.RelayServer.IpV4}:{joinAlloc.RelayServer.Port}");

            bool useSecure = true; 
            var endpoint = joinAlloc.ServerEndpoints.First(e => e.ConnectionType == (useSecure ? "dtls" : "udp"));

            Debug.Log($"[CLIENT] Relay endpoint {endpoint.ConnectionType} {endpoint.Host}:{endpoint.Port}");

            unityTransport.SetRelayServerData(
                endpoint.Host,
                (ushort)endpoint.Port,
                joinAlloc.AllocationIdBytes,
                joinAlloc.Key,
                joinAlloc.ConnectionData,
                joinAlloc.HostConnectionData,
                useSecure
            );
            Debug.Log("Relay data set on UnityTransport.");

            if (!networkManager.StartClient())
                return (false, "StartClient() failed.");
            Debug.Log($"StartClient called. IsClient={networkManager.IsClient}, LocalClientId={networkManager.LocalClientId}");

            everConnected = await WaitForLocalClientConnected(connectTimeoutSeconds);

            if (!everConnected)
            {
                networkManager.Shutdown();
                return (false, $"Could not connect within {connectTimeoutSeconds:0}s. (Is host running?)");
            }

            return (true, $"Joined lobby {lobbyId}");
        }
        catch (RelayServiceException e)
        {
            if (e.Message != null && e.Message.Contains("join code not found", StringComparison.OrdinalIgnoreCase))
                return (false, "That session is stale (host likely closed). Refresh the list and try again.");

            return (false, $"Relay error: {e.Reason} ({e.Message})");
        }
        catch (LobbyServiceException e)
        {
            return (false, $"Lobby error: {e.Reason} ({e.Message})");
        }
        catch (Exception e)
        {
            return (false, $"Join error: {e.Message}");
        }
    }

    // Waits for the local client to connect with a timeout
    private async Task<bool> WaitForLocalClientConnected(float timeoutSeconds)
    {
        var tcs = new TaskCompletionSource<bool>();

        void OnConnected(ulong clientId)
        {
            // LocalClientId is 0 until startclient, but by the time connected fires it's valid
            if (clientId == networkManager.LocalClientId)
                tcs.TrySetResult(true);
        }

        void OnDisconnected(ulong clientId)
        {
            // If disconnected before ever connecting, fail early instead of waiting full timeout
            if (clientId == networkManager.LocalClientId)
                tcs.TrySetResult(false);
        }

        void OnTransportFailure()
        {
            tcs.TrySetResult(false);
        }

        networkManager.OnClientConnectedCallback += OnConnected;
        networkManager.OnClientDisconnectCallback += OnDisconnected;
        networkManager.OnTransportFailure += OnTransportFailure;

        try
        {
            var delayTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            var completed = await Task.WhenAny(tcs.Task, delayTask);

            if (completed == tcs.Task)
                return tcs.Task.Result;

            return false; 
            // timed out
        }
        finally
        {
            networkManager.OnClientConnectedCallback -= OnConnected;
            networkManager.OnClientDisconnectCallback -= OnDisconnected;
            networkManager.OnTransportFailure -= OnTransportFailure;
        }
    }

    // Heartbeat loop to keep lobby alive while hosting. Stops when leaving lobby or on destroy.
    private void StartLobbyHeartbeat()
    {
        StopLobbyHeartbeat();

        if (string.IsNullOrEmpty(joinedLobbyId))
            return;

        heartbeatCts = new CancellationTokenSource();
        _ = HeartbeatLoopAsync(joinedLobbyId, heartbeatCts.Token);
    }

    // Stops the lobby heartbeat loop
    private void StopLobbyHeartbeat()
    {
        if (heartbeatCts != null)
        {
            heartbeatCts.Cancel();
            heartbeatCts.Dispose();
            heartbeatCts = null;
        }
    }

    // Sends heartbeat pings to the lobby service at regular intervals to keep the lobby alive
    private async Task HeartbeatLoopAsync(string lobbyId, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Lobby heartbeat failed: {e.Message}");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(15), token); }
            catch { }
        }
    }

    // Leaves the lobby and stops hosting or disconnects from the relay and stops client
    private void OnDestroy()
    {
        StopLobbyHeartbeat();
    }
}