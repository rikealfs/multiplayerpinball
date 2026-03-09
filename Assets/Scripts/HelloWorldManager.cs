using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;

namespace HelloWorld
{
    public class HelloWorldManager : MonoBehaviour
    {
        VisualElement rootVisualElement;

        TextField sessionNameField;
        Button hostButton;
        Button refreshButton;

        ListView sessionsList;
        List<NamedSessionManager.SessionRow> sessions = new();

        Label statusLabel;

        [SerializeField] private NamedSessionManager namedSessionManager;

        [SerializeField] private NetworkObject pinballA;
        [SerializeField] private NetworkObject pinballB;

        private bool assignedB = false;

        void OnEnable()
        {
            var uiDocument = GetComponent<UIDocument>();
            rootVisualElement = uiDocument.rootVisualElement;
            rootVisualElement.Clear();

            if (!namedSessionManager)
                namedSessionManager = FindFirstObjectByType<NamedSessionManager>();

            sessionNameField = new TextField("Session Name") { value = "MySession" };
            sessionNameField.style.width = 240;

            hostButton = CreateButton("HostButton", "Create Session (Host)");
            refreshButton = CreateButton("RefreshButton", "Refresh Sessions");
            statusLabel = CreateLabel("StatusLabel", "Not Connected");

            sessionsList = BuildSessionsList();

            rootVisualElement.Add(sessionNameField);
            rootVisualElement.Add(hostButton);
            rootVisualElement.Add(refreshButton);
            rootVisualElement.Add(new Label("Available Sessions:"));
            rootVisualElement.Add(sessionsList);
            rootVisualElement.Add(statusLabel);

            hostButton.clicked += OnHostButtonClicked;
            refreshButton.clicked += OnRefreshClicked;

            HookNetcodeStatusEvents();

            _ = RefreshSessionsAsync();
        }

        void Update() => UpdateUI();

        void OnDisable()
        {
            hostButton.clicked -= OnHostButtonClicked;
            refreshButton.clicked -= OnRefreshClicked;

            UnhookNetcodeStatusEvents();
        }

        void OnHostButtonClicked() => _ = HostFlowAsync();
        void OnRefreshClicked() => _ = RefreshSessionsAsync();

        async System.Threading.Tasks.Task HostFlowAsync()
        {
            try
            {
                assignedB = false;
                string sessionName = sessionNameField.value?.Trim();

                HookOwnershipEvents();

                var (ok, error) = await namedSessionManager.HostCreateSession(sessionName);
                SetStatusText(ok ? $"Hosting: {sessionName}" : $"Host failed: {error}");

                if (ok) await RefreshSessionsAsync();
            }
            catch (Exception e)
            {
                SetStatusText($"Host failed: {e.Message}");
            }
        }

        async System.Threading.Tasks.Task RefreshSessionsAsync()
        {
            if (!namedSessionManager)
            {
                SetStatusText("NamedSessionManager missing.");
                return;
            }

            var (ok, error, list) = await namedSessionManager.ListSessionsAsync();
            if (!ok)
            {
                SetStatusText($"Refresh failed: {error}");
                return;
            }

            sessions.Clear();
            sessions.AddRange(list);
            sessionsList.Rebuild();

            SetStatusText($"Found {sessions.Count} session(s).");
        }

        async System.Threading.Tasks.Task JoinSelectedLobbyAsync(string lobbyId, string lobbyName)
        {
            try
            {
                var (ok, error) = await namedSessionManager.ClientJoinSessionByLobbyId(lobbyId);
                SetStatusText(ok ? $"Joined: {lobbyName}" : $"Join failed: {error}");
            }
            catch (Exception e)
            {
                SetStatusText($"Join failed: {e.Message}");
            }
        }

        // --- Server list UI ---
        ListView BuildSessionsList()
        {
            var listView = new ListView
            {
                name = "SessionsList",
                itemsSource = sessions
            };
            listView.style.width = 420;
            listView.style.height = 220;

            listView.makeItem = () =>
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.SpaceBetween;
                row.style.alignItems = Align.Center;
                row.style.paddingLeft = 6;
                row.style.paddingRight = 6;
                row.style.height = 28;

                var label = new Label { name = "RowLabel" };
                label.style.unityTextAlign = TextAnchor.MiddleLeft;

                var joinBtn = new Button { name = "JoinRowButton", text = "Join" };
                joinBtn.style.width = 80;

                row.Add(label);
                row.Add(joinBtn);
                return row;
            };

            listView.bindItem = (element, index) =>
            {
                var row = sessions[index];

                var label = element.Q<Label>("RowLabel");
                var joinBtn = element.Q<Button>("JoinRowButton");

                label.text = $"{row.Name}   ({row.Players}/{row.MaxPlayers})";

                // Clear previous click handler
                if (joinBtn.userData is Action oldHandler)
                    joinBtn.clicked -= oldHandler;

                Action handler = () => { _ = JoinSelectedLobbyAsync(row.LobbyId, row.Name); };
                joinBtn.userData = handler;
                joinBtn.clicked += handler;
            };

            return listView;
        }

        // --- Menu visibility ---
        void SetMenuVisible(bool visible)
        {
            sessionNameField.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            hostButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            refreshButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (sessionsList != null)
                sessionsList.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void UpdateUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                SetMenuVisible(false);
                SetStatusText("NetworkManager not found");
                return;
            }

            bool connected = nm.IsClient || nm.IsServer;
            SetMenuVisible(!connected);

            if (!connected)
                return;

            var mode = nm.IsHost ? "Host" : nm.IsServer ? "Server" : "Client";
            SetStatusText($"Transport: {nm.NetworkConfig.NetworkTransport.GetType().Name}\nMode: {mode}");
        }

        void SetStatusText(string text) => statusLabel.text = text;

        // --- Ownership assignment ---
        private void HookOwnershipEvents()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            nm.OnServerStarted -= OnServerStarted;
            nm.OnServerStarted += OnServerStarted;

            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientConnectedCallback += OnClientConnected;
        }

        private void OnServerStarted()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            if (pinballA != null && pinballA.IsSpawned)
                pinballA.ChangeOwnership(nm.LocalClientId);
        }

        private void OnClientConnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;

            if (clientId == nm.LocalClientId) return;

            if (!assignedB && pinballB != null && pinballB.IsSpawned)
            {
                pinballB.ChangeOwnership(clientId);
                assignedB = true;
                Debug.Log($"Assigned PinballB to client {clientId}");
            }
        }

        // --- Netcode status events (helps debug kicks/disconnects) ---
        private void HookNetcodeStatusEvents()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            nm.OnClientDisconnectCallback += OnClientDisconnect;
            nm.OnTransportFailure += OnTransportFailure;
        }

        private void UnhookNetcodeStatusEvents()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            nm.OnClientDisconnectCallback -= OnClientDisconnect;
            nm.OnTransportFailure -= OnTransportFailure;
        }

        private void OnClientDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            if (clientId == nm.LocalClientId)
                SetStatusText("Disconnected from host.");
        }

        private void OnTransportFailure()
        {
            SetStatusText("Transport failure (Relay/connection lost).");
        }

        // --- UI helpers ---
        private Button CreateButton(string name, string text)
        {
            var button = new Button();
            button.name = name;
            button.text = text;
            button.style.width = 240;
            button.style.backgroundColor = Color.white;
            button.style.color = Color.black;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            return button;
        }

        private Label CreateLabel(string name, string content)
        {
            var label = new Label();
            label.name = name;
            label.text = content;
            label.style.color = Color.black;
            label.style.fontSize = 18;
            return label;
        }
    }
}