using UnityEngine;

// Shared so grenades and the dying drone blow up the same way.
public static class Explosion
{
    private const float PlaceholderFlashTime = 0.15f;

    public static void Explode(Vector3 position, float radius, int damage, float knockbackForce, LayerMask mask, GameObject effectPrefab)
    {
        Collider[] hits = Physics.OverlapSphere(position, radius, mask, QueryTriggerInteraction.Ignore);
        foreach (Collider hit in hits)
        {
            PlayerHealth playerHealth = hit.GetComponentInParent<PlayerHealth>();
            if (playerHealth == null) continue;

            playerHealth.TakeDamage(damage);

            if (knockbackForce > 0f)
            {
                PlayerController playerController = playerHealth.GetComponentInParent<PlayerController>();
                if (playerController != null)
                {
                    // away from the blast plus a little upward pop
                    Vector3 away = playerHealth.transform.position - position;
                    away.y = 0f;
                    Vector3 upwardPop = Vector3.up * 0.5f;
                    Vector3 pushDirection = away.normalized + upwardPop;
                    pushDirection.Normalize();

                    Vector3 knockback = pushDirection * knockbackForce;
                    playerController.AddKnockback(knockback);
                }
            }

            // player might have more than one collider, don't hurt them twice
            break;
        }

        SpawnEffect(position, radius, effectPrefab);
    }

    private static void SpawnEffect(Vector3 position, float radius, GameObject effectPrefab)
    {
        // real effect should destroy itself when it's done
        if (effectPrefab != null)
        {
            Object.Instantiate(effectPrefab, position, Quaternion.identity);
            return;
        }

        // no effect yet, flash a sphere the size of the blast for testing
        GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Collider flashCollider = flash.GetComponent<Collider>();
        flashCollider.enabled = false; // Destroy waits till end of frame, so turn it off now
        Object.Destroy(flashCollider);
        flash.name = "Explosion (Placeholder)";
        flash.transform.position = position;
        float diameter = radius * 2f;
        flash.transform.localScale = Vector3.one * diameter;
        Object.Destroy(flash, PlaceholderFlashTime);
    }
}
