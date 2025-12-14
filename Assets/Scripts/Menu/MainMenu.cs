using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using TMPro; // Thêm dòng này
public class MainMenu : MonoBehaviour
{
    [Header("UI References")]
    public Button m_HostButton;
    public Button m_JoinButton;
    public GameObject m_JoinPanel;
    public TMP_InputField m_IPInputField; // Thay thế InputField bằng TMP_InputField
    public Button m_ConfirmJoinButton;
    public Button m_CancelJoinButton;
    public Text m_StatusText; // Optional status text for connection feedback

    [Header("Network Settings")]
    public ushort m_Port = 7777;
    public float m_ConnectionTimeout = 10f; // Timeout in seconds for client connection

    private void Start()
    {
        // Setup button listeners
        if (m_HostButton != null)
            m_HostButton.onClick.AddListener(OnHostClicked);
        
        if (m_JoinButton != null)
            m_JoinButton.onClick.AddListener(OnJoinClicked);
        
        if (m_ConfirmJoinButton != null)
            m_ConfirmJoinButton.onClick.AddListener(OnConfirmJoinClicked);
        
        if (m_CancelJoinButton != null)
            m_CancelJoinButton.onClick.AddListener(OnCancelJoinClicked);

        // Hide join panel initially
        if (m_JoinPanel != null)
            m_JoinPanel.SetActive(false);

        // Subscribe to network events for connection feedback
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;
        }
    }

    private void OnHostClicked()
    {
        // Start as host
        if (NetworkManager.Singleton != null)
        {
            // Shutdown existing connection if any to avoid port conflicts
            if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient)
            {
                NetworkManager.Singleton.Shutdown();
                // Wait a moment for shutdown to complete
                StartCoroutine(StartHostAfterShutdown());
            }
            else
            {
                StartHost();
            }
        }
        else
        {
            Debug.LogError("NetworkManager not found!");
        }
    }

    private IEnumerator StartHostAfterShutdown()
    {
        yield return new WaitForSeconds(0.5f);
        StartHost();
    }

    private void StartHost()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager not found!");
            UpdateStatus("Error: NetworkManager not found!");
            return;
        }

        // CRITICAL: Configure Unity Transport to listen on all network interfaces
        // This allows clients from other machines on the network to connect
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("UnityTransport component not found on NetworkManager!");
            UpdateStatus("Error: UnityTransport not found!");
            return;
        }

        // Optimize Unity Transport settings to reduce packet send failures
        OptimizeTransportSettings(transport);

        // Set server to listen on all interfaces (0.0.0.0) to accept connections from network
        // This is required for LAN connections, not just localhost
        bool configured = false;
        try
        {
            // Method 1: Try SetConnectionData with 0.0.0.0 (all interfaces) - works in most cases
            try
            {
                transport.SetConnectionData("0.0.0.0", m_Port);
                Debug.Log($"[HOST] Set to listen on 0.0.0.0:{m_Port} (all network interfaces)");
                configured = true;
            }
            catch (System.Exception e1)
            {
                Debug.LogWarning($"SetConnectionData failed: {e1.Message}. Trying alternative methods...");
            }

            // Method 2: Try to set ServerListenAddress property (newer Unity Transport API)
            if (!configured)
            {
                var transportType = transport.GetType();
                var serverListenAddressProp = transportType.GetProperty("ServerListenAddress");
                if (serverListenAddressProp != null && serverListenAddressProp.CanWrite)
                {
                    serverListenAddressProp.SetValue(transport, "0.0.0.0");
                    Debug.Log($"[HOST] Set ServerListenAddress to 0.0.0.0:{m_Port} (all network interfaces)");
                    configured = true;
                }
            }

            // Method 3: Set ConnectionData properties directly (fallback)
            if (!configured)
            {
                var connectionData = transport.ConnectionData;
                connectionData.Address = "0.0.0.0";
                connectionData.Port = m_Port;
                Debug.Log($"[HOST] Set ConnectionData.Address to 0.0.0.0:{m_Port} (fallback method)");
                configured = true;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Could not configure server listen address: {e.Message}");
            UpdateStatus($"Warning: May only accept localhost connections. Check firewall.");
        }

        if (!configured)
        {
            Debug.LogWarning("Could not configure server to listen on all interfaces. Host may only accept localhost connections.");
        }
        
        // Start host
        bool success = NetworkManager.Singleton.StartHost();
        if (success)
        {
            Debug.Log($"[HOST] Started successfully. Listening on 0.0.0.0:{m_Port}");
            Debug.Log("[HOST] Clients can connect using your LAN IP address (e.g., 192.168.1.7:7777)");
            UpdateStatus("Host started! Waiting for players...");
            // Load waiting scene
            SceneManager.LoadScene("WaitingRoom");
        }
        else
        {
            Debug.LogError("[HOST] Failed to start host!");
            UpdateStatus("Error: Failed to start host! Check if port is available.");
        }
    }

    private void OnJoinClicked()
    {
        // Show IP input panel
        if (m_JoinPanel != null)
            m_JoinPanel.SetActive(true);
    }

    private void OnConfirmJoinClicked()
    {
        string ipAddress = m_IPInputField != null ? m_IPInputField.text.Trim() : "127.0.0.1";
        
        if (string.IsNullOrEmpty(ipAddress))
        {
            Debug.LogWarning("IP address is empty, using default: 127.0.0.1");
            ipAddress = "127.0.0.1";
        }

        // Connect to host
        if (NetworkManager.Singleton != null)
        {
            // Shutdown existing connection if any
            if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient)
            {
                NetworkManager.Singleton.Shutdown();
                StartCoroutine(StartClientAfterShutdown(ipAddress));
            }
            else
            {
                StartClientConnection(ipAddress);
            }
        }
        else
        {
            Debug.LogError("NetworkManager not found!");
        }
    }

    private IEnumerator StartClientAfterShutdown(string ipAddress)
    {
        yield return new WaitForSeconds(0.5f);
        StartClientConnection(ipAddress);
    }

    private void StartClientConnection(string ipAddress)
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[CLIENT] NetworkManager not found!");
            UpdateStatus("Error: NetworkManager not found!");
            return;
        }

        // Set the connection data (IP address)
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("[CLIENT] UnityTransport component not found on NetworkManager!");
            UpdateStatus("Error: UnityTransport not found!");
            return;
        }

        // Optimize Unity Transport settings to reduce packet send failures
        OptimizeTransportSettings(transport);

        // Set connection data - try different API approaches
        bool configured = false;
        try
        {
            // Method 1: Try SetConnectionData method (newer API)
            try
            {
                transport.SetConnectionData(ipAddress, m_Port);
                Debug.Log($"[CLIENT] Connecting to host at {ipAddress}:{m_Port}");
                configured = true;
            }
            catch (System.Exception e1)
            {
                Debug.LogWarning($"SetConnectionData failed: {e1.Message}. Trying fallback method.");
            }

            // Method 2: Fallback - set ConnectionData properties directly
            if (!configured)
            {
                var connectionData = transport.ConnectionData;
                connectionData.Address = ipAddress;
                connectionData.Port = m_Port;
                Debug.Log($"[CLIENT] Set connection data to {ipAddress}:{m_Port} (fallback method)");
                configured = true;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CLIENT] Failed to configure connection: {e.Message}");
            UpdateStatus($"Error: Failed to configure connection to {ipAddress}");
            return;
        }

        if (!configured)
        {
            Debug.LogError("[CLIENT] Could not configure transport!");
            UpdateStatus("Error: Could not configure network transport!");
            return;
        }

        // Start client
        bool success = NetworkManager.Singleton.StartClient();
        if (success)
        {
            Debug.Log($"[CLIENT] Started. Attempting to connect to {ipAddress}:{m_Port}");
            UpdateStatus($"Connecting to {ipAddress}...");
            // Wait for connection with timeout
            StartCoroutine(WaitForConnectionAndLoadScene(ipAddress));
        }
        else
        {
            Debug.LogError("[CLIENT] Failed to start client!");
            UpdateStatus("Error: Failed to start client!");
        }
    }

    private void OnCancelJoinClicked()
    {
        // Hide join panel
        if (m_JoinPanel != null)
            m_JoinPanel.SetActive(false);
    }

    private IEnumerator WaitForConnectionAndLoadScene(string ipAddress)
    {
        float elapsedTime = 0f;
        bool connected = false;

        // Wait for client to connect with timeout
        while (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsClient && elapsedTime < m_ConnectionTimeout)
        {
            // Check if we're actually connected
            if (NetworkManager.Singleton.IsClient && NetworkManager.Singleton.IsConnectedClient)
            {
                connected = true;
                break;
            }

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        if (!connected)
        {
            Debug.LogError($"[CLIENT] Connection timeout after {m_ConnectionTimeout} seconds. Could not connect to {ipAddress}:{m_Port}");
            UpdateStatus($"Connection failed! Could not connect to {ipAddress}");
            
            // Shutdown failed connection
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }
            
            yield break;
        }

        // Small delay to ensure connection is stable
        yield return new WaitForSeconds(0.5f);

        Debug.Log($"[CLIENT] Successfully connected to {ipAddress}:{m_Port}");
        UpdateStatus("Connected! Loading...");

        // Load waiting scene
        SceneManager.LoadScene("WaitingRoom");
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            Debug.Log($"[CLIENT] Successfully connected to server (Client ID: {clientId})");
            UpdateStatus("Connected!");
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            Debug.LogWarning($"[CLIENT] Disconnected from server (Client ID: {clientId})");
            UpdateStatus("Disconnected from server!");
        }
    }

    private void OnTransportFailure()
    {
        Debug.LogError("[NETWORK] Transport failure occurred!");
        UpdateStatus("Network error! Check connection and firewall.");
    }

    private void UpdateStatus(string message)
    {
        if (m_StatusText != null)
        {
            m_StatusText.text = message;
        }
        Debug.Log($"[STATUS] {message}");
    }

    /// <summary>
    /// Optimize Unity Transport settings to reduce packet send failures
    /// Increases buffer sizes and adjusts network parameters
    /// </summary>
    private void OptimizeTransportSettings(Unity.Netcode.Transports.UTP.UnityTransport transport)
    {
        if (transport == null) return;

        try
        {
            var transportType = transport.GetType();

            // Try to increase send/receive buffer sizes to reduce packet failures
            // These properties may not exist in all Unity Transport versions, so we use reflection

            // Maximum Send Queue Size (default is usually 64KB, increase to 256KB)
            var maxSendQueueSizeProp = transportType.GetProperty("MaxSendQueueSize");
            if (maxSendQueueSizeProp != null && maxSendQueueSizeProp.CanWrite)
            {
                maxSendQueueSizeProp.SetValue(transport, 262144); // 256 KB
                Debug.Log("[TRANSPORT] Set MaxSendQueueSize to 256KB");
            }

            // Maximum Receive Queue Size
            var maxReceiveQueueSizeProp = transportType.GetProperty("MaxReceiveQueueSize");
            if (maxReceiveQueueSizeProp != null && maxReceiveQueueSizeProp.CanWrite)
            {
                maxReceiveQueueSizeProp.SetValue(transport, 262144); // 256 KB
                Debug.Log("[TRANSPORT] Set MaxReceiveQueueSize to 256KB");
            }

            // Packet Buffer Size
            var packetBufferSizeProp = transportType.GetProperty("PacketBufferSize");
            if (packetBufferSizeProp != null && packetBufferSizeProp.CanWrite)
            {
                packetBufferSizeProp.SetValue(transport, 65536); // 64 KB
                Debug.Log("[TRANSPORT] Set PacketBufferSize to 64KB");
            }

            // Heartbeat Timeout (increase to reduce false disconnections)
            var heartbeatTimeoutProp = transportType.GetProperty("HeartbeatTimeoutMS");
            if (heartbeatTimeoutProp != null && heartbeatTimeoutProp.CanWrite)
            {
                heartbeatTimeoutProp.SetValue(transport, 5000); // 5 seconds
                Debug.Log("[TRANSPORT] Set HeartbeatTimeoutMS to 5000ms");
            }

            // Connection Timeout
            var connectionTimeoutProp = transportType.GetProperty("ConnectionTimeoutMS");
            if (connectionTimeoutProp != null && connectionTimeoutProp.CanWrite)
            {
                connectionTimeoutProp.SetValue(transport, 10000); // 10 seconds
                Debug.Log("[TRANSPORT] Set ConnectionTimeoutMS to 10000ms");
            }

            // Disconnect Timeout
            var disconnectTimeoutProp = transportType.GetProperty("DisconnectTimeoutMS");
            if (disconnectTimeoutProp != null && disconnectTimeoutProp.CanWrite)
            {
                disconnectTimeoutProp.SetValue(transport, 30000); // 30 seconds
                Debug.Log("[TRANSPORT] Set DisconnectTimeoutMS to 30000ms");
            }
        }
        catch (System.Exception e)
        {
            // Some properties may not exist in all Unity Transport versions
            // This is not critical, so we just log a warning
            Debug.LogWarning($"[TRANSPORT] Could not optimize all transport settings: {e.Message}. This is usually not critical.");
        }
    }

    private void OnDestroy()
    {
        // Clean up listeners
        if (m_HostButton != null)
            m_HostButton.onClick.RemoveListener(OnHostClicked);
        
        if (m_JoinButton != null)
            m_JoinButton.onClick.RemoveListener(OnJoinClicked);
        
        if (m_ConfirmJoinButton != null)
            m_ConfirmJoinButton.onClick.RemoveListener(OnConfirmJoinClicked);
        
        if (m_CancelJoinButton != null)
            m_CancelJoinButton.onClick.RemoveListener(OnCancelJoinClicked);

        // Unsubscribe from network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
        }
    }
}

