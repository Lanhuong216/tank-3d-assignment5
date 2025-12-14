using System;
using UnityEngine;
using Unity.Netcode;

[Serializable]
public class TankManager
{
    // This class is to manage various settings on a tank.
    // It works with the GameManager class to control how the tanks behave
    // and whether or not players have control of their tank in the 
    // different phases of the game.

    public Color m_PlayerColor;                             // This is the color this tank will be tinted.
    public Transform m_SpawnPoint;                          // The position and direction the tank will have when it spawns.
    [HideInInspector] public int m_PlayerNumber;            // This specifies which player this the manager for.
    [HideInInspector] public string m_ColoredPlayerText;    // A string that represents the player with their number colored to match their tank.
    [HideInInspector] public GameObject m_Instance;         // A reference to the instance of the tank when it is created.
    [HideInInspector] public int m_Wins;                    // The number of wins this player has so far.


    private TankMovement m_Movement;                        // Reference to tank's movement script, used to disable and enable control.
    private TankShooting m_Shooting;                        // Reference to tank's shooting script, used to disable and enable control.
    private GameObject m_CanvasGameObject;                  // Used to disable the world space UI during the Starting and Ending phases of each round.


    public void Setup ()
    {
        if (m_Instance == null)
        {
            Debug.LogError("TankManager.Setup: m_Instance is null!");
            return;
        }

        // Get references to the components.
        m_Movement = m_Instance.GetComponent<TankMovement> ();
        m_Shooting = m_Instance.GetComponent<TankShooting> ();
        
        Canvas canvas = m_Instance.GetComponentInChildren<Canvas> ();
        if (canvas != null)
        {
            m_CanvasGameObject = canvas.gameObject;
        }

        // Set the player numbers to be consistent across the scripts.
        if (m_Movement != null)
            m_Movement.m_PlayerNumber = m_PlayerNumber;
        
        if (m_Shooting != null)
            m_Shooting.m_PlayerNumber = m_PlayerNumber;

        // Create a string using the correct color that says 'PLAYER 1' etc based on the tank's color and the player's number.
        m_ColoredPlayerText = "<color=#" + ColorUtility.ToHtmlStringRGB(m_PlayerColor) + ">PLAYER " + m_PlayerNumber + "</color>";

        // Get all of the renderers of the tank.
        MeshRenderer[] renderers = m_Instance.GetComponentsInChildren<MeshRenderer> ();

        // Go through all the renderers...
        for (int i = 0; i < renderers.Length; i++)
        {
            // ... set their material color to the color specific to this tank.
            renderers[i].material.color = m_PlayerColor;
        }
    }


    // Used during the phases of the game where the player shouldn't be able to control their tank.
    public void DisableControl ()
    {
        // For network tanks, ClientTankBehavior handles movement/shooting
        // We only need to disable canvas for UI visibility
        
        ClientTankBehavior clientBehavior = m_Instance != null ? m_Instance.GetComponent<ClientTankBehavior>() : null;
        
        if (clientBehavior != null)
        {
            // Network tank - only disable canvas
            if (m_CanvasGameObject != null)
            {
                NetworkObject networkObj = m_Instance.GetComponent<NetworkObject>();
                if (networkObj != null && networkObj.IsOwner)
                {
                    m_CanvasGameObject.SetActive (false);
                }
            }
        }
        else
        {
            // Non-network tank - disable normally
            if (m_Movement != null)
                m_Movement.enabled = false;
            
            if (m_Shooting != null)
                m_Shooting.enabled = false;

            if (m_CanvasGameObject != null)
                m_CanvasGameObject.SetActive (false);
        }
    }


    // Used during the phases of the game where the player should be able to control their tank.
    public void EnableControl ()
    {
        // For network tanks, ClientTankBehavior already handles enabling/disabling based on ownership
        // We only need to enable/disable canvas for UI visibility
        // Movement and Shooting are controlled by ClientTankBehavior based on IsOwner
        
        ClientTankBehavior clientBehavior = m_Instance != null ? m_Instance.GetComponent<ClientTankBehavior>() : null;
        
        if (clientBehavior != null)
        {
            // Network tank - ClientTankBehavior handles movement/shooting
            // Only enable canvas if this is the owner's tank
            if (m_CanvasGameObject != null)
            {
                // Check if this is owned by local client
                NetworkObject networkObj = m_Instance.GetComponent<NetworkObject>();
                if (networkObj != null && networkObj.IsOwner)
                {
                    m_CanvasGameObject.SetActive (true);
                }
            }
        }
        else
        {
            // Non-network tank (single player or server) - enable normally
            if (m_Movement != null)
                m_Movement.enabled = true;
            
            if (m_Shooting != null)
                m_Shooting.enabled = true;

            if (m_CanvasGameObject != null)
                m_CanvasGameObject.SetActive (true);
        }
    }


    // Used at the start of each round to put the tank into it's default state.
    public void Reset ()
    {
        if (m_Instance == null || m_SpawnPoint == null)
            return;

        // Reset position and rotation
        m_Instance.transform.position = m_SpawnPoint.position;
        m_Instance.transform.rotation = m_SpawnPoint.rotation;

        // Reset Rigidbody velocity and angular velocity (important for network physics)
        Rigidbody rb = m_Instance.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Reactivate tank (this ensures all components are reset)
        m_Instance.SetActive (false);
        m_Instance.SetActive (true);
        
        // Ensure tank is at spawn point after reactivation
        m_Instance.transform.position = m_SpawnPoint.position;
        m_Instance.transform.rotation = m_SpawnPoint.rotation;
    }
}