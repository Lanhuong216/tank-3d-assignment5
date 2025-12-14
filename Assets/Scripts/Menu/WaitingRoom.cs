using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class WaitingRoom : MonoBehaviour
{
    [Header("UI References")]
    public GameObject m_PlayerListContent;
    public GameObject m_PlayerListItemPrefab;
    public Button m_PlayButton;
    public Button m_ReadyButton;
    public Text m_StatusText;
    public Button m_LeaveButton;

    private Dictionary<ulong, GameObject> m_PlayerListItems = new Dictionary<ulong, GameObject>();
    private bool m_IsReady = false;

    private void Start()
    {
        // Setup button listeners
        if (m_PlayButton != null)
        {
            m_PlayButton.onClick.AddListener(OnPlayClicked);
            m_PlayButton.gameObject.SetActive(false);
        }

        if (m_ReadyButton != null)
        {
            m_ReadyButton.onClick.AddListener(OnReadyClicked);
            m_ReadyButton.gameObject.SetActive(false);
        }

        if (m_LeaveButton != null)
            m_LeaveButton.onClick.AddListener(OnLeaveClicked);

        // Wait for network to be ready
        StartCoroutine(WaitForNetworkAndSetup());
    }

    private IEnumerator WaitForNetworkAndSetup()
    {
        // Wait for NetworkManager to be available
        while (NetworkManager.Singleton == null)
        {
            yield return null;
        }

        // Wait a bit more for network to initialize
        yield return new WaitForSeconds(0.5f);

        if (NetworkManager.Singleton.IsServer)
        {
            if (m_PlayButton != null)
                m_PlayButton.gameObject.SetActive(true);
            
            if (m_StatusText != null)
                m_StatusText.text = "Waiting for players...";
        }
        else if (NetworkManager.Singleton.IsClient)
        {
            if (m_ReadyButton != null)
                m_ReadyButton.gameObject.SetActive(true);
            
            if (m_StatusText != null)
                m_StatusText.text = "Click READY when you're ready";
        }

        // Subscribe to network events
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        // Update player list periodically
        InvokeRepeating(nameof(UpdatePlayerList), 0.5f, 0.5f);
    }

    private void OnClientConnected(ulong clientId)
    {
        UpdatePlayerList();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (m_PlayerListItems.ContainsKey(clientId))
        {
            Destroy(m_PlayerListItems[clientId]);
            m_PlayerListItems.Remove(clientId);
        }
        UpdatePlayerList();
    }

    private void UpdatePlayerList()
    {
        if (NetworkManager.Singleton == null || m_PlayerListContent == null)
            return;

        // Clear existing items
        foreach (var item in m_PlayerListItems.Values)
        {
            if (item != null)
                Destroy(item);
        }
        m_PlayerListItems.Clear();

        // Add all connected players
        if (NetworkManager.Singleton.IsServer)
        {
            AddPlayerToList(NetworkManager.ServerClientId, "Host", true);
        }

        // Add all clients
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId != NetworkManager.ServerClientId)
            {
                bool isReady = false;
                string playerName = "Player " + clientId;
                
                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
                {
                    // Safely check if PlayerObject exists and hasn't been destroyed
                    // Unity's fake null means we need to check both null and if the object is actually destroyed
                    NetworkObject playerObj = client.PlayerObject;
                    if (playerObj != null)
                    {
                        // Additional safety check: verify the GameObject is still valid
                        // Check if the GameObject itself is null or destroyed
                        if (playerObj.gameObject == null)
                        {
                            Debug.LogWarning($"PlayerObject GameObject for client {clientId} is null, skipping...");
                            continue;
                        }
                        
                        // Try to get the component, but handle potential destruction
                        try
                        {
                            // Use a safer way to check if object is destroyed
                            // If GetComponent throws MissingReferenceException, the object was destroyed
                            var readyComponent = playerObj.GetComponent<PlayerReadyNetwork>();
                            if (readyComponent != null)
                            {
                                // Double-check the component's GameObject is still valid
                                if (readyComponent.gameObject != null)
                                {
                                    isReady = readyComponent.IsReady;
                                    playerName = readyComponent.GetPlayerName();
                                }
                            }
                        }
                        catch (MissingReferenceException)
                        {
                            // PlayerObject was destroyed, skip this client
                            Debug.LogWarning($"PlayerObject for client {clientId} was destroyed, skipping...");
                            continue;
                        }
                        catch (System.Exception e)
                        {
                            // Catch any other exceptions related to destroyed objects
                            Debug.LogWarning($"Error accessing PlayerObject for client {clientId}: {e.Message}. Skipping...");
                            continue;
                        }
                    }
                }

                AddPlayerToList(clientId, playerName, isReady);
            }
        }

        // Update PLAY button state
        if (NetworkManager.Singleton.IsServer && m_PlayButton != null)
        {
            bool allReady = AreAllClientsReady();
            m_PlayButton.interactable = allReady;
            
            if (m_StatusText != null)
            {
                if (allReady)
                    m_StatusText.text = "All players ready! Click PLAY to start.";
                else
                    m_StatusText.text = "Waiting for all players to be ready...";
            }
        }
    }

    private void AddPlayerToList(ulong clientId, string playerName, bool isReady)
    {
        if (m_PlayerListItemPrefab == null || m_PlayerListContent == null)
            return;

        GameObject item = Instantiate(m_PlayerListItemPrefab, m_PlayerListContent.transform);
        
        Text nameText = item.GetComponentInChildren<Text>();
        if (nameText != null)
        {
            nameText.text = playerName + (isReady ? " [READY]" : " [NOT READY]");
        }

        m_PlayerListItems[clientId] = item;
    }

    private bool AreAllClientsReady()
    {
        if (!NetworkManager.Singleton.IsServer)
            return false;

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId == NetworkManager.ServerClientId)
                continue;

            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            {
                // Safely check PlayerObject
                NetworkObject playerObj = client.PlayerObject;
                if (playerObj == null || playerObj.gameObject == null)
                {
                    return false; // Player object not ready yet or was destroyed
                }

                try
                {
                    var readyComponent = playerObj.GetComponent<PlayerReadyNetwork>();
                    if (readyComponent == null || readyComponent.gameObject == null || !readyComponent.IsReady)
                    {
                        return false;
                    }
                }
                catch (MissingReferenceException)
                {
                    // PlayerObject was destroyed
                    return false;
                }
                catch (System.Exception)
                {
                    // Any other error means not ready
                    return false;
                }
            }
        }

        return NetworkManager.Singleton.ConnectedClientsIds.Count > 1;
    }

    private void OnReadyClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
            return;

        m_IsReady = !m_IsReady;
        StartCoroutine(SetReadyStatus());
    }

    private IEnumerator SetReadyStatus()
    {
        while (NetworkManager.Singleton.LocalClient.PlayerObject == null)
        {
            yield return null;
        }

        var playerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObject != null)
        {
            var readyComponent = playerObject.GetComponent<PlayerReadyNetwork>();
            if (readyComponent == null)
            {
                // AddComponent must be called on GameObject, not NetworkObject
                readyComponent = playerObject.gameObject.AddComponent<PlayerReadyNetwork>();
            }
            readyComponent.SetReady(m_IsReady);
        }

        if (m_ReadyButton != null)
        {
            Text buttonText = m_ReadyButton.GetComponentInChildren<Text>();
            if (buttonText != null)
            {
                buttonText.text = m_IsReady ? "NOT READY" : "READY";
            }
        }

        if (m_StatusText != null)
        {
            m_StatusText.text = m_IsReady ? "Ready! Waiting for host to start..." : "Click READY when you're ready";
        }
    }

    private void OnPlayClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        if (!AreAllClientsReady())
        {
            Debug.LogWarning("Not all clients are ready!");
            return;
        }

        // CRITICAL: Use NetworkSceneManager to load scene so all clients sync
        // This ensures both host and clients load the same scene
        NetworkSceneHelper.LoadNetworkScene("Main", LoadSceneMode.Single);
    }

    private void OnLeaveClicked()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        SceneManager.LoadScene("MainMenu");
    }

    private void OnDestroy()
    {
        if (m_PlayButton != null)
            m_PlayButton.onClick.RemoveListener(OnPlayClicked);
        
        if (m_ReadyButton != null)
            m_ReadyButton.onClick.RemoveListener(OnReadyClicked);
        
        if (m_LeaveButton != null)
            m_LeaveButton.onClick.RemoveListener(OnLeaveClicked);

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
}
