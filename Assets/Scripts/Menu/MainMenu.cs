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
                // Try SetConnectionData method (newer API)
                transport.SetConnectionData(ipAddress, 7777);
                Debug.Log($"Connecting to host at {ipAddress}:7777");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"SetConnectionData failed: {e.Message}. Trying fallback method.");
                // Fallback: set ConnectionData properties directly
                var connectionData = transport.ConnectionData;
                connectionData.Address = ipAddress;
                connectionData.Port = 7777;
                Debug.Log($"Set connection data to {ipAddress}:7777 (fallback method)");
            }
        }
        else
        {
            Debug.LogError("UnityTransport component not found on NetworkManager!");
            return;
        }

        bool success = NetworkManager.Singleton.StartClient();
        if (success)
        {
            Debug.Log($"Client started. Attempting to connect to {ipAddress}:7777");
            // Load waiting scene after connection is established
            StartCoroutine(WaitForConnectionAndLoadScene());
        }
        else
        {
            Debug.LogError("Failed to start client!");
        }
    }

    private void OnCancelJoinClicked()
    {
        // Hide join panel
        if (m_JoinPanel != null)
            m_JoinPanel.SetActive(false);
    }

    private IEnumerator WaitForConnectionAndLoadScene()
    {
        // Wait for client to connect
        while (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsClient)
        {
            yield return null;
        }

        // Small delay to ensure connection is stable
        yield return new WaitForSeconds(0.5f);

        // Load waiting scene
        SceneManager.LoadScene("WaitingRoom");
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
    }
}
