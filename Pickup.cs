using UnityEngine;
using UnityEngine.InputSystem;

public class Pickup : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject player; // used to exclude the player from the wall-clip raycast
    [SerializeField] private Camera playerCamera; // used to aim pickup/throw, defaults to Camera.main if left empty
    [SerializeField] private Transform holdPos; // where held objects sit - make this a child of the camera

    [Header("Pickup Settings")]
    [SerializeField] private float throwSpeed = 12f; // how fast thrown objects fly, in meters per second
    [SerializeField] private float pickUpRange = 5f; // how far away we can pick things up from
    [SerializeField] private float rotationSensitivity = 0.1f; // how fast the held object rotates with the mouse

    private GameObject heldObj; // object we're currently holding
    private Rigidbody heldObjRb; // its rigidbody
    private Collider[] heldObjColliders; // disabled while held, re-enabled on release
    private int heldObjOriginalLayer; // layer to restore when we let go
    private bool canDrop = true; // false while rotating, so we don't drop/throw by accident
    private int holdLayerIndex;
    private PlayerController playerController; // used to freeze camera look while rotating a held object

    void Start()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;
        if (playerCamera == null)
            Debug.LogWarning("Pickup: no camera assigned and no MainCamera found. Assign playerCamera in the Inspector.");

        if (player == null)
            Debug.LogWarning("Pickup: player is not assigned in the Inspector.");
        else
            playerController = player.GetComponent<PlayerController>();

        // HoldLayer switching is disabled for now (see PickUpObject/ReleaseHeldObject) - keeping the
        // lookup around since we'll likely bring it back per-item later.
        holdLayerIndex = LayerMask.NameToLayer("HoldLayer"); // rename this string if your layer has a different name
    }

    void Update()
    {
        if (playerCamera == null) return;

        if (Keyboard.current[Key.E].wasPressedThisFrame)
        {
            if (heldObj == null)
            {
                TryPickUp();
            }
            else if (canDrop)
            {
                StopClipping();
                DropObject();
            }
        }

        if (heldObj != null)
        {
            RotateObject();

            if (Mouse.current.leftButton.wasPressedThisFrame && canDrop)
            {
                StopClipping();
                ThrowObject();
            }
        }
    }

    // looks for something to pick up in front of the camera
    void TryPickUp()
    {
        if (Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out RaycastHit hit, pickUpRange))
        {
            if (hit.transform.CompareTag("canPickUp"))
            {
                PickUpObject(hit.transform.gameObject);
            }
        }
    }

    void PickUpObject(GameObject pickUpObj)
    {
        // some objects (like a static rock) won't have a Rigidbody until now - add one if it's missing
        if (!pickUpObj.TryGetComponent(out Rigidbody rb))
            rb = pickUpObj.AddComponent<Rigidbody>();

        heldObj = pickUpObj;
        heldObjRb = rb;
        heldObjOriginalLayer = heldObj.layer; // remember this so we can put it back later

        // kinematic so it's not affected by gravity/forces while held
        heldObjRb.isKinematic = true;
        heldObjRb.linearVelocity = Vector3.zero;
        heldObjRb.angularVelocity = Vector3.zero;

        // turn off its colliders so it can't physically touch anything at all while held,
        // no matter what's set up in the layer collision matrix
        heldObjColliders = heldObj.GetComponentsInChildren<Collider>(true);
        foreach (Collider col in heldObjColliders)
            col.enabled = false;

        heldObj.transform.SetParent(holdPos);
        heldObj.transform.localPosition = Vector3.zero;
        heldObj.transform.localRotation = Quaternion.identity;

        // HoldLayer switch disabled for now - deciding this per item later, no tag system yet
        // if (holdLayerIndex != -1)
        //     heldObj.layer = holdLayerIndex;

        if (heldObj.TryGetComponent(out ThrowDamage throwDamage))
            throwDamage.Disarm();
    }

    void DropObject()
    {
        ReleaseHeldObject();
    }

    void ThrowObject()
    {
        GameObject thrown = heldObj;
        Rigidbody thrownRb = heldObjRb;

        ReleaseHeldObject();

        // set velocity directly instead of AddForce, so throwSpeed is just meters per second
        // and isn't thrown off by the object's mass or leftover velocity from being carried
        thrownRb.linearVelocity = playerCamera.transform.forward * throwSpeed;

        if (thrown.TryGetComponent(out ThrowDamage throwDamage))
            throwDamage.Arm();
    }

    // shared cleanup for drop and throw - this is where the object becomes a real physics object again
    void ReleaseHeldObject()
    {
        // HoldLayer switch disabled for now, see PickUpObject()
        // heldObj.layer = heldObjOriginalLayer;
        heldObj.transform.SetParent(null);

        foreach (Collider col in heldObjColliders)
            col.enabled = true;
        heldObjColliders = null;

        heldObjRb.isKinematic = false;
        heldObjRb.useGravity = true;
        heldObjRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // stops fast throws tunneling through walls
        heldObjRb.linearVelocity = Vector3.zero; // clear whatever velocity it picked up while being carried around
        heldObjRb.angularVelocity = Vector3.zero;

        heldObj = null;
        heldObjRb = null;
    }

    void RotateObject()
    {
        if (Keyboard.current[Key.R].isPressed) // hold R to rotate
        {
            canDrop = false;
            if (playerController != null)
                playerController.lookEnabled = false; // stop the camera turning from the same mouse input

            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            heldObj.transform.Rotate(Vector3.down, mouseDelta.x * rotationSensitivity);
            heldObj.transform.Rotate(Vector3.right, mouseDelta.y * rotationSensitivity);
        }
        else
        {
            canDrop = true;
            if (playerController != null)
                playerController.lookEnabled = true;
        }
    }

    // stops the held object from ending up stuck inside a wall when we drop/throw it
    void StopClipping()
    {
        Vector3 origin = playerCamera.transform.position;
        Vector3 direction = playerCamera.transform.forward;
        float clipRange = Vector3.Distance(heldObj.transform.position, origin);

        RaycastHit[] hits = Physics.RaycastAll(origin, direction, clipRange);
        foreach (RaycastHit hit in hits)
        {
            // skip the held object itself and the player, we only care about walls/other stuff in the way
            if (hit.rigidbody == heldObjRb || (player != null && hit.transform.IsChildOf(player.transform)))
                continue;

            heldObj.transform.position = origin + Vector3.down * 0.5f; // pull it back near the camera instead
            break;
        }
    }
}
