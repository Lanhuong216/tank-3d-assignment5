using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class ShellExplosion : NetworkBehaviour
{
    public LayerMask m_TankMask;                        // Used to filter what the explosion affects, this should be set to "Players".
    public ParticleSystem m_ExplosionParticles;         // Reference to the particles that will play on explosion.
    public AudioSource m_ExplosionAudio;                // Reference to the audio that will play on explosion.
    public float m_MaxDamage = 100f;                    // The amount of damage done if the explosion is centred on a tank.
    public float m_ExplosionForce = 1000f;              // The amount of force added to a tank at the centre of the explosion.
    public float m_MaxLifeTime = 2f;                    // The time in seconds before the shell is removed.
    public float m_ExplosionRadius = 5f;                // The maximum distance away from the explosion tanks can be and are still affected.


    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Schedule destruction after lifetime (only on server)
        if (IsServer)
        {
            StartCoroutine(DestroyAfterLifetime());
        }
    }

    private IEnumerator DestroyAfterLifetime()
    {
        yield return new WaitForSeconds(m_MaxLifeTime);
        
        if (IsServer)
        {
            NetworkObject networkObj = GetComponent<NetworkObject>();
            if (networkObj != null && networkObj.IsSpawned)
            {
                networkObj.Despawn(true);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }


    private bool m_HasExploded = false;

    private void OnTriggerEnter (Collider other)
    {
        // Only server processes explosion (prevents duplicate explosions)
        if (!IsServer)
        {
            return;
        }

        // Prevent multiple explosions from the same shell
        if (m_HasExploded)
        {
            return;
        }

        m_HasExploded = true;

        // Collect all the colliders in a sphere from the shell's current position to a radius of the explosion radius.
        Collider[] colliders = Physics.OverlapSphere (transform.position, m_ExplosionRadius, m_TankMask);

        // Go through all the colliders...
        for (int i = 0; i < colliders.Length; i++)
        {
            // ... and find their rigidbody.
            Rigidbody targetRigidbody = colliders[i].GetComponent<Rigidbody> ();

            // If they don't have a rigidbody, go on to the next collider.
            if (!targetRigidbody)
                continue;

            // Add an explosion force (applied on all clients via physics)
            targetRigidbody.AddExplosionForce (m_ExplosionForce, transform.position, m_ExplosionRadius);

            // Find the TankHealth script associated with the rigidbody.
            TankHealth targetHealth = targetRigidbody.GetComponent<TankHealth> ();

            // If there is no TankHealth script attached to the gameobject, go on to the next collider.
            if (!targetHealth)
                continue;

            // Calculate the amount of damage the target should take based on it's distance from the shell.
            float damage = CalculateDamage (targetRigidbody.position);

            // Deal this damage to the tank (only on server, syncs via NetworkVariable)
            targetHealth.TakeDamage (damage);
        }

        // Play explosion effects on all clients (including server)
        PlayExplosionEffectsClientRpc();

        // Destroy the shell on server
        NetworkObject networkObj = GetComponent<NetworkObject>();
        if (networkObj != null && networkObj.IsSpawned)
        {
            networkObj.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void PlayExplosionEffects()
    {
        // Unparent the particles from the shell.
        if (m_ExplosionParticles != null)
        {
            m_ExplosionParticles.transform.parent = null;
            m_ExplosionParticles.Play();
        }

        // Play the explosion sound effect.
        if (m_ExplosionAudio != null)
        {
            m_ExplosionAudio.Play();
        }

        // Once the particles have finished, destroy the gameobject they are on.
        if (m_ExplosionParticles != null)
        {
            Destroy (m_ExplosionParticles.gameObject, m_ExplosionParticles.duration);
        }
    }

    [ClientRpc]
    private void PlayExplosionEffectsClientRpc()
    {
        // Only play effects once per explosion
        // This is called from server, so all clients (including server) will play effects exactly once
        PlayExplosionEffects();
    }


    private float CalculateDamage (Vector3 targetPosition)
    {
        // Create a vector from the shell to the target.
        Vector3 explosionToTarget = targetPosition - transform.position;

        // Calculate the distance from the shell to the target.
        float explosionDistance = explosionToTarget.magnitude;

        // Calculate the proportion of the maximum distance (the explosionRadius) the target is away.
        float relativeDistance = (m_ExplosionRadius - explosionDistance) / m_ExplosionRadius;

        // Calculate damage as this proportion of the maximum possible damage.
        float damage = relativeDistance * m_MaxDamage;

        // Make sure that the minimum damage is always 0.
        damage = Mathf.Max (0f, damage);

        return damage;
    }
}