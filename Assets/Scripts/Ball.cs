using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class Ball : NetworkBehaviour
{
    private new Rigidbody rb;
    //pinball machines
    public Pinball Pinball1;
    public Pinball Pinball2;

    //adjustable spring force
    public int springforce = 10;

    //starting positions after scoring
    private Vector3 startingPositionPlayer1 = new Vector3(-1.13999999f, 1.97000003f, -0.800000012f);
    private Vector3 startingPositionPlayer2 = new Vector3(9.21000004f, 1.97000003f, -0.800000012f);

    //UI elements
    public TextMeshProUGUI player1Label;
    public TextMeshProUGUI player2Label;

    // Networked score variables
    private NetworkVariable<int> player1wins = new(
       0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int> player2wins = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    //Audio
    public AudioSource audioSource;

    //Awake is called when the script instance is being loaded, gets the Rigidbody component
    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    // OnNetworkSpawn is called when the object is spawned in the network, sets up physics and UI
    public override void OnNetworkSpawn()
    {
        // Server simulates ball physics, clients just render it
        rb.isKinematic = !IsServer;

        // Update UI when values change
        player1wins.OnValueChanged += (_, v) => UpdateLabel(player1Label, "Player 1: ", v);
        player2wins.OnValueChanged += (_, v) => UpdateLabel(player2Label, "Player 2: ", v);

        // Initialize UI on spawn
        UpdateLabel(player1Label, "Player 1: ", player1wins.Value);
        UpdateLabel(player2Label, "Player 2: ", player2wins.Value);
    }

    // OnNetworkDespawn is called when the object is despawned in the network, cleans up event handlers
    public override void OnNetworkDespawn()
    {
        player1wins.OnValueChanged -= (_, v) => UpdateLabel(player1Label, "Player 1: ", v);
        player2wins.OnValueChanged -= (_, v) => UpdateLabel(player2Label, "Player 2: ", v);
    }


    // Update is called once per frame
    void Update()
    {
        // Only the server should run scoring / teleports / resets
        if (!IsServer) return;

        // Check if the ball has fallen below the table (scoring)
        if (transform.position.y < 0.5f)
        {
            if (transform.position.x < 0)
            {
                player2wins.Value++;
                HandleScoreServer(2, player2wins.Value);
            }
            else
            {
                player1wins.Value++;
                HandleScoreServer(1, player1wins.Value);
            }
        }
        //Checks if ball reached upper edge and should be teleported to the other side
        if (transform.position.y > 15f)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            Vector3 newPosition = transform.position;
            if (transform.position.x < 0) newPosition.x += 12f;
            else newPosition.x -= 12f;

            newPosition.y = 15f;
            transform.position = newPosition;
        }
    }

    //Shoots the spring
    public void Shoot()
    {
        // Only the server should apply forces
        if (!IsServer) return;
        rb.AddForce(new Vector3(0, springforce, 0), ForceMode.VelocityChange);
    }

    // Handles scoring logic on the server, including updating the score and resetting the ball position
    private void HandleScoreServer(int playerNumber, int wins)
    {
        int randomUpgrade = Random.Range(1, 5);
        int randomPosition = Random.Range(1, 8);

        if (playerNumber == 1)
        {
            transform.position = startingPositionPlayer1;
            Pinball1.AddElement(randomUpgrade, randomPosition);
        }
        else
        {
            transform.position = startingPositionPlayer2;
            Pinball2.AddElement(randomUpgrade, randomPosition);
        }
        PlayScoreSoundClientRpc();
    }

    
    [ClientRpc]
    private void PlayScoreSoundClientRpc()
    {
        if (audioSource) audioSource.Play();
    }
    //Updates the Label with the new score
    private static void UpdateLabel(TextMeshProUGUI label, string prefix, int score)
    {
        if (!label) return;

        label.text = prefix + score.ToString();
    }

}
