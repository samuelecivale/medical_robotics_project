using UnityEngine;

public class AutoROSABuilder : MonoBehaviour
{
    [Header("Build")]
    public bool buildOnStart = true;
    public bool rebuildEveryStart = true;
    public string generatedRootName = "ROSA_Approx_Generated";

    [Header("ROSA-like dimensions")]
    public float baseRadius = 0.18f;
    public float baseHeight = 0.08f;
    public float columnHeight = 0.75f;
    public float columnRadius = 0.045f;

    public float shoulderOffset = 0.23f;
    public float upperArmLength = 0.42f;
    public float forearmLength = 0.34f;
    public float wristLength = 0.20f;
    public float toolLength = 0.40f;

    public float linkRadius = 0.035f;
    public float jointRadius = 0.055f;
    public float toolRadius = 0.008f;

    [Header("Tool visual")]
    public float realTipLength = 0.035f;
    public float realTipRadius = 0.010f;

    [Header("Scene references")]
    public Vector3 entryPosition = new Vector3(1.05f, 0.78f, 0.02f);
    public Vector3 targetPosition = new Vector3(1.25f, 0.78f, 0.02f);
    public float skullRadius = 0.16f;
    public Vector3 skullCenterOffsetFromEntry = new Vector3(0.105f, 0f, 0f);
    public Vector3 skullScaleFactors = new Vector3(2.1f, 1.8f, 1.5f);

    [Header("Controller")]
    public bool addController = true;
    public bool useDemoStartPose = true;

    private Material whiteMat;
    private Material blueMat;
    private Material grayMat;
    private Material darkMat;
    private Material redMat;
    private Material greenMat;
    private Material skullMat;
    private Material tipMat;

    private void Start()
    {
        if (buildOnStart)
            Build();
    }

