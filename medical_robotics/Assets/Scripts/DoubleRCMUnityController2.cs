using UnityEngine;
using System.IO;
using System.Globalization;
using System.Collections.Generic;

public class DoubleRCMUnityController2 : MonoBehaviour
{
    public enum RCMMode
    {
        Entry,
        Target,
        Double
    }

    public enum InsertionPhase
    {
        ApproachEntry,
        InsertToTarget,
        Done
    }

    [Header("Robot")]
    public Transform[] joints;

    public Vector3[] jointAxesLocal =
    {
        Vector3.up,
        Vector3.forward,
        Vector3.forward,
        Vector3.right,
        Vector3.forward,
        Vector3.up
    };

    [Header("RCM references")]
    public Transform toolFrame;
    public Transform toolTip;
    public float toolLength = 0.32f;
    public Transform entryPoint;
    public Transform targetPoint;

    [Header("Auto tooltip")]
    public bool autoCreateToolTip = true;
    public string autoToolTipName = "ToolTip_Auto";

    [Header("Needle insertion sequence")]
    public bool useInsertionSequence = true;
    public InsertionPhase insertionPhase = InsertionPhase.ApproachEntry;
    public float entryReachedThresholdMm = 15.0f;
    public float targetReachedThresholdMm = 8.0f;

    [Tooltip("Phase 1: drives the real tip to the red entry point.")]
    public float entryApproachTipWeight = 2.6f;

    [Tooltip("Phase 1: weak axis alignment before insertion.")]
    public float preAlignEntryAxisWeight = 0.25f;

    [Tooltip("Phase 2: keeps entry as RCM point.")]
    public float insertionEntryWeight = 3.2f;

    [Tooltip("Phase 2: drives tip to internal target.")]
    public float insertionTargetWeight = 2.2f;

    [Tooltip("Phase 2: keeps the needle axis aligned with the Entry -> Target direction during insertion.")]
    public float insertionAxisWeight = 0.9f;

    [Header("Straight insertion progression")]
    public bool useProgressiveStraightInsertion = true;

    [Range(0f, 1f)]
    public float insertionProgress = 0f;

    [Tooltip("Progress speed from entry to target, in normalized progress units per second.")]
    public float insertionProgressSpeed = 0.35f;

    [Tooltip("Progress advances only when the tip is close to the current intermediate insertion target.")]
    public float insertionProgressAdvanceErrorMm = 10.0f;

    [Header("Skull avoidance")]
    public Collider skullCollider;
    public bool avoidSkullInDouble = true;
    public float allowedEntryRadiusMm = 3f;
    public float skullAvoidanceWeight = 0.8f;

    [Header("Arm-skull avoidance")]
    [Tooltip("Avoids the arm links entering the skull. The needle segment is excluded, because it must enter the skull.")]
    public bool avoidArmLinksFromSkull = true;
    public float armSkullAvoidanceWeight = 8.0f;
    public float armSafetyMargin = 0.08f;
    public int armAvoidanceSamplesPerSegment = 4;

    [Header("Plane fallback if no skull collider")]
    public bool usePlaneFallbackWhenNoCollider = true;
    public Transform skullPlaneNormalReference;
    public Vector3 skullPlaneNormalLocal = Vector3.forward;

    [Header("Task weights - used when insertion sequence is off")]
    public float entryWeight = 3.0f;
    public float targetTipWeight = 2.0f;

    [Header("Target-RCM mode + entry cone")]
    [Tooltip("When mode = Target, the target point is treated as the RCM and the shaft may move inside a small cone at the entry side.")]
    public bool useEntryConeInTargetMode = true;

    [Tooltip("If true, mode [2] actively demonstrates a small conical motion while the target RCM remains fixed.")]
    public bool animateTargetConeDemo = true;

    [Range(0.1f, 25f)]
    public float entryConeHalfAngleDeg = 3.0f;

    [Range(0f, 1f)]
    public float entryConeMotionFraction = 0.65f;

    public float entryConeFrequencyHz = 0.15f;
    public float targetModeTargetRCMWeight = 4.0f;
    public float targetModeEntryConeWeight = 1.4f;

    [Header("Controller")]
    public RCMMode mode = RCMMode.Double;
    public bool solveIK = true;
    public int solverIterations = 2;
    public float damping = 0.22f;
    public float finiteDifferenceDeg = 0.5f;
    public float maxDeltaDegPerIteration = 0.12f;

    [Range(0.01f, 1f)]
    public float ikStepScale = 0.12f;

    [Header("Entry constraint fallback")]
    [Tooltip("Fallback geometric constraint used only if the link-based RCM formula is disabled.")]
    public bool useFiniteNeedleSegmentForEntry = true;

    [Header("Link-based RCM formula")]
    [Tooltip("Implements p_RCM = p_i + lambda * (p_{i+1} - p_i). The solver treats lambda as an extra variable when optimizeEntryLambda is enabled.")]
    public bool useLinkBasedRCMFormula = true;

    [Tooltip("Segment index used for the trocar / entry RCM. -1 means automatic surgical tool segment ToolFrame -> ToolTip.")]
    public int entryRCMSegmentIndex = -1;

    [Range(0f, 1f)]
    public float entryLambda = 0.5f;

    [Tooltip("If true, lambda_dot is solved together with q_dot, matching the augmented RCM Jacobian [J_RCM  p_{i+1}-p_i].")]
    public bool optimizeEntryLambda = true;

    [Tooltip("Initializes lambda from the closest point on the selected link when the insertion phase starts.")]
    public bool initializeLambdaFromClosestPoint = true;

    [Tooltip("Use the same RCM formula also for the target task. With targetLambda = 1 and optimizeTargetLambda = false this is equivalent to constraining the real tip.")]
    public bool useTargetRCMFormula = true;

    [Tooltip("Segment index used for the internal target RCM. -1 means automatic surgical tool segment ToolFrame -> ToolTip.")]
    public int targetRCMSegmentIndex = -1;

    [Range(0f, 1f)]
    public float targetLambda = 1.0f;

    public bool optimizeTargetLambda = false;
    public float finiteDifferenceLambda = 0.001f;
    public float maxLambdaDeltaPerIteration = 0.015f;
    public float lambdaStepScale = 1.0f;

    [Header("Joint limits - ROSA-like approximate")]
    public bool useJointLimits = true;

    public float[] jointMinDeg =
    {
        -170f,
        -85f,
        -130f,
        -170f,
        -110f,
        -180f
    };

    public float[] jointMaxDeg =
    {
        170f,
        85f,
        130f,
        170f,
        110f,
        180f
    };

    public float[] currentJointAnglesDeg;

    [Header("Demo start pose")]
    public bool useDemoStartPose = true;
    public float demoWaitBeforeSolving = 0.5f;

    public float[] demoJointAnglesDeg =
    {
        18f,
        -16f,
        22f,
        -12f,
        14f,
        8f
    };

    [Header("Overlay")]
    public bool showOverlay = true;

    [Header("Debug")]
    public float tipEntryErrorMm;
    public float entryErrorMm;
    public float entryAxisErrorMm;
    public float targetTipErrorMm;
    public float skullViolationMm;
    public float armSkullViolationMm;
    public float entryProjection01;
    public float entryRCMFormulaErrorMm;
    public float targetRCMFormulaErrorMm;
    public int activeEntryRCMSegmentIndex;
    public int activeTargetRCMSegmentIndex;
    public float finalTargetTipErrorMm;
    public float insertionIntermediateTargetErrorMm;
    public float entryTargetAxisErrorDeg;
    public float entryConeAngleDeg;
    public float entryConeViolationDeg;
    public bool jointLimitsOk = true;

    [Header("CSV Logging")]
    public bool logToCsv = true;
    public string logFileName = "rcm_log.csv";
    public float logEverySeconds = 0.02f;

    private Quaternion[] initialRotations;
    private StreamWriter logWriter;
    private float lastLogTime;

    private float demoStartTime;
    private bool demoPoseApplied = false;
    private bool rcmParametersInitialized = false;

    private GUIStyle labelStyle;
    private GUIStyle titleStyle;

