using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Network component to synchronize tank color across all clients
/// </summary>
public class TankColorSync : NetworkBehaviour
{
    // NetworkVariable to sync color (using Vector4 since Color is not directly supported)
    private NetworkVariable<Vector4> m_NetworkColor = new NetworkVariable<Vector4>(
        new Vector4(1f, 1f, 1f, 1f), // Default white
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private MeshRenderer[] m_Renderers;
    private bool m_ColorApplied = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Get all renderers
        m_Renderers = GetComponentsInChildren<MeshRenderer>();

        // Subscribe to color changes
        m_NetworkColor.OnValueChanged += OnColorChanged;

        // Apply current color if already set
        if (m_NetworkColor.Value != Vector4.zero)
        {
            ApplyColor(NetworkColorToColor(m_NetworkColor.Value));
        }
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe
        if (m_NetworkColor != null)
        {
            m_NetworkColor.OnValueChanged -= OnColorChanged;
        }

        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Set the tank color (should be called on server)
    /// </summary>
    public void SetColor(Color color)
    {
        if (!IsServer)
        {
            Debug.LogWarning("SetColor should only be called on server!");
            return;
        }

        m_NetworkColor.Value = ColorToNetworkColor(color);
        Debug.Log($"TankColorSync: Set color to {color} for tank {gameObject.name}");
    }

    private void OnColorChanged(Vector4 previousValue, Vector4 newValue)
    {
        Color color = NetworkColorToColor(newValue);
        ApplyColor(color);
    }

    private void ApplyColor(Color color)
    {
        if (m_Renderers == null)
        {
            m_Renderers = GetComponentsInChildren<MeshRenderer>();
        }

        if (m_Renderers != null && m_Renderers.Length > 0)
        {
            foreach (var renderer in m_Renderers)
            {
                if (renderer != null && renderer.material != null)
                {
                    renderer.material.color = color;
                }
            }
            m_ColorApplied = true;
            Debug.Log($"TankColorSync: Applied color {color} to {m_Renderers.Length} renderers on {gameObject.name}");
        }
    }

    // Helper methods to convert between Color and Vector4
    private Vector4 ColorToNetworkColor(Color color)
    {
        return new Vector4(color.r, color.g, color.b, color.a);
    }

    private Color NetworkColorToColor(Vector4 networkColor)
    {
        return new Color(networkColor.x, networkColor.y, networkColor.z, networkColor.w);
    }
}

