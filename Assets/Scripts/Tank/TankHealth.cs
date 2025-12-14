using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class TankHealth : NetworkBehaviour
{
    public float m_StartingHealth = 100f;          
    public Slider m_Slider;                        
    public Image m_FillImage;                      
    public Color m_FullHealthColor = Color.green;  
    public Color m_ZeroHealthColor = Color.red;    
    public GameObject m_ExplosionPrefab;
    
    // Network synchronized health
    private NetworkVariable<float> m_NetworkHealth = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    
    private AudioSource m_ExplosionAudio;          
    private ParticleSystem m_ExplosionParticles;   
    private bool m_Dead;            


    private void Awake()
    {
        m_ExplosionParticles = Instantiate(m_ExplosionPrefab).GetComponent<ParticleSystem>();
        m_ExplosionAudio = m_ExplosionParticles.GetComponent<AudioSource>();

        m_ExplosionParticles.gameObject.SetActive(false);
    }


    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Subscribe to health changes
        m_NetworkHealth.OnValueChanged += OnHealthChanged;
        
        // Initialize health on server
        if (IsServer)
        {
            m_NetworkHealth.Value = m_StartingHealth;
        }
        
        // Apply initial health
        OnHealthChanged(0f, m_NetworkHealth.Value);
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe
        if (m_NetworkHealth != null)
        {
            m_NetworkHealth.OnValueChanged -= OnHealthChanged;
        }
        
        base.OnNetworkDespawn();
    }

    private void OnEnable()
    {
        m_Dead = false;
        
        // Health is managed by NetworkVariable, so we don't set it here
        // It will be set when NetworkObject spawns
    }

    private void OnHealthChanged(float previousValue, float newValue)
    {
        // Update UI when health changes (called on all clients)
        SetHealthUI();
        
        // Check for death on server
        if (IsServer && newValue <= 0f && !m_Dead && previousValue > 0f)
        {
            OnDeath();
        }
    }

    public void TakeDamage(float amount)
    {
        // Only server can process damage
        if (!IsServer)
        {
            Debug.LogWarning("TakeDamage called on client! Damage should only be processed on server.");
            return;
        }
        
        // Adjust the tank's current health
        float newHealth = m_NetworkHealth.Value - amount;
        m_NetworkHealth.Value = Mathf.Max(0f, newHealth);
        
        // Death check is handled in OnHealthChanged callback
    }


    private void SetHealthUI()
    {
        if (m_Slider == null || m_FillImage == null)
            return;
            
        // Adjust the value and colour of the slider using network health
        float currentHealth = m_NetworkHealth.Value;
        m_Slider.value = currentHealth;
        m_FillImage.color = Color.Lerp(m_ZeroHealthColor, m_FullHealthColor, currentHealth / m_StartingHealth);
    }


    private void OnDeath()
    {
        if (!IsServer) return;
        
        // Play the effects for the death of the tank and deactivate it.
        m_Dead = true;
        
        // Play death effects on all clients
        OnDeathClientRpc();
        
        // Deactivate tank on server
        gameObject.SetActive(false);
    }

    [ClientRpc]
    private void OnDeathClientRpc()
    {
        // Play death effects on all clients
        if (m_ExplosionParticles != null)
        {
            m_ExplosionParticles.transform.position = transform.position;
            m_ExplosionParticles.gameObject.SetActive(true);
            m_ExplosionParticles.Play();
        }
        
        if (m_ExplosionAudio != null)
        {
            m_ExplosionAudio.Play();
        }
    }
    
    public float GetCurrentHealth()
    {
        return m_NetworkHealth.Value;
    }
    
    public bool IsDead()
    {
        return m_Dead || m_NetworkHealth.Value <= 0f;
    }

    /// <summary>
    /// Reset health to starting health (should be called on server when starting new round)
    /// </summary>
    public void ResetHealth()
    {
        if (!IsServer)
        {
            Debug.LogWarning("ResetHealth should only be called on server!");
            return;
        }

        // Reset health to starting value
        m_NetworkHealth.Value = m_StartingHealth;
        m_Dead = false;
        
        // Reactivate tank if it was dead
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
        
        Debug.Log($"Reset health for {gameObject.name} to {m_StartingHealth}");
    }
}