using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Photon.Pun;

/// <summary>
/// PlayerShoot — handles weapon firing, ammo system, reloading, and grenade throwing.
///
/// NEW in this version:
///  - Per-weapon ammo pools (mag + reserve)
///  - Auto-reload when mag is empty
///  - Manual reload coroutine (1.5s)
///  - HUD event wiring for fire, reload, weapon switch, zoom
///  - Ammo HUD updated after every shot and reload
/// </summary>
public class PlayerShoot : MonoBehaviourPun
{
    // 0 = Rifle, 1 = Pistol, 2 = Grenade
    private int currentWeaponIndex = 0;

    [Header("Weapon 1: Rifle (Automatic)")]
    [SerializeField] private GameObject rifleModel;
    [SerializeField] private float rifleDamage   = 25f;
    [SerializeField] private float rifleFireRate  = 0.1f;

    [Header("Weapon 2: Pistol (Semi-Auto)")]
    [SerializeField] private GameObject pistolModel;
    [SerializeField] private float pistolDamage  = 10f;
    [SerializeField] private float pistolFireRate = 0.2f;

    [Header("Weapon 3: Grenade Settings")]
    [SerializeField] private GameObject grenadeModel;
    [SerializeField] private GameObject networkGrenadePrefab;
    [SerializeField] private Transform  throwPoint;
    [SerializeField] private float      throwForce           = 15f;
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private int        trajectoryResolution = 30;
    [SerializeField] private float      curveSpacing         = 0.1f;

    [Header("General Settings")]
    [SerializeField] private float range = 100f;
    [SerializeField] private Camera fpsCamera;

    [Header("Visual Effects & Animation")]
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private WeaponRecoil recoilScript;
    [SerializeField] private Transform weaponHolder;

    // ── Input ─────────────────────────────────────────────────────────────────
    private PlayerInput playerInput;
    private InputAction shootAction;
    private InputAction equipRifleAction;
    private InputAction equipPistolAction;
    private InputAction equipGrenadeAction;

    // ── Firing state ──────────────────────────────────────────────────────────
    private float currentDamage;
    private float fireTimer;
    private bool  isHoldingShoot   = false;
    private bool  hasFiredThisTap  = false;
    private Coroutine swapCoroutine;

    // ── Ammo system ───────────────────────────────────────────────────────────
    // Per-slot: [0]=Rifle, [1]=Pistol, [2]=Grenade
    private int[] maxAmmoInMag   = { 30,  15, 1 };
    private int[] ammoInMag      = { 30,  15, 1 };
    private int[] ammoReserve    = { 90,  60, 3 };
    private bool  isReloading    = false;

    private int CurrentAmmo    => ammoInMag[currentWeaponIndex];
    private int CurrentReserve => ammoReserve[currentWeaponIndex];
    private int CurrentMaxMag  => maxAmmoInMag[currentWeaponIndex];

    // ── Scope toggle tracking ─────────────────────────────────────────────────
    private bool isScoped = false;

    // ── Weapon names ──────────────────────────────────────────────────────────
    private readonly string[] weaponNames = { "RIFLE", "PISTOL", "GRENADE" };

    /// <summary>Called by PlayerSetup to inject the FPS camera at runtime.</summary>
    public void SetCamera(Camera cam) => fpsCamera = cam;

    // ═══════════════════════════════════════════════════════════════════════════
    // Unity lifecycle
    // ═══════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // CRITICAL: Only set up input for the local player.
        if (!photonView.IsMine) return;

        // AUTO-FIND CAMERA — so raycast fires from exact crosshair position
        if (fpsCamera == null)
        {
            fpsCamera = GetComponentInChildren<Camera>(true);
            if (fpsCamera != null)
                Debug.Log("[PlayerShoot] Camera auto-found: " + fpsCamera.name);
            else
                Debug.LogError("[PlayerShoot] No Camera child found! Shooting WILL NOT work.");
        }

        playerInput = GetComponent<PlayerInput>();
        if (playerInput == null || !playerInput.enabled) return;

        var actions = playerInput.actions;
        if (actions == null) return;

