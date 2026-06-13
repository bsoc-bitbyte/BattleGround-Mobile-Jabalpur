using UnityEngine;

public class WeaponRecoil : MonoBehaviour
{
    [Header("Recoil Settings")]
    [SerializeField] private Vector3 recoilAmount = new Vector3(0f, 0f, -0.2f); // Kicks backward on the Z axis
    [SerializeField] private float snapBackSpeed = 10f;  // How fast it kicks back
    [SerializeField] private float returnSpeed = 5f;     // How fast it returns to normal

    private Vector3 originalPosition;
    private Vector3 currentPosition;
    private Vector3 targetPosition;

    private void Start()
    {
        // Remember where the gun starts
        originalPosition = transform.localPosition;
    }

    private void Update()
    {
        // Smoothly move the target position back to normal
        targetPosition = Vector3.Lerp(targetPosition, originalPosition, returnSpeed * Time.deltaTime);

        // Smoothly move the actual gun toward the target position
        currentPosition = Vector3.Lerp(currentPosition, targetPosition, snapBackSpeed * Time.deltaTime);

        transform.localPosition = currentPosition;
    }

    // We will call this method every time we pull the trigger
    public void FireRecoil()
    {
        // Instantly push the target position back by the recoil amount
        targetPosition += recoilAmount;
    }
}