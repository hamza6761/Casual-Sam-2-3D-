using UnityEngine;

// Goes on the grenade prefab. Doesn't care about the mesh, so the placeholder cube can be swapped out later.
[RequireComponent(typeof(Rigidbody))]
public class Grenade : MonoBehaviour
{
    [Header("Fuse")]
    [Tooltip("Explodes after this many seconds if it never hits anything. Keep it longer than the flight time.")]
    [SerializeField] private float fuseTime = 3f;

    [Header("Explosion")]
    [SerializeField] private int explosionDamage = 20;
    [SerializeField] private float explosionRadius = 3f;
    [Tooltip("How hard the player gets pushed. Set to 0 to turn knockback off.")]
    [SerializeField] private float knockbackForce = 8f;
    [Tooltip("Which layers the explosion checks for the player. Everything by default.")]
    [SerializeField] private LayerMask explosionMask = ~0;
    [Tooltip("Optional effect spawned on explode. Leave empty for a quick placeholder flash.")]
    [SerializeField] private GameObject explosionEffectPrefab;

    private Rigidbody rb;
    private float fuseTimer;
    private bool exploded = false; // two collisions in one frame could blow it up twice

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        fuseTimer = fuseTime;
    }

    private void Update()
    {
        fuseTimer -= Time.deltaTime;
        if (fuseTimer <= 0f)
            Explode();
    }

    // ignoreColliders = the thrower, so it doesn't blow up inside the enemy that threw it
    public void Launch(Vector3 velocity, Collider[] ignoreColliders)
    {
        foreach (Collider mine in GetComponentsInChildren<Collider>())
        {
            foreach (Collider other in ignoreColliders)
            {
                if (other != null)
                    Physics.IgnoreCollision(mine, other);
            }
        }

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // stops it tunneling through thin walls
        rb.linearVelocity = velocity;
    }

    private void OnCollisionEnter(Collision collision)
    {
        Explode();
    }

    private void Explode()
    {
        if (exploded) return;
        exploded = true;

        Explosion.Explode(transform.position, explosionRadius, explosionDamage, knockbackForce, explosionMask, explosionEffectPrefab);
        Destroy(gameObject);
    }

    // used when the enemy has no grenade prefab assigned
    public static Grenade CreatePlaceholder(Vector3 position)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Grenade (Placeholder)";
        cube.transform.position = position;
        cube.transform.localScale = Vector3.one * 0.3f;
        Grenade grenade = cube.AddComponent<Grenade>(); // RequireComponent adds the Rigidbody
        return grenade;
    }
}