        shootAction        = actions.FindAction("Shoot",        throwIfNotFound: false);
        equipRifleAction   = actions.FindAction("EquipRifle",   throwIfNotFound: false);
        equipPistolAction  = actions.FindAction("EquipPistol",  throwIfNotFound: false);
        equipGrenadeAction = actions.FindAction("EquipGrenade", throwIfNotFound: false);

        currentDamage      = rifleDamage;
        currentWeaponIndex = 0;
    }

    private void Start()
    {
        if (!photonView.IsMine) return;

        var hud = GameHUDController.Instance;
        if (hud != null)
        {
            hud.OnFirePressed  += MobileFireStart;
            hud.OnFireReleased += MobileFireStop;
            hud.OnReloadPressed+= Reload;
            hud.OnGrenadePressed += () => SwitchWeaponNetworked(2);
            hud.OnMGunPressed  += () => SwitchWeaponNetworked(0);
            hud.OnSGunPressed  += () => SwitchWeaponNetworked(1);
            hud.OnZoomPressed  += () =>
            {
                var scope = GetComponent<PlayerScope>();
                scope?.SetScopeFromHUD(!isScoped);
                isScoped = !isScoped;
            };
        }

        // Push initial ammo display
        PushAmmoToHUD();
    }

    private void OnDestroy()
    {
        if (GameHUDController.Instance != null)
        {
            var hud = GameHUDController.Instance;
            hud.OnFirePressed   -= MobileFireStart;
            hud.OnFireReleased  -= MobileFireStop;
            hud.OnReloadPressed -= Reload;
        }
    }

    private void OnEnable()
    {
        if (!photonView.IsMine) return;

        if (shootAction != null)
        {
            shootAction.started  += OnShootStarted;
            shootAction.canceled += OnShootCanceled;
        }
        if (equipRifleAction   != null) equipRifleAction.performed   += OnEquipRifle;
        if (equipPistolAction  != null) equipPistolAction.performed  += OnEquipPistol;
        if (equipGrenadeAction != null) equipGrenadeAction.performed += OnEquipGrenade;
    }

    private void OnDisable()
    {
        if (shootAction != null)
        {
            shootAction.started  -= OnShootStarted;
            shootAction.canceled -= OnShootCanceled;
        }
        if (equipRifleAction   != null) equipRifleAction.performed   -= OnEquipRifle;
        if (equipPistolAction  != null) equipPistolAction.performed  -= OnEquipPistol;
        if (equipGrenadeAction != null) equipGrenadeAction.performed -= OnEquipGrenade;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Input callbacks
    // ═══════════════════════════════════════════════════════════════════════════

    private void OnShootStarted(InputAction.CallbackContext context)
    {
        if (!photonView.IsMine) return;
        isHoldingShoot = true;
        if (currentWeaponIndex == 2 && lineRenderer != null)
            lineRenderer.enabled = true;
    }

    private void OnShootCanceled(InputAction.CallbackContext context)
    {
        if (!photonView.IsMine) return;
        isHoldingShoot  = false;
        hasFiredThisTap = false;

        if (currentWeaponIndex == 2)
        {
            if (lineRenderer != null) lineRenderer.enabled = false;
            ThrowGrenade();
        }
    }

    private void OnEquipRifle(InputAction.CallbackContext ctx)   => SwitchWeaponNetworked(0);
    private void OnEquipPistol(InputAction.CallbackContext ctx)  => SwitchWeaponNetworked(1);
    private void OnEquipGrenade(InputAction.CallbackContext ctx) => SwitchWeaponNetworked(2);

    // ═══════════════════════════════════════════════════════════════════════════
    // Update loop
    // ═══════════════════════════════════════════════════════════════════════════

    private void Update()
    {
        if (!photonView.IsMine) return;

        fireTimer += Time.deltaTime;

        if (isHoldingShoot && !isReloading)
        {
            if (currentWeaponIndex == 0) // Rifle - automatic
            {
                if (fireTimer >= rifleFireRate)
                {
                    if (ammoInMag[0] > 0)
                    {
                        FireWeapon();
                        ammoInMag[0]--;
                        fireTimer = 0f;
                        PushAmmoToHUD();
                        if (ammoInMag[0] <= 0) AutoReload();
                    }
                    else { AutoReload(); }
                }
            }
            else if (currentWeaponIndex == 1) // Pistol - semi auto
            {
                if (!hasFiredThisTap && fireTimer >= pistolFireRate)
                {
                    if (ammoInMag[1] > 0)
                    {
                        FireWeapon();
                        ammoInMag[1]--;
                        hasFiredThisTap = true;
                        fireTimer = 0f;
                        PushAmmoToHUD();
                        if (ammoInMag[1] <= 0) AutoReload();
                    }
                    else { AutoReload(); }
                }
            }
        }

        if (currentWeaponIndex == 2 && lineRenderer != null && lineRenderer.enabled)
            DrawTrajectory();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Ammo system
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Manual reload triggered by HUD or keyboard.</summary>
    public void Reload()
    {
        if (!photonView.IsMine) return;
        if (isReloading) return;
        if (currentWeaponIndex == 2) return; // grenade doesn't reload
        if (ammoInMag[currentWeaponIndex] >= maxAmmoInMag[currentWeaponIndex]) return;
        if (ammoReserve[currentWeaponIndex] <= 0) return;

        StartCoroutine(ReloadRoutine(currentWeaponIndex));
    }

    private void AutoReload()
    {
        if (isReloading) return;
        if (currentWeaponIndex == 2) return;
        if (ammoReserve[currentWeaponIndex] <= 0) return;

        StartCoroutine(ReloadRoutine(currentWeaponIndex));
    }

    private IEnumerator ReloadRoutine(int slot)
    {
        isReloading = true;
        GameHUDController.Instance?.ShowToast("Reloading...", 1.5f);
        yield return new WaitForSeconds(1.5f);

        int needed = maxAmmoInMag[slot] - ammoInMag[slot];
        int take   = Mathf.Min(needed, ammoReserve[slot]);
        ammoInMag[slot]   += take;
        ammoReserve[slot] -= take;

        isReloading = false;
        PushAmmoToHUD();
    }

    private void PushAmmoToHUD()
    {
        GameHUDController.Instance?.UpdateWeapon(
            currentWeaponIndex,
            weaponNames[currentWeaponIndex],
            ammoInMag[currentWeaponIndex],
            ammoReserve[currentWeaponIndex]);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Weapon firing
    // ═══════════════════════════════════════════════════════════════════════════

    private void FireWeapon()
    {
        if (recoilScript != null && recoilScript.enabled) recoilScript.FireRecoil();

        if (fpsCamera == null) fpsCamera = GetComponentInChildren<Camera>(true);
        if (fpsCamera == null) fpsCamera = Camera.main;
        if (fpsCamera == null) { Debug.LogError("[PlayerShoot] No camera found! Cannot fire."); return; }

        // Fire from EXACTLY where the camera looks — this is where the crosshair points.
        // Using camera.forward directly is more reliable than ViewportPointToRay on mobile.
        Ray ray = new Ray(fpsCamera.transform.position, fpsCamera.transform.forward);


        // Use RaycastAll to shoot through our own body colliders
        RaycastHit[] hits = Physics.RaycastAll(ray, range);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            // Ignore hits on our own player model
            if (hit.collider.transform.IsChildOf(transform)) continue;

            // Spawn hit effect on all clients
            photonView.RPC("RPC_SpawnHitEffect", RpcTarget.All, hit.point, hit.normal);

            PlayerHealth targetHealth = hit.transform.GetComponentInParent<PlayerHealth>();
            if (targetHealth != null)
            {
                PhotonView targetView = targetHealth.GetComponent<PhotonView>();
                if (targetView != null && !targetView.IsMine)
                {
                    targetView.RPC("TakeDamage", RpcTarget.AllViaServer, currentDamage);
                }
            }

            if (hit.rigidbody != null)
                hit.rigidbody.AddForce(-hit.normal * 500f);

            break; // stop at first valid hit
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Grenade
    // ═══════════════════════════════════════════════════════════════════════════

    private void DrawTrajectory()
    {
        if (throwPoint == null) return;
        lineRenderer.positionCount = trajectoryResolution;
        Vector3 startPosition = throwPoint.position;
        Vector3 startVelocity = throwPoint.forward * throwForce;

        for (int i = 0; i < trajectoryResolution; i++)
        {
            float   time  = i * curveSpacing;
            Vector3 point = startPosition + startVelocity * time + 0.5f * Physics.gravity * time * time;
            lineRenderer.SetPosition(i, point);
        }
    }

    private void ThrowGrenade()
    {
        if (ammoInMag[2] <= 0) return;

        if (networkGrenadePrefab != null && throwPoint != null)
            PhotonNetwork.Instantiate(networkGrenadePrefab.name, throwPoint.position, throwPoint.rotation);

        ammoInMag[2]--;
        PushAmmoToHUD();

        photonView.RPC("RPC_SyncWeaponSwap", RpcTarget.AllBuffered, 0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Weapon switching
    // ═══════════════════════════════════════════════════════════════════════════

    private void SwitchWeaponNetworked(int id)
    {
        if (!photonView.IsMine) return;
        photonView.RPC("RPC_SyncWeaponSwap", RpcTarget.AllBuffered, id);
    }

    // ── HUD Event Handlers ──────────────────────────────────────────────────
    private void MobileFireStart()
    {
        if (!photonView.IsMine) return;
        isHoldingShoot = true;
        if (currentWeaponIndex == 2 && lineRenderer != null)
            lineRenderer.enabled = true;
    }

    private void MobileFireStop()
    {
        if (!photonView.IsMine) return;
        isHoldingShoot  = false;
        hasFiredThisTap = false;

        if (currentWeaponIndex == 2)
        {
            if (lineRenderer != null) lineRenderer.enabled = false;
            ThrowGrenade();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RPCs
    // ═══════════════════════════════════════════════════════════════════════════

    [PunRPC]
    private void RPC_SyncWeaponSwap(int weaponIndex)
    {
        isHoldingShoot  = false;
        hasFiredThisTap = false;
        if (lineRenderer != null) lineRenderer.enabled = false;

        currentWeaponIndex = weaponIndex;

        // Update damage for the selected weapon
        if (weaponIndex == 0) currentDamage = rifleDamage;
        if (weaponIndex == 1) currentDamage = pistolDamage;

        if (swapCoroutine != null) StopCoroutine(swapCoroutine);
        if (weaponHolder != null)
            swapCoroutine = StartCoroutine(SwapAnimationRoutine(weaponIndex));
        else
        {
            if (rifleModel   != null) rifleModel.SetActive(weaponIndex == 0);
            if (pistolModel  != null) pistolModel.SetActive(weaponIndex == 1);
            if (grenadeModel != null) grenadeModel.SetActive(weaponIndex == 2);
        }

        // Update HUD ammo display for the new weapon (only local player)
        if (photonView.IsMine) PushAmmoToHUD();
    }

    private IEnumerator SwapAnimationRoutine(int weaponIndex)
    {
        if (recoilScript != null) recoilScript.enabled = false;

        Vector3 originalPos = weaponHolder.localPosition;
        Vector3 dipPos      = originalPos + new Vector3(0, -0.6f, 0);

        float t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime * 6f;
            weaponHolder.localPosition = Vector3.Lerp(originalPos, dipPos, t);
            yield return null;
        }

        if (rifleModel   != null) rifleModel.SetActive(weaponIndex == 0);
        if (pistolModel  != null) pistolModel.SetActive(weaponIndex == 1);
        if (grenadeModel != null) grenadeModel.SetActive(weaponIndex == 2);

        t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime * 6f;
            weaponHolder.localPosition = Vector3.Lerp(dipPos, originalPos, t);
            yield return null;
        }

        weaponHolder.localPosition = originalPos;
        if (recoilScript != null) recoilScript.enabled = true;
    }

    [PunRPC]
    private void RPC_SpawnHitEffect(Vector3 hitPosition, Vector3 hitNormal)
    {
        if (hitEffectPrefab != null)
        {
            GameObject spark = Instantiate(hitEffectPrefab, hitPosition, Quaternion.LookRotation(hitNormal));
            Destroy(spark, 2f);
        }
    }
}