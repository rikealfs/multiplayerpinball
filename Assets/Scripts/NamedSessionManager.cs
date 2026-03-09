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
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private UnityTransport unityTransport;

    [Header("Settings")]
    [SerializeField] private int maxPlayers = 2;
    [SerializeField] private float connectTimeoutSeconds = 12f;

    private string joinedLobbyId;
    private const string JoinCodeKey = "joinCode";

    // Host heartbeat
    private CancellationTokenSource heartbeatCts;

    private void Reset()
    {
        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
        if (!unityTransport && networkManager) unityTransport = networkManager.GetComponent<UnityTransport>();
    }

    private void ResolveRefs()
    {
        if (!networkManager) networkManager = NetworkManager.Singleton;
        if (!unityTransport) unityTransport = networkManager.GetComponent<UnityTransport>();
        if (!networkManager || !unityTransport)
            throw new Exception("NetworkManager/UnityTransport not assigned.");
    }

    private async Task EnsureServicesReady()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        // Only switch profiles if we are signed out
        if (AuthenticationService.Instance.IsSignedIn)
            AuthenticationService.Instance.SignOut();

        // Valid profile: only [A-Za-z0-9_-], <= 30 chars
        string profile = "p_" + Guid.NewGuid().ToString("N").Substring(0, 24);
        AuthenticationService.Instance.SwitchProfile(profile);

        await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    // ---------- HOST ----------
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

            var createOptions = new CreateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { JoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, joinCode) }
                }
            };

            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(sessionName, maxPlayers, createOptions);
            joinedLobbyId = lobby.Id;

            unityTransport.SetRelayServerData(
                alloc.RelayServer.IpV4,
                (ushort)alloc.RelayServer.Port,
                alloc.AllocationIdBytes,
                alloc.Key,
                alloc.ConnectionData,
                alloc.ConnectionData,
                true
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

    // ---------- SERVER LIST ----------
    public struct SessionRow
    {
        public string LobbyId;
        public string Name;
        public int Players;
        public int MaxPlayers;
    }

    public async Task<(bool ok, string error, List<SessionRow> sessions)> ListSessionsAsync()
    {
        try
        {
            await EnsureServicesReady();

            var result = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions { Count = 25 });

            var list = new List<SessionRow>();

            foreach (var l in result.Results)
            {
                // Filter OUT stale/invalid sessions (must contain joinCode)
                if (l.Data == null || !l.Data.TryGetValue(JoinCodeKey, out var codeObj)) continue;
                if (string.IsNullOrWhiteSpace(codeObj.Value)) continue;

                int players = l.Players?.Count ?? 0;

                // Optional: hide full sessions
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

    // ---------- CLIENT JOIN ----------
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

            JoinAllocation joinAlloc = await RelayService.Instance.JoinAllocationAsync(joinCode);

            unityTransport.SetRelayServerData(
                joinAlloc.RelayServer.IpV4,
                (ushort)joinAlloc.RelayServer.Port,
                joinAlloc.AllocationIdBytes,
                joinAlloc.Key,
                joinAlloc.ConnectionData,
                joinAlloc.HostConnectionData,
                true
            );

            if (!networkManager.StartClient())
                return (false, "StartClient() failed.");

            // Wait only for THIS local client to connect
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
            // This is the error you hit when lobby is stale / relay alloc expired
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

    private async Task<bool> WaitForLocalClientConnected(float timeoutSeconds)
    {
        var tcs = new TaskCompletionSource<bool>();

        void OnConnected(ulong clientId)
        {
            if (clientId == networkManager.LocalClientId)
                tcs.TrySetResult(true);
        }

        void OnTransportFailure()
        {
            tcs.TrySetResult(false);
        }

        networkManager.OnClientConnectedCallback += OnConnected;
        networkManager.OnTransportFailure += OnTransportFailure;

        try
        {
            var delayTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            var completed = await Task.WhenAny(tcs.Task, delayTask);
            return completed == tcs.Task && tcs.Task.Result;
        }
        finally
        {
            networkManager.OnClientConnectedCallback -= OnConnected;
            networkManager.OnTransportFailure -= OnTransportFailure;
        }
    }

    // ---------- HEARTBEAT ----------
    private void StartLobbyHeartbeat()
    {
        StopLobbyHeartbeat();

        if (string.IsNullOrEmpty(joinedLobbyId))
            return;

        heartbeatCts = new CancellationTokenSource();
        _ = HeartbeatLoopAsync(joinedLobbyId, heartbeatCts.Token);
    }

    private void StopLobbyHeartbeat()
    {
        if (heartbeatCts != null)
        {
            heartbeatCts.Cancel();
            heartbeatCts.Dispose();
            heartbeatCts = null;
        }
    }

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

    private void OnDestroy()
    {
        StopLobbyHeartbeat();
    }
}