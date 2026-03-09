using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class Ball : NetworkBehaviour
{
    private new Rigidbody rb;

    public Pinball Pinball1;
    public Pinball Pinball2;

    public int springforce = 10;

    private Vector3 startingPositionPlayer1 = new Vector3(-1.13999999f, 1.97000003f, -0.800000012f);
    private Vector3 startingPositionPlayer2 = new Vector3(9.21000004f, 1.97000003f, -0.800000012f);
   
    public TextMeshProUGUI player1Label;
    public TextMeshProUGUI player2Label;

    private NetworkVariable<int> player1wins = new(
       0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int> player2wins = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public AudioSource audioSource;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        // Server simulates ball physics, clients just render it
        rb.isKinematic = !IsServer;

        // Update UI when values change (clients + server)
        player1wins.OnValueChanged += (_, v) => UpdateLabel(player1Label, "Player 1: ", v);
        player2wins.OnValueChanged += (_, v) => UpdateLabel(player2Label, "Player 2: ", v);

        // Initialize UI on spawn
        UpdateLabel(player1Label, "Player 1: ", player1wins.Value);
        UpdateLabel(player2Label, "Player 2: ", player2wins.Value);
    }

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

    public void Shoot()
    {
        if (!IsServer) return;
        rb.AddForce(new Vector3(0, springforce, 0), ForceMode.VelocityChange);
    }

    private void HandleScoreServer(int playerNumber, int wins)
    {
        int randomUpgrade = Random.Range(1, 5);
        int randomPosition = Random.Range(1, 8);

        if (playerNumber == 1)
        {
            transform.position = startingPositionPlayer1;

            // IMPORTANT: this needs to be replicated too (see section 3)
            Pinball1.AddElement(randomUpgrade, randomPosition);
        }
        else
        {
            transform.position = startingPositionPlayer2;

            // IMPORTANT: this needs to be replicated too (see section 3)
            Pinball2.AddElement(randomUpgrade, randomPosition);
        }

        PlayScoreSoundClientRpc();
    }


    [ClientRpc]
    private void PlayScoreSoundClientRpc()
    {
        if (audioSource) audioSource.Play();
    }

    private static void UpdateLabel(TextMeshProUGUI label, string prefix, int score)
    {
        if (!label) return;

        label.text = prefix + score.ToString();
    }

}
