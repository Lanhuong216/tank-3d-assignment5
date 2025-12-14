using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class NetworkGameManager : MonoBehaviour
{
    private static NetworkGameManager s_Instance;
    public static NetworkGameManager Instance => s_Instance;

    [Header("Network Settings")]
    public ushort m_Port = 7777;
    public string m_DefaultIP = "127.0.0.1";

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_Instance = this;
        DontDestroyOnLoad(gameObject);

        // Setup NetworkManager
        if (NetworkManager.Singleton != null)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                var connectionData = transport.ConnectionData;
                connectionData.Port = m_Port;
            }
        }
    }

    public void StartHost()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.StartHost();
        }
    }

    public void StartClient(string ipAddress)
    {
        if (NetworkManager.Singleton != null)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                var connectionData = transport.ConnectionData;
                connectionData.Address = ipAddress;
                connectionData.Port = m_Port;
            }
            NetworkManager.Singleton.StartClient();
        }
    }
}

