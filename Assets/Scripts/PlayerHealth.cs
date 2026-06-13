using UnityEngine;
using Photon.Pun;

/// <summary>
/// Manages player health, medkit usage, and death/respawn flow.
///
/// FEATURES:
///  - TakeDamage only runs on the owner (avoids double damage)
///  - Die() notifies TeamManager of the kill, shows respawn countdown, requests respawn
///  - UseMedkit() heals 60 HP (up to max), decrements medkit count, updates HUD
///  - On start: pushes health and medkit count to HUD
/// </summary>
public class PlayerHealth : MonoBehaviourPun
{
    [SerializeField] private float maxHealth = 100f;

    private float currentHealth;
    private bool  isDead = false;
    private GameHUD gameHUD;

    // ── Medkit ────────────────────────────────────────────────────────────────
    private int medkitsLeft = 2;

    private void Start()
    {
        currentHealth = maxHealth;

        if (photonView.IsMine)
        {
            gameHUD = Object.FindFirstObjectByType<GameHUD>();
            PushHealthToHUD();

            // Push initial medkit count
            if (gameHUD != null)
                GameHUDController.Instance?.UpdateMedkitCount(medkitsLeft);

            // Subscribe medkit button event
            if (GameHUDController.Instance != null)
                GameHUDController.Instance.OnMedkitPressed += UseMedkit;
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe from HUD events to avoid dangling references
        if (photonView.IsMine && GameHUDController.Instance != null)
            GameHUDController.Instance.OnMedkitPressed -= UseMedkit;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Public API
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Call this RPC on the VICTIM's PhotonView.
    /// Only the victim's machine actually applies damage.
    /// </summary>
    [PunRPC]
    public void TakeDamage(float damageAmount)
    {
        // Only the owner processes damage - prevents double-counting
        if (!photonView.IsMine) return;
        if (isDead) return;

        currentHealth = Mathf.Max(currentHealth - damageAmount, 0f);
        Debug.Log($"[Health] {photonView.Owner?.NickName} took {damageAmount} dmg. HP = {currentHealth}");

        PushHealthToHUD();

        // Flash damage vignette on the HUD
        if (gameHUD != null) gameHUD.OnTakeDamage();

        if (currentHealth <= 0f)
        {
            isDead = true;
            Die();
        }
    }

    /// <summary>Use a medkit to restore 60 HP (if available and not full).</summary>
    public void UseMedkit()
    {
        if (!photonView.IsMine) return;
        if (medkitsLeft <= 0) return;
        if (currentHealth >= maxHealth) return;

        currentHealth = Mathf.Min(currentHealth + 60f, maxHealth);
        medkitsLeft--;

        PushHealthToHUD();

        // Update medkit count on HUD
        GameHUDController.Instance?.UpdateMedkitCount(medkitsLeft);

        // Show toast
        GameHUDController.Instance?.ShowToast($"Medkit used! ({medkitsLeft} left)");

        Debug.Log($"[Health] Medkit used. HP = {currentHealth}, Medkits left = {medkitsLeft}");
    }

    /// <summary>Force-set health from outside (e.g. on respawn).</summary>
    public void ResetHealth()
    {
        currentHealth = maxHealth;
        isDead = false;
        medkitsLeft = 2;
        PushHealthToHUD();

        if (photonView.IsMine)
            GameHUDController.Instance?.UpdateMedkitCount(medkitsLeft);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Private helpers
    // ═══════════════════════════════════════════════════════════════════════════

    private void PushHealthToHUD()
    {
        if (gameHUD == null) gameHUD = Object.FindFirstObjectByType<GameHUD>();
        if (gameHUD != null)
            gameHUD.SetHealthRatio(currentHealth / maxHealth);
    }

    private void Die()
    {
        Debug.Log($"[Health] {photonView.Owner?.NickName} DIED");

        if (photonView.IsMine)
        {
            // Notify TeamManager — use the local player's team as the "other" team got the kill
            // Since we don't track who shot us, we register a kill for the opposing team
            string myTeam = TeamManager.Instance?.GetLocalPlayerTeam() ?? "A";
            string killerTeam = (myTeam == "A") ? "B" : "A";
            TeamManager.Instance?.RegisterKill(killerTeam);

            float delay = RoomManager.Instance != null ? RoomManager.Instance.RespawnDelay : 3f;

            // Update medkit count on HUD before destroy
            GameHUDController.Instance?.UpdateMedkitCount(medkitsLeft);

            // Show respawn countdown on HUD
            if (gameHUD != null) gameHUD.ShowRespawnCountdown(delay);

            // Notify room manager to schedule respawn BEFORE destroying
            if (RoomManager.Instance != null)
                RoomManager.Instance.RequestRespawn();

            PhotonNetwork.Destroy(gameObject);
        }
    }
}