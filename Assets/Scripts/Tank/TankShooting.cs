using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class TankShooting : NetworkBehaviour
{
    public int m_PlayerNumber = 1;              // Used to identify the different players.
    public Rigidbody m_Shell;                   // Prefab of the shell.
    public Transform m_FireTransform;           // A child of the tank where the shells are spawned.
    public Slider m_AimSlider;                  // A child of the tank that displays the current launch force.
    public AudioSource m_ShootingAudio;         // Reference to the audio source used to play the shooting audio. NB: different to the movement audio source.
    public AudioClip m_ChargingClip;            // Audio that plays when each shot is charging up.
    public AudioClip m_FireClip;                // Audio that plays when each shot is fired.
    public float m_MinLaunchForce = 15f;        // The force given to the shell if the fire button is not held.
    public float m_MaxLaunchForce = 30f;        // The force given to the shell if the fire button is held for the max charge time.
    public float m_MaxChargeTime = 0.75f;       // How long the shell can charge for before it is fired at max force.


    private string m_FireButton;                // The input axis that is used for launching shells.
    private float m_CurrentLaunchForce;         // The force that will be given to the shell when the fire button is released.
    private float m_ChargeSpeed;                // How fast the launch force increases, based on the max charge time.
    private bool m_Fired;                       // Whether or not the shell has been launched with this button press.


    private void OnEnable()
    {
        // When the tank is turned on, reset the launch force and the UI
        m_CurrentLaunchForce = m_MinLaunchForce;
        m_AimSlider.value = m_MinLaunchForce;
    }


    private void Start ()
    {
        // The fire axis is based on the player number.
        m_FireButton = "Fire" + m_PlayerNumber;

        // The rate that the launch force charges up is the range of possible forces by the max charge time.
        m_ChargeSpeed = (m_MaxLaunchForce - m_MinLaunchForce) / m_MaxChargeTime;
    }


    private void Update ()
    {
        // Only owner can control shooting
        if (!IsOwner)
            return;
            
        // The slider should have a default value of the minimum launch force.
        m_AimSlider.value = m_MinLaunchForce;

        // If the max force has been exceeded and the shell hasn't yet been launched...
        if (m_CurrentLaunchForce >= m_MaxLaunchForce && !m_Fired)
        {
            // ... use the max force and launch the shell.
            m_CurrentLaunchForce = m_MaxLaunchForce;
            FireServerRpc(m_CurrentLaunchForce);
        }
        // Otherwise, if the fire button has just started being pressed...
        else if (Input.GetButtonDown (m_FireButton))
        {
            // ... reset the fired flag and reset the launch force.
            m_Fired = false;
            m_CurrentLaunchForce = m_MinLaunchForce;

            // Change the clip to the charging clip and start it playing.
            if (m_ShootingAudio != null)
            {
                m_ShootingAudio.clip = m_ChargingClip;
                m_ShootingAudio.Play ();
            }
        }
        // Otherwise, if the fire button is being held and the shell hasn't been launched yet...
        else if (Input.GetButton (m_FireButton) && !m_Fired)
        {
            // Increment the launch force and update the slider.
            m_CurrentLaunchForce += m_ChargeSpeed * Time.deltaTime;

            m_AimSlider.value = m_CurrentLaunchForce;
        }
        // Otherwise, if the fire button is released and the shell hasn't been launched yet...
        else if (Input.GetButtonUp (m_FireButton) && !m_Fired)
        {
            // ... launch the shell.
            FireServerRpc(m_CurrentLaunchForce);
        }
    }


    [ServerRpc]
    private void FireServerRpc(float launchForce)
    {
        // Server spawns the shell and syncs to all clients
        Fire(launchForce);
    }

    private void Fire(float launchForce)
    {
        // Set the fired flag so only Fire is only called once.
        m_Fired = true;

        // Create an instance of the shell
        // m_Shell is a Rigidbody prefab, so we instantiate it and get the GameObject
        Rigidbody shellRigidbody = Instantiate(m_Shell, m_FireTransform.position, m_FireTransform.rotation);
        GameObject shellInstance = shellRigidbody.gameObject;
        
        if (shellRigidbody == null)
        {
            Debug.LogError("Shell prefab does not have Rigidbody component!");
            if (shellInstance != null)
                Destroy(shellInstance);
            return;
        }

        // Check if shell has NetworkObject component
        NetworkObject shellNetworkObject = shellInstance.GetComponent<NetworkObject>();
        if (shellNetworkObject != null)
        {
            // CRITICAL: Remove any nested NetworkObjects from shell before spawning
            // Unity Netcode doesn't support nested NetworkObjects in spawned prefabs
            // This must be done BEFORE calling Spawn()
            RemoveNestedNetworkObjects(shellInstance);
            
            // Double-check after removal
            if (HasNestedNetworkObjects(shellInstance))
            {
                Debug.LogError("Shell prefab still has nested NetworkObjects after removal! Please fix the Shell prefab by removing NetworkObject components from child objects.");
                Destroy(shellInstance);
                return;
            }
            
            // Spawn shell on network so all clients see it
            shellNetworkObject.Spawn();
            
            // CRITICAL: Set velocity AFTER spawning to ensure it syncs properly
            // Check if shell has ShellVelocitySync component
            ShellVelocitySync velocitySync = shellInstance.GetComponent<ShellVelocitySync>();
            if (velocitySync != null)
            {
                // Use ShellVelocitySync to sync velocity across network
                StartCoroutine(SetShellVelocityAfterSpawn(velocitySync, launchForce, m_FireTransform.forward));
            }
            else
            {
                // Fallback: Set velocity directly and hope NetworkTransform syncs position
                StartCoroutine(SetShellVelocityAfterSpawn(shellRigidbody, launchForce, m_FireTransform.forward));
                Debug.LogWarning("Shell prefab does not have ShellVelocitySync component! Shell movement may not sync correctly. Please add ShellVelocitySync component to Shell prefab.");
            }
        }
        else
        {
            // If no NetworkObject, just set velocity directly (only visible to server)
            shellRigidbody.linearVelocity = launchForce * m_FireTransform.forward;
            Debug.LogWarning("Shell prefab does not have NetworkObject component! Shell will only be visible to server.");
        }

        // Play firing sound on all clients
        FireClientRpc();

        // Reset the launch force.  This is a precaution in case of missing button events.
        m_CurrentLaunchForce = m_MinLaunchForce;
    }

    private void RemoveNestedNetworkObjects(GameObject obj)
    {
        // Remove NetworkObject components from all children
        NetworkObject[] nestedNetworkObjects = obj.GetComponentsInChildren<NetworkObject>();
        foreach (var nestedObj in nestedNetworkObjects)
        {
            // Skip the root NetworkObject
            if (nestedObj.gameObject == obj)
                continue;
                
            Debug.LogWarning($"Removing nested NetworkObject from {nestedObj.gameObject.name} in Shell prefab. This should be fixed in the prefab itself.");
            Destroy(nestedObj);
        }
    }

    private bool HasNestedNetworkObjects(GameObject obj)
    {
        NetworkObject rootNetworkObject = obj.GetComponent<NetworkObject>();
        if (rootNetworkObject == null)
            return false;
            
        NetworkObject[] allNetworkObjects = obj.GetComponentsInChildren<NetworkObject>();
        // If there's more than one NetworkObject, we have nested ones
        return allNetworkObjects.Length > 1;
    }

    private System.Collections.IEnumerator SetShellVelocityAfterSpawn(ShellVelocitySync velocitySync, float launchForce, Vector3 direction)
    {
        // Wait a frame to ensure NetworkObject is fully spawned
        yield return null;
        
        if (velocitySync != null && velocitySync.IsSpawned)
        {
            Vector3 velocity = launchForce * direction;
            velocitySync.SetVelocity(velocity);
        }
    }

    private System.Collections.IEnumerator SetShellVelocityAfterSpawn(Rigidbody shellRigidbody, float launchForce, Vector3 direction)
    {
        // Fallback method if ShellVelocitySync is not available
        // Wait a frame to ensure NetworkObject is fully spawned
        yield return null;
        
        if (shellRigidbody != null && shellRigidbody.gameObject != null)
        {
            NetworkObject networkObj = shellRigidbody.GetComponent<NetworkObject>();
            if (networkObj != null && networkObj.IsSpawned)
            {
                // Set velocity on server - NetworkTransform will sync position to clients
                shellRigidbody.linearVelocity = launchForce * direction;
            }
            else
            {
                // Fallback: set velocity directly if not spawned yet
                shellRigidbody.linearVelocity = launchForce * direction;
            }
        }
    }

    [ClientRpc]
    private void FireClientRpc()
    {
        // Play firing sound on all clients
        if (m_ShootingAudio != null)
        {
            m_ShootingAudio.clip = m_FireClip;
            m_ShootingAudio.Play();
        }
    }
}