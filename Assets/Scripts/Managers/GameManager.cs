using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class GameManager : NetworkBehaviour
{
    [Header("Game Settings")]
    public int m_NumRoundsToWin = 5;            // The number of rounds a single player has to win to win the game.
    public float m_StartDelay = 3f;             // The delay between the start of RoundStarting and RoundPlaying phases.
    public float m_EndDelay = 3f;               // The delay between the end of RoundPlaying and RoundEnding phases.
    
    [Header("References")]
    public CameraControl m_CameraControl;       // Reference to the CameraControl script for control during different phases.
    public Text m_MessageText;                  // Reference to the overlay Text to display winning text, etc.
    public GameObject m_NetworkTankPrefab;       // Reference to the network tank prefab.
    public Transform[] m_SpawnPoints;           // Array of spawn points for tanks.
    public Color[] m_PlayerColors;              // Array of colors for each player.
    
    // Network synchronized game state
    private NetworkVariable<int> m_RoundNumber = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<GameState> m_GameState = new NetworkVariable<GameState>(GameState.Waiting, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Local game state
    private WaitForSeconds m_StartWait;
    private WaitForSeconds m_EndWait;
    private Dictionary<ulong, TankManager> m_PlayerTanks = new Dictionary<ulong, TankManager>();
    private List<ulong> m_ConnectedClients = new List<ulong>();
    private TankManager m_RoundWinner;
    private TankManager m_GameWinner;
    private Coroutine m_GameLoopCoroutine;

    public enum GameState
    {
        Waiting,
        RoundStarting,
        RoundPlaying,
        RoundEnding
    }

    private static GameManager s_Instance;
    public static GameManager Instance => s_Instance;

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        s_Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Create the delays so they only have to be made once.
        m_StartWait = new WaitForSeconds(m_StartDelay);
        m_EndWait = new WaitForSeconds(m_EndDelay);

        // Find CameraControl if not assigned (important for network scene loading)
        if (m_CameraControl == null)
        {
            m_CameraControl = FindObjectOfType<CameraControl>();
            if (m_CameraControl != null)
            {
                Debug.Log("GameManager: Found CameraControl automatically.");
            }
            else
            {
                Debug.LogWarning("GameManager: CameraControl not found! Camera may not work correctly.");
            }
        }

        // Subscribe to network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }

        // Subscribe to NetworkVariable changes for clients
        m_RoundNumber.OnValueChanged += OnRoundNumberChanged;
        m_GameState.OnValueChanged += OnGameStateChanged;

        // Note: Unity Netcode for GameObjects doesn't have PlayerPrefab auto-spawn
        // We handle all tank spawning manually in GameManager, so no need to disable anything

        // Only server starts the game loop
        if (IsServer)
        {
            StartCoroutine(WaitForPlayersAndStart());
        }
        else
        {
            // Client waits for tanks to spawn then sets camera targets
            StartCoroutine(WaitForTanksAndSetCamera());
            
            // Client also updates message based on current round number
            if (m_RoundNumber.Value > 0)
            {
                UpdateMessageText($"ROUND {m_RoundNumber.Value}");
            }
        }
    }

    private void OnRoundNumberChanged(int previousValue, int newValue)
    {
        // Update message text when round number changes (for clients)
        if (!IsServer && newValue > 0)
        {
            // Only update if we're in RoundStarting state
            if (m_GameState.Value == GameState.RoundStarting)
            {
                UpdateMessageText($"ROUND {newValue}");
            }
        }
    }

    private void OnGameStateChanged(GameState previousValue, GameState newValue)
    {
        // Update message text based on game state (for clients)
        if (!IsServer)
        {
            if (newValue == GameState.RoundStarting && m_RoundNumber.Value > 0)
            {
                UpdateMessageText($"ROUND {m_RoundNumber.Value}");
            }
            else if (newValue == GameState.RoundPlaying)
            {
                UpdateMessageText(string.Empty);
            }
        }
    }

    private void UpdateMessageText(string message)
    {
        if (m_MessageText != null)
        {
            m_MessageText.text = message;
        }
    }

    private IEnumerator WaitForTanksAndSetCamera()
    {
        // Wait for scene to fully load
        yield return new WaitForSeconds(0.5f);
        
        // Ensure CameraControl is found (in case it wasn't found in OnNetworkSpawn)
        if (m_CameraControl == null)
        {
            m_CameraControl = FindObjectOfType<CameraControl>();
            if (m_CameraControl == null)
            {
                Debug.LogError("Client: CameraControl not found! Camera will not work. Make sure CameraControl exists in the scene.");
                yield break; // Exit coroutine if camera control is missing
            }
            else
            {
                Debug.Log("Client: Found CameraControl in WaitForTanksAndSetCamera.");
            }
        }
        
        // Wait for tanks to spawn - check for at least 1 tank (minimum for camera to work)
        // We'll wait for tanks to match the number of connected players
        int maxWaitTime = 10; // Maximum wait time in seconds
        float elapsedTime = 0f;
        int tankCount = 0;
        int expectedTankCount = 0;
        
        // Determine expected number of tanks based on connected players
        if (NetworkManager.Singleton != null)
        {
            expectedTankCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
            if (expectedTankCount == 0)
            {
                expectedTankCount = 1; // At least host
            }
        }
        else
        {
            expectedTankCount = 1; // Fallback
        }
        
        Debug.Log($"Client: Waiting for {expectedTankCount} tanks to spawn...");
        
        while (tankCount < expectedTankCount && elapsedTime < maxWaitTime)
        {
            // Count spawned tanks
            ClientTankBehavior[] allTanks = FindObjectsOfType<ClientTankBehavior>(true);
            tankCount = 0;
            foreach (var tank in allTanks)
            {
                if (tank != null && tank.gameObject != null)
                {
                    NetworkObject networkObj = tank.GetComponent<NetworkObject>();
                    if (networkObj != null && networkObj.IsSpawned)
                    {
                        tankCount++;
                    }
                }
            }
            
            if (tankCount < expectedTankCount)
            {
                yield return new WaitForSeconds(0.2f);
                elapsedTime += 0.2f;
            }
        }
        
        Debug.Log($"Client: Found {tankCount} tanks after waiting {elapsedTime:F1} seconds. Setting camera targets...");
        
        // Set camera targets for client
        SetCameraTargets();
        
        // Also call SetStartPositionAndSize to ensure camera is positioned correctly
        if (m_CameraControl != null)
        {
            m_CameraControl.SetStartPositionAndSize();
            Debug.Log("Client: Camera position and size set.");
        }
        else
        {
            Debug.LogError("Client: m_CameraControl is still null after search! Cannot set camera position.");
        }
        
        // Update message text based on current game state
        if (m_GameState.Value == GameState.RoundStarting && m_RoundNumber.Value > 0)
        {
            UpdateMessageText($"ROUND {m_RoundNumber.Value}");
        }
        else if (m_GameState.Value == GameState.RoundPlaying)
        {
            UpdateMessageText(string.Empty);
        }
        
        // Update camera targets periodically in case tanks spawn later or are reset
        while (true)
        {
            yield return new WaitForSeconds(1f);
            SetCameraTargets();
        }
    }

    private void Start()
    {
        // Fallback: If network spawn hasn't been called yet, wait for it
        if (!IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            // This handles the case where GameManager is already in the scene
            StartCoroutine(WaitForNetworkSpawn());
        }
    }

    private IEnumerator WaitForNetworkSpawn()
    {
        while (!IsSpawned)
        {
            yield return null;
        }
    }

    private IEnumerator WaitForPlayersAndStart()
    {
        // Wait for at least 2 players (host + 1 client)
        while (NetworkManager.Singleton == null || NetworkManager.Singleton.ConnectedClientsIds.Count < 2)
        {
            yield return new WaitForSeconds(0.5f);
        }

        // Small delay to ensure all clients are ready
        yield return new WaitForSeconds(1f);

        // CRITICAL: Only spawn tanks if game hasn't started yet
        if (m_GameState.Value == GameState.Waiting)
        {
            Debug.Log("Starting game. Spawning all tanks...");
            // Spawn all tanks
            SpawnAllTanks();
            SetCameraTargets();
            
            // Notify all clients that tanks have been spawned and they should update their cameras
            NotifyTanksSpawnedClientRpc();

            // Start the game loop
            m_GameLoopCoroutine = StartCoroutine(GameLoop());
        }
        else
        {
            Debug.LogWarning("Game already started! Skipping tank spawn.");
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer)
        {
            // Check if client is already in the list (prevent duplicate handling)
            if (m_ConnectedClients.Contains(clientId))
            {
                Debug.LogWarning($"Client {clientId} is already in connected clients list!");
                return;
            }

            m_ConnectedClients.Add(clientId);
            
            // Only spawn tank if game is already running AND client doesn't have a tank yet
            // If game hasn't started yet, WaitForPlayersAndStart() will spawn tanks for all clients
            if ((m_GameState.Value == GameState.RoundPlaying || m_GameState.Value == GameState.RoundStarting) 
                && (m_PlayerTanks == null || !m_PlayerTanks.ContainsKey(clientId)))
            {
                Debug.Log($"Client {clientId} connected during game. Spawning tank...");
                SpawnTankForClient(clientId);
                SetCameraTargets();
            }
            else
            {
                Debug.Log($"Client {clientId} connected. Will spawn tank when game starts.");
            }
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (IsServer)
        {
            m_ConnectedClients.Remove(clientId);
            
            // Remove tank from dictionary
            if (m_PlayerTanks.ContainsKey(clientId))
            {
                if (m_PlayerTanks[clientId].m_Instance != null)
                {
                    Destroy(m_PlayerTanks[clientId].m_Instance);
                }
                m_PlayerTanks.Remove(clientId);
            }

            // Update camera targets
            SetCameraTargets();

            // Check if game should end (less than 2 players)
            if (NetworkManager.Singleton.ConnectedClientsIds.Count < 2 && m_GameLoopCoroutine != null)
            {
                StopCoroutine(m_GameLoopCoroutine);
                m_GameState.Value = GameState.Waiting;
                if (m_MessageText != null)
                {
                    m_MessageText.text = "Not enough players. Waiting for more players...";
                }
            }
        }
    }

    private void SpawnAllTanks()
    {
        if (!IsServer) return;

        Debug.Log($"SpawnAllTanks called. Connected clients: {NetworkManager.Singleton.ConnectedClientsIds.Count}");

        // CRITICAL: Destroy ALL existing tanks in the scene first (including any spawned by NetworkManager Player Prefab)
        DestroyAllExistingTanks();

        // Clear existing tanks dictionary to prevent duplicates
        if (m_PlayerTanks == null)
        {
            m_PlayerTanks = new Dictionary<ulong, TankManager>();
        }
        else
        {
            // Destroy any tanks in our dictionary
            foreach (var tankManager in m_PlayerTanks.Values)
            {
                if (tankManager != null && tankManager.m_Instance != null)
                {
                    Destroy(tankManager.m_Instance);
                }
            }
            m_PlayerTanks.Clear();
        }

        // Count how many players we need to spawn for
        int totalPlayers = 1; // Host
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId != NetworkManager.ServerClientId)
            {
                totalPlayers++;
            }
        }

        Debug.Log($"Total players to spawn tanks for: {totalPlayers} (1 host + {totalPlayers - 1} clients)");

        int spawnIndex = 0;
        
        // Spawn tank for host (server)
        SpawnTankForClient(NetworkManager.ServerClientId, spawnIndex++);
        Debug.Log($"Spawned tank for host (ServerClientId: {NetworkManager.ServerClientId})");

        // Spawn tanks for all connected clients
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId != NetworkManager.ServerClientId)
            {
                SpawnTankForClient(clientId, spawnIndex++);
                Debug.Log($"Spawned tank for client {clientId}");
            }
        }

        // Verify: Count actual spawned tanks
        int actualTankCount = CountSpawnedTanks();
        Debug.Log($"Spawn complete. Expected: {totalPlayers}, Actual: {actualTankCount}, Dictionary: {m_PlayerTanks.Count}");
        
        if (actualTankCount != totalPlayers)
        {
            Debug.LogError($"Tank count mismatch! Expected {totalPlayers} but found {actualTankCount} tanks in scene!");
        }
    }

    private void DestroyAllExistingTanks()
    {
        // Find and destroy ALL tanks in the scene (including any spawned by NetworkManager Player Prefab)
        ClientTankBehavior[] allTanks = FindObjectsOfType<ClientTankBehavior>(true);
        Debug.Log($"Found {allTanks.Length} existing tanks in scene. Destroying them...");
        
        foreach (var tank in allTanks)
        {
            if (tank != null && tank.gameObject != null)
            {
                NetworkObject networkObj = tank.GetComponent<NetworkObject>();
                if (networkObj != null && networkObj.IsSpawned)
                {
                    Debug.Log($"Destroying existing tank: {tank.gameObject.name} (Owner: {networkObj.OwnerClientId})");
                    networkObj.Despawn(true); // Despawn properly on network
                }
                else
                {
                    Destroy(tank.gameObject);
                }
            }
        }

        // Also check for tanks with TankMovement component (fallback)
        TankMovement[] allTankMovements = FindObjectsOfType<TankMovement>(true);
        foreach (var tankMovement in allTankMovements)
        {
            if (tankMovement != null && tankMovement.gameObject != null)
            {
                NetworkObject networkObj = tankMovement.GetComponent<NetworkObject>();
                if (networkObj != null && networkObj.IsSpawned)
                {
                    if (!tankMovement.GetComponent<ClientTankBehavior>()) // Only if not already handled
                    {
                        Debug.Log($"Destroying existing tank (TankMovement): {tankMovement.gameObject.name}");
                        networkObj.Despawn(true);
                    }
                }
                else
                {
                    Destroy(tankMovement.gameObject);
                }
            }
        }
    }

    private int CountSpawnedTanks()
    {
        ClientTankBehavior[] allTanks = FindObjectsOfType<ClientTankBehavior>(true);
        int count = 0;
        foreach (var tank in allTanks)
        {
            if (tank != null && tank.gameObject != null)
            {
                NetworkObject networkObj = tank.GetComponent<NetworkObject>();
                if (networkObj != null && networkObj.IsSpawned)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private void SpawnTankForClient(ulong clientId, int spawnIndex = -1)
    {
        if (!IsServer) return;

        // CRITICAL: Check if client already has a tank - prevent duplicate spawning
        if (m_PlayerTanks != null && m_PlayerTanks.ContainsKey(clientId))
        {
            Debug.LogWarning($"Client {clientId} already has a tank! Skipping spawn.");
            return;
        }

        // Initialize dictionary if null
        if (m_PlayerTanks == null)
        {
            m_PlayerTanks = new Dictionary<ulong, TankManager>();
        }

        // Determine spawn index if not provided
        if (spawnIndex == -1)
        {
            spawnIndex = m_PlayerTanks.Count;
        }

        // Make sure we have enough spawn points
        if (m_SpawnPoints == null || spawnIndex >= m_SpawnPoints.Length)
        {
            Debug.LogError($"Not enough spawn points! Need {spawnIndex + 1}, have {(m_SpawnPoints != null ? m_SpawnPoints.Length : 0)}");
            return;
        }

        if (m_NetworkTankPrefab == null)
        {
            Debug.LogError("NetworkTank prefab is not assigned!");
            return;
        }

        Transform spawnPoint = m_SpawnPoints[spawnIndex];
        Color playerColor = (m_PlayerColors != null && spawnIndex < m_PlayerColors.Length) ? m_PlayerColors[spawnIndex] : Color.white;

        // Spawn the network tank
        GameObject tankInstance = Instantiate(m_NetworkTankPrefab, spawnPoint.position, spawnPoint.rotation);
        NetworkObject networkObject = tankInstance.GetComponent<NetworkObject>();
        
        if (networkObject != null)
        {
            // CRITICAL: Check if NetworkObject is already spawned (shouldn't happen, but safety check)
            if (networkObject.IsSpawned)
            {
                Debug.LogError($"Tank NetworkObject is already spawned! Destroying duplicate.");
                Destroy(tankInstance);
                return;
            }

            // CRITICAL: TankColorSync component MUST be on the prefab, not added at runtime
            // NetworkBehaviour components must be the same between server and client
            // Check if TankColorSync exists on prefab
            TankColorSync colorSync = tankInstance.GetComponent<TankColorSync>();
            
            // Spawn on network and assign ownership FIRST
            networkObject.SpawnWithOwnership(clientId);
            
            if (colorSync != null)
            {
                // Use TankColorSync component (preferred method - syncs via NetworkVariable)
                StartCoroutine(SetTankColorAfterSpawn(colorSync, playerColor));
            }
            else
            {
                // Fallback: Use ClientRpc to sync color (if TankColorSync not on prefab)
                Debug.LogWarning("TankColorSync component not found on NetworkTank prefab! Using ClientRpc fallback. Please add TankColorSync component to the prefab for better performance.");
                // Wait a frame then call ClientRpc
                StartCoroutine(DelayedSetTankColor(networkObject.NetworkObjectId, playerColor));
            }
            
            // Create TankManager for this player
            TankManager tankManager = new TankManager
            {
                m_Instance = tankInstance,
                m_SpawnPoint = spawnPoint,
                m_PlayerNumber = (int)clientId + 1,
                m_PlayerColor = playerColor
            };
            
            // Setup tank (this sets color locally on server, but TankColorSync will sync to clients)
            tankManager.Setup();
            m_PlayerTanks[clientId] = tankManager;
            
            Debug.Log($"Spawned tank for client {clientId} at spawn index {spawnIndex}");
        }
        else
        {
            Debug.LogError("NetworkTank prefab does not have NetworkObject component!");
            Destroy(tankInstance);
        }
    }

    private void SetCameraTargets()
    {
        if (m_CameraControl == null) return;

        // Collect all active tank transforms
        List<Transform> targets = new List<Transform>();
        
        if (IsServer)
        {
            // Server uses its own dictionary
            if (m_PlayerTanks != null)
            {
                foreach (var tankManager in m_PlayerTanks.Values)
                {
                    if (tankManager != null && tankManager.m_Instance != null && tankManager.m_Instance.activeSelf)
                    {
                        targets.Add(tankManager.m_Instance.transform);
                    }
                }
            }
        }
        else
        {
            // Client finds all tanks in the scene (all network tanks are visible to all clients)
            // Find all NetworkObjects that are tanks by checking for ClientTankBehavior component
            // This is more reliable than checking TankMovement (which might not be enabled)
            ClientTankBehavior[] allTanks = FindObjectsOfType<ClientTankBehavior>(true);
            foreach (var tankBehavior in allTanks)
            {
                if (tankBehavior != null && tankBehavior.gameObject.activeSelf)
                {
                    NetworkObject networkObj = tankBehavior.GetComponent<NetworkObject>();
                    if (networkObj != null && networkObj.IsSpawned)
                    {
                        targets.Add(tankBehavior.transform);
                    }
                }
            }
            
            // Fallback: if no ClientTankBehavior found, try finding by TankMovement
            if (targets.Count == 0)
            {
                TankMovement[] allTankMovements = FindObjectsOfType<TankMovement>(true);
                foreach (var tankMovement in allTankMovements)
                {
                    if (tankMovement != null && tankMovement.gameObject.activeSelf)
                    {
                        NetworkObject networkObj = tankMovement.GetComponent<NetworkObject>();
                        if (networkObj != null && networkObj.IsSpawned)
                        {
                            targets.Add(tankMovement.transform);
                        }
                    }
                }
            }
        }

        m_CameraControl.m_Targets = targets.ToArray();
    }

    // This is called from start and will run each phase of the game one after another.
    private IEnumerator GameLoop()
    {
        if (!IsServer) yield break;

        // Start off by running the 'RoundStarting' coroutine but don't return until it's finished.
        yield return StartCoroutine(RoundStarting());

        // Once the 'RoundStarting' coroutine is finished, run the 'RoundPlaying' coroutine but don't return until it's finished.
        yield return StartCoroutine(RoundPlaying());

        // Once execution has returned here, run the 'RoundEnding' coroutine, again don't return until it's finished.
        yield return StartCoroutine(RoundEnding());

        // This code is not run until 'RoundEnding' has finished.  At which point, check if a game winner has been found.
        if (m_GameWinner != null)
        {
            // If there is a game winner, restart the level after delay.
            yield return new WaitForSeconds(5f);
            
            // Use NetworkSceneManager to load scene so all clients sync
            NetworkSceneHelper.LoadNetworkScene("MainMenu", LoadSceneMode.Single);
        }
        else
        {
            // If there isn't a winner yet, restart this coroutine so the loop continues.
            StartCoroutine(GameLoop());
        }
    }

    private IEnumerator RoundStarting()
    {
        if (!IsServer) yield break;

        m_GameState.Value = GameState.RoundStarting;

        // As soon as the round starts reset the tanks and make sure they can't move.
        ResetAllTanks();
        DisableTankControl();

        // Snap the camera's zoom and position to something appropriate for the reset tanks.
        if (m_CameraControl != null)
        {
            // Update camera targets first (for server)
            SetCameraTargets();
            // Then set start position and size
            m_CameraControl.SetStartPositionAndSize();
        }
        
        // Notify clients to update their camera targets
        UpdateCameraTargetsClientRpc();

        // Increment the round number and display text showing the players what round it is.
        m_RoundNumber.Value++;
        UpdateMessageTextClientRpc($"ROUND {m_RoundNumber.Value}", new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = null } });

        // Wait for the specified length of time until yielding control back to the game loop.
        yield return m_StartWait;
    }

    private IEnumerator RoundPlaying()
    {
        if (!IsServer) yield break;

        m_GameState.Value = GameState.RoundPlaying;

        // As soon as the round begins playing let the players control the tanks.
        EnableTankControl();

        // Clear the text from the screen.
        UpdateMessageTextClientRpc(string.Empty, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = null } });

        // While there is not one tank left...
        while (!OneTankLeft())
        {
            // ... return on the next frame.
            yield return null;
        }
    }

    private IEnumerator RoundEnding()
    {
        if (!IsServer) yield break;

        m_GameState.Value = GameState.RoundEnding;

        // Stop tanks from moving.
        DisableTankControl();

        // Clear the winner from the previous round.
        m_RoundWinner = null;

        // See if there is a winner now the round is over.
        m_RoundWinner = GetRoundWinner();

        // If there is a winner, increment their score.
        if (m_RoundWinner != null)
        {
            m_RoundWinner.m_Wins++;
        }

        // Now the winner's score has been incremented, see if someone has won the game.
        m_GameWinner = GetGameWinner();

        // Get a message based on the scores and whether or not there is a game winner and display it.
        string message = EndMessage();
        UpdateMessageTextClientRpc(message, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = null } });

        // Wait for the specified length of time until yielding control back to the game loop.
        yield return m_EndWait;
    }

    // This is used to check if there is one or fewer tanks remaining and thus the round should end.
    private bool OneTankLeft()
    {
        if (!IsServer) return false;

        // Check if dictionary is null or empty
        if (m_PlayerTanks == null || m_PlayerTanks.Count == 0)
            return true;

        // Start the count of tanks left at zero.
        int numTanksLeft = 0;

        // Go through all the tanks...
        foreach (var tankManager in m_PlayerTanks.Values)
        {
            // Check if tankManager is null
            if (tankManager == null)
                continue;

            // ... and if they are active, increment the counter.
            if (tankManager.m_Instance != null && tankManager.m_Instance.activeSelf)
                numTanksLeft++;
        }

        // If there are one or fewer tanks remaining return true, otherwise return false.
        return numTanksLeft <= 1;
    }

    // This function is to find out if there is a winner of the round.
    // This function is called with the assumption that 1 or fewer tanks are currently active.
    private TankManager GetRoundWinner()
    {
        if (!IsServer) return null;

        // Check if dictionary is null or empty
        if (m_PlayerTanks == null || m_PlayerTanks.Count == 0)
            return null;

        // Go through all the tanks...
        foreach (var tankManager in m_PlayerTanks.Values)
        {
            // Check if tankManager is null
            if (tankManager == null)
                continue;

            // ... and if one of them is active, it is the winner so return it.
            if (tankManager.m_Instance != null && tankManager.m_Instance.activeSelf)
                return tankManager;
        }

        // If none of the tanks are active it is a draw so return null.
        return null;
    }

    // This function is to find out if there is a winner of the game.
    private TankManager GetGameWinner()
    {
        if (!IsServer) return null;

        // Check if dictionary is null or empty
        if (m_PlayerTanks == null || m_PlayerTanks.Count == 0)
            return null;

        // Go through all the tanks...
        foreach (var tankManager in m_PlayerTanks.Values)
        {
            // Check if tankManager is null
            if (tankManager == null)
                continue;

            // ... and if one of them has enough rounds to win the game, return it.
            if (tankManager.m_Wins >= m_NumRoundsToWin)
                return tankManager;
        }

        // If no tanks have enough rounds to win, return null.
        return null;
    }

    // Returns a string message to display at the end of each round.
    private string EndMessage()
    {
        // By default when a round ends there are no winners so the default end message is a draw.
        string message = "DRAW!";

        // If there is a winner then change the message to reflect that.
        if (m_RoundWinner != null)
            message = m_RoundWinner.m_ColoredPlayerText + " WINS THE ROUND!";

        // Add some line breaks after the initial message.
        message += "\n\n\n\n";

        // Go through all the tanks and add each of their scores to the message.
        if (m_PlayerTanks != null)
        {
            foreach (var tankManager in m_PlayerTanks.Values)
            {
                if (tankManager != null)
                {
                    message += tankManager.m_ColoredPlayerText + ": " + tankManager.m_Wins + " WINS\n";
                }
            }
        }

        // If there is a game winner, change the entire message to reflect that.
        if (m_GameWinner != null)
            message = m_GameWinner.m_ColoredPlayerText + " WINS THE GAME!";

        return message;
    }

    // This function is used to turn all the tanks back on and reset their positions and properties.
    private void ResetAllTanks()
    {
        if (!IsServer) return;

        if (m_PlayerTanks == null) return;

        foreach (var tankManager in m_PlayerTanks.Values)
        {
            if (tankManager != null && tankManager.m_Instance != null && tankManager.m_SpawnPoint != null)
            {
                // Reset health to full first
                TankHealth tankHealth = tankManager.m_Instance.GetComponent<TankHealth>();
                if (tankHealth != null)
                {
                    tankHealth.ResetHealth();
                }

                // Reset tank position and state (resets to spawn point)
                NetworkObject networkObj = tankManager.m_Instance.GetComponent<NetworkObject>();
                if (networkObj != null && networkObj.IsSpawned)
                {
                    // Use ClientRpc to sync position reset to all clients
                    Vector3 spawnPosition = tankManager.m_SpawnPoint.position;
                    Quaternion spawnRotation = tankManager.m_SpawnPoint.rotation;
                    
                    ResetTankPositionClientRpc(networkObj.NetworkObjectId, spawnPosition, spawnRotation);
                    
                    // Also reset on server
                    ResetTankPositionLocal(tankManager.m_Instance, spawnPosition, spawnRotation);
                }
                else
                {
                    // Fallback: Use TankManager.Reset() if not a network object
                    tankManager.Reset();
                }
            }
        }
    }

    private void ResetTankPositionLocal(GameObject tankInstance, Vector3 position, Quaternion rotation)
    {
        // Reset position and rotation
        tankInstance.transform.position = position;
        tankInstance.transform.rotation = rotation;

        // Reset Rigidbody velocity
        Rigidbody rb = tankInstance.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Reactivate tank to ensure all components are reset
        if (!tankInstance.activeSelf)
        {
            tankInstance.SetActive(true);
        }
        
        // Ensure position is set after reactivation
        tankInstance.transform.position = position;
        tankInstance.transform.rotation = rotation;
    }

    [ClientRpc]
    private void ResetTankPositionClientRpc(ulong networkObjectId, Vector3 position, Quaternion rotation, ClientRpcParams rpcParams = default)
    {
        // Find the tank by NetworkObjectId
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObj))
            {
                if (networkObj != null)
                {
                    ResetTankPositionLocal(networkObj.gameObject, position, rotation);
                }
            }
        }
    }

    private void EnableTankControl()
    {
        if (!IsServer) return;

        if (m_PlayerTanks == null) return;

        foreach (var tankManager in m_PlayerTanks.Values)
        {
            if (tankManager != null && tankManager.m_Instance != null)
            {
                tankManager.EnableControl();
            }
        }
    }

    private void DisableTankControl()
    {
        if (!IsServer) return;

        if (m_PlayerTanks == null) return;

        foreach (var tankManager in m_PlayerTanks.Values)
        {
            if (tankManager != null && tankManager.m_Instance != null)
            {
                tankManager.DisableControl();
            }
        }
    }

    [ClientRpc]
    private void UpdateMessageTextClientRpc(string message, ClientRpcParams rpcParams = default)
    {
        UpdateMessageText(message);
    }

    [ClientRpc]
    private void UpdateCameraTargetsClientRpc(ClientRpcParams rpcParams = default)
    {
        // Client updates camera targets when server starts a new round
        if (!IsServer && m_CameraControl != null)
        {
            SetCameraTargets();
            m_CameraControl.SetStartPositionAndSize();
        }
    }

    [ClientRpc]
    private void NotifyTanksSpawnedClientRpc(ClientRpcParams rpcParams = default)
    {
        // Client updates camera targets when server spawns all tanks
        if (!IsServer && m_CameraControl != null)
        {
            Debug.Log("Client: Received NotifyTanksSpawnedClientRpc. Updating camera targets...");
            SetCameraTargets();
            m_CameraControl.SetStartPositionAndSize();
        }
    }

    private IEnumerator SetTankColorAfterSpawn(TankColorSync colorSync, Color color)
    {
        // Wait a frame to ensure NetworkObject is fully spawned and NetworkBehaviour is initialized
        yield return null;
        
        if (colorSync != null && colorSync.IsSpawned)
        {
            colorSync.SetColor(color);
            Debug.Log($"Set tank color to {color} for spawned tank");
        }
        else
        {
            Debug.LogWarning("TankColorSync is not spawned yet, retrying...");
            // Retry after a short delay
            yield return new WaitForSeconds(0.1f);
            if (colorSync != null && colorSync.IsSpawned)
            {
                colorSync.SetColor(color);
                Debug.Log($"Set tank color to {color} for spawned tank (retry)");
            }
        }
    }

    private IEnumerator DelayedSetTankColor(ulong networkObjectId, Color color)
    {
        // Wait a frame to ensure NetworkObject is fully spawned
        yield return null;
        
        // Use ClientRpc to sync color to all clients
        SetTankColorClientRpc(networkObjectId, color, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = null } });
    }

    [ClientRpc]
    private void SetTankColorClientRpc(ulong networkObjectId, Color color, ClientRpcParams rpcParams = default)
    {
        // Fallback method: Apply color directly on all clients
        // This is used if TankColorSync component is not on the prefab
        NetworkObject networkObj = null;
        
        // Find the tank by NetworkObjectId
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out networkObj))
            {
                if (networkObj != null)
                {
                    MeshRenderer[] renderers = networkObj.GetComponentsInChildren<MeshRenderer>();
                    foreach (var renderer in renderers)
                    {
                        if (renderer != null && renderer.material != null)
                        {
                            renderer.material.color = color;
                        }
                    }
                    Debug.Log($"Applied color {color} via ClientRpc fallback to {networkObj.gameObject.name}");
                }
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        // Clean up
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (m_GameLoopCoroutine != null)
        {
            StopCoroutine(m_GameLoopCoroutine);
        }

        base.OnNetworkDespawn();
    }

    private void OnDestroy()
    {
        // Clean up
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
}

