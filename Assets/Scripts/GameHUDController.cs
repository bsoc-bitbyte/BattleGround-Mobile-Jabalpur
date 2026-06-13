using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// GameHUDController — handles all HUD visuals and mobile input events.
///
/// NEW in this version:
///  - TeamScorePanel / timer / match-over overlay
///  - Medkit count badge
///  - SwipeZone replaces right joystick for free-look on mobile
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class GameHUDController : MonoBehaviourPunCallbacks
{
    [Header("Kill feed settings")]
    [SerializeField] private float killFeedEntryLifetime = 4f;
    [SerializeField] private int   maxKillFeedEntries    = 4;

    // ── References injected by PlayerSetup after spawn ────────────────────────
    private PlayerScope playerScope;

    // ── UI Element cache ──────────────────────────────────────────────────────
    private VisualElement root;

    private Label         roomCodeLabel;
    private Label         playerCountLabel;
    private Label         weaponNameLabel;
    private Label         ammoCountLabel;
    private Label         ammoReserveLabel;
    private VisualElement killFeedPanel;
    private VisualElement crosshair;
    private VisualElement scopeOverlay;
    private VisualElement damageVignette;
    private Label         healthValueLabel;
    private VisualElement healthFill;
    private VisualElement respawnOverlay;
    private Label         respawnTimerLabel;
    private VisualElement toastPanel;
    private Label         toastLabel;

    // ── Team / Timer / Match Over UI ──────────────────────────────────────────
    private VisualElement teamScorePanel;
    private Label         teamALabel;
    private Label         teamBLabel;
    private Label         matchTimerLabel;
    private VisualElement matchOverlay;
    private Label         matchOverlayTitle;
    private Label         matchOverlayDetail;
    private Label         medkitCountLabel;

    // ── State ─────────────────────────────────────────────────────────────────
    private Coroutine vignetteCoroutine;
    private Coroutine toastCoroutine;

    // ── Mobile Inputs ─────────────────────────────────────────────────────────
    public event Action OnJumpPressed;
    public event Action OnFirePressed;
    public event Action OnFireReleased;
    public event Action OnReloadPressed;
    public event Action OnGrenadePressed;
    public event Action OnMedkitPressed;
    public event Action OnMGunPressed;
    public event Action OnSGunPressed;
    public event Action OnZoomPressed;

    public Vector2 MoveInput { get; private set; }
    public Vector2 LookInput { get; private set; }

    // ── Left joystick (retained) ──────────────────────────────────────────────
    private VisualElement leftZone, leftKnob;
    private int   leftPointerId  = -1;
    private Vector2 leftStartPos;

    // ── Swipe zone (replaces right joystick) ─────────────────────────────────
    private VisualElement swipeZone;
    private int   swipePointerId = -1;
    private Vector2 swipeLastPos;
    // NO pre-multiplier here — raw pixel delta is passed to AdvancedPlayerController
    // which normalises by screen height and applies mobileLookSensitivity there.

    // ── Singleton ─────────────────────────────────────────────────────────────
    public static GameHUDController Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        var doc = GetComponent<UIDocument>();
        if (doc == null) { Debug.LogError("[GameHUDController] No UIDocument!"); return; }

        root = doc.rootVisualElement;
        QueryElements();

        RefreshRoomInfo();
        SetHealthRatio(1f);
        SetWeaponDisplay(0, "RIFLE", 30, 90);

        SetVisible(scopeOverlay,   false);
        SetVisible(damageVignette, false);
        SetVisible(respawnOverlay, false);
        SetVisible(toastPanel,     false);
        SetVisible(matchOverlay,   false);
    }

    public void SetPlayerScope(PlayerScope scope) => playerScope = scope;

    private void QueryElements()
    {
        roomCodeLabel    = root.Q<Label>("RoomCodeLabel");
        playerCountLabel = root.Q<Label>("PlayerCountLabel");
        weaponNameLabel  = root.Q<Label>("WeaponNameLabel");
        ammoCountLabel   = root.Q<Label>("AmmoCount");
        ammoReserveLabel = root.Q<Label>("AmmoReserve");
        killFeedPanel    = root.Q<VisualElement>("KillFeed");
        crosshair        = root.Q<VisualElement>("Crosshair");
        scopeOverlay     = root.Q<VisualElement>("ScopeOverlay");
        damageVignette   = root.Q<VisualElement>("DamageVignette");
        healthValueLabel = root.Q<Label>("HealthValueLabel");
        healthFill       = root.Q<VisualElement>("HealthFill");
        respawnOverlay   = root.Q<VisualElement>("RespawnOverlay");
        respawnTimerLabel= root.Q<Label>("RespawnTimer");
        toastPanel       = root.Q<VisualElement>("ToastPanel");
        toastLabel       = root.Q<Label>("ToastLabel");

        // New team/timer/match-over elements
        teamScorePanel    = root.Q<VisualElement>("TeamScorePanel");
        teamALabel        = root.Q<Label>("TeamALabel");
        teamBLabel        = root.Q<Label>("TeamBLabel");
        matchTimerLabel   = root.Q<Label>("MatchTimerLabel");
        matchOverlay      = root.Q<VisualElement>("MatchOverlay");
        matchOverlayTitle = root.Q<Label>("MatchOverlayTitle");
        matchOverlayDetail= root.Q<Label>("MatchOverlayDetail");
        medkitCountLabel  = root.Q<Label>("MedkitCountLabel");

        BindMobileControls();
    }

    private void BindMobileControls()
    {
        // ── Buttons ──
        var btnJump = root.Q<Label>("BtnJump");
        if (btnJump != null) btnJump.RegisterCallback<PointerDownEvent>(e => OnJumpPressed?.Invoke());

        var btnFire = root.Q<Label>("BtnFire");
        if (btnFire != null)
        {
            btnFire.RegisterCallback<PointerDownEvent>(e => {
                btnFire.CapturePointer(e.pointerId);
                OnFirePressed?.Invoke();
            });
            Action<IPointerEvent> fireUp = e => {
                if (btnFire.HasPointerCapture(e.pointerId))
                {
                    btnFire.ReleasePointer(e.pointerId);
                    OnFireReleased?.Invoke();
                }
            };
            btnFire.RegisterCallback<PointerUpEvent>(e => fireUp(e));
            btnFire.RegisterCallback<PointerCancelEvent>(e => fireUp(e));
        }

        var btnReload = root.Q<Label>("BtnReload");
        if (btnReload != null) btnReload.RegisterCallback<PointerDownEvent>(e => OnReloadPressed?.Invoke());

        var btnGrenade = root.Q<Label>("BtnGrenade");
        if (btnGrenade != null) btnGrenade.RegisterCallback<PointerDownEvent>(e => OnGrenadePressed?.Invoke());

        var btnMedkit = root.Q<Label>("BtnMedkit");
        if (btnMedkit != null) btnMedkit.RegisterCallback<PointerDownEvent>(e => OnMedkitPressed?.Invoke());

        var btnMGun = root.Q<Label>("BtnMGun");
        if (btnMGun != null) btnMGun.RegisterCallback<PointerDownEvent>(e => OnMGunPressed?.Invoke());

        var btnSGun = root.Q<Label>("BtnSGun");
        if (btnSGun != null) btnSGun.RegisterCallback<PointerDownEvent>(e => OnSGunPressed?.Invoke());

        var btnZoom = root.Q<Label>("BtnZoom");
        if (btnZoom != null) btnZoom.RegisterCallback<PointerDownEvent>(e => OnZoomPressed?.Invoke());

        // ── Left Joystick ──
        leftZone = root.Q<VisualElement>("JoystickLeftZone");
        leftKnob = root.Q<VisualElement>("JoystickLeftKnob");

        if (leftZone != null)
        {
            leftZone.RegisterCallback<PointerDownEvent>(e => {
                if (leftPointerId != -1) return;
                leftPointerId = e.pointerId;
                leftStartPos  = e.position;
                leftZone.CapturePointer(e.pointerId);
            });
            leftZone.RegisterCallback<PointerMoveEvent>(e => {
                if (e.pointerId != leftPointerId) return;
                Vector2 delta   = (Vector2)e.position - leftStartPos;
                float   maxDist = 80f;
                if (delta.magnitude > maxDist) delta = delta.normalized * maxDist;
                if (leftKnob != null)
                    leftKnob.style.translate = new StyleTranslate(new Translate(delta.x, delta.y, 0));
                MoveInput = new Vector2(delta.x / maxDist, -delta.y / maxDist);
            });
            Action<IPointerEvent> releaseLeft = e => {
                if (e.pointerId != leftPointerId) return;
                leftPointerId = -1;
                MoveInput     = Vector2.zero;
                if (leftKnob != null)
                    leftKnob.style.translate = new StyleTranslate(new Translate(0, 0, 0));
                leftZone.ReleasePointer(e.pointerId);
            };
            leftZone.RegisterCallback<PointerUpEvent>(e => releaseLeft(e));
            leftZone.RegisterCallback<PointerCancelEvent>(e => releaseLeft(e));
        }

        // ── Swipe Zone (right 50% → free look) ──
        swipeZone = root.Q<VisualElement>("SwipeZone");

        if (swipeZone != null)
        {
            swipeZone.RegisterCallback<PointerDownEvent>(e => {
                if (swipePointerId != -1) return;
                swipePointerId = e.pointerId;
                swipeLastPos   = e.position;
                swipeZone.CapturePointer(e.pointerId);
            });
            swipeZone.RegisterCallback<PointerMoveEvent>(e => {
                if (e.pointerId != swipePointerId) return;
                Vector2 delta = (Vector2)e.position - swipeLastPos;
                swipeLastPos  = e.position;
                // Raw pixel delta — AdvancedPlayerController normalises by Screen.height
                // Negative Y because UI Y goes down, world Y goes up
                LookInput = new Vector2(delta.x, -delta.y);
            });
            Action<IPointerEvent> releaseSwipe = e => {
                if (e.pointerId != swipePointerId) return;
                swipePointerId = -1;
                LookInput      = Vector2.zero;
                swipeZone.ReleasePointer(e.pointerId);
            };
            swipeZone.RegisterCallback<PointerUpEvent>(e => releaseSwipe(e));
            swipeZone.RegisterCallback<PointerCancelEvent>(e => releaseSwipe(e));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Public API — existing
    // ═══════════════════════════════════════════════════════════════════════════

    public void ConsumeLookInput()
    {
        LookInput = Vector2.zero;
    }

    public void SetHealthRatio(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        int hp = Mathf.RoundToInt(ratio * 100f);

        if (healthValueLabel != null) healthValueLabel.text = hp.ToString();

        if (healthFill != null)
        {
            healthFill.style.width = new StyleLength(
                new Length(ratio * 100f, LengthUnit.Percent));

            Color fill;
            if (ratio > 0.5f)
                fill = Color.Lerp(new Color(1f, 0.75f, 0f),
                                  new Color(0.18f, 0.85f, 0.27f),
                                  (ratio - 0.5f) * 2f);
            else
                fill = Color.Lerp(new Color(0.9f, 0.15f, 0.1f),
                                  new Color(1f, 0.75f, 0f),
                                  ratio * 2f);

            healthFill.style.backgroundColor = new StyleColor(fill);
        }
    }

    public void ShowDamageFlash()
    {
        if (vignetteCoroutine != null) StopCoroutine(vignetteCoroutine);
        vignetteCoroutine = StartCoroutine(DamageVignetteRoutine());
    }

    public void SetScopeActive(bool active)
    {
        SetVisible(scopeOverlay, active);
        SetVisible(crosshair, !active);
    }

    public void SetWeaponDisplay(int weaponIndex, string weaponName, int ammo, int reserve)
    {
        if (weaponNameLabel  != null) weaponNameLabel.text  = weaponName;
        if (ammoCountLabel   != null) ammoCountLabel.text   = ammo.ToString();
        if (ammoReserveLabel != null) ammoReserveLabel.text = $"/ {reserve}";
    }

    /// <summary>Alias used by PlayerShoot for ammo display.</summary>
    public void UpdateWeapon(int weaponIndex, string weaponName, int ammo, int reserve)
        => SetWeaponDisplay(weaponIndex, weaponName, ammo, reserve);

    public void AddKillFeedEntry(string killer, string victim, bool localInvolved)
    {
        if (killFeedPanel == null) return;

        var entry = new Label($"{killer}  ☠  {victim}");
        entry.AddToClassList("killfeed-entry");
        entry.AddToClassList("killfeed-text");
        if (localInvolved) entry.AddToClassList("killfeed-you");

        killFeedPanel.Add(entry);
        while (killFeedPanel.childCount > maxKillFeedEntries)
            killFeedPanel.RemoveAt(0);

        StartCoroutine(FadeOutKillEntry(entry, killFeedEntryLifetime));
    }

    public void ShowRespawnOverlay(float duration) => StartCoroutine(RespawnCountdownRoutine(duration));

    public void ShowToast(string message, float duration = 2.5f)
    {
        if (toastCoroutine != null) StopCoroutine(toastCoroutine);
        toastCoroutine = StartCoroutine(ToastRoutine(message, duration));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Public API — NEW (Team / Timer / Match Over / Medkit)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Updates the team kill score labels in the top-center panel.</summary>
    public void UpdateTeamScore(int teamA, int teamB)
    {
        if (teamALabel != null) teamALabel.text = $"A: {teamA}";
        if (teamBLabel != null) teamBLabel.text = $"B: {teamB}";
    }

    /// <summary>Formats seconds as M:SS and sets the match timer label.</summary>
    public void UpdateMatchTimer(float seconds)
    {
        if (matchTimerLabel == null) return;
        int totalSec = Mathf.CeilToInt(Mathf.Max(seconds, 0f));
        int mins     = totalSec / 60;
        int secs     = totalSec % 60;
        matchTimerLabel.text = $"{mins}:{secs:00}";
    }

    /// <summary>Shows the match-over overlay with winner and scores.
    /// Also displays a "Return to Menu" button so the player goes back without
    /// having to close and reopen the game.</summary>
    public void ShowMatchOver(string winner, int teamAKills, int teamBKills)
    {
        if (matchOverlay == null) return;
        if (matchOverlayTitle  != null) matchOverlayTitle.text  = $"{winner} WINS!";
        if (matchOverlayDetail != null) matchOverlayDetail.text = $"A: {teamAKills}  |  B: {teamBKills}";
        SetVisible(matchOverlay, true);

        // Add a Return to Menu button dynamically if not already present
        if (matchOverlay.Q<Button>("ReturnToMenuBtn") == null)
        {
            var returnBtn = new Button(() =>
            {
                SetVisible(matchOverlay, false);
                // Leave room → OnLeftRoom in MainMenuUI will call ShowMenu()
                PhotonNetwork.LeaveRoom();
            });
            returnBtn.name = "ReturnToMenuBtn";
            returnBtn.text = "🏠  Return to Menu";
            returnBtn.AddToClassList("match-return-btn");
            matchOverlay.Add(returnBtn);
        }
    }

    /// <summary>Updates the medkit count badge. Dims badge when count is 0.</summary>
    public void UpdateMedkitCount(int count)
    {
        if (medkitCountLabel == null) return;
        medkitCountLabel.text = count.ToString();
        medkitCountLabel.style.opacity = count == 0 ? 0.4f : 1f;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Photon room info
    // ═══════════════════════════════════════════════════════════════════════════

    public override void OnJoinedRoom()                    => RefreshRoomInfo();
    public override void OnPlayerEnteredRoom(Player _)     => RefreshRoomInfo();
    public override void OnPlayerLeftRoom(Player _)        => RefreshRoomInfo();

    private void RefreshRoomInfo()
    {
        if (!PhotonNetwork.InRoom) return;
        string code = PhotonNetwork.CurrentRoom.Name;
        int cur  = PhotonNetwork.CurrentRoom.PlayerCount;
        int max  = (int)PhotonNetwork.CurrentRoom.MaxPlayers;
        if (roomCodeLabel    != null) roomCodeLabel.text    = code;
        if (playerCountLabel != null) playerCountLabel.text = $"{cur}/{max} Players";
    }

    private static void SetVisible(VisualElement el, bool show)
    {
        if (el == null) return;
        el.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Coroutines
    // ═══════════════════════════════════════════════════════════════════════════

    private IEnumerator DamageVignetteRoutine()
    {
        SetVisible(damageVignette, true);
        yield return new WaitForSeconds(0.45f);
        SetVisible(damageVignette, false);
        vignetteCoroutine = null;
    }

    private IEnumerator FadeOutKillEntry(VisualElement entry, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (killFeedPanel != null && killFeedPanel.Contains(entry))
            killFeedPanel.Remove(entry);
    }

    private IEnumerator RespawnCountdownRoutine(float duration)
    {
        SetVisible(respawnOverlay, true);
        float remaining = duration;
        while (remaining > 0f)
        {
            if (respawnTimerLabel != null)
                respawnTimerLabel.text = Mathf.CeilToInt(remaining).ToString();
            yield return new WaitForSeconds(1f);
            remaining -= 1f;
        }
        SetVisible(respawnOverlay, false);
    }

    private IEnumerator ToastRoutine(string msg, float dur)
    {
        if (toastLabel != null) toastLabel.text = msg;
        SetVisible(toastPanel, true);
        yield return new WaitForSeconds(dur);
        SetVisible(toastPanel, false);
        toastCoroutine = null;
    }
}
