using UnityEngine;

// ---------------------------------------------------------------------------------------------
// Builds a simplified ROSA-like 6-DOF arm + needle and wires it to DoubleRCMUnityController2.
// Project 4 (multi-RCM): AttachController() now uses the controller that already behaves better
// in the scene and configures it according to the paper-style link RCM formulation:
//   p_RCM = p_i + lambda * (p_{i+1} - p_i), with lambda optimized during IK.
// The generated scene exposes four demo tasks through the controller keyboard shortcuts.
// ---------------------------------------------------------------------------------------------
public class AutoROSABuilder : MonoBehaviour
{
    [Header("Build")]
    public bool buildOnStart = true;
    public bool rebuildEveryStart = true;
    public string generatedRootName = "ROSA_DoubleRCM_Generated";

    [Header("ROSA-like dimensions")]
    public float baseRadius = 0.18f;
    public float baseHeight = 0.08f;
    public float columnHeight = 0.90f;
    public float columnRadius = 0.045f;

    public float shoulderOffset = 0.23f;
    public float upperArmLength = 0.42f;
    public float forearmLength = 0.34f;
    public float wristLength = 0.20f;
    public float toolLength = 0.40f;

    public float linkRadius = 0.035f;
    public float jointRadius = 0.040f;
    public float toolRadius = 0.008f;
    public float toolMountOffset = 0.075f;
    public float distalJointRadiusScale = 0.65f;

    [Header("Tool visual")]
    public float realTipLength = 0.035f;
    public float realTipRadius = 0.010f;

    [Header("Scene references")]
    public Vector3 entryPosition = new Vector3(0.82f, 0.70f, 0.06f);
    public Vector3 targetPosition = new Vector3(0.90f, 0.56f, 0.06f);
    public float skullRadius = 0.135f;
    // Top-lateral entry, but the skull center remains around the previous scene height.
    // The offset puts the entry on the ellipsoid surface and the target inside.
    public Vector3 skullCenterOffsetFromEntry = new Vector3(0.06f, -0.13f, 0f);
    public Vector3 skullScaleFactors = new Vector3(1.40f, 1.50f, 1.20f);

    [Header("Needle-skull avoidance")]
    public bool avoidNeedleFromSkullBeforeInsertion = true;
    public float needleSkullAvoidanceWeight = 14.0f;
    public float needleSafetyMargin = 0.02f;
    public int needleAvoidanceSamples = 16;
    public float needleInsertionCorridorRadiusMm = 22.0f;

    

    [Header("Robot placement")]
    [Tooltip("Move only the generated robot base/arm. Entry, target and skull are kept at their world positions when created.")]
    public Vector3 robotBaseOffset = new Vector3(-0.18f, 0f, -0.46f);

    [Tooltip("Automatically rotates the base yaw so the arm initially faces the entry point.")]
    public bool autoOrientBaseToEntry = true;

    [Tooltip("After build, if the initial arm geometry intersects the skull, the whole patient group is shifted a few millimeters until the scene starts collision-free.")]
    public bool autoClearInitialSkullArmOverlap = true;

    public float initialSkullClearanceMm = 12.0f;

    [Header("Build validation")]
    public bool validateWorkspaceAtBuild = true;

    [Range(0.5f, 1.2f)]
    public float reachWarningRatio = 0.98f;

    [Header("Visual quality")]
    public bool useEnhancedMaterials = true;
    public bool useMeshColliderForSkull = true;
    public bool addBaseMountDetails = true;

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
        DisableLegacyControllersOnBuilderRoot();

        if (rebuildEveryStart)
            DeleteExistingGeneratedRoot();

        CreateMaterials();

        GameObject root = new GameObject(generatedRootName);
        root.transform.SetParent(transform, false);
        // Shift the robot base/arm slightly to the left/back relative to the patient.
        // The patient objects are later created with world positions, so they stay fixed.
        root.transform.localPosition = robotBaseOffset;
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

        // Small distal support so the needle does not start inside the blue joint sphere.
        CreateLocalHorizontalXLink(
            "Distal_Tool_Mount_Link",
            j5,
            toolMountOffset,
            linkRadius * 0.45f,
            darkMat
        );

        GameObject toolFrameObj = new GameObject("ToolFrame");
        toolFrameObj.transform.SetParent(j5, false);

