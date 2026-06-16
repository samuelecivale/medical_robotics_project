using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Attach this script to Main Camera.
// Controls:
//   Right Mouse + drag : orbit around the robot / target
//   WASD               : pan the orbit target on the camera plane
//   Q / E              : move target down / up
//   Mouse wheel        : zoom in / out
//   Shift              : faster pan
//   F                  : reset camera around the robot
//
// This is safer than a pure free-fly camera for the ROSA scene:
// the camera always looks at an orbit target, so the robot does not disappear.

public class MainCameraOrbitWASD : MonoBehaviour
{
    [Header("Target")]
    public Transform orbitTarget;
    public string autoTargetName = "ROSA_DoubleRCM_Generated";

    [Tooltip("If no target is found, the camera orbits around this world point.")]
    public Vector3 fallbackTargetPosition = new Vector3(0.65f, 0.55f, -0.05f);

    [Header("Initial View")]
    public bool resetViewOnStart = true;
    public float initialDistance = 1.45f;
    public float initialYawDeg = -35f;
    public float initialPitchDeg = 22f;

    [Header("Orbit")]
    public float mouseSensitivity = 0.18f;
    public float minPitchDeg = -15f;
    public float maxPitchDeg = 80f;

    [Header("Pan / Movement")]
    public float panSpeed = 0.65f;
    public float verticalSpeed = 0.45f;
    public float fastMultiplier = 3.0f;

    [Header("Zoom")]
    public float zoomSpeed = 0.25f;
    public float minDistance = 0.25f;
    public float maxDistance = 4.0f;

    [Header("Cursor")]
    public bool lockCursorWhileOrbiting = false;

    private Vector3 targetPosition;
    private float yawDeg;
    private float pitchDeg;
    private float distance;

    private void Start()
    {
        FindTargetIfNeeded();

        targetPosition = orbitTarget != null ? orbitTarget.position : fallbackTargetPosition;

        if (resetViewOnStart)
        {
            yawDeg = initialYawDeg;
            pitchDeg = initialPitchDeg;
            distance = Mathf.Clamp(initialDistance, minDistance, maxDistance);
            ApplyCameraPose();
        }
        else
        {
            Vector3 toCamera = transform.position - targetPosition;
            distance = Mathf.Clamp(toCamera.magnitude, minDistance, maxDistance);

            if (distance < 0.001f)
                distance = initialDistance;

            Vector3 dir = toCamera.normalized;
            yawDeg = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            pitchDeg = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }

    private void Update()
    {
        FindTargetIfNeeded();

        if (WasResetPressed())
            ResetView();

        HandleOrbit();
        HandlePan();
        HandleZoom();

        ApplyCameraPose();
    }

    private void FindTargetIfNeeded()
    {
        if (orbitTarget != null)
            return;

        if (!string.IsNullOrEmpty(autoTargetName))
        {
            GameObject found = GameObject.Find(autoTargetName);
            if (found != null)
            {
                orbitTarget = found.transform;
                targetPosition = orbitTarget.position;
                return;
            }
        }

        if (targetPosition == Vector3.zero)
            targetPosition = fallbackTargetPosition;
    }

    private void ResetView()
    {
        targetPosition = orbitTarget != null ? orbitTarget.position : fallbackTargetPosition;
        yawDeg = initialYawDeg;
        pitchDeg = initialPitchDeg;
        distance = Mathf.Clamp(initialDistance, minDistance, maxDistance);
        ApplyCameraPose();
    }

    private void HandleOrbit()
    {
        bool orbiting = IsRightMouseHeld();

        if (lockCursorWhileOrbiting)
        {
            Cursor.lockState = orbiting ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !orbiting;
        }

        if (!orbiting)
            return;

        Vector2 delta = GetMouseDelta();

        yawDeg += delta.x * mouseSensitivity;
        pitchDeg -= delta.y * mouseSensitivity;
        pitchDeg = Mathf.Clamp(pitchDeg, minPitchDeg, maxPitchDeg);
    }

    private void HandlePan()
    {
        Vector3 input = Vector3.zero;

        if (IsKeyHeld(KeyCode.W)) input.z += 1f;
        if (IsKeyHeld(KeyCode.S)) input.z -= 1f;
        if (IsKeyHeld(KeyCode.D)) input.x += 1f;
        if (IsKeyHeld(KeyCode.A)) input.x -= 1f;
        if (IsKeyHeld(KeyCode.E)) input.y += 1f;
        if (IsKeyHeld(KeyCode.Q)) input.y -= 1f;

        if (input.sqrMagnitude < 1e-6f)
            return;

        input = Vector3.ClampMagnitude(input, 1f);

        float speed = panSpeed;
        if (IsShiftHeld())
            speed *= fastMultiplier;

        // Pan relative to the camera view, but keep vertical movement separate.
        Vector3 right = transform.right;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.forward;

        Vector3 move =
            right * input.x * speed +
            forward * input.z * speed +
            Vector3.up * input.y * verticalSpeed;

        targetPosition += move * Time.unscaledDeltaTime;
    }

    private void HandleZoom()
    {
        float scroll = GetScrollY();

        if (Mathf.Abs(scroll) < 1e-5f)
            return;

        distance -= scroll * zoomSpeed;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    private void ApplyCameraPose()
    {
        Quaternion rotation = Quaternion.Euler(pitchDeg, yawDeg, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -distance);

        transform.position = targetPosition + offset;
        transform.rotation = rotation;
    }

    private bool IsRightMouseHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.rightButton.isPressed;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(1);
#else
        return false;
#endif
    }

    private Vector2 GetMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.delta.ReadValue();
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
#else
        return Vector2.zero;
#endif
    }

    private float GetScrollY()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.scroll.ReadValue().y * 0.01f;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mouseScrollDelta.y;
#else
        return 0f;
#endif
    }

    private bool IsKeyHeld(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            switch (key)
            {
                case KeyCode.W: return Keyboard.current.wKey.isPressed;
                case KeyCode.A: return Keyboard.current.aKey.isPressed;
                case KeyCode.S: return Keyboard.current.sKey.isPressed;
                case KeyCode.D: return Keyboard.current.dKey.isPressed;
                case KeyCode.Q: return Keyboard.current.qKey.isPressed;
                case KeyCode.E: return Keyboard.current.eKey.isPressed;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(key);
#else
        return false;
#endif
    }

    private bool WasResetPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
            return Keyboard.current.fKey.wasPressedThisFrame;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.F);
#else
        return false;
#endif
    }

    private bool IsShiftHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
            return Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#else
        return false;
#endif
    }
}
