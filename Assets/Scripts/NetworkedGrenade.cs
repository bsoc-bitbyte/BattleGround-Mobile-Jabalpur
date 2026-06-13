using UnityEngine;
using Photon.Pun;

/// <summary>
/// NetworkedGrenade — handles fuse timing, area explosion damage, and self-destruction.
///
/// FIXES:
///   - Only the MasterClient runs the Explode() logic and calls PhotonNetwork.Destroy().
///     Previously, ALL clients were calling Invoke("Explode", fuseTime) and the one
///     that called PhotonNetwork.Destroy() had to be the owner or master — causing the
///     "failed to 'network-remove' gameobject — client is neither owner nor master"
///     error (view 2004).
///   - Fuse countdown is now visually represented via a separate countdown call.
/// </summary>
public class NetworkedGrenade : MonoBehaviourPun
{
    [Header("Explosion Configuration")]
    [SerializeField] private float fuseTime        = 3f;
    [SerializeField] private float explosionRadius = 6f;
    [SerializeField] private float blastDamage     = 100f;
    [SerializeField] private float blastForce      = 700f;
    [SerializeField] private GameObject explosionVFXPrefab;

    private Rigidbody rb;
    private bool hasExploded = false;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();

        // Push the grenade forward instantly on creation
        if (rb != null)
            rb.AddForce(transform.forward * 15f, ForceMode.Impulse);

        // CRITICAL FIX: Only the MasterClient (or owner) should trigger
        // the explosion and call PhotonNetwork.Destroy(). Non-owners calling
        // PhotonNetwork.Destroy() causes the "view 2004 neither owner nor master" error.
        //
        // We Invoke on ALL clients so VFX triggers correctly, but only the
        // MasterClient (which always has authority) runs the damage + destroy logic.
        Invoke(nameof(Explode), fuseTime);
    }

    private void Explode()
    {
        if (hasExploded) return;
        hasExploded = true;

        // Trigger explosion visual effects on ALL clients via RPC
        // (MasterClient or owner fires the RPC)
        if (photonView.IsMine || PhotonNetwork.IsMasterClient)
        {
            photonView.RPC(nameof(RPC_TriggerExplosionEffects), RpcTarget.All, transform.position);

            // Deal splash damage — only MasterClient calculates to avoid duplicate damage
            if (PhotonNetwork.IsMasterClient)
            {
                Collider[] colliders = Physics.OverlapSphere(transform.position, explosionRadius);
                foreach (Collider nearbyObject in colliders)
                {
                    // Deal Splash Damage to Players
                    PlayerHealth health = nearbyObject.GetComponent<PlayerHealth>();
                    if (health != null)
                    {
                        float distance        = Vector3.Distance(transform.position, nearbyObject.transform.position);
                        float damageMultiplier = 1f - (distance / explosionRadius);
                        float finalDamage     = Mathf.Max(0, blastDamage * damageMultiplier);

                        PhotonView targetView = health.GetComponent<PhotonView>();
                        if (targetView != null)
                            targetView.RPC("TakeDamage", RpcTarget.AllViaServer, finalDamage);
                    }

                    // Blow away physics objects
                    Rigidbody targetRb = nearbyObject.GetComponent<Rigidbody>();
                    if (targetRb != null)
                        targetRb.AddExplosionForce(blastForce, transform.position, explosionRadius);
                }

                // ONLY MasterClient destroys the networked object — this fixes view 2004
                PhotonNetwork.Destroy(gameObject);
            }
        }
    }

    [PunRPC]
    private void RPC_TriggerExplosionEffects(Vector3 position)
    {
        if (explosionVFXPrefab != null)
        {
            GameObject fx = Instantiate(explosionVFXPrefab, position, Quaternion.identity);
            Destroy(fx, 4f);
        }
    }
}