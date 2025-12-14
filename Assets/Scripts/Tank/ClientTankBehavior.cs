using UnityEngine;
using Unity.Netcode;

public class ClientTankBehavior : NetworkBehaviour
{
    [SerializeField]
    private TankMovement m_TankMovement;
    
    [SerializeField]
    private TankShooting m_TankShooting;
    
    [SerializeField]
    private TankHealth m_TankHealth;

    private void Awake()
    {
        // Always disable controls initially
        if (m_TankMovement != null)
            m_TankMovement.enabled = false;
        if (m_TankShooting != null)
            m_TankShooting.enabled = false;
        if (m_TankHealth != null)
            m_TankHealth.enabled = false;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // CRITICAL: Only enable controls for the owner
        // This ensures only the owner can control this tank
        if (IsOwner)
        {
            if (m_TankMovement != null)
                m_TankMovement.enabled = true;
            if (m_TankShooting != null)
                m_TankShooting.enabled = true;
            if (m_TankHealth != null)
                m_TankHealth.enabled = true;
        }
        else
        {
            // Explicitly disable for non-owners (safety check)
            if (m_TankMovement != null)
                m_TankMovement.enabled = false;
            if (m_TankShooting != null)
                m_TankShooting.enabled = false;
            // Health can stay enabled for all (to show damage)
            // if (m_TankHealth != null)
            //     m_TankHealth.enabled = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        // Disable all controls when tank is despawned
        if (m_TankMovement != null)
            m_TankMovement.enabled = false;
        if (m_TankShooting != null)
            m_TankShooting.enabled = false;
        
        base.OnNetworkDespawn();
    }

    // Safety check: Disable controls if ownership changes
    public override void OnGainedOwnership()
    {
        base.OnGainedOwnership();
        
        if (m_TankMovement != null)
            m_TankMovement.enabled = true;
        if (m_TankShooting != null)
            m_TankShooting.enabled = true;
        if (m_TankHealth != null)
            m_TankHealth.enabled = true;
    }

    public override void OnLostOwnership()
    {
        base.OnLostOwnership();
        
        // Disable controls when ownership is lost
        if (m_TankMovement != null)
            m_TankMovement.enabled = false;
        if (m_TankShooting != null)
            m_TankShooting.enabled = false;
    }
}
