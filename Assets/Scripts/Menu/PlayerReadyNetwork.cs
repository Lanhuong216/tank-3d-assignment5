using UnityEngine;
using Unity.Netcode;

public class PlayerReadyNetwork : NetworkBehaviour
{
    private NetworkVariable<bool> m_IsReady = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool IsReady => m_IsReady.Value;
    
    // Player name is calculated locally based on ClientId, not synced via NetworkVariable
    // because Unity Netcode cannot serialize string in NetworkVariable
    public string GetPlayerName()
    {
        // Check if this is the host (server client)
        if (NetworkManager.Singleton != null && OwnerClientId == NetworkManager.ServerClientId)
        {
            return "Host";
        }
        return "Player " + OwnerClientId;
    }

    public void SetReady(bool ready)
    {
        // With WritePermission.Owner, the owner can write directly to NetworkVariable
        // No need for ServerRpc since the owner has write permission
        if (IsOwner)
        {
            m_IsReady.Value = ready;
        }
    }
}
