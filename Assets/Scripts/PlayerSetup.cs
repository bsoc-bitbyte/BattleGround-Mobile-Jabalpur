using UnityEngine;
using UnityEngine.InputSystem;
using Photon.Pun;

/// <summary>
/// SELF-HEALING PlayerSetup — works even if the player prefab has no camera child.
///
/// For the LOCAL player:
///   - Finds the existing Main Camera in the scene and PARENTS it to this player
///   - Enables controls (AdvancedPlayerController, PlayerShoot, PlayerScope)
///
/// For REMOTE clones:
///   - Disables PlayerInput, control scripts
///   - Disables any cameras that happen to be children (safety)
///   - Disables CharacterController so NetworkPlayerSync can freely set transform.position
/// </summary>
[DefaultExecutionOrder(-100)]
public class PlayerSetup : MonoBehaviourPun
{
    [Header("Camera Offset from player root (eye height)")]
    [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 1.6f, 0.1f);

    // Cached so AdvancedPlayerController can grab it via PlayerSetup.LocalCamera
    public static Camera LocalCamera { get; private set; }

    private void Awake()
    {
        if (photonView.IsMine)
            SetupLocalPlayer();
        else
            SetupRemotePlayer();
    }

    // ── LOCAL PLAYER ──────────────────────────────────────────────────────────
    private void SetupLocalPlayer()
    {
        // ---- Step 1: Find the built-in Camera in the player prefab ----
        Camera cam = GetComponentInChildren<Camera>(true);

        if (cam == null)
        {
            // Fallback (only if prefab has no camera)
            GameObject camGO = new GameObject("FPSCamera");
            cam = camGO.AddComponent<Camera>();
            cam.transform.SetParent(transform);
            cam.transform.localPosition = cameraOffset;
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView   = 60f;
            Debug.Log("[PlayerSetup] Created new FPSCamera.");
        }

        // ---- Step 1.5: Disable any OTHER cameras in the scene (e.g. Lobby Camera) ----
        foreach (var sceneCam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (sceneCam != cam)
            {
                sceneCam.enabled = false;
            }
        }

        cam.enabled = true;
        cam.tag     = "MainCamera";
        LocalCamera = cam;

        // ---- Step 2.5: Enforce EXACTLY ONE AudioListener in the scene ----
        // Disable any pre-existing listeners (from SampleScene, dead players, etc)
        foreach (var listener in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            listener.enabled = false;
        }

        // Ensure OUR camera has the one active listener
        AudioListener localListener = cam.gameObject.GetComponent<AudioListener>();
        if (localListener == null) localListener = cam.gameObject.AddComponent<AudioListener>();
        localListener.enabled = true;

        // ---- Step 3: Pass camera reference to AdvancedPlayerController ----
        var controller = GetComponent<AdvancedPlayerController>();
        if (controller != null)
        {
            controller.SetCameraTransform(cam.transform);
            controller.enabled = true;
        }

        // ---- Step 4: Pass camera reference to PlayerShoot ----
        var shoot = GetComponent<PlayerShoot>();
        if (shoot != null)
        {
            shoot.SetCamera(cam);
            shoot.enabled = true;
        }

        // ---- Step 5: Pass camera reference to PlayerScope ----
        var scope = GetComponent<PlayerScope>();
        if (scope != null)
        {
            scope.SetCamera(cam);
            scope.enabled = true;
        }

        // ---- Step 6: Enable PlayerInput ----
        var playerInput = GetComponent<PlayerInput>();
        if (playerInput != null) playerInput.enabled = true;

        // ── Register scope with HUD so zoom button actually changes FOV ──────
        // (PlayerScope.Start() also does this, but doing it here too as fallback)
        var hud = GameHUDController.Instance;
        if (hud != null && scope != null)
            hud.SetPlayerScope(scope);

        // ---- Step 7: Wire NetworkPlayerSync camera reference ----
        var netSync = GetComponent<NetworkPlayerSync>();
        if (netSync != null)
        {
            // The camera transform is already assigned — nothing extra needed,
            // but we force-enable in case it was disabled by mistake.
            netSync.enabled = true;
        }

        Debug.Log("[PlayerSetup] Local player ready. Camera attached.");
    }

    // ── REMOTE CLONE ──────────────────────────────────────────────────────────
    private void SetupRemotePlayer()
    {
        // Kill input first
        var playerInput = GetComponent<PlayerInput>();
        if (playerInput != null) playerInput.enabled = false;

        // Disable any cameras that are children (shouldn't exist, but safety)
        foreach (var cam in GetComponentsInChildren<Camera>(true))
            cam.enabled = false;

        // Disable audio listener duplicates
        foreach (var al in GetComponentsInChildren<AudioListener>(true))
            al.enabled = false;

        // Disable control scripts
        var movement = GetComponent<AdvancedPlayerController>();
        if (movement != null) movement.enabled = false;

        var shooting = GetComponent<PlayerShoot>();
        if (shooting != null) shooting.enabled = false;

        var scope = GetComponent<PlayerScope>();
        if (scope != null) scope.enabled = false;

        // CRITICAL FIX: Disable CharacterController on remote clones.
        // If CharacterController is active on a remote clone, it intercepts
        // transform.position writes from NetworkPlayerSync, making the remote
        // player appear invisible or frozen to the host.
        var cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
            Debug.Log($"[PlayerSetup] CharacterController disabled on remote '{photonView.Owner?.NickName}'.");
        }

        Debug.Log($"[PlayerSetup] Remote clone '{photonView.Owner?.NickName}' disabled.");
    }
}