using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Attach this to Main Camera or to any active GameObject in the scene.
// It draws a persistent left-side IMGUI overlay with keyboard camera controls.
// It is intentionally separate from the camera movement script, so it cannot break camera motion.
//
// Toggle: H
//
// If you do not see it:
// - make sure this component is enabled;
// - make sure "Show Overlay" is checked;
// - put Overlay X/Y to 20/20;
// - ensure the Game view is visible, not only Scene view.

public class CameraControlsOverlay : MonoBehaviour
{
    [Header("Visibility")]
    public bool showOverlay = true;
    public KeyCode toggleKey = KeyCode.H;

    [Header("Layout")]
    public float overlayX = 18f;
    public float overlayY = 18f;
    public float overlayWidth = 365f;
    public float lineHeight = 24f;
    public int fontSize = 16;

    [Header("Style")]
    public Color textColor = Color.white;
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.78f);
    public Color titleColor = new Color(1f, 0.92f, 0.55f, 1f);

    private Texture2D backgroundTexture;
    private GUIStyle boxStyle;
    private GUIStyle titleStyle;
    private GUIStyle keyStyle;
    private GUIStyle textStyle;

    private void Update()
    {
        if (WasKeyPressed(toggleKey))
            showOverlay = !showOverlay;
    }

    private void OnGUI()
    {
        if (!showOverlay)
            return;

        InitStyles();

        // Draw above most other IMGUI elements.
        int oldDepth = GUI.depth;
        GUI.depth = -10000;

        float height = 318f;
        Rect box = new Rect(overlayX, overlayY, overlayWidth, height);
        GUI.Box(box, GUIContent.none, boxStyle);

        float y = overlayY + 12f;
        GUI.Label(new Rect(overlayX + 14f, y, overlayWidth - 28f, lineHeight), "Camera controls", titleStyle);
        y += lineHeight + 8f;

        DrawLine(ref y, "W / S", "avanti / indietro");
        DrawLine(ref y, "A / D", "sinistra / destra");
        DrawLine(ref y, "Q / E", "giù / su");
        DrawLine(ref y, "← / →", "ruota sinistra / destra");
        DrawLine(ref y, "↑ / ↓", "guarda su / giù");
        DrawLine(ref y, "Shift", "più veloce");
        DrawLine(ref y, "Ctrl", "più lento");
        DrawLine(ref y, "F", "reset vista");
        DrawLine(ref y, "H", "mostra / nascondi overlay");

        y += 7f;
        GUI.Label(
            new Rect(overlayX + 14f, y, overlayWidth - 28f, lineHeight),
            "Projection: Perspective | FOV: 50",
            textStyle
        );

        GUI.depth = oldDepth;
    }

    private void DrawLine(ref float y, string key, string description)
    {
        GUI.Label(new Rect(overlayX + 14f, y, 95f, lineHeight), key, keyStyle);
        GUI.Label(new Rect(overlayX + 112f, y, overlayWidth - 126f, lineHeight), description, textStyle);
        y += lineHeight;
    }

    private void InitStyles()
    {
        if (boxStyle != null)
            return;

        backgroundTexture = new Texture2D(1, 1);
        backgroundTexture.SetPixel(0, 0, backgroundColor);
        backgroundTexture.Apply();

        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = backgroundTexture;
        boxStyle.border = new RectOffset(8, 8, 8, 8);
        boxStyle.padding = new RectOffset(0, 0, 0, 0);

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = fontSize + 3;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.normal.textColor = titleColor;

        keyStyle = new GUIStyle(GUI.skin.label);
        keyStyle.fontSize = fontSize;
        keyStyle.fontStyle = FontStyle.Bold;
        keyStyle.normal.textColor = textColor;

        textStyle = new GUIStyle(GUI.skin.label);
        textStyle.fontSize = fontSize;
        textStyle.normal.textColor = textColor;
    }

    private bool WasKeyPressed(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            switch (key)
            {
                case KeyCode.H:
                    return Keyboard.current.hKey.wasPressedThisFrame;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(key);
#else
        return false;
#endif
    }

    private void OnDestroy()
    {
        if (backgroundTexture != null)
            Destroy(backgroundTexture);
    }
}
