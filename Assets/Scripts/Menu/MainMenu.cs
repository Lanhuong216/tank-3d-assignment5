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

        // Subscribe to network events for connection status
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client connected successfully! Client ID: {clientId}");
        // If we're a client connecting to a host, load waiting room
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            Debug.Log("Client connected to host. Loading waiting room...");
            SceneManager.LoadScene("WaitingRoom");
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.ServerClientId)
        {
            Debug.LogError("Disconnected from server!");
        }
        else
        {
            Debug.Log($"Client {clientId} disconnected.");
        }
    }

    private void OnTransportFailure()
    {
        Debug.LogError("Transport failure occurred! Check network connection and firewall settings.");
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
        if (NetworkManager.Singleton != null)
        {
            // CRITICAL: Configure Unity Transport to listen on all network interfaces
            // This allows clients from other machines on the network to connect
            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport != null)
            {
                // Set server to listen on all interfaces (0.0.0.0) to accept connections from network
                // This is required for LAN connections, not just localhost
                try
                {
                    // Method 1: Try SetConnectionData with 0.0.0.0 (all interfaces)
                    try
                    {
                        transport.SetConnectionData("0.0.0.0", 7777);
                        Debug.Log("Set host to listen on 0.0.0.0:7777 (all network interfaces)");
                    }
                    catch
                    {
                        // Method 2: Set ConnectionData properties directly
                        var connectionData = transport.ConnectionData;
                        connectionData.Address = "0.0.0.0"; // Listen on all interfaces
                        connectionData.Port = 7777;
                        Debug.Log("Set ConnectionData.Address to 0.0.0.0 (all network interfaces)");
                    }
                    
                    // Method 3: Try to set ServerListenAddress property if available
                    // This is the proper way in newer Unity Transport versions
                    var transportType = transport.GetType();
                    var serverListenAddressProp = transportType.GetProperty("ServerListenAddress");
                    if (serverListenAddressProp != null && serverListenAddressProp.CanWrite)
                    {
                        serverListenAddressProp.SetValue(transport, "0.0.0.0");
                        Debug.Log("Set ServerListenAddress to 0.0.0.0 (all network interfaces)");
                    }
                    else
                    {
                        // Try alternative property names
                        var serverAddressProp = transportType.GetProperty("ServerAddress");
                        if (serverAddressProp != null && serverAddressProp.CanWrite)
                        {
                            serverAddressProp.SetValue(transport, "0.0.0.0");
                            Debug.Log("Set ServerAddress to 0.0.0.0 (all network interfaces)");
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Could not fully configure server listen address: {e.Message}. Host may only accept localhost connections. Check firewall settings.");
                }
            }
            else
            {
                Debug.LogError("UnityTransport component not found on NetworkManager!");
            }
            
            bool success = NetworkManager.Singleton.StartHost();
            if (success)
            {
                Debug.Log("Host started successfully. Listening on all network interfaces (0.0.0.0:7777)");
                Debug.Log("Clients can connect using your LAN IP address (e.g., 192.168.1.7:7777)");
                // Load waiting scene
                SceneManager.LoadScene("WaitingRoom");
            }
            else
            {
                Debug.LogError("Failed to start host!");
            }
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
            Debug.LogError("NetworkManager not found!");
            return;
        }

        // Set the connection data (IP address)
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            // Set connection data - try different API approaches
            try
            {
                // Method 1: Try SetConnectionData method (newer API)
                try
                {
                    transport.SetConnectionData(ipAddress, 7777);
                    Debug.Log($"[CLIENT] Set connection data to {ipAddress}:7777 using SetConnectionData");
                }
                catch
                {
                    // Method 2: Set ConnectionData properties directly
                    var connectionData = transport.ConnectionData;
                    connectionData.Address = ipAddress;
                    connectionData.Port = 7777;
                    Debug.Log($"[CLIENT] Set connection data to {ipAddress}:7777 using ConnectionData property");
                }

                // Method 3: Try setting ServerAddress property (for some Unity Transport versions)
                var transportType = transport.GetType();
                var serverAddressProp = transportType.GetProperty("ServerAddress");
                if (serverAddressProp != null && serverAddressProp.CanWrite)
                {
                    serverAddressProp.SetValue(transport, ipAddress);
                    Debug.Log($"[CLIENT] Set ServerAddress property to {ipAddress}");
                }

                // Verify the connection data was set correctly
                var verifyData = transport.ConnectionData;
                Debug.Log($"[CLIENT] Verified connection data - Address: {verifyData.Address}, Port: {verifyData.Port}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[CLIENT] Failed to set connection data: {e.Message}\nStackTrace: {e.StackTrace}");
                return;
            }
        }
        else
        {
            Debug.LogError("UnityTransport component not found on NetworkManager!");
            return;
        }

        // Start client with timeout handling
        bool success = NetworkManager.Singleton.StartClient();
        if (success)
        {
            Debug.Log($"[CLIENT] Client started. Attempting to connect to {ipAddress}:7777");
            // Start timeout coroutine
            StartCoroutine(WaitForConnectionWithTimeout(ipAddress));
        }
        else
        {
            Debug.LogError("[CLIENT] Failed to start client! Check NetworkManager configuration.");
        }
    }

    private void OnCancelJoinClicked()
    {
        // Hide join panel
        if (m_JoinPanel != null)
            m_JoinPanel.SetActive(false);
    }

    private IEnumerator WaitForConnectionWithTimeout(string ipAddress)
    {
        float timeout = 10f; // 10 seconds timeout
        float elapsedTime = 0f;
        bool connected = false;

        Debug.Log($"[CLIENT] Waiting for connection to {ipAddress}:7777 (timeout: {timeout}s)...");

        // Wait for client to connect or timeout
        while (NetworkManager.Singleton != null && elapsedTime < timeout)
        {
            // Check if we're connected
            if (NetworkManager.Singleton.IsClient && NetworkManager.Singleton.IsConnectedClient)
            {
                connected = true;
                Debug.Log($"[CLIENT] Successfully connected to {ipAddress}:7777!");
                break;
            }

            // Check if connection failed
            if (NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsConnectedClient)
            {
                // Still trying to connect
                yield return new WaitForSeconds(0.1f);
                elapsedTime += 0.1f;
                continue;
            }

            yield return new WaitForSeconds(0.1f);
            elapsedTime += 0.1f;
        }

        if (!connected)
        {
            Debug.LogError($"[CLIENT] Connection timeout! Could not connect to {ipAddress}:7777 after {timeout} seconds.");
            Debug.LogError("[CLIENT] Possible causes:");
            Debug.LogError("  1. Host is not running or not listening on the network");
            Debug.LogError("  2. Firewall is blocking the connection (check Windows Firewall)");
            Debug.LogError("  3. IP address is incorrect");
            Debug.LogError("  4. Host and client are not on the same network");
            Debug.LogError("  5. Port 7777 is blocked or in use");
            
            // Shutdown failed connection
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }
        else
        {
            // Small delay to ensure connection is stable
            yield return new WaitForSeconds(0.5f);
            Debug.Log("[CLIENT] Connection stable. Loading waiting room...");
            // Load waiting scene
            SceneManager.LoadScene("WaitingRoom");
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe from network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
        }

        // Clean up listeners
        if (m_HostButton != null)
            m_HostButton.onClick.RemoveListener(OnHostClicked);
        
        if (m_JoinButton != null)
            m_JoinButton.onClick.RemoveListener(OnJoinClicked);
        
        if (m_ConfirmJoinButton != null)
            m_ConfirmJoinButton.onClick.RemoveListener(OnConfirmJoinClicked);
        
        if (m_CancelJoinButton != null)
            m_CancelJoinButton.onClick.RemoveListener(OnCancelJoinClicked);
    }
}
