using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    // Speed values for walking and sprinting.
    [SerializeField] private float WalkSpeed = 5.5f;
    [SerializeField] private float RunningSpeed = 9.0f;

    // Jump and gravity settings.
    [SerializeField] private float JumpForce = 8.0f;
    [SerializeField] private float Gravity = 20.0f;

    // Mouse look settings.
    [SerializeField] private float LookSensitivity = 0.2f;
    [SerializeField] private float LookAngleLimit = 90.0f;

    // Crouch setup.
    [SerializeField] private Key CrouchKey = Key.LeftCtrl;
    [SerializeField] private float CrouchHeight = 1.0f;
    [SerializeField] private float CrouchSpeed = 2.75f;

    // Dash setup (Q to dash).
    [SerializeField] private Key DashKey = Key.Q;
    [SerializeField] private float DashSpeed = 25.0f;
    [SerializeField] private float DashDuration = 0.15f;
    [SerializeField] private float DashCooldown = 1.0f;

    // Slide setup (sprint first, then crouch on or just above the ground).
    [SerializeField]
    [Tooltip("Extra speed added on top of RunningSpeed when the slide starts. Keep it small.")]
    private float SlideSpeedBoost = 2.5f;

    [SerializeField]
    [Tooltip("How fast the slide slows down (units per second). Higher = shorter slide.")]
    private float SlideDeceleration = 14.0f;

    [SerializeField]
    [Tooltip("Max slide time in seconds, in case the player is still going after this.")]
    private float SlideMaxDuration = 1.0f;

    [SerializeField]
    [Tooltip("How close to the ground (from the feet) the player must be to start a slide while falling.")]
    private float SlideGroundDistance = 0.6f;

    // Main camera and character controller refs.
    private Camera mainCamera;
    private CharacterController characterController;

    // Input actions from the input system.
    private InputAction moveInput;
    private InputAction runInput;
    private InputAction jumpInput;

    // Other scripts (like Pickup, while rotating a held object) can set this false to freeze camera look.
    public bool lookEnabled = true;

    // Current move state.
    private float currentMoveSpeed = 0f;
    private Vector3 moveDirection = Vector3.zero;
    private float lookAngle = 0f;
    private bool jumped = false;

    // Crouch state and saved standing size.
    private bool isCrouching = false;
    private float standingHeight;
    private Vector3 standingCenter;
    private Vector3 standingCameraLocalPosition;

    // Slide state.
    private bool isSliding = false;
    private Vector3 slideDirection = Vector3.zero;
    private float slideSpeed = 0f;
    private float slideTimeRemaining = 0f;
    private float slideAirTime = 0f;
    private bool sprintWasHeld = false;
    private const float SlideAirGrace = 0.1f;

    // Dash state.
    private bool isDashing = false;
    private Vector3 dashVelocity = Vector3.zero;
    private float dashTimeRemaining = 0f;
    private float dashCooldownTimer = 0f;
    [SerializeField]
    private bool dashAvailable = true;
    [SerializeField]
    [Tooltip("Multiplier applied to the camera's vertical component when dashing.\n~0.93 gives ~3-4 units vertical for DashSpeed=25 and DashDuration=0.15.")]
    private float DashVerticalMultiplier = 0.93f;

    [SerializeField]
    [Tooltip("How much of the dash's vertical speed is carried into normal movement after the dash ends. 0 = none.")]
    [Range(0f, 1f)]
    private float PostDashVerticalCarry = 0f;

    // Explosion knockback.
    [SerializeField]
    [Tooltip("How fast explosion knockback fades out (units per second). Higher = shorter push.")]
    private float KnockbackDecay = 20.0f;
    private Vector3 knockbackVelocity = Vector3.zero;

    private void Start()
    {
        // Grab the needed refs.
        mainCamera = GetComponentInChildren<Camera>();
        characterController = GetComponent<CharacterController>();

        // Find the input actions.
        moveInput = InputSystem.actions.FindAction("Move");
        runInput = InputSystem.actions.FindAction("Sprint");
        jumpInput = InputSystem.actions.FindAction("Jump");

        // Lock the cursor so the camera feels like a normal FPS.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        currentMoveSpeed = WalkSpeed;

        jumpInput.started += Jumped;

        // Save the normal character size before crouching.
        standingHeight = characterController.height;
        standingCenter = characterController.center;
        standingCameraLocalPosition = mainCamera.transform.localPosition;

        // Dash is ready when the player starts on the ground.
        dashAvailable = characterController.isGrounded;
    }

    private void Update()
    {
        // Read movement and mouse input.
        Vector2 moveVector = moveInput.ReadValue<Vector2>();
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        // Jump flag resets if the player is not on the ground.
        if (!characterController.isGrounded)
            jumped = false;

        // Dash can be used again after landing.
        if (characterController.isGrounded)
            dashAvailable = true;

        // Handle crouch, dash, slide, then move.
        HandleCrouch();
        HandleDash(moveVector);
        HandleSlide(moveVector);

        // Pick the speed for this frame: crouching is slowest, then sprint, then normal walking.
        if (isCrouching)
        {
            currentMoveSpeed = CrouchSpeed;
        }
        else if (runInput.IsPressed())
        {
            currentMoveSpeed = RunningSpeed;
        }
        else
        {
            currentMoveSpeed = WalkSpeed;
        }

        HandleMovement(moveVector);
        if (lookEnabled)
            HandleLooking(mouseDelta);
    }

    private void HandleMovement(Vector2 moveVector)
    {
        // Dash overrides normal movement while it is active.
        if (isDashing)
        {
            characterController.Move(dashVelocity * Time.deltaTime);
            return;
        }

        // Get the camera-facing directions.
        Vector3 forward = transform.TransformDirection(Vector3.forward);
        Vector3 right = transform.TransformDirection(Vector3.right);

        float oldY = moveDirection.y;

        // Turn input into movement speed.
        Vector2 newSpeed = new Vector2(moveVector.y * currentMoveSpeed, moveVector.x * currentMoveSpeed);

        // Build the move direction from camera facing.
        moveDirection = (forward * newSpeed.x) + (right * newSpeed.y);

        // Sliding ignores the keys and keeps going the way it started.
        if (isSliding)
            moveDirection = slideDirection * slideSpeed;

        // Jumping only works from the ground. Otherwise keep the vertical speed we already had.
        bool isJumpingOffTheGround = jumped && characterController.isGrounded;
        if (isJumpingOffTheGround)
        {
            moveDirection.y = JumpForce;
        }
        else
        {
            moveDirection.y = oldY;
        }

        // Apply gravity when the player is in the air.
        if (!characterController.isGrounded)
            moveDirection.y -= Gravity * Time.deltaTime;

        // Move the character controller, plus any explosion knockback.
        Vector3 totalVelocity = moveDirection + knockbackVelocity;
        characterController.Move(totalVelocity * Time.deltaTime);

        float knockbackFade = KnockbackDecay * Time.deltaTime;
        knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackFade);
    }

    // Sideways push fades out, the upward part goes into moveDirection so gravity handles it like a jump.
    public void AddKnockback(Vector3 force)
    {
        Vector3 sidewaysForce = new Vector3(force.x, 0f, force.z);
        knockbackVelocity += sidewaysForce;

        if (force.y > 0f)
        {
            float startingUpSpeed = Mathf.Max(moveDirection.y, 0f);
            moveDirection.y = startingUpSpeed + force.y;
        }
    }

    private void HandleCrouch()
    {
        // Check if crouch is being held.
        bool crouchHeld = Keyboard.current[CrouchKey].isPressed;

        // No change, so stop here.
        if (crouchHeld == isCrouching)
            return;

        isCrouching = crouchHeld;

        // Pick the target height.
        float targetHeight;
        if (isCrouching)
        {
            targetHeight = CrouchHeight;
        }
        else
        {
            targetHeight = standingHeight;
        }
        float heightDifference = standingHeight - targetHeight;

        // Change the capsule height.
        characterController.height = targetHeight;

        // Keep the bottom of the player in the same place.
        characterController.center = standingCenter - new Vector3(0f, heightDifference / 2f, 0f);

        // Move the camera with the body.
        Vector3 cameraPosition = standingCameraLocalPosition;
        cameraPosition.y -= heightDifference;
        mainCamera.transform.localPosition = cameraPosition;
    }

    private void HandleSlide(Vector2 moveVector)
    {
        bool crouchHeld = Keyboard.current[CrouchKey].isPressed;
        bool sprintHeld = runInput.IsPressed();

        if (isSliding)
        {
            // Slow down fast until we hit crouch speed.
            slideSpeed = Mathf.MoveTowards(slideSpeed, CrouchSpeed, SlideDeceleration * Time.deltaTime);
            slideTimeRemaining -= Time.deltaTime;

            // Count time in the air. Being close to the ground doesn't count as air.
            bool inAir = !characterController.isGrounded && !IsNearGround();
            if (inAir)
            {
                slideAirTime += Time.deltaTime;
            }
            else
            {
                slideAirTime = 0f;
            }

            // Slide ends on crouch release, jump, dash, air, or when it runs out.
            if (!crouchHeld || jumped || isDashing || slideAirTime > SlideAirGrace
                || slideTimeRemaining <= 0f || slideSpeed <= CrouchSpeed + 0.01f)
                isSliding = false;
        }
        else
        {
            // Sprint must already be held when crouch gets pressed.
            // Needs to be on the ground, or falling and close to it (not going up).
            bool canSlide = Keyboard.current[CrouchKey].wasPressedThisFrame
                && sprintHeld && sprintWasHeld
                && !isDashing && !jumped
                && moveVector.sqrMagnitude > 0.01f
                && (characterController.isGrounded || (moveDirection.y <= 0f && IsNearGround()));

            if (canSlide)
            {
                // Slide the way the player is moving right now.
                Vector3 forward = transform.TransformDirection(Vector3.forward);
                Vector3 right = transform.TransformDirection(Vector3.right);
                slideDirection = (forward * moveVector.y + right * moveVector.x).normalized;

                // Start a bit faster than sprint.
                slideSpeed = RunningSpeed + SlideSpeedBoost;
                slideTimeRemaining = SlideMaxDuration;
                slideAirTime = 0f;
                isSliding = true;
            }
        }

        sprintWasHeld = sprintHeld;
    }

    private bool IsNearGround()
    {
        // Shoot a ray down from the feet to see if the ground is close.
        Bounds bounds = characterController.bounds;
        Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + 0.05f, bounds.center.z);
        return Physics.Raycast(origin, Vector3.down, SlideGroundDistance + 0.05f, ~0, QueryTriggerInteraction.Ignore);
    }

    private void HandleDash(Vector2 moveVector)
    {
        // Lower the cooldown timer.
        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;

        // Dash is running.
        if (isDashing)
        {
            dashTimeRemaining -= Time.deltaTime;

            if (dashTimeRemaining <= 0f)
            {
                isDashing = false;

                // Keep a little upward/downward speed after the dash ends.
                moveDirection.y = dashVelocity.y * PostDashVerticalCarry;
            }

            return;
        }

        // Dash checks.
        if (dashCooldownTimer > 0f || !dashAvailable || !Keyboard.current[DashKey].wasPressedThisFrame)
            return;

        // Get the camera direction for the dash.
        Vector3 cameraForward = mainCamera.transform.forward;
        Vector3 cameraRight = mainCamera.transform.right;

        // Ignore camera pitch for horizontal movement.
        Vector3 cameraForwardH = new Vector3(cameraForward.x, 0f, cameraForward.z);
        Vector3 cameraRightH = new Vector3(cameraRight.x, 0f, cameraRight.z);

        // Choose dash direction from input or camera facing.
        Vector3 horizontalInputDir;
        if (moveVector.sqrMagnitude > 0.0001f)
            horizontalInputDir = (cameraForwardH.normalized * moveVector.y + cameraRightH.normalized * moveVector.x);
        else
            horizontalInputDir = cameraForwardH.normalized;

        Vector3 horizontalVelocity;
        if (horizontalInputDir.sqrMagnitude > 0.0001f)
        {
            horizontalVelocity = horizontalInputDir.normalized * DashSpeed;
        }
        else
        {
            horizontalVelocity = Vector3.zero;
        }

        // Use the camera's up/down look to set vertical dash speed.
        float dashVerticalSpeed = cameraForward.y * DashSpeed * DashVerticalMultiplier;

        // Save the dash velocity.
        dashVelocity = horizontalVelocity + Vector3.up * dashVerticalSpeed;
        isDashing = true;
        dashAvailable = false;
        dashTimeRemaining = DashDuration;
        dashCooldownTimer = DashCooldown;
    }

    private void HandleLooking(Vector2 mouseDelta)
    {
        // Look up and down, but keep it in a safe range.
        lookAngle += -mouseDelta.y * LookSensitivity;
        lookAngle = Mathf.Clamp(lookAngle, -LookAngleLimit, LookAngleLimit);

        // Apply camera pitch.
        mainCamera.transform.localRotation = Quaternion.Euler(lookAngle, 0, 0);

        // Turn the player left and right.
        transform.rotation *= Quaternion.Euler(0, mouseDelta.x * LookSensitivity, 0);
    }

    private void Jumped(InputAction.CallbackContext _)
    {
        // Jump flag is set when the jump button is pressed.
        jumped = true;
    }
}