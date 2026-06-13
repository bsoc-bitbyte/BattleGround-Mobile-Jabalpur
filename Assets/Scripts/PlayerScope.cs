using UnityEngine;
using Photon.Pun;
using UnityEngine.InputSystem;

/// <summary>
/// PlayerScope — handles sniper/zoom scoping.
///
/// BUG FIXES:
///  1. SetScopeFromHUD() called by GameHUDController so the zoom button
///     actually changes camera FOV (previously only the UI overlay toggled).
///  2. Smooth FOV transition via lerp coroutine instead of instant snap.
///  3. Registers itself with GameHUDController on Start so the HUD
///     can call back into it.
///  4. Scope FOV = 15° (strong zoom), Normal = 60°.
/// </summary>
public class PlayerScope : MonoBehaviourPun
{
    [Header("Scope Settings")]
    [SerializeField] private float scopedFOV  = 15f;   // zoomed in (sniper)
    [SerializeField] private float normalFOV  = 60f;   // normal view
    [SerializeField] private float fovLerpSpeed = 8f;  // smooth transition

    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private GameObject weaponModel;

    private PlayerInput playerInput;
    private InputAction scopeAction;
    private bool isScoped = false;
    private Coroutine fovCoroutine;

    // ── Called by PlayerSetup to inject the FPS camera at runtime ────────────
    public void SetCamera(Camera cam) => mainCamera = cam;

    private void Awake()
    {
        if (!photonView.IsMine) return;

        playerInput = GetComponent<PlayerInput>();
        if (playerInput != null && playerInput.enabled && playerInput.actions != null)
            scopeAction = playerInput.actions.FindAction("Scope", throwIfNotFound: false);
    }

    private void Start()
    {
        if (!photonView.IsMine) return;

        // Auto-find camera if not assigned
        if (mainCamera == null) mainCamera = Camera.main;

        // Register with HUD so zoom button calls back into us
        var hud = GameHUDController.Instance;
        if (hud != null) hud.SetPlayerScope(this);
    }

    private void OnEnable()
    {
        if (!photonView.IsMine) return;
        if (scopeAction != null) scopeAction.performed += OnScopePerformed;
    }

    private void OnDisable()
    {
        if (scopeAction != null) scopeAction.performed -= OnScopePerformed;
    }

    // ── Called by keyboard/gamepad input action ───────────────────────────────
    private void OnScopePerformed(InputAction.CallbackContext context)
    {
        if (!photonView.IsMine) return;
        ToggleScope();
    }

    /// <summary>
    /// Called by GameHUDController when the zoom button is pressed.
    /// This is the FIX: the HUD now actually changes the camera FOV.
    /// </summary>
    public void SetScopeFromHUD(bool scoped)
    {
        if (!photonView.IsMine) return;
        isScoped = scoped;
        ApplyScope(scoped);
    }

    private void ToggleScope()
    {
        isScoped = !isScoped;
        ApplyScope(isScoped);
    }

    private void ApplyScope(bool scoped)
    {
        // ── Camera FOV (smooth transition) ─────────────────────────────────
        if (mainCamera == null) mainCamera = Camera.main;

        float targetFOV = scoped ? scopedFOV : normalFOV;
        if (fovCoroutine != null) StopCoroutine(fovCoroutine);
        fovCoroutine = StartCoroutine(SmoothFOV(targetFOV));

        // ── Weapon model visibility ─────────────────────────────────────────
        if (weaponModel != null) weaponModel.SetActive(!scoped);

        // ── HUD scope overlay + crosshair ───────────────────────────────────
        var hud = GameHUDController.Instance;
        if (hud != null)
            hud.SetScopeActive(scoped);
    }

    private System.Collections.IEnumerator SmoothFOV(float targetFOV)
    {
        while (mainCamera != null &&
               !Mathf.Approximately(mainCamera.fieldOfView, targetFOV))
        {
            mainCamera.fieldOfView = Mathf.Lerp(
                mainCamera.fieldOfView, targetFOV,
                Time.deltaTime * fovLerpSpeed);

            // Snap when close enough
            if (Mathf.Abs(mainCamera.fieldOfView - targetFOV) < 0.1f)
            {
                mainCamera.fieldOfView = targetFOV;
                break;
            }
            yield return null;
        }
        fovCoroutine = null;
    }
}