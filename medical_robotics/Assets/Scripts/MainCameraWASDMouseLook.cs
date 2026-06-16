using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Attach this script to the Main Camera.
// Controls during Play:
//   WASD  = move horizontally relative to camera direction
//   Q/E   = move down/up
//   Mouse = look around while RMB is held, or always if holdRightMouseToLook = false
//   Shift = faster movement
//   Esc   = unlock cursor
public class MainCameraWASDMouseLook : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 2.5f;
    public float fastMoveMultiplier = 3.0f;
    public float verticalSpeed = 2.0f;

    [Header("Mouse Look")]
    public float mouseSensitivity = 0.12f;
    public bool holdRightMouseToLook = true;
    public bool lockCursorWhileLooking = true;
    public bool invertY = false;

    [Header("Limits")]
    public float minPitchDeg = -85.0f;
    public float maxPitchDeg = 85.0f;

    private float yawDeg;
    private float pitchDeg;

    private void Start()
    {
        Vector3 euler = transform.rotation.eulerAngles;
        yawDeg = euler.y;
        pitchDeg = NormalizeAngle(euler.x);
    }

    private void Update()
    {
        HandleCursorState();
        HandleMouseLook();
        HandleMovement();
    }

    private void HandleMovement()
    {
        Vector3 input = Vector3.zero;

        if (GetKey(KeyCode.W)) input.z += 1f;
        if (GetKey(KeyCode.S)) input.z -= 1f;
        if (GetKey(KeyCode.D)) input.x += 1f;
        if (GetKey(KeyCode.A)) input.x -= 1f;
        if (GetKey(KeyCode.E)) input.y += 1f;
        if (GetKey(KeyCode.Q)) input.y -= 1f;

        if (input.sqrMagnitude > 1f)
            input.Normalize();

        float speed = moveSpeed;
        if (GetKey(KeyCode.LeftShift) || GetKey(KeyCode.RightShift))
            speed *= fastMoveMultiplier;

        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        Vector3 up = Vector3.up;

        Vector3 movement =
            right * input.x * speed +
            forward * input.z * speed +
            up * input.y * verticalSpeed;

        transform.position += movement * Time.unscaledDeltaTime;
    }

    private void HandleMouseLook()
    {
        bool shouldLook = !holdRightMouseToLook || GetMouseButton(1);
        if (!shouldLook)
            return;

        Vector2 delta = GetMouseDelta();

        yawDeg += delta.x * mouseSensitivity;

        float ySign = invertY ? 1f : -1f;
        pitchDeg += delta.y * mouseSensitivity * ySign;
        pitchDeg = Mathf.Clamp(pitchDeg, minPitchDeg, maxPitchDeg);

        transform.rotation = Quaternion.Euler(pitchDeg, yawDeg, 0f);
    }

    private void HandleCursorState()
    {
        bool shouldLook = !holdRightMouseToLook || GetMouseButton(1);

        if (lockCursorWhileLooking && shouldLook)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else if (lockCursorWhileLooking && GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (lockCursorWhileLooking && holdRightMouseToLook && !shouldLook)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    private bool GetKey(KeyCode key)
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
                case KeyCode.LeftShift: return Keyboard.current.leftShiftKey.isPressed;
                case KeyCode.RightShift: return Keyboard.current.rightShiftKey.isPressed;
                case KeyCode.Escape: return Keyboard.current.escapeKey.isPressed;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(key);
#else
        return false;
#endif
    }

    private bool GetKeyDown(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            switch (key)
            {
                case KeyCode.Escape: return Keyboard.current.escapeKey.wasPressedThisFrame;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(key);
#else
        return false;
#endif
    }

    private bool GetMouseButton(int button)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            if (button == 0) return Mouse.current.leftButton.isPressed;
            if (button == 1) return Mouse.current.rightButton.isPressed;
            if (button == 2) return Mouse.current.middleButton.isPressed;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(button);
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
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#else
        return Vector2.zero;
#endif
    }
}