    [ContextMenu("Build ROSA Approx")]
    public void Build()
    {
        if (rebuildEveryStart)
            DeleteExistingGeneratedRoot();

        CreateMaterials();

        GameObject root = new GameObject(generatedRootName);
        root.transform.SetParent(transform, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        BuildBase(root.transform);

        Transform j0 = CreateJointPivot("Joint_0_BaseYaw", root.transform, new Vector3(0f, baseHeight, 0f));

        CreateLocalVerticalLink("Vertical_Column", j0, columnHeight, columnRadius, whiteMat);
        Transform columnTop = CreateOffset("ColumnTop", j0, new Vector3(0f, columnHeight, 0f));

        CreateLocalHorizontalXLink("Shoulder_Offset_Link", columnTop, shoulderOffset, linkRadius, grayMat);
        Transform j1 = CreateJointPivot("Joint_1_Shoulder", columnTop, new Vector3(shoulderOffset, 0f, 0f));

        CreateLocalHorizontalXLink("Upper_Arm_Link", j1, upperArmLength, linkRadius, whiteMat);
        Transform j2 = CreateJointPivot("Joint_2_UpperArm", j1, new Vector3(upperArmLength, 0f, 0f));

        CreateLocalHorizontalXLink("Forearm_Link", j2, forearmLength, linkRadius, grayMat);
        Transform j3 = CreateJointPivot("Joint_3_Elbow", j2, new Vector3(forearmLength, 0f, 0f));

        float wristA = wristLength * 0.55f;
        CreateLocalHorizontalXLink("Wrist_Link_A", j3, wristA, linkRadius * 0.8f, whiteMat);
        Transform j4 = CreateJointPivot("Joint_4_WristPitch", j3, new Vector3(wristA, 0f, 0f));

        float wristB = wristLength * 0.45f;
        CreateLocalHorizontalXLink("Wrist_Link_B", j4, wristB, linkRadius * 0.75f, grayMat);
        Transform j5 = CreateJointPivot("Joint_5_ToolAxis", j4, new Vector3(wristB, 0f, 0f));

        GameObject toolFrameObj = new GameObject("ToolFrame");
        toolFrameObj.transform.SetParent(j5, false);
        toolFrameObj.transform.localPosition = Vector3.zero;
        toolFrameObj.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        toolFrameObj.transform.localScale = Vector3.one;

        CreateNeedleVisual(toolFrameObj.transform);

        GameObject toolTipObj = new GameObject("ToolTip");
        toolTipObj.transform.SetParent(toolFrameObj.transform, false);
        toolTipObj.transform.localPosition = Vector3.forward * toolLength;
        toolTipObj.transform.localRotation = Quaternion.identity;
        toolTipObj.transform.localScale = Vector3.one;

        GameObject entryObj = CreateSphere("EntryPoint", root.transform, entryPosition, 0.025f, redMat);
        GameObject targetObj = CreateSphere("TargetPoint", root.transform, targetPosition, 0.025f, greenMat);
        GameObject skullObj = CreateSkull(root.transform, entryPosition, skullRadius);

        Transform[] joints =
        {
            j0,
            j1,
            j2,
            j3,
            j4,
            j5
        };

        if (addController)
            AttachController(root, joints, toolFrameObj.transform, toolTipObj.transform, entryObj.transform, targetObj.transform, skullObj);

        CreateSimpleLightAndCamera();
    }

    private void CreateNeedleVisual(Transform toolFrame)
    {
        float clampedTipLength = Mathf.Clamp(realTipLength, 0.001f, toolLength * 0.9f);
        float shaftLength = Mathf.Max(0.001f, toolLength - clampedTipLength);

        CreateLocalForwardLink("Surgical_Tool_Shaft_Black", toolFrame, shaftLength, toolRadius, darkMat);

        GameObject tipVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tipVisual.name = "Surgical_Tool_RealTip_Red";
        tipVisual.transform.SetParent(toolFrame, false);
        tipVisual.transform.localPosition = new Vector3(0f, 0f, shaftLength + clampedTipLength * 0.5f);
        tipVisual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        tipVisual.transform.localScale = new Vector3(realTipRadius * 2f, clampedTipLength * 0.5f, realTipRadius * 2f);
        SetMaterial(tipVisual, tipMat);
    }

    private void AttachController(
        GameObject root,
        Transform[] joints,
        Transform toolFrame,
        Transform toolTip,
        Transform entryPoint,
        Transform targetPoint,
        GameObject skullObj)
    {
        DoubleRCMUnityController2 controller = root.GetComponent<DoubleRCMUnityController2>();

        if (controller == null)
            controller = root.AddComponent<DoubleRCMUnityController2>();

        controller.joints = joints;
        controller.jointAxesLocal = new Vector3[]
        {
            Vector3.up,
            Vector3.forward,
            Vector3.forward,
            Vector3.right,
            Vector3.forward,
            Vector3.up
        };

        controller.toolFrame = toolFrame;
        controller.toolTip = toolTip;
        controller.autoCreateToolTip = false;
        controller.toolLength = toolLength;
        controller.entryPoint = entryPoint;
        controller.targetPoint = targetPoint;
        controller.skullCollider = skullObj.GetComponent<Collider>();
        controller.skullPlaneNormalReference = entryPoint;

        controller.mode = DoubleRCMUnityController2.RCMMode.Double;
        controller.useInsertionSequence = true;
        controller.insertionPhase = DoubleRCMUnityController2.InsertionPhase.ApproachEntry;

        controller.entryReachedThresholdMm = 15.0f;
        controller.targetReachedThresholdMm = 8.0f;

        controller.entryApproachTipWeight = 2.6f;
        controller.preAlignEntryAxisWeight = 0.45f;
        controller.insertionEntryWeight = 4.2f;
        controller.insertionTargetWeight = 2.8f;
        controller.insertionAxisWeight = 1.0f;
        controller.useProgressiveStraightInsertion = true;
        controller.insertionProgressSpeed = 0.35f;
        controller.insertionProgressAdvanceErrorMm = 10.0f;

        controller.solverIterations = 4;
        controller.damping = 0.18f;
        controller.finiteDifferenceDeg = 0.5f;
        controller.maxDeltaDegPerIteration = 0.22f;
        controller.ikStepScale = 0.16f;

        controller.entryWeight = 3.4f;
        controller.targetTipWeight = 2.6f;
        controller.useEntryConeInTargetMode = true;
        controller.animateTargetConeDemo = true;
        controller.entryConeHalfAngleDeg = 7.0f;
        controller.entryConeMotionFraction = 0.65f;
        controller.entryConeFrequencyHz = 0.15f;
        controller.targetModeTargetRCMWeight = 4.2f;
        controller.targetModeEntryConeWeight = 1.4f;
        controller.skullAvoidanceWeight = 0.45f;
        controller.useFiniteNeedleSegmentForEntry = true;

        // Link-based RCM formula:
        // p_RCM = p_i + lambda * (p_{i+1} - p_i)
        // -1 selects the surgical tool segment ToolFrame -> ToolTip.
        controller.useLinkBasedRCMFormula = true;
        controller.entryRCMSegmentIndex = -1;
        controller.entryLambda = 0.5f;
        controller.optimizeEntryLambda = true;
        controller.initializeLambdaFromClosestPoint = true;

        // The target is represented by the same formula with lambda = 1,
        // which makes p_RCM coincide with the physical tool tip.
        controller.useTargetRCMFormula = true;
        controller.targetRCMSegmentIndex = -1;
        controller.targetLambda = 1.0f;
        controller.optimizeTargetLambda = false;
        controller.finiteDifferenceLambda = 0.001f;
        controller.maxLambdaDeltaPerIteration = 0.015f;
        controller.lambdaStepScale = 1.0f;

        controller.useJointLimits = true;
        controller.jointMinDeg = new float[]
        {
            -170f,
            -85f,
            -130f,
            -170f,
            -110f,
            -180f
        };
        controller.jointMaxDeg = new float[]
        {
            170f,
            85f,
            130f,
            170f,
            110f,
            180f
        };

        controller.avoidArmLinksFromSkull = true;
        controller.armSkullAvoidanceWeight = 5.0f;
        controller.armSafetyMargin = 0.07f;
        controller.armAvoidanceSamplesPerSegment = 4;

        controller.showOverlay = true;

        controller.useDemoStartPose = useDemoStartPose;
        controller.demoWaitBeforeSolving = 0.5f;
        controller.demoJointAnglesDeg = new float[]
        {
            18f,
            -16f,
            22f,
            -12f,
            14f,
            8f
        };
    }

    private void DeleteExistingGeneratedRoot()
    {
        Transform oldRoot = transform.Find(generatedRootName);

        if (oldRoot == null)
            return;

        if (Application.isPlaying)
            Destroy(oldRoot.gameObject);
        else
            DestroyImmediate(oldRoot.gameObject);
    }

    private void CreateMaterials()
    {
        whiteMat = MakeMaterial("ROSA_White", new Color(0.92f, 0.92f, 0.88f, 1f));
        blueMat = MakeMaterial("ROSA_Blue_Joints", new Color(0.0f, 0.20f, 0.85f, 1f));
        grayMat = MakeMaterial("ROSA_Gray_Links", new Color(0.45f, 0.45f, 0.45f, 1f));
        darkMat = MakeMaterial("ROSA_Dark_Tool_Base", new Color(0.04f, 0.04f, 0.04f, 1f));

        redMat = MakeMaterial("ENTRY_Red", new Color(1f, 0f, 0f, 1f));
        greenMat = MakeMaterial("TARGET_Green", new Color(0f, 1f, 0.1f, 1f));
        tipMat = MakeMaterial("Tool_Tip_Red", new Color(0.85f, 0.05f, 0.05f, 1f));

        skullMat = MakeMaterial("Skull_Transparent_Beige", new Color(1f, 0.72f, 0.46f, 0.25f));
    }

    private Material MakeMaterial(string name, Color color)
    {
        Shader shader =
            Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("HDRP/Lit") ??
            Shader.Find("Standard") ??
            Shader.Find("Sprites/Default") ??
            Shader.Find("Unlit/Color");

        Material mat = new Material(shader);
        mat.name = name;

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);

        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);

