using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Network component to synchronize tank position reset across all clients
/// </summary>
public class TankPositionSync : NetworkBehaviour
{
    private Rigidbody m_Rigidbody;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        m_Rigidbody = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// Reset tank position to spawn point (should be called on server)
    /// </summary>
    public void ResetPosition(Vector3 position, Quaternion rotation)
    {
        if (!IsServer)
        {
            Debug.LogWarning("ResetPosition should only be called on server!");
            return;
        }

        // Reset position and rotation on server
        transform.position = position;
        transform.rotation = rotation;

        // Reset Rigidbody velocity
        if (m_Rigidbody != null)
        {
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
        }

        // Sync position to all clients via ClientRpc
        ResetPositionClientRpc(position, rotation);
    }

    [ClientRpc]
    private void ResetPositionClientRpc(Vector3 position, Quaternion rotation)
    {
        // Apply position reset on all clients
        transform.position = position;
        transform.rotation = rotation;

        // Reset Rigidbody velocity on all clients
        if (m_Rigidbody != null)
        {
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
        }
    }
}

