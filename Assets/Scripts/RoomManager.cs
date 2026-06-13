using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// Handles ONLY player spawning after a room is joined.
/// Connection is handled by MainMenuUI.
///
/// FIXES:
///  - Respawn after death using a coroutine
///  - Spawn index uses PhotonNetwork.LocalPlayer.ActorNumber to avoid collisions
///    when players leave and rejoin (PlayerCount can go back to 1)
///  - Exposes a public Respawn() method so PlayerHealth can call it
/// </summary>
public class RoomManager : MonoBehaviourPunCallbacks
{
    [Header("Drag your player prefab here (must be inside Assets/Resources/)")]
    [SerializeField] private GameObject playerPrefab;

    [Header("Spawn Points (optional - add Transform objects from scene)")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Respawn")]
    [SerializeField] private float respawnDelay = 3f;

    /// <summary>Public so PlayerHealth can read it for the HUD countdown.</summary>
    public float RespawnDelay => respawnDelay;

    // Singleton-style so PlayerHealth can find it
    public static RoomManager Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        SpawnLocalPlayer();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning("[RoomManager] Disconnected: " + cause);
    }

    // ── Spawn / Respawn ───────────────────────────────────────────────────────
    public void SpawnLocalPlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("[RoomManager] playerPrefab is NULL! Drag the player prefab into RoomManager.");
            return;
        }

        // Use ActorNumber (unique per session, never reused) instead of PlayerCount
        int playerIndex = PhotonNetwork.LocalPlayer.ActorNumber - 1;

        Vector3    spawnPos = new Vector3(playerIndex * 3f, 1f, 0f);
        Quaternion spawnRot = Quaternion.identity;

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int idx  = playerIndex % spawnPoints.Length;
            spawnPos = spawnPoints[idx].position;
            spawnRot = spawnPoints[idx].rotation;
        }

        Debug.Log($"[RoomManager] Spawning player at {spawnPos}");
        PhotonNetwork.Instantiate(playerPrefab.name, spawnPos, spawnRot);
    }

    /// <summary>Called by PlayerHealth after local player dies.</summary>
    public void RequestRespawn()
    {
        StartCoroutine(RespawnRoutine());
    }

    private System.Collections.IEnumerator RespawnRoutine()
    {
        Debug.Log($"[RoomManager] Respawning in {respawnDelay}s...");
        yield return new WaitForSeconds(respawnDelay);
        SpawnLocalPlayer();
    }

    // ── Optional: called by ServerBrowserUI ──────────────────────────────────
    public void CreateCustomRoom(string roomName)
    {
        var options = new RoomOptions { MaxPlayers = 10, IsOpen = true, IsVisible = true };
        PhotonNetwork.CreateRoom(roomName, options);
    }
}