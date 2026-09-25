using System.Collections;
using UnityEngine;

// Flying drone enemy - wanders, chases the player, throws grenades, crashes and explodes on death.
[RequireComponent(typeof(Rigidbody))]
public class EnemyAI : MonoBehaviour
{
    public enum State { Idle, Chase, Attack, Dead }

    [Header("References")]
    [Tooltip("The player. Leave empty to find the PlayerController in the scene automatically.")]
    [SerializeField] private Transform player;
    [Tooltip("Where grenades spawn from. Leave empty to throw from the enemy's center.")]
    [SerializeField] private Transform throwPoint;
    [Tooltip("Grenade prefab (needs a Rigidbody, a Collider and the Grenade script). Leave empty to use a placeholder cube.")]
    [SerializeField] private Grenade grenadePrefab;

    [Header("Health")]
    [SerializeField] private int maxHealth = 100;
    [Tooltip("Shows HP above the enemy's head. For testing only.")]
    [SerializeField] private bool showDebugHealth = true;

    [Header("Idle / Wander")]
    [Tooltip("Size of the box it wanders in, centered on where it was placed.\nY = how much its height can vary (0 = always flies at its start height).")]
    [SerializeField] private Vector3 wanderBounds = new Vector3(10f, 0f, 10f);
    [SerializeField] private float idleSpeed = 3f;
    [Tooltip("How long it hovers in place after reaching a wander point.")]
    [SerializeField] private float idlePauseTime = 0.5f;

    [Header("Detection")]
    [SerializeField] private float detectionRange = 20f;
    [Tooltip("Extra distance past detectionRange before it gives up the chase (stops it flickering between states at the edge).")]
    [SerializeField] private float loseRangeBuffer = 5f;

    [Header("Chase")]
    [Tooltip("How far in front of the player (horizontally) it tries to hover.")]
    [SerializeField] private float chaseOffsetDistance = 5f;
    [Tooltip("How far above the player it tries to hover.")]
    [SerializeField] private float chaseOffsetHeight = 3f;
    [Tooltip("Approach speed. Match this to your player (PlayerController: WalkSpeed 5.5, RunningSpeed 9).")]
    [SerializeField] private float chaseSpeed = 9f;
    [Tooltip("If the player gets closer than this, the enemy backs away.")]
    [SerializeField] private float tooCloseDistance = 4f;
    [Tooltip("Back-away speed. Keep it lower than the player's speed so they can catch it.")]
    [SerializeField] private float retreatSpeed = 4.5f;

    [Header("Movement Feel")]
    [Tooltip("How quickly it speeds up / slows down. Lower = floatier.")]
    [SerializeField] private float acceleration = 15f;
    [SerializeField] private float turnSpeed = 5f;

    [Header("Wall Avoidance")]
    [Tooltip("Roughly the enemy's radius. Used for the SphereCast so it doesn't clip into walls.")]
    [SerializeField] private float obstacleCheckRadius = 0.5f;
    [Tooltip("How far ahead it looks for walls.")]
    [SerializeField] private float obstacleCheckDistance = 1.5f;
    [Tooltip("What counts as a wall. Everything by default.")]
    [SerializeField] private LayerMask obstacleMask = ~0;

    [Header("Attack")]
    [SerializeField] private float attackCooldown = 3f;
    [Tooltip("Wait before the first attack after it spots the player.")]
    [SerializeField] private float firstAttackDelay = 1f;
    [Tooltip("Time between grenades in the same burst.")]
    [SerializeField] private float timeBetweenGrenades = 0.35f;
    [Tooltip("Roughly how fast grenades fly. The arc is worked out so they land where the player was.")]
    [SerializeField] private float grenadeThrowSpeed = 12f;

    [Header("Death")]
    [SerializeField] private int deathExplosionDamage = 8;
    [SerializeField] private float deathExplosionRadius = 2.5f;
    [Tooltip("How hard the player gets pushed. Set to 0 to turn knockback off.")]
    [SerializeField] private float deathKnockbackForce = 5f;
    [Tooltip("Explodes after this long even if it never hits anything.")]
    [SerializeField] private float deathFuseTime = 5f;
    [Tooltip("Which layers the explosion checks for the player. Everything by default.")]
    [SerializeField] private LayerMask explosionMask = ~0;
    [Tooltip("Optional effect spawned when it explodes. Leave empty for a quick placeholder flash.")]
    [SerializeField] private GameObject explosionEffectPrefab;

