using UnityEngine;
using Photon.Pun;

/// <summary>
/// Smooth network position + rotation sync for remote player clones.
///
/// HOW TO USE:
///   1. Add this script to your player prefab.
///   2. Add a PhotonView component (if not already there).
///   3. In the PhotonView's "Observed Components" list, add this script.
///   4. Set PhotonView's "Synchronization" to "Unreliable On Change".
///
/// KEY FIXES:
///   - CharacterController is disabled on remote clones so it doesn't block
///     transform.position lerp (this was causing the "join player not visible" bug).
///   - Uses lag-compensation via PhotonMessageInfo.SentServerTime for smoother
///     remote movement.
///   - Camera pitch synced correctly so remote players look up/down.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class NetworkPlayerSync : MonoBehaviourPun, IPunObservable
{
    [Header("Camera to sync vertical look (assign the FPS camera)")]
    [SerializeField] private Transform cameraTransform;

    [Header("Interpolation speed for remote players")]
    [SerializeField] private float positionLerpSpeed = 15f;
    [SerializeField] private float rotationLerpSpeed = 15f;

    // Values received over the network for remote clones
    private Vector3    networkPosition;
    private Quaternion networkBodyRotation;
    private float      networkCameraPitch;   // camera X rotation

    // Lag-compensation: extrapolate position based on send latency
    private Vector3 networkVelocity = Vector3.zero;
    private double  lastPacketTime  = 0;

    private bool firstUpdate = true;

    // CharacterController reference — disabled on remote clones
    private CharacterController cc;

    private void Awake()
    {
        // Seed with current transform so first lerp is not from world origin
        networkPosition     = transform.position;
        networkBodyRotation = transform.rotation;

        cc = GetComponent<CharacterController>();

        // CRITICAL: Disable CharacterController on remote clones.
        // If it stays enabled, it blocks NetworkPlayerSync's transform.position
        // assignment, making remote players appear stuck or invisible.
        if (!photonView.IsMine && cc != null)
        {
            cc.enabled = false;
            Debug.Log($"[NetworkPlayerSync] CharacterController disabled on remote clone '{photonView.Owner?.NickName}'.");
        }
    }

    // ── IPunObservable ────────────────────────────────────────────────────────
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Local player → send data to other clients
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);

            float pitch = (cameraTransform != null)
                ? cameraTransform.localEulerAngles.x
                : 0f;
            stream.SendNext(pitch);
            stream.SendNext(GetComponent<Rigidbody>() != null
                ? GetComponent<Rigidbody>().linearVelocity
                : Vector3.zero);
        }
        else
        {
            // Remote clone → receive data
            Vector3    receivedPos = (Vector3)    stream.ReceiveNext();
            Quaternion receivedRot = (Quaternion) stream.ReceiveNext();
            float      receivedPitch = (float)   stream.ReceiveNext();
            Vector3    receivedVel = (Vector3)    stream.ReceiveNext();

            // Lag compensation: extrapolate forward by network round-trip lag
            double lag = Mathf.Abs((float)(PhotonNetwork.Time - info.SentServerTime));
            networkPosition     = receivedPos + receivedVel * (float)lag;
            networkBodyRotation = receivedRot;
            networkCameraPitch  = receivedPitch;
            networkVelocity     = receivedVel;
            lastPacketTime      = info.SentServerTime;

            if (firstUpdate)
            {
                // Snap on first packet to avoid lerping from origin
                transform.position = networkPosition;
                transform.rotation = networkBodyRotation;
                firstUpdate = false;
            }
        }
    }

    // ── Update: smoothly interpolate remote players ───────────────────────────
    private void Update()
    {
        if (photonView.IsMine) return;  // Only move remote clones here

        transform.position = Vector3.Lerp(
            transform.position,
            networkPosition,
            Time.deltaTime * positionLerpSpeed
        );

        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            networkBodyRotation,
            Time.deltaTime * rotationLerpSpeed
        );

        // Sync camera pitch so remote players look up/down correctly
        if (cameraTransform != null)
        {
            Vector3 camEuler = cameraTransform.localEulerAngles;
            camEuler.x = Mathf.LerpAngle(camEuler.x, networkCameraPitch, Time.deltaTime * rotationLerpSpeed);
            cameraTransform.localEulerAngles = camEuler;
        }
    }
}
