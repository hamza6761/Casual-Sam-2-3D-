using UnityEngine;

public class Healing : MonoBehaviour
{
    [SerializeField] private int healAmount = 10;
    [SerializeField] private bool oneTime = true;
    [SerializeField] private float cooldown = 1f;

    // Used only when oneTime is false, to block retriggering.
    private float cooldownTimer = 0f;

    private void Update()
    {
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!oneTime && cooldownTimer > 0f) return;

        PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();
        if (playerHealth == null) return;

        playerHealth.Heal(healAmount);

        if (oneTime)
        {
            Destroy(gameObject);
        }
        else
        {
            cooldownTimer = cooldown;
        }
    }
}