    private const float SlowDownDistance = 2f;       // start easing off this far out so we don't overshoot
    private const float WanderArriveDistance = 0.3f;
    private const float WanderGiveUpTime = 6f;        // probably stuck on a wall by then
    private const float SkinWidth = 0.05f;
    private const float MinGrenadeFlightTime = 0.3f;  // point blank throws get way too fast otherwise
    private const float DeathCollisionGrace = 0.15f;  // so the thing that killed us doesn't set it off instantly
    private const float DebugHealthHeight = 1.2f;

    // angles to try when something's in the way
    private static readonly float[] SteerAngles = { 45f, -45f, 90f, -90f };

    private State currentState = State.Idle;
    private int currentHealth;
    private Rigidbody rb;
    private Collider[] myColliders;
    private Vector3 startPosition;
    private Vector3 currentVelocity;

    private Vector3 wanderTarget;
    private float idlePauseTimer;
    private float wanderGiveUpTimer;

    private float attackTimer;

    private float timeSinceDeath;
    private bool exploded = false;

    private readonly RaycastHit[] castHits = new RaycastHit[8];
    private GUIStyle debugHealthStyle;

    public State CurrentState
    {
        get { return currentState; }
    }

    private void Start()
    {
        // kinematic while alive, we move it ourselves until it dies
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        myColliders = GetComponentsInChildren<Collider>();
        startPosition = transform.position;
        currentHealth = maxHealth;

        if (player == null)
        {
            PlayerController playerController = FindAnyObjectByType<PlayerController>();
            if (playerController != null)
                player = playerController.transform;
            else
                Debug.LogWarning($"{name}: no player assigned and no PlayerController found in the scene.");
        }

        PickNewWanderPoint();
    }

    private void Update()
    {
        switch (currentState)
        {
            case State.Idle:
                UpdateIdle();
                break;
            case State.Chase:
                UpdateChase();
                break;
            case State.Attack:
                UpdateAttack();
                break;
            case State.Dead:
                UpdateDead();
                break;
        }
    }

    // Idle

    private void UpdateIdle()
    {
        if (PlayerInDetectionRange())
        {
            StartChase();
            return;
        }

        if (idlePauseTimer > 0f)
        {
            idlePauseTimer -= Time.deltaTime;
            FlyWith(Vector3.zero);
            return;
        }

        // new point once we arrive or get stuck
        wanderGiveUpTimer -= Time.deltaTime;
        float distanceToWanderTarget = Vector3.Distance(transform.position, wanderTarget);
        bool arrived = distanceToWanderTarget < WanderArriveDistance;
        bool gaveUp = wanderGiveUpTimer <= 0f;
        if (arrived || gaveUp)
        {
            PickNewWanderPoint();
            idlePauseTimer = idlePauseTime;
            FlyWith(Vector3.zero);
            return;
        }

        FlyWith(ArriveVelocity(wanderTarget, idleSpeed));
        FaceDirection(currentVelocity);
    }

    private void PickNewWanderPoint()
    {
        // based on the start position, not the current one, so it never drifts off over time
        Vector3 half = wanderBounds * 0.5f;
        float randomX = Random.Range(-half.x, half.x);
        float randomY = Random.Range(-half.y, half.y);
        float randomZ = Random.Range(-half.z, half.z);
        Vector3 randomOffset = new Vector3(randomX, randomY, randomZ);
        wanderTarget = startPosition + randomOffset;

        wanderGiveUpTimer = WanderGiveUpTime;
    }

    // Detection

    private bool PlayerInDetectionRange()
    {
        if (player == null) return false;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        return distanceToPlayer <= detectionRange;
    }

    // bigger range than detection so it doesn't flicker at the edge
    private bool PlayerLost()
    {
        if (player == null) return true;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        float loseRange = detectionRange + loseRangeBuffer;
        return distanceToPlayer > loseRange;
    }

    // Chase

    private void StartChase()
    {
        currentState = State.Chase;
        attackTimer = firstAttackDelay;
    }

    private void UpdateChase()
    {
        if (PlayerLost())
        {
            currentState = State.Idle;
            PickNewWanderPoint();
            return;
        }

        MoveAroundPlayer();

        attackTimer -= Time.deltaTime;
        if (attackTimer <= 0f)
            StartCoroutine(AttackRoutine());
    }

