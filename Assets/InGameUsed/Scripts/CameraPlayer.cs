using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

public class CameraPlayer : MonoBehaviour
{
    [Header("Mode")]
    public bool firstPerson = true;            // start in first person
    public bool allowToggle = true;            // press the key below to switch 1st <-> 3rd person
    public Key toggleKey = Key.V;

    [Header("Target")]
    public Transform target;                                   // the Player
    public Vector3 pivotOffset = new Vector3(0f, 1.6f, 0f);    // roughly head/shoulder height (3rd person)

    [Header("First Person")]
    public float eyeHeight = 1.65f;            // camera height above the player's feet/pivot
    public float eyeForwardOffset = 0.15f;     // pushes the camera slightly ahead of the head so it doesn't clip into the face
    public float fpMinPitch = -80f;            // look down limit
    public float fpMaxPitch = 80f;             // look up limit
    [Tooltip("Hide the player's own model in 1st person (it still casts a shadow).")]
    public bool hideBodyInFirstPerson = true;
    public float nearClip = 0.05f;             // small near clip so walls right in front of you don't cut through

    [Header("Third Person Position")]
    public float distance = 4f;
    public float shoulderOffset = 0.7f;   // positive = camera over right shoulder

    [Header("Look")]
    public float sensitivity = 0.1f;      // new Input System mouse delta is in pixels, so this is much smaller
    public float minPitch = -30f;         // 3rd person limits
    public float maxPitch = 60f;
    public bool lockCursor = true;

    [Header("Collision (3rd person only)")]
    public LayerMask collisionMask = ~0;   // untick the Player layer here
    public float collisionPadding = 0.25f;
    public float minDistance = 0.5f;       // the camera never gets closer to the pivot than this
    [Tooltip("How fast the camera eases back out after a wall pushed it in. Pushing IN is instant (no clipping).")]
    public float zoomOutSpeed = 6f;
    [Tooltip("Turn OFF to test: if the glitch disappears, something is pushing the camera in and out.")]
    public bool useCollision = true;
    [Tooltip("Logs which object is pushing the camera in (only when it changes).")]
    public bool debugCollision = false;

    private float yaw;
    private float pitch = 15f;
    private float currentDistance;
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private string lastBlocker = "";

    private Camera cam;
    private float defaultNearClip;
    private Renderer[] bodyRenderers;
    private bool bodyHidden;

    private void Start()
    {
        yaw = transform.eulerAngles.y;
        currentDistance = new Vector3(shoulderOffset, 0f, -distance).magnitude;

        cam = GetComponent<Camera>();
        if (cam != null) defaultNearClip = cam.nearClipPlane;

        if (target != null)
            bodyRenderers = target.GetComponentsInChildren<Renderer>(true);

        ApplyModeSettings();
    }

    private void Update()
    {
        // Don't mess with the cursor while the game is paused
        if (GameData.GamePause) return;

        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (lockCursor && mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // switch between first and third person
        if (allowToggle && kb != null && kb[toggleKey].wasPressedThisFrame)
        {
            firstPerson = !firstPerson;
            ApplyModeSettings();
        }
    }

    // Called on start and whenever the mode changes: near clip + show/hide the player's body
    private void ApplyModeSettings()
    {
        if (cam != null)
            cam.nearClipPlane = firstPerson ? nearClip : defaultNearClip;

        SetBodyHidden(firstPerson && hideBodyInFirstPerson);
    }

    private void SetBodyHidden(bool hide)
    {
        if (bodyRenderers == null || bodyHidden == hide) return;
        bodyHidden = hide;

        foreach (Renderer r in bodyRenderers)
        {
            if (r == null) continue;
            // ShadowsOnly = invisible to the camera but still casts a shadow
            r.shadowCastingMode = hide
                ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                : UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }

    // LateUpdate runs after the player has moved this frame, so the camera always uses the final position.
    private void LateUpdate()
    {
        if (target == null) return;

        // mouse look (only while cursor is locked)
        Mouse mouse = Mouse.current;
        if (!GameData.GamePause && mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * sensitivity;
            pitch -= delta.y * sensitivity;
        }

        // each mode has its own pitch limits
        pitch = firstPerson
            ? Mathf.Clamp(pitch, fpMinPitch, fpMaxPitch)
            : Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);

        if (firstPerson)
            UpdateFirstPerson(rotation);
        else
            UpdateThirdPerson(rotation);
    }

    // ---------------- FIRST PERSON ----------------
    private void UpdateFirstPerson(Quaternion rotation)
    {
        // camera sits at the player's eyes, slightly forward of the head
        Vector3 flatForward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 eyePos = target.position + Vector3.up * eyeHeight + flatForward * eyeForwardOffset;

        transform.position = eyePos;
        transform.rotation = rotation;

        // the body turns with the camera instantly, so what you see and where you walk always match
        target.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    // ---------------- THIRD PERSON (your original code) ----------------
    private void UpdateThirdPerson(Quaternion rotation)
    {
        Vector3 pivot = target.position + pivotOffset;

        // direction and full length from the pivot to where the camera wants to be
        Vector3 localOffset = new Vector3(shoulderOffset, 0f, -distance);
        float maxDistance = localOffset.magnitude;
        Vector3 dir = (rotation * localOffset) / maxDistance;

        // how far out the camera is allowed to go before a wall is in the way
        float allowed = maxDistance;
        string blocker = "";
        if (useCollision)
        {
            int count = Physics.RaycastNonAlloc(pivot, dir, hits, maxDistance + collisionPadding, collisionMask, QueryTriggerInteraction.Ignore);

            // nearest hit that is NOT the player itself or a cop (agents), so they can't shove the camera around
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (c.transform.IsChildOf(target) || c.GetComponentInParent<NavMeshAgent>() != null) continue;
                if (hits[i].distance < nearest) { nearest = hits[i].distance; blocker = c.name; }
            }

            if (nearest < float.MaxValue)
                allowed = Mathf.Clamp(nearest - collisionPadding, minDistance, maxDistance);
        }

        if (debugCollision && blocker != lastBlocker)
        {
            lastBlocker = blocker;
            Debug.Log(blocker == "" ? "[Camera] nothing blocking" : $"[Camera] pushed in by '{blocker}'");
        }

        // snap IN instantly (so it never clips a wall), ease OUT smoothly (framerate independent)
        if (allowed < currentDistance)
            currentDistance = allowed;
        else
            currentDistance = Mathf.Lerp(currentDistance, allowed, 1f - Mathf.Exp(-zoomOutSpeed * Time.deltaTime));

        // NO position smoothing: the camera sits exactly at pivot + offset, so it can't lag or bounce at high speed
        transform.position = pivot + dir * currentDistance;
        transform.rotation = rotation;
    }
}