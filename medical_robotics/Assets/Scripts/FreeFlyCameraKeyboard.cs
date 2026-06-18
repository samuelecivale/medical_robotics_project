using UnityEngine;

/// <summary>
/// Keyboard-only free camera with a persistent left-side overlay.
/// W/S forward/back, A/D left/right, Q/E down/up.
/// Arrow keys yaw/pitch. Shift faster, Ctrl slower, F reset.
/// Works with the old Input Manager. In Unity Player Settings, use
/// Active Input Handling = Both or Input Manager (Old).
///
/// If you already have your own camera script, you can keep it: this script is
/// independent and only moves the camera + draws the controls overlay.
/// </summary>
public class FreeFlyCameraKeyboard : MonoBehaviour
{
    [Header("Motion")]
    public float moveSpeed = 1.2f;
    public float fastMultiplier = 3.0f;
    public float slowMultiplier = 0.25f;
    public float rotationSpeedDeg = 75f;

    [Header("Overlay")]
    public bool showOverlay = true;
    public bool showCameraPose = true;
    public int overlayX = 10;
    public int overlayY = 10;
    public int overlayWidth = 315;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private GUIStyle titleStyle;
    private GUIStyle labelStyle;
    private GUIStyle boxStyle;
    private Texture2D darkTexture;

    private void Start()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) speed *= fastMultiplier;
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) speed *= slowMultiplier;

        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += transform.forward;
        if (Input.GetKey(KeyCode.S)) move -= transform.forward;
        if (Input.GetKey(KeyCode.D)) move += transform.right;
        if (Input.GetKey(KeyCode.A)) move -= transform.right;
        if (Input.GetKey(KeyCode.E)) move += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;
        if (move.sqrMagnitude > 1e-8f) transform.position += move.normalized * speed * dt;

        float yaw = 0f;
        float pitch = 0f;
        if (Input.GetKey(KeyCode.RightArrow)) yaw += 1f;
        if (Input.GetKey(KeyCode.LeftArrow)) yaw -= 1f;
        if (Input.GetKey(KeyCode.UpArrow)) pitch -= 1f;
        if (Input.GetKey(KeyCode.DownArrow)) pitch += 1f;

        transform.rotation = Quaternion.AngleAxis(yaw * rotationSpeedDeg * dt, Vector3.up) * transform.rotation;
        transform.rotation = transform.rotation * Quaternion.AngleAxis(pitch * rotationSpeedDeg * dt, Vector3.right);

        if (Input.GetKeyDown(KeyCode.F))
        {
            transform.position = initialPosition;
            transform.rotation = initialRotation;
        }
        if (Input.GetKeyDown(KeyCode.H)) showOverlay = !showOverlay;
    }

    private void OnGUI()
    {
        if (!showOverlay) return;
        InitStyles();

        int h = showCameraPose ? 210 : 172;
        GUI.Box(new Rect(overlayX, overlayY, overlayWidth, h), GUIContent.none, boxStyle);

        float y = overlayY + 10;
        GUI.Label(new Rect(overlayX + 12, y, overlayWidth - 24, 22), "Camera overlay", titleStyle);
        y += 30;
        DrawLine(ref y, "W / S          avanti / indietro");
        DrawLine(ref y, "A / D          sinistra / destra");
        DrawLine(ref y, "Q / E          giu / su");
        DrawLine(ref y, "Freccia ← / →  ruota sx / dx");
        DrawLine(ref y, "Freccia ↑ / ↓  guarda su / giu");
        DrawLine(ref y, "Shift          piu veloce");
        DrawLine(ref y, "Ctrl           piu lento");
        DrawLine(ref y, "F              reset vista");
        DrawLine(ref y, "H              mostra/nascondi overlay");

        if (showCameraPose)
        {
            y += 4;
            Vector3 p = transform.position;
            Vector3 e = transform.eulerAngles;
            DrawLine(ref y, string.Format("pos: ({0:F2}, {1:F2}, {2:F2})", p.x, p.y, p.z));
            DrawLine(ref y, string.Format("rot: ({0:F0}, {1:F0}, {2:F0})", e.x, e.y, e.z));
        }
    }

    private void DrawLine(ref float y, string text)
    {
        GUI.Label(new Rect(overlayX + 12, y, overlayWidth - 24, 18), text, labelStyle);
        y += 18;
    }

    private void InitStyles()
    {
        if (darkTexture == null)
        {
            darkTexture = new Texture2D(1, 1);
            darkTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
            darkTexture.Apply();
        }
        if (boxStyle == null)
        {
            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = darkTexture;
        }
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.normal.textColor = Color.white;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.fontSize = 15;
        }
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.normal.textColor = Color.white;
            labelStyle.fontSize = 12;
        }
    }
}
