using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

/// <summary>
/// Helper script to ensure NetworkManager is properly configured for scene synchronization.
/// Attach this to NetworkManager GameObject or call VerifySceneConfiguration() manually.
/// </summary>
public class NetworkSceneHelper : MonoBehaviour
{
    [Header("Scene Configuration")]
    [Tooltip("List of scene names that should be synchronized across network")]
    public string[] NetworkScenes = { "WaitingRoom", "Main" };

    private void Start()
    {
        // Auto-verify when script starts
        VerifySceneConfiguration();
    }

    /// <summary>
    /// Verifies and configures NetworkManager for scene synchronization
    /// </summary>
    public void VerifySceneConfiguration()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager.Singleton is null! Cannot configure scene synchronization.");
            return;
        }

        // Check if SceneManager exists
        if (NetworkManager.Singleton.SceneManager == null)
        {
            Debug.LogError("NetworkSceneManager is null! Scene synchronization will not work.");
            Debug.LogError("Please ensure NetworkManager has Scene Management enabled in the inspector.");
            return;
        }

        Debug.Log("NetworkSceneManager is properly configured. Scene synchronization should work.");

        // Log current scene configuration
        var sceneManager = NetworkManager.Singleton.SceneManager;
        Debug.Log($"NetworkSceneManager is active. Current scene: {SceneManager.GetActiveScene().name}");
    }

    /// <summary>
    /// Safely loads a scene using NetworkSceneManager if available, otherwise falls back to SceneManager
    /// </summary>
    public static void LoadNetworkScene(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError($"NetworkManager is null! Loading scene '{sceneName}' locally.");
            SceneManager.LoadScene(sceneName, mode);
            return;
        }

        if (NetworkManager.Singleton.SceneManager == null)
        {
            Debug.LogError($"NetworkSceneManager is null! Loading scene '{sceneName}' locally.");
            Debug.LogError("Scene synchronization will not work. Please enable Scene Management in NetworkManager.");
            SceneManager.LoadScene(sceneName, mode);
            return;
        }

        // Use NetworkSceneManager to load scene (synchronizes across all clients)
        Debug.Log($"Loading scene '{sceneName}' using NetworkSceneManager (will sync to all clients)...");
        NetworkManager.Singleton.SceneManager.LoadScene(sceneName, mode);
    }

    /// <summary>
    /// Checks if NetworkManager is properly configured for scene synchronization
    /// </summary>
    public static bool IsSceneSynchronizationEnabled()
    {
        if (NetworkManager.Singleton == null)
            return false;

        return NetworkManager.Singleton.SceneManager != null;
    }
}