    private static DoubleRCMUnityController2 overlayOwner;
    private bool ownsOverlay = false;

    private void Awake()
    {
        ClaimOverlayOwnership();
    }

    private void OnEnable()
    {
        ClaimOverlayOwnership();
        showOverlay = true;
    }

    private void OnDisable()
    {
        if (overlayOwner == this)
            overlayOwner = null;

        ownsOverlay = false;
    }

    private void ClaimOverlayOwnership()
    {
        overlayOwner = this;
        ownsOverlay = true;
    }

    private void Start()
    {
        EnsureAutoToolTip();
        StoreInitialPose();

        insertionPhase = InsertionPhase.ApproachEntry;

        if (useDemoStartPose)
        {
            ApplyDemoStartPose();
            solveIK = false;
            demoStartTime = Time.time;
        }

        StartLogging();
    }

    private void Update()
    {
        HandleInput();
        EnsureAutoToolTip();

        if (useDemoStartPose && demoPoseApplied && !solveIK)
        {
            if (Time.time - demoStartTime >= demoWaitBeforeSolving)
                solveIK = true;
        }

        if (!ReferencesAreValid())
            return;

        UpdateDebugValues();
        UpdateInsertionPhase();
        UpdateInsertionProgress();

        if (solveIK)
        {
            for (int i = 0; i < solverIterations; i++)
                SolveOneIKStep();
        }

        UpdateDebugValues();
        LogSample();
    }

    private void UpdateDebugValues()
    {
        if (!ReferencesAreValid())
            return;

        Vector3 entryError = GetEntryErrorVector(entryPoint.position);
        Vector3 targetError = GetTargetTaskErrorVector(targetPoint.position);

        tipEntryErrorMm = Vector3.Distance(GetToolTipPosition(), entryPoint.position) * 1000f;
        entryAxisErrorMm = PointToInfiniteToolAxisDistance(entryPoint.position) * 1000f;
        entryErrorMm = entryError.magnitude * 1000f;
        targetTipErrorMm = targetError.magnitude * 1000f;
        skullViolationMm = GetSkullViolationDistance() * 1000f;
        armSkullViolationMm = GetMaxArmSkullViolation() * 1000f;
        entryProjection01 = useLinkBasedRCMFormula ? entryLambda : GetProjection01OnNeedleSegment(entryPoint.position);
        entryRCMFormulaErrorMm = entryError.magnitude * 1000f;
        targetRCMFormulaErrorMm = targetError.magnitude * 1000f;
        finalTargetTipErrorMm = Vector3.Distance(GetToolTipPosition(), targetPoint.position) * 1000f;
        insertionIntermediateTargetErrorMm = Vector3.Distance(GetToolTipPosition(), GetCurrentInsertionTargetPoint()) * 1000f;
        entryTargetAxisErrorDeg = GetEntryTargetAxisAngleDeg();
        UpdateEntryConeDebugValues();
        jointLimitsOk = AreJointLimitsSatisfied();
        activeEntryRCMSegmentIndex = ResolveRCMSegmentIndex(entryRCMSegmentIndex);
        activeTargetRCMSegmentIndex = ResolveRCMSegmentIndex(targetRCMSegmentIndex);
    }

    private void UpdateInsertionPhase()
    {
        if (!useInsertionSequence)
            return;

        InsertionPhase oldPhase = insertionPhase;

        if (insertionPhase == InsertionPhase.ApproachEntry && tipEntryErrorMm <= entryReachedThresholdMm)
        {
            insertionPhase = InsertionPhase.InsertToTarget;
            insertionProgress = 0f;
        }

        if (insertionPhase == InsertionPhase.InsertToTarget && insertionProgress >= 0.999f && finalTargetTipErrorMm <= targetReachedThresholdMm)
            insertionPhase = InsertionPhase.Done;

        if (oldPhase != insertionPhase)
        {
            rcmParametersInitialized = false;

            if (insertionPhase == InsertionPhase.InsertToTarget)
                InitializeRCMParametersFromClosestPoints();
        }
    }

    private void UpdateInsertionProgress()
    {
        if (!useInsertionSequence)
            return;

        if (insertionPhase == InsertionPhase.ApproachEntry)
        {
            insertionProgress = 0f;
            return;
        }

        if (insertionPhase != InsertionPhase.InsertToTarget || !useProgressiveStraightInsertion)
            return;

        float currentErrorMm = Vector3.Distance(GetToolTipPosition(), GetCurrentInsertionTargetPoint()) * 1000f;

        if (currentErrorMm <= insertionProgressAdvanceErrorMm || insertionProgress < 0.02f)
            insertionProgress = Mathf.Clamp01(insertionProgress + Time.deltaTime * Mathf.Max(0.01f, insertionProgressSpeed));
    }

    private void EnsureAutoToolTip()
    {
        if (!autoCreateToolTip || toolTip != null || toolFrame == null)
            return;

        Transform existing = toolFrame.Find(autoToolTipName);

        if (existing != null)
        {
            toolTip = existing;
            return;
        }

        GameObject tip = new GameObject(autoToolTipName);
        tip.transform.SetParent(toolFrame, false);
        tip.transform.localPosition = Vector3.forward * toolLength;
        tip.transform.localRotation = Quaternion.identity;
        tip.transform.localScale = Vector3.one;

        toolTip = tip.transform;
    }

    private bool ReferencesAreValid()
    {
        return joints != null &&
               joints.Length > 0 &&
               toolFrame != null &&
               entryPoint != null &&
               targetPoint != null;
    }