    // hover diagonally in front of + above the player, back off if they get too close
    private void MoveAroundPlayer()
    {
        Vector3 playerPos = player.position;

        // flatten so looking up/down doesn't move the hover point
        Vector3 playerForward = player.forward;
        playerForward.y = 0f;
        if (playerForward.sqrMagnitude < 0.001f)
            playerForward = Vector3.forward;
        playerForward.Normalize();

        Vector3 forwardOffset = playerForward * chaseOffsetDistance;
        Vector3 heightOffset = Vector3.up * chaseOffsetHeight;
        Vector3 hoverPoint = playerPos + forwardOffset + heightOffset;

        Vector3 desiredVelocity;
        float distanceToPlayer = Vector3.Distance(transform.position, playerPos);
        if (distanceToPlayer < tooCloseDistance)
        {
            Vector3 away = transform.position - playerPos;
            away.y = 0f;
            if (away.sqrMagnitude < 0.001f)
                away = playerForward; // right on top of them, just pick a direction
            Vector3 retreatOffset = away.normalized * tooCloseDistance;
            Vector3 retreatPoint = transform.position + retreatOffset;
            retreatPoint.y = hoverPoint.y;
            Vector3 retreatDirection = (retreatPoint - transform.position).normalized;
            desiredVelocity = retreatDirection * retreatSpeed;
        }
        else
        {
            desiredVelocity = ArriveVelocity(hoverPoint, chaseSpeed);
        }

        FlyWith(desiredVelocity);

        Vector3 toPlayer = playerPos - transform.position;
        FaceDirection(toPlayer);
    }

    // Attack

    // same movement as chase, just throwing grenades at the same time
    private void UpdateAttack()
    {
        if (player == null) return;
        MoveAroundPlayer();
    }

    private IEnumerator AttackRoutine()
    {
        currentState = State.Attack;

        int grenadeCount = RollGrenadeCount();
        for (int i = 0; i < grenadeCount; i++)
        {
            ThrowGrenade();
            if (i < grenadeCount - 1)
                yield return new WaitForSeconds(timeBetweenGrenades);
        }

        attackTimer = attackCooldown;
        currentState = State.Chase;
    }

    // weighted toward 1 so a burst of 3 feels special
    private int RollGrenadeCount()
    {
        float roll = Random.value;
        if (roll < 0.5f) return 1; // 50%
        if (roll < 0.8f) return 2; // 30%
        return 3;                  // 20%
    }

