using System.Collections;
using UnityEngine;
using Photon.Pun;
using ExitGames.Client.Photon;

/// <summary>
/// Manages Team A / Team B assignment, kill counts, and the configurable match timer.
/// Odd ActorNumbers = Team A, Even = Team B.
/// Must be placed on a scene GameObject with a PhotonView component.
///
/// NEW:
///  - Match duration is read from Room Custom Properties ("matchTime" key in seconds).
///    This is set by MainMenuUI when creating the room, so each room can have its own timer.
///  - After match ends, broadcasts RPC_EndMatch which also tells MainMenuUI to re-show.
///  - ResetMatch() allows reuse without a scene reload.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class TeamManager : MonoBehaviourPunCallbacks
{
    // ── Singleton ──────────────────────────────────────────────────────────────
    public static TeamManager Instance { get; private set; }

    // ── Match state ────────────────────────────────────────────────────────────
    private int   teamAKills    = 0;
    private int   teamBKills    = 0;
    private float timeRemaining = 300f; // fallback; overwritten from room props
    private bool  matchEnded    = false;

    // ── Sync interval ──────────────────────────────────────────────────────────
    private const float SyncInterval = 5f;

    // Custom property key used across all scripts
    public const string PropMatchTime = "matchTime";

    // ═══════════════════════════════════════════════════════════════════════════
    // Unity lifecycle
    // ═══════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // NOTE: Start() fires when the scene loads — at that moment the player
        // may NOT yet be in a room (still on the menu). So we do NOT read room
        // props here. Instead we do it inside OnJoinedRoom() below which fires
        // reliably after room custom properties are available.
        // We just set a safe default in case OnJoinedRoom never fires.
        timeRemaining = 300f;
    }

    // ── Photon: joined a room ─────────────────────────────────────────────────
    public override void OnJoinedRoom()
    {
        // Reset match state for a fresh start in the new room
        teamAKills = 0;
        teamBKills = 0;
        matchEnded = false;
        
        // Read match duration from room custom properties (set by host in MainMenuUI)
        // This is the correct place — room props are guaranteed available here.
        object val;
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(PropMatchTime, out val))
        {
            // Photon serialises int as int — safe double-cast
            if (val is int intVal)
                timeRemaining = (float)intVal;
            else if (val is float floatVal)
                timeRemaining = floatVal;
            else
                timeRemaining = 300f;

            Debug.Log($"[TeamManager] Match time from room props: {timeRemaining}s");
        }
        else
        {
            timeRemaining = 300f;
            Debug.Log("[TeamManager] No matchTime in room props — defaulting to 300s.");
        }

        // Push initial timer to HUD
        GameHUDController.Instance?.UpdateMatchTimer(timeRemaining);

        // Only the MasterClient drives the timer
        if (PhotonNetwork.IsMasterClient)
        {
            StopAllCoroutines();
            StartCoroutine(SyncStateCoroutine());
        }
    }

    public override void OnLeftRoom()
    {
        // Immediately stop logic when we leave
        matchEnded = true;
        StopAllCoroutines();
    }

    private void Update()
    {
        if (!PhotonNetwork.IsMasterClient || matchEnded) return;
        if (!PhotonNetwork.InRoom) return;

        if (timeRemaining > 0f)
        {
            timeRemaining -= Time.deltaTime;
            if (timeRemaining <= 0f)
            {
                timeRemaining = 0f;
                EndMatch();
            }
        }

        // Broadcast timer update to HUD locally on master
        GameHUDController.Instance?.UpdateMatchTimer(timeRemaining);
        GameHUDController.Instance?.UpdateTeamScore(teamAKills, teamBKills);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Public API
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Returns "A" or "B" for the local player based on ActorNumber parity.</summary>
    public string GetLocalPlayerTeam()
    {
        int actor = PhotonNetwork.LocalPlayer.ActorNumber;
        return (actor % 2 == 1) ? "A" : "B";
    }

    /// <summary>
    /// Call when a player dies. killerTeam = "A" or "B".
    /// Should only be called on the local machine (photonView.IsMine check in caller).
    /// </summary>
    public void RegisterKill(string killerTeam)
    {
        if (matchEnded) return;

        // Send kill RPC to master to register authoritatively
        photonView.RPC("RPC_RegisterKill", RpcTarget.MasterClient, killerTeam);
    }

    /// <summary>
    /// Resets match state. Called when host starts a rematch without scene reload.
    /// </summary>
    public void ResetMatch()
    {
        teamAKills    = 0;
        teamBKills    = 0;
        matchEnded    = false;

        // Reload timer from room props in case it changed
        if (PhotonNetwork.InRoom)
        {
            object val;
            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(PropMatchTime, out val))
                timeRemaining = (float)(int)val;
            else
                timeRemaining = 300f;
        }

        GameHUDController.Instance?.UpdateTeamScore(0, 0);
        GameHUDController.Instance?.UpdateMatchTimer(timeRemaining);

        if (PhotonNetwork.IsMasterClient)
            StartCoroutine(SyncStateCoroutine());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RPCs
    // ═══════════════════════════════════════════════════════════════════════════

    [PunRPC]
    private void RPC_RegisterKill(string killerTeam)
    {
        // Only master client processes kills authoritatively
        if (!PhotonNetwork.IsMasterClient) return;
        if (matchEnded) return;

        if (killerTeam == "A")
            teamAKills++;
        else if (killerTeam == "B")
            teamBKills++;

        // Immediately sync state to all clients
        photonView.RPC("SyncState", RpcTarget.All, teamAKills, teamBKills, timeRemaining);
    }

    [PunRPC]
    public void SyncState(int a, int b, float time)
    {
        teamAKills    = a;
        teamBKills    = b;
        timeRemaining = time;

        GameHUDController.Instance?.UpdateTeamScore(teamAKills, teamBKills);
        GameHUDController.Instance?.UpdateMatchTimer(timeRemaining);
    }

    [PunRPC]
    private void RPC_EndMatch(string winnerTeam)
    {
        matchEnded = true;
        string winnerName = (winnerTeam == "A") ? "TEAM A" : (winnerTeam == "B") ? "TEAM B" : "DRAW";
        GameHUDController.Instance?.ShowMatchOver(winnerName, teamAKills, teamBKills);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Photon callbacks
    // ═══════════════════════════════════════════════════════════════════════════

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        // If this client just became the master, take over timer responsibility
        if (PhotonNetwork.IsMasterClient && !matchEnded)
        {
            StartCoroutine(SyncStateCoroutine());
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Private helpers
    // ═══════════════════════════════════════════════════════════════════════════

    private void EndMatch()
    {
        matchEnded = true;
        string winner;
        if      (teamAKills > teamBKills) winner = "A";
        else if (teamBKills > teamAKills) winner = "B";
        else                              winner = "DRAW";

        photonView.RPC("RPC_EndMatch", RpcTarget.All, winner);
    }

    private IEnumerator SyncStateCoroutine()
    {
        while (!matchEnded)
        {
            yield return new WaitForSeconds(SyncInterval);
            if (PhotonNetwork.InRoom && photonView != null)
            {
                photonView.RPC("SyncState", RpcTarget.All, teamAKills, teamBKills, timeRemaining);
            }
        }
    }
}
