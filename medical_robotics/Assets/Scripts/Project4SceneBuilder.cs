using UnityEngine;

/// <summary>
/// One-click procedural setup for Project 4 — Multi-RCM ROSA-like robot.
/// Attach this script to an empty GameObject in a fresh Unity scene and press Play.
/// It creates: a ROSA-like 6-DOF robot, entry point, target point,
/// optional skull/body sphere, visual guides, lights and a keyboard-only free camera.
/// </summary>
public class Project4SceneBuilder : MonoBehaviour
{
    [Header("Build")]
    public bool buildOnStart = true;
    public bool addFreeFlyCamera = true;
    public bool createSkull = false;

    [Header("Surgical scene, in Unity world units = meters")]
    // Unity is Y-up. These defaults place the surgical field above the ground,
    // instead of flat on the horizontal plane.
    // Scaled to match toolLength = 0.283 m (28.3 cm needle) in ROSADoubleRCMController:
    // entry/target are kept at the same relative position w.r.t. the robot wrist
    // (default pose), just brought closer in by the same factor the needle shrank
    // (0.283 / 0.72 ~= 0.393), so the shorter needle can still reach them.
    public Vector3 entryPoint = new Vector3(0.624f, 1.629f, 0.161f);
    public Vector3 targetPoint = new Vector3(0.703f, 1.550f, 0.161f);
    public float skullRadius = 0.118f;

    private void Start()
    {
        if (buildOnStart)
            Build();
    }

    [ContextMenu("Build Project 4 Scene")]
    public void Build()
    {
        GameObject root = new GameObject("Project4_MultiRCM_ROSA_Runtime");

        // Entry and target markers.
        Transform entry = CreateMarker("EntryPoint_Trocar", entryPoint, 0.01f, new Color(0.1f, 0.9f, 0.2f, 1.0f), root.transform);
        Transform target = CreateMarker("TargetPoint_DeepTarget", targetPoint, 0.01f, new Color(1.0f, 0.05f, 0.05f, 1.0f), root.transform);

        // Optional skull/body sphere. Disabled by default while tuning the insertion geometry.
        GameObject skull = null;
        if (createSkull)
        {
            Vector3 entryToTarget = (targetPoint - entryPoint).normalized;
            Vector3 skullCenter = entryPoint + entryToTarget * skullRadius;
            skull = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            skull.name = "Skull_Body_NoGoSphere";
            skull.transform.SetParent(root.transform);
            skull.transform.position = skullCenter;
            skull.transform.localScale = Vector3.one * (2.0f * skullRadius);
            skull.GetComponent<Renderer>().material = MakeMaterial("TransparentSkull", new Color(1.0f, 0.72f, 0.58f, 0.22f), true);
            DestroyCollider(skull);
        }

        // Robot controller.
        GameObject robot = new GameObject("ROSA_Like_6DOF_DoubleRCM_Controller");
        robot.transform.SetParent(root.transform);
        robot.transform.position = new Vector3(-0.35f, 1.48f, 0.20f);
        // Important: DH convention is Z-up, while Unity is Y-up.
        // This rotation maps the DH vertical Z axis to Unity's vertical Y axis.
        robot.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        ROSADoubleRCMController ctrl = robot.AddComponent<ROSADoubleRCMController>();
        ctrl.entryPoint = entry;
        ctrl.targetPoint = target;
        ctrl.skull = skull != null ? skull.transform : null;
        ctrl.skullRadius = skullRadius;
        ctrl.useSkullAvoidance = false;
        ctrl.mode = ROSADoubleRCMController.RCMMode.InsertionSequence;
        ctrl.autoRun = true;
        ctrl.showOverlay = true;
        ctrl.writeCsvLog = true;
        ctrl.logPeriod = 0.05f;
        ctrl.buildRobotVisualsOnStart = true;

        CreateLightRig();
        CreateGround();
        CreateRobotPedestal(robot.transform.position, root.transform);

        if (addFreeFlyCamera)
            CreateCamera(entryPoint, targetPoint);
    }

    private Transform CreateMarker(string name, Vector3 position, float radius, Color color, Transform parent)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = name;
        marker.transform.SetParent(parent);
        marker.transform.position = position;
        marker.transform.localScale = Vector3.one * (2.0f * radius);
        marker.GetComponent<Renderer>().material = MakeMaterial(name + "Material", color, false);
        DestroyCollider(marker);
        return marker.transform;
    }

    private void CreateLightRig()
    {
        GameObject key = new GameObject("Key Light");
        Light keyLight = key.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.intensity = 1.3f;
        key.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

        GameObject fill = new GameObject("Fill Light");
        Light fillLight = fill.AddComponent<Light>();
        fillLight.type = LightType.Point;
        fillLight.intensity = 0.75f;
        fillLight.range = 6f;
        fill.transform.position = new Vector3(-1.5f, 2.0f, -1.5f);
    }

    private void CreateGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Neutral_GroundPlane";
        ground.transform.position = new Vector3(0.20f, -0.04f, 0.20f);
        ground.transform.localScale = new Vector3(1.10f, 1f, 1.00f);
        ground.GetComponent<Renderer>().material = MakeMaterial("GroundMat", new Color(0.72f, 0.72f, 0.72f, 1f), false);
        DestroyCollider(ground);
    }

    private void CreateRobotPedestal(Vector3 topPosition, Transform parent)
    {
        GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pedestal.name = "ROSA_Mobile_Base_Pedestal";
        pedestal.transform.SetParent(parent);
        float h = Mathf.Max(0.05f, topPosition.y);
        pedestal.transform.position = new Vector3(topPosition.x, h * 0.5f, topPosition.z);
        pedestal.transform.localScale = new Vector3(0.13f, h * 0.5f, 0.13f);
        pedestal.GetComponent<Renderer>().material = MakeMaterial("PedestalMat", new Color(0.08f, 0.09f, 0.11f, 1f), false);
        DestroyCollider(pedestal);
    }

    private void CreateCamera(Vector3 entry, Vector3 target)
    {
        Camera old = Camera.main;
        GameObject camObj = old != null ? old.gameObject : new GameObject("Main Camera");
        camObj.name = "Main Camera";
        Camera cam = camObj.GetComponent<Camera>();
        if (cam == null) cam = camObj.AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 50f;
        cam.orthographic = false;
        camObj.transform.position = new Vector3(1.65f, 1.55f, -1.15f);
        camObj.transform.LookAt((entry + target) * 0.5f);
        if (camObj.GetComponent<FreeFlyCameraKeyboard>() == null)
            camObj.AddComponent<FreeFlyCameraKeyboard>();
    }

    private Material MakeMaterial(string name, Color color, bool transparent)
    {
        Material mat = new Material(FindBestShader());
        mat.name = name;

        // Support both Built-in Render Pipeline and URP.
        // Pink/magenta objects in Unity almost always mean: shader not found or unsupported.
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        if (transparent)
        {
            // Built-in Standard shader transparency.
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            // URP Lit transparency.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            mat.renderQueue = 3000;
        }
        return mat;
    }

    private Shader FindBestShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null) return shader;
        shader = Shader.Find("Standard");
        if (shader != null) return shader;
        shader = Shader.Find("Sprites/Default");
        if (shader != null) return shader;
        return Shader.Find("Legacy Shaders/Diffuse");
    }

    private void DestroyCollider(GameObject go)
    {
        Collider c = go.GetComponent<Collider>();
        if (c != null)
        {
            if (Application.isPlaying) Destroy(c);
            else DestroyImmediate(c);
        }
    }
}