        if (color.a < 0.99f)
        {
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);

            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", 0f);

            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 3000;
        }

        return mat;
    }

    private void BuildBase(Transform parent)
    {
        GameObject baseObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseObj.name = "Mobile_Base";
        baseObj.transform.SetParent(parent, false);
        baseObj.transform.localPosition = new Vector3(0f, baseHeight * 0.5f, 0f);
        baseObj.transform.localRotation = Quaternion.identity;
        baseObj.transform.localScale = new Vector3(baseRadius * 2f, baseHeight * 0.5f, baseRadius * 2f);
        SetMaterial(baseObj, darkMat);
    }

    private Transform CreateJointPivot(string name, Transform parent, Vector3 localPosition)
    {
        GameObject pivot = new GameObject(name);
        pivot.transform.SetParent(parent, false);
        pivot.transform.localPosition = localPosition;
        pivot.transform.localRotation = Quaternion.identity;
        pivot.transform.localScale = Vector3.one;

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = name + "_Visual";
        visual.transform.SetParent(pivot.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one * jointRadius * 2f;
        SetMaterial(visual, blueMat);

        return pivot.transform;
    }

    private Transform CreateOffset(string name, Transform parent, Vector3 localPosition)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPosition;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one;
        return obj.transform;
    }

    private GameObject CreateSphere(string name, Transform parent, Vector3 worldPosition, float radius, Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.name = name;
        obj.transform.SetParent(parent, true);
        obj.transform.position = worldPosition;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one * radius * 2f;
        SetMaterial(obj, mat);
        return obj;
    }

    private GameObject CreateSkull(Transform parent, Vector3 entryPos, float radius)
    {
        GameObject skull = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        skull.name = "Transparent_Skull";
        skull.transform.SetParent(parent, true);

        Vector3 center = entryPos + skullCenterOffsetFromEntry;
        skull.transform.position = center;
        skull.transform.localRotation = Quaternion.identity;
        skull.transform.localScale = new Vector3(radius * skullScaleFactors.x, radius * skullScaleFactors.y, radius * skullScaleFactors.z);

        SetMaterial(skull, skullMat);

        SphereCollider collider = skull.GetComponent<SphereCollider>();
        if (collider == null)
            collider = skull.AddComponent<SphereCollider>();

        return skull;
    }

    private void CreateLocalVerticalLink(string name, Transform parent, float length, float radius, Material mat)
    {
        GameObject link = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        link.name = name;
        link.transform.SetParent(parent, false);
        link.transform.localPosition = new Vector3(0f, length * 0.5f, 0f);
        link.transform.localRotation = Quaternion.identity;
        link.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
        SetMaterial(link, mat);
    }

    private void CreateLocalHorizontalXLink(string name, Transform parent, float length, float radius, Material mat)
    {
        GameObject link = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        link.name = name;
        link.transform.SetParent(parent, false);
        link.transform.localPosition = new Vector3(length * 0.5f, 0f, 0f);
        link.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        link.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
        SetMaterial(link, mat);
    }

    private void CreateLocalForwardLink(string name, Transform parent, float length, float radius, Material mat)
    {
        GameObject link = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        link.name = name;
        link.transform.SetParent(parent, false);
        link.transform.localPosition = new Vector3(0f, 0f, length * 0.5f);
        link.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        link.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
        SetMaterial(link, mat);
    }

    private void SetMaterial(GameObject obj, Material mat)
    {
        Renderer renderer = obj.GetComponent<Renderer>();

        if (renderer != null && mat != null)
            renderer.sharedMaterial = mat;
    }

    private void CreateSimpleLightAndCamera()
    {
        if (Object.FindAnyObjectByType<Light>() == null)
        {
            GameObject lightObj = new GameObject("Auto_Directional_Light");
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        if (Camera.main == null)
        {
            GameObject cameraObj = new GameObject("Main Camera");
            Camera camera = cameraObj.AddComponent<Camera>();
            camera.tag = "MainCamera";
            cameraObj.transform.position = new Vector3(1.4f, 1.0f, -1.5f);
            cameraObj.transform.rotation = Quaternion.Euler(28f, -38f, 0f);
        }
    }
}