    private void ThrowGrenade()
    {
        if (player == null) return;

        Vector3 spawnPos;
        if (throwPoint != null)
        {
            spawnPos = throwPoint.position;
        }
        else
        {
            spawnPos = transform.position;
        }

        Grenade grenade;
        if (grenadePrefab != null)
        {
            grenade = Instantiate(grenadePrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            grenade = Grenade.CreatePlaceholder(spawnPos);
        }

        // projectile formula (start + v*t + 0.5*g*t^2) solved for v, so it lands where the player is now
        Vector3 toTarget = player.position - spawnPos;
        float distanceToTarget = toTarget.magnitude;
        float flightTime = distanceToTarget / grenadeThrowSpeed;
        flightTime = Mathf.Max(MinGrenadeFlightTime, flightTime);

        Vector3 straightLineVelocity = toTarget / flightTime;
        Vector3 gravityCorrection = 0.5f * Physics.gravity * flightTime;
        Vector3 launchVelocity = straightLineVelocity - gravityCorrection;

        grenade.Launch(launchVelocity, myColliders);
    }

    // Movement

    // slows down near the target so it glides in instead of overshooting
    private Vector3 ArriveVelocity(Vector3 target, float maxSpeed)
    {
        Vector3 toTarget = target - transform.position;
        float distance = toTarget.magnitude;
        if (distance < 0.01f) return Vector3.zero;

        float slowDownPercent = Mathf.Clamp01(distance / SlowDownDistance);
        float speed = maxSpeed * slowDownPercent;
        Vector3 direction = toTarget / distance;
        return direction * speed;
    }

    private void FlyWith(Vector3 desiredVelocity)
    {
        desiredVelocity = AvoidObstacles(desiredVelocity);

        float maxSpeedChange = acceleration * Time.deltaTime;
        currentVelocity = Vector3.MoveTowards(currentVelocity, desiredVelocity, maxSpeedChange);

        // last check so a single step never ends up inside a wall
        Vector3 step = currentVelocity * Time.deltaTime;
        if (step.sqrMagnitude > 0f)
        {
            Vector3 stepDirection = step.normalized;
            float checkDistance = step.magnitude + SkinWidth;
            if (IsBlocked(stepDirection, checkDistance))
            {
                currentVelocity = Vector3.zero;
                return;
            }
        }

        transform.position += step;
    }

    // try turning left/right a bit, otherwise just stop (no pathfinding, getting stuck is fine for now)
    private Vector3 AvoidObstacles(Vector3 desiredVelocity)
    {
        if (desiredVelocity.sqrMagnitude < 0.0001f) return desiredVelocity;

        Vector3 direction = desiredVelocity.normalized;
        float speed = desiredVelocity.magnitude;

        if (!IsBlocked(direction, obstacleCheckDistance))
            return desiredVelocity;

        foreach (float angle in SteerAngles)
        {
            Quaternion turn = Quaternion.AngleAxis(angle, Vector3.up);
            Vector3 newDirection = turn * direction;
            if (!IsBlocked(newDirection, obstacleCheckDistance))
                return newDirection * speed;
        }

        return Vector3.zero;
    }

    private bool IsBlocked(Vector3 direction, float distance)
    {
        int hitCount = Physics.SphereCastNonAlloc(transform.position, obstacleCheckRadius, direction, castHits,
            distance, obstacleMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = castHits[i];

            // already overlapping - ignore it or we'd be frozen forever
            if (hit.distance <= 0f) continue;

            Collider hitCollider = hit.collider;
            if (hitCollider.transform.IsChildOf(transform)) continue;

            Grenade hitGrenade = hitCollider.GetComponentInParent<Grenade>();
            if (hitGrenade != null) continue;

            return true;
        }
        return false;
    }

    // yaw only, drones don't tilt their whole body
    private void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        float turnAmount = turnSpeed * Time.deltaTime;
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnAmount);
    }

    // Health / death

    // called by ThrowDamage
    public void TakeDamage(int amount)
    {
        if (currentState == State.Dead) return;

        currentHealth -= amount;
        currentHealth = Mathf.Max(currentHealth, 0);
        if (currentHealth <= 0)
            Die();
    }

    private void Die()
    {
        currentState = State.Dead;
        StopAllCoroutines(); // cancel any grenade burst
        timeSinceDeath = 0f;

        // let physics take over so it falls
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = currentVelocity;               // keep momentum instead of dropping straight down
        rb.angularVelocity = Random.insideUnitSphere * 3f; // bit of tumble so it looks like a crash
    }

    private void UpdateDead()
    {
        timeSinceDeath += Time.deltaTime;
        if (timeSinceDeath >= deathFuseTime)
            ExplodeOnDeath();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (currentState != State.Dead) return;
        if (timeSinceDeath < DeathCollisionGrace) return;
        ExplodeOnDeath();
    }

    // in case it was already touching something when it died, Enter won't fire again for that
    private void OnCollisionStay(Collision collision)
    {
        OnCollisionEnter(collision);
    }

    private void ExplodeOnDeath()
    {
        if (exploded) return;
        exploded = true;

        Explosion.Explode(transform.position, deathExplosionRadius, deathExplosionDamage, deathKnockbackForce, explosionMask, explosionEffectPrefab);
        Destroy(gameObject);
    }

    // Debug

    // testing only, turn off showDebugHealth to hide
    private void OnGUI()
    {
        if (!showDebugHealth) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 textWorldPos = transform.position + Vector3.up * DebugHealthHeight;
        Vector3 screenPos = cam.WorldToScreenPoint(textWorldPos);
        if (screenPos.z <= 0f) return; // behind the camera

        if (debugHealthStyle == null)
        {
            debugHealthStyle = new GUIStyle(GUI.skin.label);
            debugHealthStyle.alignment = TextAnchor.MiddleCenter;
            debugHealthStyle.fontStyle = FontStyle.Bold;
            debugHealthStyle.normal.textColor = Color.red;
        }

        // GUI y goes top-down, screen y goes bottom-up
        float guiX = screenPos.x - 60f;
        float guiY = Screen.height - screenPos.y - 10f;
        Rect rect = new Rect(guiX, guiY, 120f, 20f);

        string healthText = $"HP: {currentHealth} / {maxHealth}";
        GUI.Label(rect, healthText, debugHealthStyle);
    }

    // scene view only, never shows in the game
    private void OnDrawGizmosSelected()
    {
        // in the editor the current position is the start position
        Vector3 center;
        if (Application.isPlaying)
        {
            center = startPosition;
        }
        else
        {
            center = transform.position;
        }

        Gizmos.color = Color.cyan;
        Vector3 size = wanderBounds;
        size.y = Mathf.Max(size.y, 0.1f); // flat box is hard to see
        Gizmos.DrawWireCube(center, size);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = new Color(1f, 0.5f, 0f);
        float loseRange = detectionRange + loseRangeBuffer;
        Gizmos.DrawWireSphere(transform.position, loseRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, tooCloseDistance);
    }
}
