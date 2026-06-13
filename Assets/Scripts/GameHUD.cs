using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// GameHUD — thin bridge between game logic and GameHUDController (the UIDocument controller).
///
/// This script is kept so that PlayerHealth can call SetHealthRatio without
/// knowing about UIDocument internals. All actual UI work is in GameHUDController.
///
/// Attach this script to the SAME GameObject as GameHUDController + UIDocument.
/// </summary>
public class GameHUD : MonoBehaviourPunCallbacks
{
    // Convenience accessor
    private GameHUDController Controller => GameHUDController.Instance;

    // ── Called by PlayerHealth ───────────────────────────────────────────────
    public void SetHealthRatio(float ratio)
    {
        Controller?.SetHealthRatio(ratio);

        // Flash vignette when health drops significantly
        if (ratio < 0.35f)
            Controller?.ShowDamageFlash();
    }

    // ── Called by PlayerHealth.TakeDamage ────────────────────────────────────
    public void OnTakeDamage()
    {
        Controller?.ShowDamageFlash();
    }

    // ── Called by RoomManager before respawn ─────────────────────────────────
    public void ShowRespawnCountdown(float seconds)
    {
        Controller?.ShowRespawnOverlay(seconds);
    }

    // ── Called by PlayerShoot on weapon switch ───────────────────────────────
    public void UpdateWeapon(int index, string name, int ammo, int reserve)
    {
        Controller?.SetWeaponDisplay(index, name, ammo, reserve);
    }

    // ── Called by PlayerShoot on kill ────────────────────────────────────────
    public void AddKillFeed(string killer, string victim)
    {
        bool involved = killer == PhotonNetwork.LocalPlayer.NickName ||
                        victim == PhotonNetwork.LocalPlayer.NickName;
        Controller?.AddKillFeedEntry(killer, victim, involved);
    }

    // ── Called by PlayerScope ────────────────────────────────────────────────
    public void SetScoped(bool scoped)
    {
        Controller?.SetScopeActive(scoped);
    }

    // ── Toast helper ─────────────────────────────────────────────────────────
    public void ShowToast(string msg)
    {
        Controller?.ShowToast(msg);
    }
}
