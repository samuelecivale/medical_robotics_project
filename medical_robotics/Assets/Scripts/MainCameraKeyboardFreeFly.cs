using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Attach to Main Camera.
// Keyboard-only free-fly camera.
// No mouse required.
//
// Controls:
//   W / S           : forward / backward
//   A / D           : left / right
//   Q / E           : down / up
//   Arrow Left/Right: yaw
//   Arrow Up/Down   : pitch
//   Z / X           : roll left / roll right, optional
//   Left Shift      : faster movement
//   Left Ctrl       : slower movement
//   F               : reset to initial pose
//
// Recommended Main Camera projection for this project:
//   Projection = Perspective
//   Field of View = 45-55
//   Near Clip = 0.01
//   Far Clip = 100

public class MainCameraKeyboardFreeFly : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 1.6f;
    public float fastMultiplier = 3.0f;
    public float slowMultiplier = 0.30f;
    public float verticalSpeed = 1.2f;

    [Header("Rotation")]
    public float yawSpeedDegPerSec = 75.0f;
    public float pitchSpeedDegPerSec = 65.0f;
    public float rollSpeedDegPerSec = 55.0f;
    public bool enableRoll = false;
    public bool clampPitch = true;
    public float minPitchDeg = -80.0f;
    public float maxPitchDeg = 80.0f;

    [Header("Reset pose")]
    public bool setInitialPoseOnStart = true;
    public Vector3 initialPosition = new Vector3(0.65f, 0.95f, -1.55f);
    public Vector3 initialEulerDeg = new Vector3(24.0f, 0.0f, 0.0f);

    private Vector3 savedStartPosition;
    private Vector3 savedStartEuler;
    private float yaw;
    private float pitch;
    private float roll;

    private void Start()
    {
        if (setInitialPoseOnStart)
        {
            transform.position = initialPosition;
            transform.rotation = Quaternion.Euler(initialEulerDeg);
        }

        savedStartPosition = transform.position;
        savedStartEuler = transform.rotation.eulerAngles;

        Vector3 e = transform.rotation.eulerAngles;
        pitch = NormalizeAngle(e.x);
        yaw = NormalizeAngle(e.y);
        roll = NormalizeAngle(e.z);
    }

    private void Update()
    {
        if (WasResetPressed())
            ResetPose();

        HandleRotation();
        HandleMovement();
    }

    private void HandleMovement()
    {
        Vector3 localMove = Vector3.zero;

        if (IsKeyHeld(KeyCode.W)) localMove += Vector3.forward;
        if (IsKeyHeld(KeyCode.S)) localMove += Vector3.back;
        if (IsKeyHeld(KeyCode.A)) localMove += Vector3.left;
        if (IsKeyHeld(KeyCode.D)) localMove += Vector3.right;

        Vector3 worldMove = transform.TransformDirection(localMove.normalized);

        float vertical = 0f;
        if (IsKeyHeld(KeyCode.E)) vertical += 1f;
        if (IsKeyHeld(KeyCode.Q)) vertical -= 1f;

        float speed = moveSpeed;

        if (IsShiftHeld())
            speed *= fastMultiplier;

        if (IsCtrlHeld())
            speed *= slowMultiplier;

        transform.position += worldMove * speed * Time.unscaledDeltaTime;
        transform.position += Vector3.up * vertical * verticalSpeed * speed * Time.unscaledDeltaTime;
    }

    private void HandleRotation()
    {
        float yawInput = 0f;
        float pitchInput = 0f;
        float rollInput = 0f;

        if (IsKeyHeld(KeyCode.LeftArrow)) yawInput -= 1f;
        if (IsKeyHeld(KeyCode.RightArrow)) yawInput += 1f;
        if (IsKeyHeld(KeyCode.UpArrow)) pitchInput -= 1f;
        if (IsKeyHeld(KeyCode.DownArrow)) pitchInput += 1f;

        if (enableRoll)
        {
            if (IsKeyHeld(KeyCode.Z)) rollInput += 1f;
            if (IsKeyHeld(KeyCode.X)) rollInput -= 1f;
        }

        yaw += yawInput * yawSpeedDegPerSec * Time.unscaledDeltaTime;
        pitch += pitchInput * pitchSpeedDegPerSec * Time.unscaledDeltaTime;
        roll += rollInput * rollSpeedDegPerSec * Time.unscaledDeltaTime;

        if (clampPitch)
            pitch = Mathf.Clamp(pitch, minPitchDeg, maxPitchDeg);

        transform.rotation = Quaternion.Euler(pitch, yaw, roll);
    }

    private void ResetPose()
    {
        transform.position = savedStartPosition;
        transform.rotation = Quaternion.Euler(savedStartEuler);

        Vector3 e = transform.rotation.eulerAngles;
        pitch = NormalizeAngle(e.x);
        yaw = NormalizeAngle(e.y);
        roll = NormalizeAngle(e.z);
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
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
                case KeyCode.Z: return Keyboard.current.zKey.isPressed;
                case KeyCode.X: return Keyboard.current.xKey.isPressed;
                case KeyCode.LeftArrow: return Keyboard.current.leftArrowKey.isPressed;
                case KeyCode.RightArrow: return Keyboard.current.rightArrowKey.isPressed;
                case KeyCode.UpArrow: return Keyboard.current.upArrowKey.isPressed;
                case KeyCode.DownArrow: return Keyboard.current.downArrowKey.isPressed;
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

    private bool IsCtrlHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
            return Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#else
        return false;
#endif
    }
}