    private void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.H))
        {
            showOverlay = !showOverlay;
            ClaimOverlayOwnership();
        }

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            // Entry-RCM mode: the entry point is fixed as trocar RCM and the tip goes to target.
            mode = RCMMode.Entry;
            useInsertionSequence = false;
            insertionPhase = InsertionPhase.Done;
            rcmParametersInitialized = false;
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            // Target-RCM mode: the target is the fixed RCM; the entry side is allowed to move inside a cone.
            mode = RCMMode.Target;
            useInsertionSequence = false;
            insertionPhase = InsertionPhase.Done;
            targetLambda = 1.0f;
            optimizeTargetLambda = false;
            rcmParametersInitialized = false;
        }

        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            mode = RCMMode.Double;
            useInsertionSequence = true;
            insertionPhase = InsertionPhase.ApproachEntry;
            insertionProgress = 0f;
            rcmParametersInitialized = false;
        }

        if (Input.GetKeyDown(KeyCode.I))
        {
            mode = RCMMode.Double;
            useInsertionSequence = true;
            insertionPhase = InsertionPhase.InsertToTarget;
            insertionProgress = Mathf.Max(0f, insertionProgress);
            rcmParametersInitialized = false;
        }

        if (Input.GetKeyDown(KeyCode.C))
            animateTargetConeDemo = !animateTargetConeDemo;

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetPose();
            insertionPhase = InsertionPhase.ApproachEntry;
            insertionProgress = 0f;
            rcmParametersInitialized = false;

            if (useDemoStartPose)
            {
                ApplyDemoStartPose();
                solveIK = false;
                demoStartTime = Time.time;
            }
        }

        if (Input.GetKeyDown(KeyCode.Space))
            solveIK = !solveIK;
    }

    private void StoreInitialPose()
    {
        if (joints == null)
            return;

        EnsureJointLimitArrays();

        initialRotations = new Quaternion[joints.Length];
        currentJointAnglesDeg = new float[joints.Length];

        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i] != null)
                initialRotations[i] = joints[i].localRotation;

            currentJointAnglesDeg[i] = 0f;
        }

        rcmParametersInitialized = false;
    }

    private void ResetPose()
    {
        if (initialRotations == null || joints == null)
            return;

        EnsureJointLimitArrays();

        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i] != null)
                joints[i].localRotation = initialRotations[i];

            if (currentJointAnglesDeg != null && i < currentJointAnglesDeg.Length)
                currentJointAnglesDeg[i] = 0f;
        }

        rcmParametersInitialized = false;
        demoPoseApplied = false;
    }

    private void ApplyDemoStartPose()
    {
        if (joints == null || demoJointAnglesDeg == null)
            return;

        EnsureJointLimitArrays();

        int count = Mathf.Min(joints.Length, demoJointAnglesDeg.Length);

        for (int i = 0; i < count; i++)
        {
            if (joints[i] == null)
                continue;

            float desiredAngle = demoJointAnglesDeg[i];

            if (useJointLimits)
                desiredAngle = Mathf.Clamp(desiredAngle, GetJointMinDeg(i), GetJointMaxDeg(i));

            SetJointAngleDeg(i, desiredAngle);
        }

        rcmParametersInitialized = false;
        demoPoseApplied = true;
    }

    private void SolveOneIKStep()
    {
        if (useInsertionSequence && insertionPhase == InsertionPhase.Done)
            return;

        EnsureJointLimitArrays();

        if (!rcmParametersInitialized)
            InitializeRCMParametersFromClosestPoints();

        Vector3[] errorVectors = GetCurrentErrorVectors();

        int m = errorVectors.Length * 3;
        int variableCount = GetActiveVariableCount();

        if (m <= 0 || variableCount <= 0)
            return;

        float[] e = new float[m];

        for (int i = 0; i < errorVectors.Length; i++)
        {
            e[i * 3 + 0] = errorVectors[i].x;
            e[i * 3 + 1] = errorVectors[i].y;
            e[i * 3 + 2] = errorVectors[i].z;
        }

        float[,] J = ComputeNumericalJacobian(e, m, variableCount);
        float[] dx = SolveDampedLeastSquares(J, e, m, variableCount);

        ApplySolutionStep(dx);
    }

    private Vector3[] GetCurrentErrorVectors()
    {
        if (useInsertionSequence)
            return GetInsertionSequenceErrorVectors();

        if (mode == RCMMode.Entry)
        {
            List<Vector3> entryErrors = new List<Vector3>();
            entryErrors.Add(entryWeight * GetEntryErrorVector(entryPoint.position));
            entryErrors.Add(targetTipWeight * GetTargetTaskErrorVector(targetPoint.position));
            entryErrors.Add(insertionAxisWeight * EntryTargetAxisAlignmentErrorVector());
            AddSkullAvoidanceErrors(entryErrors);
            return entryErrors.ToArray();
        }

        if (mode == RCMMode.Target)
        {
            List<Vector3> targetErrors = new List<Vector3>();
            targetErrors.Add(targetModeTargetRCMWeight * GetTargetTaskErrorVector(targetPoint.position));

            if (useEntryConeInTargetMode)
                targetErrors.Add(targetModeEntryConeWeight * EntryConeErrorVector());

            AddArmSkullAvoidanceErrors(targetErrors);
            return targetErrors.ToArray();
        }

        List<Vector3> errors = new List<Vector3>();

        errors.Add(entryWeight * GetEntryErrorVector(entryPoint.position));
        errors.Add(targetTipWeight * GetTargetTaskErrorVector(targetPoint.position));
        errors.Add(insertionAxisWeight * EntryTargetAxisAlignmentErrorVector());
        AddSkullAvoidanceErrors(errors);

        return errors.ToArray();
    }

    private Vector3[] GetInsertionSequenceErrorVectors()
    {
        List<Vector3> errors = new List<Vector3>();

        if (insertionPhase == InsertionPhase.ApproachEntry)
        {
            // Phase 1: physical needle tip goes to the red entry point.
            errors.Add(entryApproachTipWeight * TipToPointErrorVector(entryPoint.position));

            // Weak pre-alignment. This helps the next insertion phase by aligning the shaft with Entry -> Target.
            errors.Add(preAlignEntryAxisWeight * PointToInfiniteToolAxisErrorVector(entryPoint.position));
            errors.Add(preAlignEntryAxisWeight * EntryTargetAxisAlignmentErrorVector());

            // During approach, only keep the arm outside the skull.
            AddArmSkullAvoidanceErrors(errors);
            return errors.ToArray();
        }

        if (insertionPhase == InsertionPhase.InsertToTarget)
        {
            // Phase 2: entry becomes the trocar RCM point on the selected link,
            // while the target task is represented with the same RCM formula
            // using targetLambda = 1 by default, i.e. the physical tip.
            Vector3 insertionTarget = GetCurrentInsertionTargetPoint();

            errors.Add(insertionEntryWeight * GetEntryErrorVector(entryPoint.position));
            errors.Add(insertionTargetWeight * GetTargetTaskErrorVector(insertionTarget));
            errors.Add(insertionAxisWeight * EntryTargetAxisAlignmentErrorVector());

            AddSkullAvoidanceErrors(errors);
            return errors.ToArray();
        }

        errors.Add(Vector3.zero);
        AddSkullAvoidanceErrors(errors);
        return errors.ToArray();
    }

    private void AddSkullAvoidanceErrors(List<Vector3> errors)
    {
        if (avoidSkullInDouble)
            errors.Add(skullAvoidanceWeight * SkullEntryErrorVector());

        AddArmSkullAvoidanceErrors(errors);
    }

    private void AddArmSkullAvoidanceErrors(List<Vector3> errors)
    {
        int samples = Mathf.Max(2, armAvoidanceSamplesPerSegment);
        int expectedSegments = GetExpectedArmAvoidanceSegments();

        if (!avoidArmLinksFromSkull || skullCollider == null || joints == null || joints.Length < 2)
        {
            for (int i = 0; i < expectedSegments * samples; i++)
                errors.Add(Vector3.zero);

            return;
        }

        for (int i = 0; i < joints.Length - 1; i++)
        {
            if (joints[i] == null || joints[i + 1] == null)
                AddZeroAvoidanceSamples(errors, samples);
            else
                AddSegmentSkullAvoidanceErrors(errors, joints[i].position, joints[i + 1].position, samples);
        }

        if (joints[joints.Length - 1] != null && toolFrame != null)
            AddSegmentSkullAvoidanceErrors(errors, joints[joints.Length - 1].position, toolFrame.position, samples);
        else
            AddZeroAvoidanceSamples(errors, samples);
    }

    private int GetExpectedArmAvoidanceSegments()
    {
        if (joints == null || joints.Length < 2)
            return 0;

        // Joint-to-joint segments plus last joint to tool frame.
        return (joints.Length - 1) + 1;
    }

    private void AddZeroAvoidanceSamples(List<Vector3> errors, int samples)
    {
        for (int s = 0; s < samples; s++)
            errors.Add(Vector3.zero);
    }

    private void AddSegmentSkullAvoidanceErrors(List<Vector3> errors, Vector3 a, Vector3 b, int samples)
    {
        for (int s = 0; s < samples; s++)
        {
            float t = samples == 1 ? 0f : (float)s / (float)(samples - 1);
            Vector3 p = Vector3.Lerp(a, b, t);
            Vector3 push = ComputePushOutOfSkull(p);

            // Always add one vector per sample, even if it is zero.
            errors.Add(armSkullAvoidanceWeight * push);
        }
    }

    private Vector3 ComputePushOutOfSkull(Vector3 point)
    {
        if (skullCollider == null)
            return Vector3.zero;

        Bounds b = skullCollider.bounds;
        Vector3 center = b.center;

        Vector3 radii = b.extents + Vector3.one * Mathf.Max(0f, armSafetyMargin);
        radii.x = Mathf.Max(radii.x, 1e-4f);
        radii.y = Mathf.Max(radii.y, 1e-4f);
        radii.z = Mathf.Max(radii.z, 1e-4f);

        Vector3 q = new Vector3(
            (point.x - center.x) / radii.x,
            (point.y - center.y) / radii.y,
            (point.z - center.z) / radii.z
        );

        float qNorm = q.magnitude;

        if (qNorm >= 1f)
            return Vector3.zero;

        Vector3 dir = qNorm < 1e-5f ? Vector3.up : q / qNorm;

        Vector3 safePoint = center + new Vector3(dir.x * radii.x, dir.y * radii.y, dir.z * radii.z);

        // Error convention is desired - current.
        return safePoint - point;
    }

    private float GetMaxArmSkullViolation()
    {
        if (!avoidArmLinksFromSkull || skullCollider == null || joints == null || joints.Length < 2)
            return 0f;

        float maxViolation = 0f;
        int samples = Mathf.Max(2, armAvoidanceSamplesPerSegment);

        for (int i = 0; i < joints.Length - 1; i++)
        {
            if (joints[i] == null || joints[i + 1] == null)
                continue;

            maxViolation = Mathf.Max(maxViolation, GetMaxSegmentSkullViolation(joints[i].position, joints[i + 1].position, samples));
        }

        if (joints[joints.Length - 1] != null && toolFrame != null)
            maxViolation = Mathf.Max(maxViolation, GetMaxSegmentSkullViolation(joints[joints.Length - 1].position, toolFrame.position, samples));

        return maxViolation;
    }

    private float GetMaxSegmentSkullViolation(Vector3 a, Vector3 b, int samples)
    {
        float maxViolation = 0f;

        for (int s = 0; s < samples; s++)
        {
            float t = samples == 1 ? 0f : (float)s / (float)(samples - 1);
            Vector3 p = Vector3.Lerp(a, b, t);
            maxViolation = Mathf.Max(maxViolation, ComputePushOutOfSkull(p).magnitude);
        }

        return maxViolation;
    }

    private Vector3 GetEntryErrorVector(Vector3 point)
    {
        if (useLinkBasedRCMFormula)
            return RCMPointErrorVector(point, entryRCMSegmentIndex, entryLambda);

        if (useFiniteNeedleSegmentForEntry)
            return PointToNeedleSegmentErrorVector(point);

        return PointToInfiniteToolAxisErrorVector(point);
    }

    private Vector3 GetTargetTaskErrorVector(Vector3 point)
    {
        if (useTargetRCMFormula)
            return RCMPointErrorVector(point, targetRCMSegmentIndex, targetLambda);

        return TipToPointErrorVector(point);
    }

    private Vector3 GetCurrentInsertionTargetPoint()
    {
        if (entryPoint == null || targetPoint == null)
            return Vector3.zero;

        if (!useProgressiveStraightInsertion)
            return targetPoint.position;

        return Vector3.Lerp(entryPoint.position, targetPoint.position, Mathf.Clamp01(insertionProgress));
    }

    private Vector3 EntryTargetAxisAlignmentErrorVector()
    {
        if (entryPoint == null || targetPoint == null || toolFrame == null)
            return Vector3.zero;

        Vector3 desired = targetPoint.position - entryPoint.position;
        float length = desired.magnitude;

        if (length < 1e-6f)
            return Vector3.zero;

        desired /= length;

        Vector3 actual = GetToolDirection();

        // For line alignment, choose the sign of the tool axis that is closest to Entry -> Target.
        if (Vector3.Dot(actual, desired) < 0f)
            actual = -actual;

        float sampleLength = Mathf.Max(0.05f, length);
        Vector3 actualSample = entryPoint.position + actual * sampleLength;
        Vector3 desiredSample = entryPoint.position + desired * sampleLength;

        return desiredSample - actualSample;
    }

    private float GetEntryTargetAxisAngleDeg()
    {
        if (entryPoint == null || targetPoint == null || toolFrame == null)
            return 0f;

        Vector3 desired = targetPoint.position - entryPoint.position;

        if (desired.sqrMagnitude < 1e-8f)
            return 0f;

        desired.Normalize();
        Vector3 actual = GetToolDirection();

        if (Vector3.Dot(actual, desired) < 0f)
            actual = -actual;

        return Vector3.Angle(desired, actual);
    }

    private Vector3 EntryConeErrorVector()
    {
        if (entryPoint == null || targetPoint == null || toolFrame == null)
            return Vector3.zero;

        Vector3 nominalDir = GetNominalTargetToEntryDirection();
        Vector3 actualDir = GetActualTargetToEntrySideDirection();
        float sampleLength = Mathf.Max(0.05f, Vector3.Distance(entryPoint.position, targetPoint.position));

        if (animateTargetConeDemo)
        {
            Vector3 desiredDir = GetAnimatedConeDirection(nominalDir);
            Vector3 actualSample = targetPoint.position + actualDir * sampleLength;
            Vector3 desiredSample = targetPoint.position + desiredDir * sampleLength;
            return desiredSample - actualSample;
        }

        return EntryConeLimitErrorVector(nominalDir, actualDir, sampleLength);
    }

    private Vector3 EntryConeLimitErrorVector(Vector3 nominalDir, Vector3 actualDir, float sampleLength)
    {
        float angle = Vector3.Angle(nominalDir, actualDir);
        float allowed = Mathf.Max(0.01f, entryConeHalfAngleDeg);

        if (angle <= allowed)
            return Vector3.zero;

        float t = allowed / Mathf.Max(angle, 1e-4f);
        Vector3 boundaryDir = Vector3.Slerp(nominalDir, actualDir, t).normalized;
        Vector3 actualSample = targetPoint.position + actualDir * sampleLength;
        Vector3 boundarySample = targetPoint.position + boundaryDir * sampleLength;

        return boundarySample - actualSample;
    }

    private Vector3 GetNominalTargetToEntryDirection()
    {
        Vector3 dir = entryPoint.position - targetPoint.position;

        if (dir.sqrMagnitude < 1e-8f)
            return Vector3.forward;

        return dir.normalized;
    }

    private Vector3 GetActualTargetToEntrySideDirection()
    {
        Vector3 nominalDir = GetNominalTargetToEntryDirection();
        Vector3 actualDir = GetToolDirection();

        // Use the tool-axis direction pointing from target toward the entry side.
        if (Vector3.Dot(actualDir, nominalDir) < 0f)
            actualDir = -actualDir;

        return actualDir.normalized;
    }

    private Vector3 GetAnimatedConeDirection(Vector3 nominalDir)
    {
        Vector3 u;
        Vector3 v;
        GetConeBasis(nominalDir, out u, out v);

        float angleDeg = Mathf.Clamp(entryConeHalfAngleDeg * entryConeMotionFraction, 0f, entryConeHalfAngleDeg);
        float theta = 2f * Mathf.PI * Mathf.Max(0.01f, entryConeFrequencyHz) * Time.time;
        Vector3 radialAxis = Mathf.Cos(theta) * u + Mathf.Sin(theta) * v;
        Quaternion rot = Quaternion.AngleAxis(angleDeg, radialAxis.normalized);

        return (rot * nominalDir).normalized;
    }

    private void GetConeBasis(Vector3 axis, out Vector3 u, out Vector3 v)
    {
        Vector3 helper = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
        u = Vector3.Cross(axis, helper).normalized;
        v = Vector3.Cross(axis, u).normalized;
    }

    private void UpdateEntryConeDebugValues()
    {
        if (entryPoint == null || targetPoint == null || toolFrame == null)
        {
            entryConeAngleDeg = 0f;
            entryConeViolationDeg = 0f;
            return;
        }

        Vector3 nominalDir = GetNominalTargetToEntryDirection();
        Vector3 actualDir = GetActualTargetToEntrySideDirection();
        entryConeAngleDeg = Vector3.Angle(nominalDir, actualDir);
        entryConeViolationDeg = Mathf.Max(0f, entryConeAngleDeg - entryConeHalfAngleDeg);
    }

    private bool AreJointLimitsSatisfied()
    {
        if (!useJointLimits || currentJointAnglesDeg == null)
            return true;

        for (int i = 0; i < currentJointAnglesDeg.Length; i++)
        {
            if (currentJointAnglesDeg[i] < GetJointMinDeg(i) - 0.01f || currentJointAnglesDeg[i] > GetJointMaxDeg(i) + 0.01f)
                return false;
        }

        return true;
    }

    private Vector3 RCMPointErrorVector(Vector3 fixedPoint, int segmentIndex, float lambda)
    {
        Vector3 pi;
        Vector3 piPlusOne;

        if (!TryGetKinematicSegment(segmentIndex, out pi, out piPlusOne))
            return Vector3.zero;

        float clampedLambda = Mathf.Clamp01(lambda);
        Vector3 pRcm = pi + clampedLambda * (piPlusOne - pi);

        // Error convention used by the IK solver: desired - current.
        return fixedPoint - pRcm;
    }

    private void InitializeRCMParametersFromClosestPoints()
    {
        if (!ReferencesAreValid())
            return;

        if (useLinkBasedRCMFormula && initializeLambdaFromClosestPoint)
            entryLambda = ClosestLambdaOnSegment(entryPoint.position, entryRCMSegmentIndex);
        else
            entryLambda = Mathf.Clamp01(entryLambda);

        if (useTargetRCMFormula && optimizeTargetLambda && initializeLambdaFromClosestPoint)
            targetLambda = ClosestLambdaOnSegment(targetPoint.position, targetRCMSegmentIndex);
        else
            targetLambda = Mathf.Clamp01(targetLambda);

        rcmParametersInitialized = true;
    }

    private float ClosestLambdaOnSegment(Vector3 point, int segmentIndex)
    {
        Vector3 a;
        Vector3 b;

        if (!TryGetKinematicSegment(segmentIndex, out a, out b))
            return 0f;

        Vector3 ab = b - a;
        float denom = Vector3.Dot(ab, ab);

        if (denom < 1e-8f)
            return 0f;

        return Mathf.Clamp01(Vector3.Dot(point - a, ab) / denom);
    }

    private bool TryGetKinematicSegment(int segmentIndex, out Vector3 a, out Vector3 b)
    {
        a = Vector3.zero;
        b = Vector3.zero;

        int resolvedSegment = ResolveRCMSegmentIndex(segmentIndex);
        int segmentCount = GetKinematicSegmentCount();

        if (resolvedSegment < 0 || resolvedSegment >= segmentCount)
            return false;

        return TryGetKinematicPoint(resolvedSegment, out a) &&
               TryGetKinematicPoint(resolvedSegment + 1, out b);
    }

    private int ResolveRCMSegmentIndex(int configuredSegmentIndex)
    {
        if (configuredSegmentIndex >= 0)
            return configuredSegmentIndex;

        return GetToolSegmentIndex();
    }

    private int GetToolSegmentIndex()
    {
        if (joints == null)
            return 0;

        // Kinematic points are: Joint_0 ... Joint_n, ToolFrame, ToolTip.
        // Therefore the surgical needle segment ToolFrame -> ToolTip has index joints.Length.
        return joints.Length;
    }

    private int GetKinematicSegmentCount()
    {
        return Mathf.Max(0, GetKinematicPointCount() - 1);
    }

    private int GetKinematicPointCount()
    {
        int count = 0;

        if (joints != null)
            count += joints.Length;

        if (toolFrame != null)
            count += 1;

        if (toolFrame != null || toolTip != null)
            count += 1;

        return count;
    }

    private bool TryGetKinematicPoint(int index, out Vector3 point)
    {
        point = Vector3.zero;

        int jointCount = joints == null ? 0 : joints.Length;

        if (index >= 0 && index < jointCount)
        {
            if (joints[index] == null)
                return false;

            point = joints[index].position;
            return true;
        }

        if (index == jointCount && toolFrame != null)
        {
            point = toolFrame.position;
            return true;
        }

        if (index == jointCount + 1 && (toolTip != null || toolFrame != null))
        {
            point = GetToolTipPosition();
            return true;
        }

        return false;
    }

    private Vector3 TipToPointErrorVector(Vector3 point)
    {
        return point - GetToolTipPosition();
    }

    private Vector3 SkullEntryErrorVector()
    {
        if (!avoidSkullInDouble || mode != RCMMode.Double)
            return Vector3.zero;

        Vector3 hitPoint;

        if (!TryGetSkullIntersection(out hitPoint))
            return Vector3.zero;

        float allowedRadius = allowedEntryRadiusMm * 0.001f;
        float distanceFromEntry = Vector3.Distance(hitPoint, entryPoint.position);

        if (distanceFromEntry <= allowedRadius)
            return Vector3.zero;

        return entryPoint.position - hitPoint;
    }

    private float GetSkullViolationDistance()
    {
        if (!avoidSkullInDouble || mode != RCMMode.Double)
            return 0f;

        Vector3 hitPoint;

        if (!TryGetSkullIntersection(out hitPoint))
            return 0f;

        float allowedRadius = allowedEntryRadiusMm * 0.001f;
        float distanceFromEntry = Vector3.Distance(hitPoint, entryPoint.position);

        if (distanceFromEntry <= allowedRadius)
            return 0f;

        return distanceFromEntry;
    }

    private bool TryGetSkullIntersection(out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;

        Vector3 basePos = GetToolBasePosition();
        Vector3 tipPos = GetToolTipPosition();

        Vector3 segment = tipPos - basePos;
        float length = segment.magnitude;

        if (length < 1e-6f)
            return false;

        Vector3 direction = segment / length;

        if (skullCollider != null)
        {
            Physics.SyncTransforms();

            Ray ray = new Ray(basePos - direction * 0.01f, direction);
            RaycastHit hit;

            if (skullCollider.Raycast(ray, out hit, length + 0.02f))
            {
                hitPoint = hit.point;
                return true;
            }

            return false;
        }

        if (usePlaneFallbackWhenNoCollider)
            return TryGetSkullPlaneIntersection(basePos, tipPos, out hitPoint);

        return false;
    }

    private bool TryGetSkullPlaneIntersection(Vector3 basePos, Vector3 tipPos, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;

        if (entryPoint == null)
            return false;

        Vector3 normal = GetSkullPlaneNormal();
        Vector3 planePoint = entryPoint.position;

        float d0 = Vector3.Dot(basePos - planePoint, normal);
        float d1 = Vector3.Dot(tipPos - planePoint, normal);

        if (Mathf.Abs(d0 - d1) < 1e-6f)
            return false;

        if ((d0 > 0f && d1 > 0f) || (d0 < 0f && d1 < 0f))
            return false;

        float t = d0 / (d0 - d1);

        if (t < 0f || t > 1f)
            return false;

        hitPoint = Vector3.Lerp(basePos, tipPos, t);
        return true;
    }

    private Vector3 GetSkullPlaneNormal()
    {
        if (skullPlaneNormalReference != null)
        {
            Vector3 n = skullPlaneNormalReference.TransformDirection(skullPlaneNormalLocal);

            if (n.sqrMagnitude > 1e-6f)
                return n.normalized;
        }

        if (entryPoint != null && entryPoint.forward.sqrMagnitude > 1e-6f)
            return entryPoint.forward.normalized;

        return Vector3.forward;
    }

    private float[,] ComputeNumericalJacobian(float[] baseError, int m, int variableCount)
    {
        float[,] J = new float[m, variableCount];

        int jointCount = joints == null ? 0 : joints.Length;
        float[] originalAngles = new float[jointCount];

        for (int i = 0; i < jointCount; i++)
            originalAngles[i] = GetCurrentJointAngleDeg(i);

        float originalEntryLambda = entryLambda;
        float originalTargetLambda = targetLambda;

        for (int j = 0; j < jointCount; j++)
        {
            if (joints[j] == null)
                continue;

            float stepDeg = GetUsableJointFiniteDifferenceDeg(j, originalAngles[j]);

            if (Mathf.Abs(stepDeg) < 1e-8f)
                continue;

            SetJointAngleDeg(j, originalAngles[j] + stepDeg);
            FillJacobianColumn(J, j, baseError, stepDeg * Mathf.Deg2Rad, m);
            SetJointAngleDeg(j, originalAngles[j]);
        }

        int column = jointCount;

        if (useLinkBasedRCMFormula && optimizeEntryLambda && column < variableCount)
        {
            float step = GetUsableLambdaFiniteDifference(entryLambda);

            if (Mathf.Abs(step) > 1e-8f)
            {
                entryLambda = Mathf.Clamp01(originalEntryLambda + step);
                FillJacobianColumn(J, column, baseError, step, m);
                entryLambda = originalEntryLambda;
            }

            column++;
        }

        if (useTargetRCMFormula && optimizeTargetLambda && column < variableCount)
        {
            float step = GetUsableLambdaFiniteDifference(targetLambda);

            if (Mathf.Abs(step) > 1e-8f)
            {
                targetLambda = Mathf.Clamp01(originalTargetLambda + step);
                FillJacobianColumn(J, column, baseError, step, m);
                targetLambda = originalTargetLambda;
            }
        }

        RestoreJointAngles(originalAngles);
        entryLambda = originalEntryLambda;
        targetLambda = originalTargetLambda;

        return J;
    }

    private void FillJacobianColumn(float[,] J, int column, float[] baseError, float step, int m)
    {
        Vector3[] perturbedErrors = GetCurrentErrorVectors();
        float[] ePerturbed = new float[m];

        int usableCount = Mathf.Min(perturbedErrors.Length, m / 3);

        for (int k = 0; k < usableCount; k++)
        {
            ePerturbed[k * 3 + 0] = perturbedErrors[k].x;
            ePerturbed[k * 3 + 1] = perturbedErrors[k].y;
            ePerturbed[k * 3 + 2] = perturbedErrors[k].z;
        }

        for (int row = 0; row < m; row++)
            J[row, column] = (ePerturbed[row] - baseError[row]) / step;
    }

    private int GetActiveVariableCount()
    {
        int count = joints == null ? 0 : joints.Length;

        if (useLinkBasedRCMFormula && optimizeEntryLambda)
            count++;

        if (useTargetRCMFormula && optimizeTargetLambda)
            count++;

        return count;
    }

    private void ApplySolutionStep(float[] dx)
    {
        if (dx == null)
            return;

        int jointCount = joints == null ? 0 : joints.Length;
        int usableJointCount = Mathf.Min(jointCount, dx.Length);

        for (int i = 0; i < usableJointCount; i++)
        {
            if (joints[i] == null)
                continue;

            float deltaDeg = dx[i] * Mathf.Rad2Deg * ikStepScale;
            deltaDeg = Mathf.Clamp(deltaDeg, -maxDeltaDegPerIteration, maxDeltaDegPerIteration);

            ApplyJointDeltaDeg(i, deltaDeg);
        }

        int index = jointCount;

        if (useLinkBasedRCMFormula && optimizeEntryLambda && index < dx.Length)
        {
            ApplyLambdaDelta(ref entryLambda, dx[index]);
            index++;
        }

        if (useTargetRCMFormula && optimizeTargetLambda && index < dx.Length)
            ApplyLambdaDelta(ref targetLambda, dx[index]);
    }

    private void ApplyLambdaDelta(ref float lambda, float delta)
    {
        float scaledDelta = delta * lambdaStepScale;
        scaledDelta = Mathf.Clamp(scaledDelta, -maxLambdaDeltaPerIteration, maxLambdaDeltaPerIteration);
        lambda = Mathf.Clamp01(lambda + scaledDelta);
    }

    private float GetUsableJointFiniteDifferenceDeg(int jointIndex, float originalAngle)
    {
        float stepDeg = Mathf.Abs(finiteDifferenceDeg);

        if (!useJointLimits)
            return stepDeg;

        float min = GetJointMinDeg(jointIndex);
        float max = GetJointMaxDeg(jointIndex);

        if (originalAngle + stepDeg <= max)
            return stepDeg;

        if (originalAngle - stepDeg >= min)
            return -stepDeg;

        return 0f;
    }

    private float GetUsableLambdaFiniteDifference(float lambda)
    {
        float step = Mathf.Max(1e-5f, Mathf.Abs(finiteDifferenceLambda));

        if (lambda + step <= 1f)
            return step;

        if (lambda - step >= 0f)
            return -step;

        return 0f;
    }

    private void ApplyJointDeltaDeg(int jointIndex, float deltaDeg)
    {
        float desiredAngle = GetCurrentJointAngleDeg(jointIndex) + deltaDeg;

        if (useJointLimits)
            desiredAngle = Mathf.Clamp(desiredAngle, GetJointMinDeg(jointIndex), GetJointMaxDeg(jointIndex));

        SetJointAngleDeg(jointIndex, desiredAngle);
    }

    private void SetJointAngleDeg(int jointIndex, float angleDeg)
    {
        if (joints == null || jointIndex < 0 || jointIndex >= joints.Length || joints[jointIndex] == null)
            return;

        EnsureJointLimitArrays();

        if (initialRotations == null || jointIndex >= initialRotations.Length)
        {
            joints[jointIndex].localRotation = joints[jointIndex].localRotation * Quaternion.AngleAxis(angleDeg, GetJointAxis(jointIndex));
        }
        else
        {
            joints[jointIndex].localRotation = initialRotations[jointIndex] * Quaternion.AngleAxis(angleDeg, GetJointAxis(jointIndex));
        }

        currentJointAnglesDeg[jointIndex] = angleDeg;
    }

    private float GetCurrentJointAngleDeg(int jointIndex)
    {
        EnsureJointLimitArrays();

        if (currentJointAnglesDeg == null || jointIndex < 0 || jointIndex >= currentJointAnglesDeg.Length)
            return 0f;

        return currentJointAnglesDeg[jointIndex];
    }

    private void RestoreJointAngles(float[] angles)
    {
        if (angles == null)
            return;

        int count = Mathf.Min(angles.Length, joints == null ? 0 : joints.Length);

        for (int i = 0; i < count; i++)
            SetJointAngleDeg(i, angles[i]);
    }

    private void EnsureJointLimitArrays()
    {
        int n = joints == null ? 0 : joints.Length;

        if (n <= 0)
            return;

        if (currentJointAnglesDeg == null || currentJointAnglesDeg.Length != n)
            currentJointAnglesDeg = new float[n];

        jointMinDeg = EnsureLimitArray(jointMinDeg, n, true);
        jointMaxDeg = EnsureLimitArray(jointMaxDeg, n, false);
    }

    private float[] EnsureLimitArray(float[] input, int count, bool isMin)
    {
        if (input != null && input.Length == count)
            return input;

        float[] output = new float[count];

        for (int i = 0; i < count; i++)
        {
            if (input != null && i < input.Length)
                output[i] = input[i];
            else
                output[i] = isMin ? GetDefaultJointMinDeg(i) : GetDefaultJointMaxDeg(i);
        }

        return output;
    }

    private float GetJointMinDeg(int jointIndex)
    {
        if (jointMinDeg != null && jointIndex >= 0 && jointIndex < jointMinDeg.Length)
            return jointMinDeg[jointIndex];

        return GetDefaultJointMinDeg(jointIndex);
    }

    private float GetJointMaxDeg(int jointIndex)
    {
        if (jointMaxDeg != null && jointIndex >= 0 && jointIndex < jointMaxDeg.Length)
            return jointMaxDeg[jointIndex];

        return GetDefaultJointMaxDeg(jointIndex);
    }

    private float GetDefaultJointMinDeg(int jointIndex)
    {
        switch (jointIndex)
        {
            case 0: return -170f;
            case 1: return -85f;
            case 2: return -130f;
            case 3: return -170f;
            case 4: return -110f;
            case 5: return -180f;
            default: return -180f;
        }
    }

    private float GetDefaultJointMaxDeg(int jointIndex)
    {
        switch (jointIndex)
        {
            case 0: return 170f;
            case 1: return 85f;
            case 2: return 130f;
            case 3: return 170f;
            case 4: return 110f;
            case 5: return 180f;
            default: return 180f;
        }
    }

    private float[] SolveDampedLeastSquares(float[,] J, float[] e, int m, int n)
    {
        float[,] A = new float[m, m];

        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < m; c++)
            {
                float sum = 0f;

                for (int k = 0; k < n; k++)
                    sum += J[r, k] * J[c, k];

                A[r, c] = sum;
            }

            A[r, r] += damping * damping;
        }

        float[] y = SolveLinearSystem(A, e, m);

        float[] dq = new float[n];

        for (int j = 0; j < n; j++)
        {
            float sum = 0f;

            for (int r = 0; r < m; r++)
                sum += J[r, j] * y[r];

            dq[j] = -sum;
        }

        return dq;
    }

    private float[] SolveLinearSystem(float[,] A, float[] b, int n)
    {
        float[,] M = new float[n, n + 1];

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
                M[r, c] = A[r, c];

            M[r, n] = b[r];
        }

        for (int i = 0; i < n; i++)
        {
            int pivot = i;
            float maxAbs = Mathf.Abs(M[i, i]);

            for (int r = i + 1; r < n; r++)
            {
                float value = Mathf.Abs(M[r, i]);

                if (value > maxAbs)
                {
                    maxAbs = value;
                    pivot = r;
                }
            }

            if (pivot != i)
            {
                for (int c = i; c <= n; c++)
                {
                    float tmp = M[i, c];
                    M[i, c] = M[pivot, c];
                    M[pivot, c] = tmp;
                }
            }

            float diag = M[i, i];

            if (Mathf.Abs(diag) < 1e-8f)
                diag = 1e-8f;

            for (int c = i; c <= n; c++)
                M[i, c] /= diag;

            for (int r = 0; r < n; r++)
            {
                if (r == i)
                    continue;

                float factor = M[r, i];

                for (int c = i; c <= n; c++)
                    M[r, c] -= factor * M[i, c];
            }
        }

        float[] x = new float[n];

        for (int i = 0; i < n; i++)
            x[i] = M[i, n];

        return x;
    }

    private Vector3 PointToInfiniteToolAxisErrorVector(Vector3 point)
    {
        Vector3 a = GetToolBasePosition();
        Vector3 d = GetToolDirection();

        Vector3 ap = point - a;
        Vector3 closest = a + Vector3.Dot(ap, d) * d;

        return point - closest;
    }

    private float PointToInfiniteToolAxisDistance(Vector3 point)
    {
        return PointToInfiniteToolAxisErrorVector(point).magnitude;
    }

    private Vector3 PointToNeedleSegmentErrorVector(Vector3 point)
    {
        Vector3 closest = ClosestPointOnNeedleSegment(point);
        return point - closest;
    }

    private Vector3 ClosestPointOnNeedleSegment(Vector3 point)
    {
        Vector3 a = GetToolBasePosition();
        Vector3 b = GetToolTipPosition();
        Vector3 ab = b - a;

        float denom = Vector3.Dot(ab, ab);

        if (denom < 1e-8f)
            return a;

        float t = Vector3.Dot(point - a, ab) / denom;
        t = Mathf.Clamp01(t);

        return a + t * ab;
    }

    private float GetProjection01OnNeedleSegment(Vector3 point)
    {
        Vector3 a = GetToolBasePosition();
        Vector3 b = GetToolTipPosition();
        Vector3 ab = b - a;

        float denom = Vector3.Dot(ab, ab);

        if (denom < 1e-8f)
            return 0f;

        return Vector3.Dot(point - a, ab) / denom;
    }

    private Vector3 ClosestPointOnInfiniteToolAxis(Vector3 point)
    {
        Vector3 a = GetToolBasePosition();
        Vector3 d = GetToolDirection();

        return a + Vector3.Dot(point - a, d) * d;
    }

    private Vector3 GetToolBasePosition()
    {
        return toolFrame.position;
    }

    private Vector3 GetToolTipPosition()
    {
        if (toolTip != null)
            return toolTip.position;

        if (toolFrame == null)
            return Vector3.zero;

        return toolFrame.position + toolFrame.forward.normalized * toolLength;
    }

    private Vector3 GetToolDirection()
    {
        if (toolTip != null && toolFrame != null)
        {
            Vector3 dir = toolTip.position - toolFrame.position;

            if (dir.sqrMagnitude > 1e-6f)
                return dir.normalized;
        }

        return toolFrame.forward.normalized;
    }

    private Vector3 GetJointAxis(int i)
    {
        if (jointAxesLocal == null || i >= jointAxesLocal.Length)
            return Vector3.forward;

        if (jointAxesLocal[i].sqrMagnitude < 0.0001f)
            return Vector3.forward;

        return jointAxesLocal[i].normalized;
    }

    private void StartLogging()
    {
        if (!logToCsv)
            return;

        string folder = Path.Combine(Application.dataPath, "../RCM_logs");

        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, logFileName);

        logWriter = new StreamWriter(path, false);
        logWriter.WriteLine(
            "time,phase,mode,tip_entry_error_mm,entry_rcm_error_mm,entry_axis_error_deg,target_rcm_or_tip_error_mm,final_target_tip_error_mm,insertion_progress,insertion_intermediate_error_mm,entry_cone_angle_deg,entry_cone_violation_deg,skull_violation_mm,arm_skull_violation_mm,entry_lambda,target_lambda,entry_segment,target_segment,joint_limits_ok," +
            "tool_x,tool_y,tool_z,tip_x,tip_y,tip_z," +
            "entry_x,entry_y,entry_z,target_x,target_y,target_z," +
            "q0_deg,q1_deg,q2_deg,q3_deg,q4_deg,q5_deg"
        );

        Debug.Log("RCM CSV log saved to: " + path);
    }

    private void LogSample()
    {
        if (!logToCsv || logWriter == null)
            return;

        if (Time.time - lastLogTime < logEverySeconds)
            return;

        lastLogTime = Time.time;

        Vector3 toolPos = toolFrame.position;
        Vector3 tipPos = GetToolTipPosition();
        Vector3 entryPos = entryPoint.position;
        Vector3 targetPos = targetPoint.position;

        logWriter.WriteLine(
            F(Time.time) + "," +
            insertionPhase.ToString() + "," +
            mode.ToString() + "," +
            F(tipEntryErrorMm) + "," +
            F(entryErrorMm) + "," +
            F(entryTargetAxisErrorDeg) + "," +
            F(targetTipErrorMm) + "," +
            F(finalTargetTipErrorMm) + "," +
            F(insertionProgress) + "," +
            F(insertionIntermediateTargetErrorMm) + "," +
            F(entryConeAngleDeg) + "," +
            F(entryConeViolationDeg) + "," +
            F(skullViolationMm) + "," +
            F(armSkullViolationMm) + "," +
            F(entryLambda) + "," +
            F(targetLambda) + "," +
            activeEntryRCMSegmentIndex.ToString(CultureInfo.InvariantCulture) + "," +
            activeTargetRCMSegmentIndex.ToString(CultureInfo.InvariantCulture) + "," +
            (jointLimitsOk ? "1" : "0") + "," +
            F(toolPos.x) + "," +
            F(toolPos.y) + "," +
            F(toolPos.z) + "," +
            F(tipPos.x) + "," +
            F(tipPos.y) + "," +
            F(tipPos.z) + "," +
            F(entryPos.x) + "," +
            F(entryPos.y) + "," +
            F(entryPos.z) + "," +
            F(targetPos.x) + "," +
            F(targetPos.y) + "," +
            F(targetPos.z) + "," +
            JointAngleCsv(0) + "," +
            JointAngleCsv(1) + "," +
            JointAngleCsv(2) + "," +
            JointAngleCsv(3) + "," +
            JointAngleCsv(4) + "," +
            JointAngleCsv(5)
        );
    }

    private string JointAngleCsv(int index)
    {
        if (currentJointAnglesDeg == null || index < 0 || index >= currentJointAnglesDeg.Length)
            return "0.00000";

        return F(currentJointAnglesDeg[index]);
    }

    private string F(float value)
    {
        return value.ToString("F5", CultureInfo.InvariantCulture);
    }

    private void CloseLog()
    {
        if (logWriter != null)
        {
            logWriter.Flush();
            logWriter.Close();
            logWriter = null;
        }
    }

    private void OnApplicationQuit()
    {
        CloseLog();
    }

    private void OnDestroy()
    {
        CloseLog();

        if (overlayOwner == this)
            overlayOwner = null;
    }

    private void InitGUIStyles()
    {
        if (labelStyle != null)
            return;

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 13;
        labelStyle.normal.textColor = Color.white;
        labelStyle.alignment = TextAnchor.MiddleLeft;

        titleStyle = new GUIStyle(labelStyle);
        titleStyle.fontSize = 17;
        titleStyle.fontStyle = FontStyle.Bold;
    }

    private void OnGUI()
    {
        if (!showOverlay)
            return;

        if (overlayOwner == null || overlayOwner == this)
            ClaimOverlayOwnership();
        else
            return;

        InitGUIStyles();

        int oldDepth = GUI.depth;
        GUI.depth = -100;

        float x = 20f;
        float y = 20f;
        float w = 650f;
        float h = 356f;
        float lineH = 24f;

        Color oldColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.94f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = oldColor;

        float tx = x + 16f;
        float ty = y + 14f;

        GUI.Label(new Rect(tx, ty, 610f, 28f), "Multi-RCM ROSA demo", titleStyle);
        ty += 34f;

        GUI.Label(new Rect(tx, ty, 610f, lineH), "[1] Entry-RCM + tip target    [2] Target-RCM + entry cone    [3] Insertion sequence", labelStyle);
        ty += lineH;

        GUI.Label(new Rect(tx, ty, 610f, lineH), "[I] Force insertion    [C] Toggle cone animation    [R] Reset    [Space] Pause    [H] Overlay", labelStyle);
        ty += lineH + 8f;

        GUI.Label(new Rect(tx, ty, 610f, lineH), "Current demo: 1=classic trocar RCM, 2=target RCM + cone, 3=full insertion sequence", labelStyle);
        ty += lineH;

        GUI.Label(new Rect(tx, ty, 610f, lineH), "Mode: " + mode + "    Phase: " + insertionPhase + "    Solving: " + (solveIK ? "on" : "off"), labelStyle);
        ty += lineH;

        GUI.Label(new Rect(tx, ty, 610f, lineH), "Entry RCM error: " + entryRCMFormulaErrorMm.ToString("F2") + " mm    lambda: " + entryLambda.ToString("F3"), labelStyle);
        ty += lineH;

        GUI.Label(new Rect(tx, ty, 610f, lineH), "Target RCM/tip error: " + targetRCMFormulaErrorMm.ToString("F2") + " mm    final target tip error: " + finalTargetTipErrorMm.ToString("F2") + " mm", labelStyle);
        ty += lineH;

        GUI.Label(new Rect(tx, ty, 610f, lineH), "Insertion progress: " + insertionProgress.ToString("F2") + "    intermediate target error: " + insertionIntermediateTargetErrorMm.ToString("F2") + " mm", labelStyle);
        ty += lineH;

        GUI.Label(new Rect(tx, ty, 610f, lineH), "Entry -> Target axis error: " + entryTargetAxisErrorDeg.ToString("F2") + " deg", labelStyle);
        ty += lineH;

        GUI.Label(new Rect(tx, ty, 610f, lineH), "Target-RCM cone angle: " + entryConeAngleDeg.ToString("F2") + " deg / " + entryConeHalfAngleDeg.ToString("F1") + " deg    violation: " + entryConeViolationDeg.ToString("F2") + " deg", labelStyle);
        ty += lineH;

        GUI.Label(new Rect(tx, ty, 610f, lineH), "Skull violation: needle " + skullViolationMm.ToString("F2") + " mm, arm " + armSkullViolationMm.ToString("F2") + " mm", labelStyle);
        ty += lineH;

        GUI.Label(new Rect(tx, ty, 610f, lineH), "Joint limits: " + (jointLimitsOk ? "OK" : "LIMIT HIT") + "    Cone animation: " + (animateTargetConeDemo ? "on" : "off"), labelStyle);

        GUI.depth = oldDepth;
    }

    private void OnDrawGizmos()
    {
        if (toolFrame == null)
            return;

        Vector3 basePos = GetToolBasePosition();
        Vector3 tipPos = GetToolTipPosition();
        Vector3 dir = GetToolDirection();

        Gizmos.color = Color.black;
        Gizmos.DrawLine(basePos, tipPos);
        Gizmos.DrawSphere(tipPos, 0.012f);

        Gizmos.color = new Color(0.25f, 0.25f, 0.25f, 0.6f);
        Gizmos.DrawLine(basePos - dir * 0.25f, tipPos + dir * 0.25f);

        if (entryPoint != null)
        {
            Vector3 closestSegment = ClosestPointOnNeedleSegment(entryPoint.position);
            Vector3 closestAxis = ClosestPointOnInfiniteToolAxis(entryPoint.position);

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(entryPoint.position, 0.014f);
            Gizmos.DrawLine(entryPoint.position, closestSegment);

            Gizmos.color = new Color(1f, 0.6f, 0.6f, 0.5f);
            Gizmos.DrawLine(entryPoint.position, closestAxis);

            if (useLinkBasedRCMFormula)
            {
                Vector3 a;
                Vector3 b;

                if (TryGetKinematicSegment(entryRCMSegmentIndex, out a, out b))
                {
                    Vector3 pRcm = a + Mathf.Clamp01(entryLambda) * (b - a);
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawSphere(pRcm, 0.016f);
                    Gizmos.DrawLine(entryPoint.position, pRcm);
                }
            }
        }

        if (targetPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(targetPoint.position, 0.014f);
            Gizmos.DrawLine(targetPoint.position, tipPos);
        }

        if (entryPoint != null && targetPoint != null)
        {
            Vector3 insertionTarget = GetCurrentInsertionTargetPoint();
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(insertionTarget, 0.010f);
            Gizmos.DrawLine(entryPoint.position, targetPoint.position);

            if (mode == RCMMode.Target && useEntryConeInTargetMode)
                DrawEntryConeGizmo();
        }

        if (avoidArmLinksFromSkull && skullCollider != null && joints != null)
        {
            Gizmos.color = new Color(1f, 0.35f, 0f, 0.9f);
            int samples = Mathf.Max(2, armAvoidanceSamplesPerSegment);

            for (int i = 0; i < joints.Length - 1; i++)
            {
                if (joints[i] == null || joints[i + 1] == null)
                    continue;

                DrawArmSkullAvoidanceGizmos(joints[i].position, joints[i + 1].position, samples);
            }

            if (joints.Length > 0 && joints[joints.Length - 1] != null && toolFrame != null)
                DrawArmSkullAvoidanceGizmos(joints[joints.Length - 1].position, toolFrame.position, samples);
        }
    }

    private void DrawEntryConeGizmo()
    {
        if (entryPoint == null || targetPoint == null)
            return;

        Vector3 axis = GetNominalTargetToEntryDirection();
        Vector3 u;
        Vector3 v;
        GetConeBasis(axis, out u, out v);

        float length = Mathf.Max(0.05f, Vector3.Distance(entryPoint.position, targetPoint.position));
        float radius = Mathf.Tan(entryConeHalfAngleDeg * Mathf.Deg2Rad) * length;
        Vector3 center = targetPoint.position + axis * length;

        Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
        int segments = 24;
        Vector3 prev = center + u * radius;

        for (int i = 1; i <= segments; i++)
        {
            float a = 2f * Mathf.PI * (float)i / (float)segments;
            Vector3 p = center + (Mathf.Cos(a) * u + Mathf.Sin(a) * v) * radius;
            Gizmos.DrawLine(prev, p);
            prev = p;
        }

        Gizmos.DrawLine(targetPoint.position, center + u * radius);
        Gizmos.DrawLine(targetPoint.position, center - u * radius);
        Gizmos.DrawLine(targetPoint.position, center + v * radius);
        Gizmos.DrawLine(targetPoint.position, center - v * radius);
    }

    private void DrawArmSkullAvoidanceGizmos(Vector3 a, Vector3 b, int samples)
    {
        for (int s = 0; s < samples; s++)
        {
            float t = samples == 1 ? 0f : (float)s / (float)(samples - 1);
            Vector3 p = Vector3.Lerp(a, b, t);
            Vector3 err = ComputePushOutOfSkull(p);

            if (err.sqrMagnitude > 1e-10f)
            {
                Vector3 safe = p + err;
                Gizmos.DrawSphere(p, 0.018f);
                Gizmos.DrawLine(p, safe);
            }
        }
    }
}
