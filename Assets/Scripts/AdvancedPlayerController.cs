using UnityEngine;
using UnityEngine.InputSystem;
using Photon.Pun;

[RequireComponent(typeof(CharacterController))]
public class AdvancedPlayerController : MonoBehaviourPun
{
    [Header("Movement Speeds")]
    [SerializeField] private float walkSpeed   = 5.0f;
    [SerializeField] private float crouchSpeed = 2.5f;
    [SerializeField] private float gravity     = -9.81f;
    [SerializeField] private float jumpHeight  = 1.5f;

    [Header("Look Sensitivity")]
    [SerializeField] private float mouseSensitivity      = 0.15f;
    [Tooltip("Swipe sensitivity. 200 = responsive (like PUBG). Lower = slower. Tune in Inspector.")]
    [SerializeField] private float mobileLookSensitivity = 200f;
    [Tooltip("Auto-found at runtime. Leave empty in Inspector.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Crouch Settings")]
    [SerializeField] private float standingHeight  = 2.0f;
    [SerializeField] private float crouchingHeight = 1.0f;
    [SerializeField] private float timeToCrouch    = 0.15f;

    private CharacterController controller;
    private PlayerInput playerInput;

    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction jumpAction;
    private InputAction crouchAction;

    private Vector3 playerVelocity;
    private bool isGrounded;
    private bool isCrouching;
    private float xRotation = 0f;

    /// <summary>Called by PlayerSetup to inject the camera at runtime.</summary>
    public void SetCameraTransform(Transform cam) => cameraTransform = cam;

    private void Awake()
    {
        // CRITICAL: Skip input setup entirely for remote clones
        if (!photonView.IsMine) return;

        controller  = GetComponent<CharacterController>();
        playerInput = GetComponent<PlayerInput>();

        // AUTO-FIND CAMERA TRANSFORM — this is the fix for up/down look not working
        // The cameraTransform must be the Camera child inside the player prefab.
        if (cameraTransform == null)
        {
            var cam = GetComponentInChildren<Camera>(true);
            if (cam != null)
                cameraTransform = cam.transform;
        }

        if (cameraTransform == null)
            Debug.LogError("[PlayerController] No Camera found as child of player prefab! Up/down look WILL NOT work.");
        else
            Debug.Log("[PlayerController] Camera auto-found: " + cameraTransform.name);

        if (playerInput == null || !playerInput.enabled) return;

        var actions = playerInput.actions;
        if (actions == null) return;

        moveAction   = actions.FindAction("Move",   throwIfNotFound: false);
        lookAction   = actions.FindAction("Look",   throwIfNotFound: false);
        jumpAction   = actions.FindAction("Jump",   throwIfNotFound: false);
        crouchAction = actions.FindAction("Crouch", throwIfNotFound: false);
    }

    private void Start()
    {
        if (!photonView.IsMine) return;

        var hud = GameHUDController.Instance;
        if (hud != null)
            hud.OnJumpPressed += MobileJump;
    }

    private void MobileJump()
    {
        if (!photonView.IsMine) return;
        if (isGrounded && !isCrouching)
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
    }

    private void OnEnable()
    {
        if (!photonView.IsMine) return;
        if (jumpAction   != null) jumpAction.performed   += OnJumpPerformed;
        if (crouchAction != null) crouchAction.performed += OnCrouchPerformed;

        // Lock cursor on desktop
#if !UNITY_ANDROID && !UNITY_IOS
        Cursor.lockState = CursorLockMode.Locked;
#endif
    }

    private void OnDisable()
    {
        if (jumpAction   != null) jumpAction.performed   -= OnJumpPerformed;
        if (crouchAction != null) crouchAction.performed -= OnCrouchPerformed;
    }

    private void OnDestroy()
    {
        // Unsubscribe from HUD to prevent dangling references on respawn
        if (photonView != null && photonView.IsMine && GameHUDController.Instance != null)
        {
            GameHUDController.Instance.OnJumpPressed -= MobileJump;
        }
    }

    private void Update()
    {
        if (!photonView.IsMine) return;
        if (controller == null) controller = GetComponent<CharacterController>();

        HandleGrounding();
        HandleLocomotion();
        HandleRotation();
        HandleHeightTransitions();
    }

    private void HandleGrounding()
    {
        isGrounded = controller.isGrounded;
        if (isGrounded && playerVelocity.y < 0)
            playerVelocity.y = -2f;
    }

    private void HandleLocomotion()
    {
        Vector2 inputVector = Vector2.zero;
        if (moveAction != null) inputVector = moveAction.ReadValue<Vector2>();

        var hud = GameHUDController.Instance;
        if (hud != null && hud.MoveInput.sqrMagnitude > 0.01f)
        {
            inputVector = hud.MoveInput;
        }

        Vector3 moveDirection = transform.right * inputVector.x + transform.forward * inputVector.y;
        float   currentSpeed  = isCrouching ? crouchSpeed : walkSpeed;

        controller.Move(moveDirection * currentSpeed * Time.deltaTime);

        playerVelocity.y += gravity * Time.deltaTime;
        controller.Move(playerVelocity * Time.deltaTime);
    }

    private void HandleRotation()
    {
        // ── Input System look (desktop mouse / gamepad) ──
        Vector2 lookVector = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;

        bool isMobileSwipe = false;
        var hud = GameHUDController.Instance;
        if (hud != null && hud.LookInput.sqrMagnitude > 0.001f)
        {
            lookVector    = hud.LookInput;
            isMobileSwipe = true;
        }

        float lookX, lookY;

        if (isMobileSwipe)
        {
            // UI Toolkit PointerMove gives deltas in CSS/logical pixels (NOT physical pixels).
            // A multiplier of 0.3f gives a balanced turn speed for swipe aiming.
            lookX = lookVector.x * mobileLookSensitivity * 0.1f;
            lookY = lookVector.y * mobileLookSensitivity * 0.1f;

            // Consume now so it doesn't persist into next frame
            hud.ConsumeLookInput();
        }
        else
        {
            lookX = lookVector.x * mouseSensitivity;
            lookY = lookVector.y * mouseSensitivity;
        }

        // ── Vertical (camera pitch) ─────────────────────────────────────
        xRotation -= lookY;
        xRotation  = Mathf.Clamp(xRotation, -85f, 85f);

        // Re-check camera every frame in case it was found late
        if (cameraTransform == null)
        {
            var cam = GetComponentInChildren<Camera>(true);
            if (cam != null) cameraTransform = cam.transform;
        }

        if (cameraTransform != null)
            cameraTransform.localEulerAngles = new Vector3(xRotation, 0f, 0f);

        // ── Horizontal (body yaw) ───────────────────────────────────────
        transform.Rotate(Vector3.up * lookX);
    }


    private void OnJumpPerformed(InputAction.CallbackContext context)
    {
        if (!photonView.IsMine) return;
        if (isGrounded && !isCrouching)
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
    }

    private void OnCrouchPerformed(InputAction.CallbackContext context)
    {
        if (!photonView.IsMine) return;
        isCrouching = !isCrouching;
    }

    private void HandleHeightTransitions()
    {
        float targetHeight  = isCrouching ? crouchingHeight : standingHeight;
        float currentHeight = controller.height;

        if (!Mathf.Approximately(currentHeight, targetHeight))
        {
            controller.height = Mathf.Lerp(currentHeight, targetHeight, timeToCrouch / Time.deltaTime);
            Vector3 center = controller.center;
            center.y = controller.height / 2f;
            controller.center = center;
        }
    }
}