        // Move the needle base outside the last blue joint.
        toolFrameObj.transform.localPosition = new Vector3(toolMountOffset, 0f, 0f);
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

        if (autoOrientBaseToEntry)
            AutoOrientBaseYawToEntry(root.transform, j0);

        if (autoClearInitialSkullArmOverlap)
            AutoClearInitialSkullArmOverlap(joints, entryObj.transform, targetObj.transform, skullObj.transform);

        if (validateWorkspaceAtBuild)
            ValidateWorkspace(root.transform, j0, entryObj.transform, targetObj.transform);

        if (addController)
            AttachController(root, joints, toolFrameObj.transform, toolTipObj.transform, entryObj.transform, targetObj.transform, skullObj);

        CreateSimpleLightAndCamera();
    }

    private void AutoOrientBaseYawToEntry(Transform root, Transform baseYawJoint)
    {
        if (root == null || baseYawJoint == null)
            return;

        Vector3 localEntry = root.InverseTransformPoint(entryPosition);
        Vector3 localBase = baseYawJoint.localPosition;
        Vector3 flatDirection = new Vector3(localEntry.x - localBase.x, 0f, localEntry.z - localBase.z);

        if (flatDirection.sqrMagnitude < 1e-6f)
            return;

        Quaternion yaw = Quaternion.FromToRotation(Vector3.right, flatDirection.normalized);
        baseYawJoint.localRotation = yaw;
    }

    private void ValidateWorkspace(Transform root, Transform baseYawJoint, Transform entryPoint, Transform targetPoint)
    {
        if (root == null || baseYawJoint == null || entryPoint == null || targetPoint == null)
            return;

        float approximateReach =
            columnHeight +
            shoulderOffset +
            upperArmLength +
            forearmLength +
            wristLength +
            toolMountOffset +
            toolLength;

        float warningReach = approximateReach * Mathf.Clamp(reachWarningRatio, 0.5f, 1.2f);
        WarnIfOutsideReach("entry", baseYawJoint.position, entryPoint.position, warningReach, approximateReach);
        WarnIfOutsideReach("target", baseYawJoint.position, targetPoint.position, warningReach, approximateReach);
    }

    private void WarnIfOutsideReach(string label, Vector3 basePosition, Vector3 point, float warningReach, float approximateReach)
    {
        float distance = Vector3.Distance(basePosition, point);

        if (distance <= warningReach)
            return;

        Debug.LogWarning(
            "[AutoROSABuilder] " + label + " point may be close to/outside the approximate workspace. " +
            "distance=" + distance.ToString("F3") + " m, warningReach=" + warningReach.ToString("F3") +
            " m, approximateReach=" + approximateReach.ToString("F3") + " m. " +
            "Adjust robotBaseOffset, entryPosition/targetPosition, or link lengths if IK struggles."
        );
    }

    private void CreateNeedleVisual(Transform toolFrame)
    {
        float clampedTipLength = Mathf.Clamp(realTipLength, 0.001f, toolLength * 0.9f);
        float shaftLength = Mathf.Max(0.001f, toolLength - clampedTipLength);

        CreateLocalForwardLink("Surgical_Tool_Shaft_Black", toolFrame, shaftLength, toolRadius, darkMat);
        CreateLocalForwardLink("Surgical_Tool_Base_Sleeve", toolFrame, Mathf.Min(0.075f, shaftLength), toolRadius * 1.9f, grayMat);

        GameObject tipVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tipVisual.name = "Surgical_Tool_RealTip_Red";
        tipVisual.transform.SetParent(toolFrame, false);
        tipVisual.transform.localPosition = new Vector3(0f, 0f, shaftLength + clampedTipLength * 0.5f);
        tipVisual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        tipVisual.transform.localScale = new Vector3(realTipRadius * 2f, clampedTipLength * 0.5f, realTipRadius * 2f);
        SetMaterial(tipVisual, tipMat);
    }


    private void AutoClearInitialSkullArmOverlap(Transform[] joints, Transform entry, Transform target, Transform skull)
    {
        if (joints == null || joints.Length < 2 || entry == null || target == null || skull == null)
            return;

        float clearance = Mathf.Max(0f, initialSkullClearanceMm) * 0.001f;

        for (int iter = 0; iter < 12; iter++)
        {
            Vector3 worstPoint;
            Vector3 pushForPoint;
            float penetration = FindWorstArmSkullPenetration(joints, skull, out worstPoint, out pushForPoint);

            if (penetration <= 0.0005f)
                return;

            Vector3 direction;
            if (pushForPoint.sqrMagnitude > 1e-8f)
                direction = -pushForPoint.normalized;
            else
                direction = new Vector3(0f, -1f, 0.25f).normalized;

            Vector3 shift = direction * (penetration + clearance);
            entry.position += shift;
            target.position += shift;
            skull.position += shift;
        }
    }

    private float FindWorstArmSkullPenetration(Transform[] joints, Transform skull, out Vector3 worstPoint, out Vector3 pushForPoint)
    {
        worstPoint = Vector3.zero;
        pushForPoint = Vector3.zero;

        if (joints == null || skull == null)
            return 0f;

        int samples = 5;
        int lastSafeJointSegment = Mathf.Max(0, joints.Length - 3);
        float maxPenetration = 0f;

        for (int s = 0; s <= lastSafeJointSegment; s++)
        {
            if (joints[s] == null || joints[s + 1] == null)
                continue;

            for (int k = 0; k < samples; k++)
            {
                float t = samples == 1 ? 0f : (float)k / (float)(samples - 1);
                Vector3 p = Vector3.Lerp(joints[s].position, joints[s + 1].position, t);
                Vector3 push;
                float penetration = EllipsoidPenetrationAndPush(p, skull, out push);

                if (penetration > maxPenetration)
                {
                    maxPenetration = penetration;
                    worstPoint = p;
                    pushForPoint = push;
                }
            }
        }

        return maxPenetration;
    }

    private float EllipsoidPenetrationAndPush(Vector3 point, Transform ellipsoid, out Vector3 push)
    {
        push = Vector3.zero;

        if (ellipsoid == null)
            return 0f;

        Vector3 local = ellipsoid.InverseTransformPoint(point);
        float d = local.magnitude;

        if (d >= 0.5f)
            return 0f;

        Vector3 dir = d < 1e-5f ? Vector3.up : local / d;
        Vector3 surface = ellipsoid.TransformPoint(dir * 0.5f);
        push = surface - point;
        return push.magnitude;
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
        DisableControllerByName(root, "PaperRCMController");

        DoubleRCMUnityController2 controller = root.GetComponent<DoubleRCMUnityController2>();

        if (controller == null)
            controller = root.AddComponent<DoubleRCMUnityController2>();

        controller.enabled = true;

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
        controller.toolLength = toolLength;
        controller.autoCreateToolTip = false;
        controller.entryPoint = entryPoint;
        controller.targetPoint = targetPoint;

        MeshCollider skullMeshCollider = skullObj.GetComponent<MeshCollider>();
        controller.skullCollider = skullMeshCollider != null && skullMeshCollider.enabled
            ? (Collider)skullMeshCollider
            : skullObj.GetComponent<Collider>();

        // Default demo: real insertion sequence.
        // Phase 1: drive the physical tip to the entry point.
        // Phase 2: keep the trocar as RCM while the tip advances to the target.
        controller.mode = DoubleRCMUnityController2.RCMMode.Double;
        controller.useInsertionSequence = true;
        controller.insertionPhase = DoubleRCMUnityController2.InsertionPhase.ApproachEntry;
        controller.insertionProgress = 0f;
        controller.useProgressiveStraightInsertion = true;
        controller.insertionProgressSpeed = 0.14f;
        controller.insertionProgressAdvanceErrorMm = 10.0f;
        controller.entryReachedThresholdMm = 8.0f;
        controller.targetReachedThresholdMm = 6.0f;

        // The insertion sequence must not switch phase just because the tip touches the entry.
        // It first goes to a pre-entry point, then aligns the shaft with Entry->Target, then inserts.
        controller.preEntryDistanceMm = 85.0f;
        controller.preEntryReachedThresholdMm = 12.0f;
        controller.insertionStartAxisThresholdDeg = 7.0f;
        controller.insertionStartAxisDistanceThresholdMm = 6.0f;
        controller.alignAtEntryTipWeight = 4.0f;
        controller.alignAtEntryAxisWeight = 3.8f;
        controller.requireAlignedPoseBeforeInsertion = true;
        controller.showInsertionGateDebug = true;

        // Paper-style link RCM variables. Segment -1 means ToolFrame -> ToolTip.
        controller.useLinkBasedRCMFormula = true;
        controller.entryRCMSegmentIndex = -1;
        controller.entryLambda = 0.92f;
        controller.optimizeEntryLambda = true;
        controller.initializeLambdaFromClosestPoint = true;

        controller.useTargetRCMFormula = true;
        controller.targetRCMSegmentIndex = -1;
        controller.targetLambda = 1.0f;
        controller.optimizeTargetLambda = false;
        controller.finiteDifferenceLambda = 0.001f;
        controller.lambdaStepScale = 0.75f;
        controller.maxLambdaDeltaPerIteration = 0.035f;

        // Solver: stronger than the old DoubleRCM defaults, but less jumpy than the failed PaperRCM setup.
        controller.solveIK = true;
        controller.solverIterations = 8;
        controller.damping = 0.11f;
        controller.finiteDifferenceDeg = 0.35f;
        controller.ikStepScale = 0.32f;
        controller.maxDeltaDegPerIteration = 0.42f;

        // Task gains. These prioritize the hard RCM before the decorative cone motions.
        controller.entryApproachTipWeight = 3.4f;
        controller.preAlignEntryAxisWeight = 1.15f;
        controller.insertionEntryWeight = 5.4f;
        controller.insertionTargetWeight = 3.0f;
        controller.insertionAxisWeight = 1.4f;

        controller.entryWeight = 5.0f;
        controller.targetTipWeight = 3.0f;
        controller.targetModeTargetRCMWeight = 5.5f;
        controller.targetModeEntryConeWeight = 1.6f;
        controller.entryTipConeRCMWeight = 5.8f;
        controller.entryTipConeTipWeight = 2.4f;
        controller.entryTipConeAxisWeight = 1.0f;

        // Cone demos must remain secondary; otherwise they can fight the RCM constraint.
        controller.useEntryConeInTargetMode = true;
        controller.animateTargetConeDemo = true;
        controller.entryConeHalfAngleDeg = 7.0f;
        controller.entryConeMotionFraction = 0.55f;
        controller.entryConeFrequencyHz = 0.15f;

        controller.useTipConeAroundTargetMode = true;
        controller.tipConeHalfAngleDeg = 4.0f;
        controller.tipConeMotionFraction = 0.65f;
        controller.tipConeFrequencyHz = 0.15f;

        // Safety: keep the arm away from the skull, but do not let safety terms dominate the surgical task.
        controller.avoidSkullInDouble = true;
        controller.allowedEntryRadiusMm = 3f;
        controller.skullAvoidanceWeight = 0.8f;

        controller.avoidNeedleFromSkullBeforeInsertion = avoidNeedleFromSkullBeforeInsertion;
        controller.needleSkullAvoidanceWeight = needleSkullAvoidanceWeight;
        controller.needleSafetyMargin = needleSafetyMargin;
        controller.needleAvoidanceSamples = Mathf.Max(2, needleAvoidanceSamples);
        controller.needleInsertionCorridorRadiusMm = needleInsertionCorridorRadiusMm;

        controller.avoidArmLinksFromSkull = true;
        controller.armSkullAvoidanceWeight = 10.0f;
        controller.armSafetyMargin = 0.020f;
        controller.armAvoidanceSamplesPerSegment = 4;

        controller.useJointLimits = true;
        controller.jointMinDeg = new float[] { -170f, -85f, -130f, -170f, -110f, -180f };
        controller.jointMaxDeg = new float[] { 170f, 85f, 130f, 170f, 110f, 180f };

        controller.showOverlay = true;
        controller.overlayOnRightSide = true;
        controller.overlayWidth = 410f;
        controller.smoothOverlayValues = true;
        controller.overlayRefreshSeconds = 0.10f;
        controller.overlaySmoothing = 0.35f;

        controller.logToCsv = false;
        controller.logFileName = "double_rcm_log.csv";
        controller.useTimestampedLogFile = true;
        controller.logEverySeconds = 0.03f;

        controller.useDemoStartPose = useDemoStartPose;
        controller.demoWaitBeforeSolving = 0.35f;
        controller.demoJointAnglesDeg = new float[]
        {
            0f,
            -18f,
            26f,
            0f,
            -14f,
            0f
        };
    }

    private void DisableLegacyControllersOnBuilderRoot()
    {
        // Prevent controllers accidentally left on the builder object from stealing input/overlay
        // or opening the same CSV log twice. The active controller is added only to the generated root.
        DisableControllerByName(gameObject, "PaperRCMController");
        DisableControllerByName(gameObject, "DoubleRCMUnityController2");
    }

    private void DisableControllerByName(GameObject owner, string typeName)
    {
        if (owner == null || string.IsNullOrEmpty(typeName))
            return;

        MonoBehaviour[] behaviours = owner.GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null || behaviour == this)
                continue;

            if (behaviour.GetType().Name == typeName)
                behaviour.enabled = false;
        }
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
        float linkMetallic = useEnhancedMaterials ? 0.15f : 0f;
        float jointMetallic = useEnhancedMaterials ? 0.25f : 0f;
        float linkSmoothness = useEnhancedMaterials ? 0.72f : 0.45f;
        float jointSmoothness = useEnhancedMaterials ? 0.85f : 0.45f;

        whiteMat = MakeMaterial("ROSA_White", new Color(0.92f, 0.92f, 0.88f, 1f), linkMetallic, linkSmoothness);
        blueMat = MakeMaterial("ROSA_Blue_Joints", new Color(0.0f, 0.20f, 0.85f, 1f), jointMetallic, jointSmoothness);
        grayMat = MakeMaterial("ROSA_Gray_Links", new Color(0.45f, 0.45f, 0.45f, 1f), linkMetallic, linkSmoothness);
        darkMat = MakeMaterial("ROSA_Dark_Tool_Base", new Color(0.04f, 0.04f, 0.04f, 1f), 0.2f, 0.65f);

        redMat = MakeMaterial("ENTRY_Red", new Color(1f, 0f, 0f, 1f), 0f, 0.55f);
        greenMat = MakeMaterial("TARGET_Green", new Color(0f, 1f, 0.1f, 1f), 0f, 0.55f);
        tipMat = MakeMaterial("Tool_Tip_Red", new Color(0.85f, 0.05f, 0.05f, 1f), 0.05f, 0.8f);

        skullMat = MakeMaterial("Skull_Transparent_Beige", new Color(1f, 0.72f, 0.46f, 0.25f), 0f, 0.35f);
    }

    private Material MakeMaterial(string name, Color color, float metallic = 0f, float smoothness = 0.45f)
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

        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", Mathf.Clamp01(metallic));

        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", Mathf.Clamp01(smoothness));

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

        if (!addBaseMountDetails)
            return;

        GameObject lowerPlate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        lowerPlate.name = "Mobile_Base_Footprint_Plate";
        lowerPlate.transform.SetParent(parent, false);
        lowerPlate.transform.localPosition = new Vector3(0f, 0.012f, 0f);
        lowerPlate.transform.localRotation = Quaternion.identity;
        lowerPlate.transform.localScale = new Vector3(baseRadius * 2.45f, 0.012f, baseRadius * 2.45f);
        SetMaterial(lowerPlate, grayMat);

        GameObject topMount = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        topMount.name = "Base_Rotary_Mount";
        topMount.transform.SetParent(parent, false);
        topMount.transform.localPosition = new Vector3(0f, baseHeight + 0.018f, 0f);
        topMount.transform.localRotation = Quaternion.identity;
        topMount.transform.localScale = new Vector3(baseRadius * 1.35f, 0.018f, baseRadius * 1.35f);
        SetMaterial(topMount, blueMat);
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

        if (useMeshColliderForSkull)
        {
            SphereCollider sphereCollider = skull.GetComponent<SphereCollider>();

            if (sphereCollider != null)
                sphereCollider.enabled = false;

            MeshFilter meshFilter = skull.GetComponent<MeshFilter>();
            MeshCollider meshCollider = skull.GetComponent<MeshCollider>();

            if (meshCollider == null)
                meshCollider = skull.AddComponent<MeshCollider>();

            if (meshFilter != null)
                meshCollider.sharedMesh = meshFilter.sharedMesh;

            // Static mesh collider: accurate raycast/collision proxy for the visible ellipsoid.
            meshCollider.convex = false;
        }
        else
        {
            SphereCollider collider = skull.GetComponent<SphereCollider>();

            if (collider == null)
                collider = skull.AddComponent<SphereCollider>();

            collider.enabled = true;
        }

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
            cameraObj.transform.position = new Vector3(1.25f, 1.05f, -1.55f);
            cameraObj.transform.rotation = Quaternion.Euler(26f, -34f, 0f);
        }
    }
}