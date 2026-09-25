using UnityEngine;

// Put this on a pickup-able object so it can hurt enemies when thrown at them.
public class ThrowDamage : MonoBehaviour
{
    [SerializeField] private int damageAmount = 10;
    [SerializeField] private bool breakOnEnemyHit = true; // destroy this object when it hits something tagged "Enemy"
    [SerializeField] private bool breakOnGroundHit = false; // destroy this object when it hits anything that isn't tagged "Enemy"

    // Only reacts while true, so a held/dropped object can't damage or break just by touching things.
    private bool isArmed = false;

    // Pickup.cs calls this right after throwing the object.
    public void Arm()
    {
        isArmed = true;
    }

    // Pickup.cs calls this when the object gets picked up again.
    public void Disarm()
    {
        isArmed = false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!isArmed) return;
        isArmed = false; // only reacts once per throw

        if (collision.gameObject.CompareTag("Enemy"))
        {
            // InParent in case we hit a child collider
            EnemyAI enemy = collision.gameObject.GetComponentInParent<EnemyAI>();
            if (enemy != null)
                enemy.TakeDamage(damageAmount);

            if (breakOnEnemyHit)
                Destroy(gameObject);
        }
        else if (breakOnGroundHit) // hit anything that isn't an enemy - no tag needed
        {
            Destroy(gameObject);
        }
    }
}
