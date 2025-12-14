using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Network component to synchronize shell velocity across all clients
/// This ensures shell moves correctly on all clients
/// </summary>
public class ShellVelocitySync : NetworkBehaviour
{
    private Rigidbody m_Rigidbody;
    private bool m_VelocitySet = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        m_Rigidbody = GetComponent<Rigidbody>();
        
        if (m_Rigidbody == null)
        {
            Debug.LogError("ShellVelocitySync: Shell does not have Rigidbody component!");
        }
    }

    /// <summary>
    /// Set the shell velocity (should be called on server after spawn)
    /// </summary>
    public void SetVelocity(Vector3 velocity)
    {
        if (!IsServer)
        {
            Debug.LogWarning("SetVelocity should only be called on server!");
            return;
        }

        if (m_Rigidbody != null)
        {
            m_Rigidbody.linearVelocity = velocity;
            m_VelocitySet = true;
            
            // Sync velocity to all clients via ClientRpc
            SetVelocityClientRpc(velocity);
        }
    }

    [ClientRpc]
    private void SetVelocityClientRpc(Vector3 velocity)
    {
        // Apply velocity on all clients
        if (m_Rigidbody != null)
        {
            m_Rigidbody.linearVelocity = velocity;
            m_VelocitySet = true;
        }
    }

    private void FixedUpdate()
    {
        // Ensure velocity is maintained (in case physics resets it)
        // This is a safety measure, but NetworkTransform should handle position sync
        if (m_Rigidbody != null && m_VelocitySet && IsServer)
        {
            // NetworkTransform will sync position, so we don't need to maintain velocity
            // But we keep this for reference
        }
    }
}

