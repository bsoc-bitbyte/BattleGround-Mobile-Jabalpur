using UnityEngine;
using Photon.Pun;
using UnityEngine.InputSystem;

public class GrenadeThrower : MonoBehaviourPun
{
    [Header("Grenade Settings")]
    [SerializeField] private GameObject networkGrenadePrefabName; // Make sure this prefab is in your "Resources" folder!
    [SerializeField] private Transform throwPoint;
    [SerializeField] private float throwForce = 15f;

    [Header("Trajectory Preview")]
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private int trajectoryResolution = 30;
    [SerializeField] private float curveSpacing = 0.1f;

    private PlayerInput playerInput;
    private InputAction shootAction;
    private bool isHoldingGrenade = false;

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        shootAction = playerInput.actions["Shoot"];
    }

    private void OnEnable()
    {
        if (shootAction != null)
        {
            shootAction.started += OnThrowStarted;
            shootAction.canceled += OnThrowReleased;
        }
    }

    private void OnDisable()
    {
        if (shootAction != null)
        {
            shootAction.started -= OnThrowStarted;
            shootAction.canceled -= OnThrowReleased;
        }
    }

    // Call this from your Weapon Switching script when selecting the grenade slot
    public void SetGrenadeActive(bool active)
    {
        isHoldingGrenade = active;
        if (!active && lineRenderer != null) lineRenderer.enabled = false;
    }

    private void OnThrowStarted(InputAction.CallbackContext context)
    {
        if (!photonView.IsMine || !isHoldingGrenade) return;
        if (lineRenderer != null) lineRenderer.enabled = true;
    }

    private void Update()
    {
        if (!photonView.IsMine || !isHoldingGrenade || lineRenderer == null || !lineRenderer.enabled) return;

        DrawTrajectory();
    }

    private void DrawTrajectory()
    {
        lineRenderer.positionCount = trajectoryResolution;
        Vector3 startPosition = throwPoint.position;
        Vector3 startVelocity = throwPoint.forward * throwForce;

        for (int i = 0; i < trajectoryResolution; i++)
        {
            float time = i * curveSpacing;
            // Standard kinematic equation: x = x0 + v0*t + 0.5*g*t^2
            Vector3 point = startPosition + startVelocity * time + 0.5f * Physics.gravity * time * time;
            lineRenderer.SetPosition(i, point);
        }
    }

    private void OnThrowReleased(InputAction.CallbackContext context)
    {
        if (!photonView.IsMine || !isHoldingGrenade) return;

        if (lineRenderer != null) lineRenderer.enabled = false;

        // Spawn the grenade over the network using Photon
        if (networkGrenadePrefabName != null)
        {
            PhotonNetwork.Instantiate(networkGrenadePrefabName.name, throwPoint.position, throwPoint.rotation);
        }

        // Switch back to rifle automatically after throwing
        GetComponent<PlayerShoot>().Invoke("OnEquipRifle", 0.1f);
    }
}