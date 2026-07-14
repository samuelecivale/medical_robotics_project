using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Complete procedural ROSA-like 6-DOF manipulator with double-RCM kinematic control.
///
/// The implementation follows the extended-task idea:
///     [ task velocity ]   [ J_task   0 ] [ qdot      ]
///     [ RCM velocity  ] = [ J_RCM      ] [ lambdadot ]
/// solved through damped least squares with a null-space term.
///
/// lambda is an explicit penetration coordinate along the tool shaft:
///     p_RCM(q, lambda) = p_tool_base(q) + lambda * L_tool * z_tool(q), 0 <= lambda <= 1.
/// This makes the DH table replaceable while preserving the controller structure.
/// </summary>
public class ROSADoubleRCMController : MonoBehaviour
{
    public enum RCMMode
    {
        EntryRCM_TipTarget = 0,
        TargetRCM_EntryCone = 1,
        InsertionSequence = 2,
        EntryRCM_TipConeAroundTarget = 3,
        Hold = 4
    }

    public enum InsertionPhase
    {
        ApproachEntry,     // tip above the trocar, needle axis already aligned with entry-target
        PierceEntry,       // tip moves from the outside point to the trocar point
        InsertToTarget,    // RCM is locked at the trocar and the tip advances to the target
        Done
    }

    [Serializable]
    public class DHLink
    {
        public float a;
        public float alphaDeg;
        public float d;
        public float thetaOffsetDeg;
        public float qMinDeg = -170f;
        public float qMaxDeg = 170f;

        public DHLink(float a, float alphaDeg, float d, float thetaOffsetDeg, float qMinDeg, float qMaxDeg)
        {
            this.a = a;
            this.alphaDeg = alphaDeg;
            this.d = d;
            this.thetaOffsetDeg = thetaOffsetDeg;
            this.qMinDeg = qMinDeg;
            this.qMaxDeg = qMaxDeg;
        }
    }

    [Header("Scene references")]
    public Transform entryPoint;
    public Transform targetPoint;
    public Transform skull;
    public float skullRadius = 0.32f;

    [Header("Robot model")]
    public List<DHLink> dh = new List<DHLink>();
    public float toolLength = 0.283f;
    [Tooltip("Physical needle/electrode radius used for corridor and safety checks. " +
             "Real stereotactic biopsy needles are ~1.6-2.5 mm diameter, DBS/SEEG electrodes are thinner " +
             "(0.8-1.27 mm). 0.0012 m = 2.4 mm diameter, a realistic biopsy-needle scale.")]
    public float toolRadius = 0.0012f;
    [Tooltip("Radius used only for the shaft cylinder mesh, so the needle stays visible on screen " +
             "even though the physical toolRadius is sub-millimetric. Does NOT affect collision, " +
             "corridor, or safety-margin calculations, only the drawn geometry.")]
    public float toolVisualRadius = 0.004f;
    public float linkRadius = 0.035f;
    public bool buildRobotVisualsOnStart = true;

    [Header("State")]
    public RCMMode mode = RCMMode.InsertionSequence;
    public InsertionPhase insertionPhase = InsertionPhase.ApproachEntry;
    [Tooltip("Joint values in degrees, stored in the inspector for readability.")]
    public float[] qDeg = new float[] { 0f, 5.24f, -81.16f, 0f, 59.36f, 0f };
    [Range(0.02f, 1.0f)] public float lambda = 0.92f;
    public bool autoRun = true;

    [Header("Control gains")]
    public float tipGain = 3.5f;
    public float rcmGain = 17.0f;
    public float coneGain = 6.0f;
    public float lambdaNullGain = 0.9f;
    public float jointLimitNullGain = 0.08f;
    public float skullAvoidanceNullGain = 1.3f;
    public float nullspaceScale = 0.55f;
    public float damping = 0.055f;
    public float maxJointSpeedDeg = 80f;
    public float maxLambdaSpeed = 0.55f;

    [Header("Insertion sequence")]
    [Tooltip("Distance of the pre-entry tip target above/outside the trocar, along the opposite of the entry-target direction.")]
    public float outsideEntryDistance = 0.10f;
    [Tooltip("Deprecated fallback. The new sequence enters through the trocar first, then advances toward the target.")]
    public float initialInsertionDepth = 0.00f;
    public float insertionProgressSpeed = 0.10f;
    public float insertionStartErrorThreshold = 0.030f;
    [Tooltip("Tool parameter used as an extra guide point to keep the whole needle aligned with the entry-target line during insertion.")]
    [Range(0.15f, 0.95f)] public float insertionAxisGuideS = 0.72f;
    public float insertionAxisGain = 2.0f;
    [Range(0f, 1f)] public float insertionProgress = 0f;

    [Header("Target-RCM cone")]
    public float coneHalfAngleDeg = 4.5f;
    [Tooltip("Angular speed of the cone used in Task 2 (entry-side cone around the trocar). " +
             "Lowered from 12 to 8 deg/s: at 12 deg/s the steady-state tip-target error measured " +
             "~3.5 mm, just over the 3 mm clinical threshold, due to the same moving-target lag " +
             "coupling seen in Task 4 (see coneAngularSpeedTask4Deg).")]
    public float coneAngularSpeedDeg = 8.0f;
    [Tooltip("Angular speed of the cone used in Task 4 (tip cone around the deep target). " +
             "Kept separate and slower than Task 2's, because in Task 4 the RCM must also stay " +
             "locked at the entry point: a fast-moving secondary target drags the RCM off through " +
             "the weighted DLS coupling. Lower this if T4 entry-RCM stability keeps failing.")]
    public float coneAngularSpeedTask4Deg = 7.0f;
    [Tooltip("Radius used in task 4 when the tip traces a circle around the deep target while the needle keeps the entry RCM.")]
    public float tipTargetConeRadius = 0.0138f;
    public bool animateCone = true;
    public float entrySidePointSafetyOffset = 0.0f;

    [Header("Task 2 priority tuning (TargetRCM_EntryCone)")]
    [Tooltip("Tip tracking multiplier used only in Task 2.")]
    public float task2TipGainMultiplier = 1.65f;
    [Tooltip("Entry-cone tracking multiplier used only in Task 2.")]
    public float task2ConeGainMultiplier = 0.70f;
    [Tooltip("DLS damping multiplier used only in Task 2.")]
    public float task2DampingMultiplier = 1.0f;
    [Tooltip("Nullspace multiplier used only in Task 2.")]
    public float task2NullspaceScaleMultiplier = 0.6f;
    [Tooltip("Distance from either q4 limit at which Task 2 stops outward q4 motion.")]
    public float task2Joint4SoftLimitMarginDeg = 25f;

    [Header("Task 4 priority tuning (EntryRCM_TipConeAroundTarget)")]
    [Tooltip("Multiplies tipGain for the cone-tracking task while in Task 4, so the RCM task can " +
             "win the conflict instead of being dragged by the moving cone target.")]
    public float task4TipGainMultiplier = 0.85f;
    [Tooltip("Multiplies rcmGain for the entry-RCM task while in Task 4. Kept as a modest increase " +
             "(not pushed further) after the Task 2 gain-only attempt caused instability; the " +
             "nullspace-scale reduction below is the safer lever for closing the remaining gap.")]
    public float task4RcmGainMultiplier = 1.6f;
    [Tooltip("Multiplies the DLS damping while in Task 4. Lower values track more exactly but are " +
             "closer to singular behavior; raise back toward 1 if you see jitter near the workspace edge.")]
    public float task4DampingMultiplier = 0.7f;
    [Tooltip("Multiplies nullspaceScale while in Task 4. The nullspace injection is itself solved with " +
             "an approximate DLS projector, so it can leak a small amount of error into the primary " +
             "RCM task; reducing it during Task 4 trades some joint-limit/skull-avoidance authority for " +
             "tighter RCM stability. Lowered further (0.5 -> 0.35) after measuring 2.7 mm settled " +
             "entry-RCM error, just over the 2.0 mm threshold.")]
    public float task4NullspaceScaleMultiplier = 0.35f;

    [Header("Safety / avoidance")]
    public float skullSafetyMargin = 0.035f;
    [Tooltip("Must stay larger than tipTargetConeRadius, with margin. Task 4's tip deliberately " +
             "traces a circle of radius tipTargetConeRadius around the deep target, which lies " +
             "inside the skull by design. If allowedNeedleCorridorRadius is too close to " +
             "tipTargetConeRadius (previously both were 0.035 m, i.e. zero margin), any small " +
             "tracking deviation pushes the tip outside the 'allowed corridor' tube and it gets " +
             "misclassified as an unauthorized skull penetration, even though the motion is the " +
             "intended clinical behavior. Raised to 0.06 m (25 mm margin over the 35 mm cone radius) " +
             "and enforced at runtime in Awake().")]
    public float allowedNeedleCorridorRadius = 0.06f;
    public bool useSkullAvoidance = true;

    [Header("Task transition")]
    public float coneWarmupTime = 1.5f;

    private RCMMode previousModeForTiming;
    private float modeChangedAt;

    [Header("Debug / overlay / logging")]
    public bool showOverlay = true;
    public bool writeCsvLog = true;
    [Tooltip("CSV sampling period. 0.05 s gives about 20 Hz, enough for clean presentation plots without huge files.")]
    public float logPeriod = 0.05f;
    public bool logPresentationMetrics = true;

    [Header("Pass/Fail thresholds (CSV log + report)")]
    [Tooltip("Real clinical accuracy for ROSA-like stereotactic robots is ~1-2 mm median, up to " +
             "~4 mm in the worst-case referencing methods. Previous default of 10 mm was a coarse " +
             "safety margin, not a realistic accuracy target.")]
    public float entryRcmOkThresholdMm = 2.0f;
    [Tooltip("Real clinical target accuracy is typically 1-3 mm. Previous default of 25 mm was far " +
             "too loose to be a meaningful clinical accuracy check.")]
    public float tipTargetOkThresholdMm = 3.0f;

    private readonly List<GameObject> jointSpheres = new List<GameObject>();
    private readonly List<GameObject> linkCylinders = new List<GameObject>();
    private GameObject shaftCylinder;
    private GameObject tipSphere;
    private GameObject rcmSphere;
    private GameObject coneDesiredSphere;
    private GameObject nominalLineCylinder;
    private Material linkMat;
    private Material jointMat;
    private Material wristMat;
    private Material shaftMat;
    private Material tipMat;
    private Material rcmMat;
    private Material coneMat;
    private Material lineMat;

    private string csvPath;
    private StreamWriter csvWriter;
    private float logTimer;
    private double[] lastXdot = new double[7];
    private bool lastTask2Joint4LimitActive;
    private Vector3 lastTip;
    private Vector3 lastRCM;
    private Vector3 lastEntrySide;
    private Vector3 lastConeDesired;
    private Vector3 lastTipConeDesired;
    private float lastSkullViolation;

    private const int N = 6;
    private const int NX = 7;

    private void Reset()
    {
        SetDefaultDH();
    }

    private void Awake()
    {
        if (dh == null || dh.Count != N) SetDefaultDH();
        EnsureStateArrays();
        EnsureCorridorMargin();
    }

    private void EnsureCorridorMargin()
    {
        // Zero (or negative) margin between the corridor tube and the Task 4 tip-cone radius
        // makes the intended cone-tracing motion near the target register as a skull violation
        // (see allowedNeedleCorridorRadius tooltip). Auto-correct and warn instead of silently
        // producing a controller that fights its own safety check.
        float minRequired = tipTargetConeRadius + 0.015f;
        if (allowedNeedleCorridorRadius < minRequired)
        {
            Debug.LogWarning(string.Format(
                "ROSADoubleRCMController: allowedNeedleCorridorRadius ({0:F3} m) was too close to " +
                "tipTargetConeRadius ({1:F3} m). Raised to {2:F3} m to keep Task 4's cone-tracing " +
                "motion from being misclassified as a skull violation.",
                allowedNeedleCorridorRadius, tipTargetConeRadius, minRequired));
            allowedNeedleCorridorRadius = minRequired;
        }
    }

    private void Start()
    {
        if (buildRobotVisualsOnStart) BuildVisualRobot();
        ResetControllerState(false);
        if (writeCsvLog) StartLogging();
        previousModeForTiming = mode;
        modeChangedAt = Time.time;
    }

    private void Update()
    {
        HandleKeyboard();
        if (mode != previousModeForTiming)
        {
            previousModeForTiming = mode;
            modeChangedAt = Time.time;
        }
        if (autoRun && mode != RCMMode.Hold)
            StepController(Time.deltaTime);
        UpdateVisualRobot();
        if (writeCsvLog) AppendLogIfNeeded(Time.deltaTime);
    }

    private float ConeLocalTime()
    {
        return Mathf.Max(0f, Time.time - modeChangedAt - coneWarmupTime);
    }

    private void OnDisable()
    {
        StopLogging();
    }

    private void OnApplicationQuit()
    {
        StopLogging();
    }

    public void SetDefaultDH()
    {
        dh = new List<DHLink>();
        // Standard DH, meters and degrees. Plausible ROSA-like 6R arm:
        // anthropomorphic shoulder/elbow + compact spherical wrist. Replace these values with real ROSA DH if available.
        dh.Add(new DHLink(0.045f, -90f, 0.42f, 0f, -170f, 170f));
        dh.Add(new DHLink(0.42f, 0f, 0.00f, 0f, -120f, 120f));
        dh.Add(new DHLink(0.055f, -90f, 0.00f, 0f, -150f, 150f));
        dh.Add(new DHLink(0.000f, 90f, 0.36f, 0f, -170f, 170f));
        dh.Add(new DHLink(0.000f, -90f, 0.00f, 0f, -120f, 120f));
        dh.Add(new DHLink(0.000f, 0f, 0.12f, 0f, -360f, 360f));
    }

    public void ResetControllerState(bool keepMode)
    {
        EnsureStateArrays();
        // Upright initial posture, chosen for the default elevated neurosurgical scene.
        // The robot starts in a high, top-down posture instead of approaching the target from below.
        qDeg[0] = 0f;
        qDeg[1] = 5.24f;
        qDeg[2] = -81.16f;
        qDeg[3] = 0f;
        qDeg[4] = 59.36f;
        qDeg[5] = 0f;
        lambda = NominalEntryLambda();
        insertionPhase = InsertionPhase.ApproachEntry;
        insertionProgress = 0f;
        if (!keepMode) mode = RCMMode.InsertionSequence;
    }

    private void EnsureStateArrays()
    {
        if (qDeg == null || qDeg.Length != N)
            qDeg = new float[] { 0f, 5.24f, -81.16f, 0f, 59.36f, 0f };
    }

    private void HandleKeyboard()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) { mode = RCMMode.EntryRCM_TipTarget; insertionPhase = InsertionPhase.Done; }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { mode = RCMMode.TargetRCM_EntryCone; insertionPhase = InsertionPhase.Done; lambda = NominalTargetLambda(); }
        if (Input.GetKeyDown(KeyCode.Alpha3)) { mode = RCMMode.InsertionSequence; insertionPhase = InsertionPhase.ApproachEntry; insertionProgress = 0f; lambda = NominalEntryLambda(); }
        if (Input.GetKeyDown(KeyCode.Alpha4)) { mode = RCMMode.EntryRCM_TipConeAroundTarget; insertionPhase = InsertionPhase.Done; lambda = DesiredEntryLambdaForTip(DesiredTipConeAroundTarget()); }
        if (Input.GetKeyDown(KeyCode.Alpha0)) mode = RCMMode.Hold;
        if (Input.GetKeyDown(KeyCode.Space)) autoRun = !autoRun;
        if (Input.GetKeyDown(KeyCode.C)) animateCone = !animateCone;
        if (Input.GetKeyDown(KeyCode.R)) ResetControllerState(true);
        if (Input.GetKeyDown(KeyCode.L))
        {
            writeCsvLog = !writeCsvLog;
            if (writeCsvLog) StartLogging();
            else StopLogging();
        }
    }

    [Header("Integration stability")]
    [Tooltip("Maximum integration sub-step. Smaller values improve closed-loop stability.")]
    public float maxIntegrationSubstepSeconds = 0.008f;

    private void StepController(float dt)
    {
        dt = Mathf.Clamp(dt, 0.001f, 0.033f);
        int substeps = Mathf.Clamp(Mathf.CeilToInt(dt / Mathf.Max(1e-4f, maxIntegrationSubstepSeconds)), 1, 8);
        float subDt = dt / substeps;
        for (int s = 0; s < substeps; ++s)
        {
            RunIntegrationSubstep(subDt);
        }

        if (mode == RCMMode.InsertionSequence)
            UpdateInsertionPhase(dt);

        lastTip = ToolTip(GetQRad());
        lastRCM = RCMPoint(GetQRad(), lambda);
        lastEntrySide = EntrySidePointOnTool(GetQRad(), lambda);
        lastConeDesired = DesiredConeEntryPoint();
        lastTipConeDesired = DesiredTipConeAroundTarget();
        lastSkullViolation = ComputeMaxSkullViolation(GetQRad(), lambda) * 1000f;
    }

    private void RunIntegrationSubstep(float dt)
    {
        float[] q = GetQRad();
        List<PointTask> tasks = BuildTasks(q, lambda);

        int rows = tasks.Count * 3;
        double[,] J = new double[rows, NX];
        double[] y = new double[rows];

        int r = 0;
        foreach (PointTask task in tasks)
        {
            Vector3 current = task.func(q, lambda);
            Vector3 error = task.desired - current;
            if (error.magnitude > task.maxErrorForVelocity)
                error = error.normalized * task.maxErrorForVelocity;

            double[,] Jp = NumericJacobian(task.func, q, lambda);
            for (int i = 0; i < 3; ++i)
            {
                for (int c = 0; c < NX; ++c) J[r + i, c] = Jp[i, c];
            }
            y[r + 0] = task.gain * error.x;
            y[r + 1] = task.gain * error.y;
            y[r + 2] = task.gain * error.z;
            r += 3;
        }

        // Task-specific numerical scaling. Task 2's hierarchy and q4 active set are applied below.
        float effectiveDamping = damping;
        float effectiveNullspaceScale = nullspaceScale;
        if (mode == RCMMode.TargetRCM_EntryCone)
        {
            effectiveDamping = damping * task2DampingMultiplier;
            effectiveNullspaceScale = nullspaceScale * task2NullspaceScaleMultiplier;
        }
        else if (mode == RCMMode.EntryRCM_TipConeAroundTarget)
        {
            effectiveDamping = damping * task4DampingMultiplier;
            effectiveNullspaceScale = nullspaceScale * task4NullspaceScaleMultiplier;
        }

        double[] xdotBase;
        float boostedDamping;
        if (mode == RCMMode.TargetRCM_EntryCone && rows == 6)
            xdotBase = SolveTask2WithStrictPriorityAndGuard(J, y, effectiveDamping, out boostedDamping);
        else
        {
            lastTask2Joint4LimitActive = false;
            xdotBase = SolveWithSingularityGuard(J, y, effectiveDamping, out boostedDamping);
        }
        double[] w = BuildNullspaceVelocity(q, lambda);
        double[] jTimesW = MatVec(J, w);
        // Reuse the same (possibly boosted) damping for the nullspace projector, so the two
        // solves stay numerically consistent near the same near-singular configuration.
        double[] projectedJW = DampedLeastSquares(J, jTimesW, boostedDamping);
        double[] xdot = new double[NX];
        for (int i = 0; i < NX; ++i)
        {
            double nullspaceContrib = effectiveNullspaceScale * (w[i] - projectedJW[i]);
            if (mode == RCMMode.TargetRCM_EntryCone && lastTask2Joint4LimitActive && i == 3)
                nullspaceContrib = 0.0;
            xdot[i] = xdotBase[i] + nullspaceContrib;
        }

        // Saturation.
        float maxJointSpeed = maxJointSpeedDeg * Mathf.Deg2Rad;
        for (int i = 0; i < N; ++i) xdot[i] = Clamp(xdot[i], -maxJointSpeed, maxJointSpeed);
        xdot[6] = Clamp(xdot[6], -maxLambdaSpeed, maxLambdaSpeed);

        for (int i = 0; i < N; ++i)
        {
            float qi = q[i] + (float)(xdot[i] * dt);
            qi = Mathf.Clamp(qi, dh[i].qMinDeg * Mathf.Deg2Rad, dh[i].qMaxDeg * Mathf.Deg2Rad);
            qDeg[i] = qi * Mathf.Rad2Deg;
        }
        lambda = Mathf.Clamp(lambda + (float)(xdot[6] * dt), 0.02f, 0.995f);
        lastXdot = xdot;
    }

    private List<PointTask> BuildTasks(float[] q, float lam)
    {
        List<PointTask> tasks = new List<PointTask>();
        Vector3 entry = EntryPosition();
        Vector3 target = TargetPosition();

        if (mode == RCMMode.EntryRCM_TipTarget)
        {
            tasks.Add(new PointTask(ToolTip, target, tipGain, 0.18f));
            tasks.Add(new PointTask(RCMPoint, entry, rcmGain, 0.08f));
        }
        else if (mode == RCMMode.TargetRCM_EntryCone)
        {
            // Task 2: target-side pivot after insertion.
            // The physical tip is the most important task and is kept fixed on the target.
            // An entry-side point on the needle traces a small cone around the trocar.
            // The explicit RCMPoint->target task was removed because, with lambda close to 1,
            // it duplicates the tip task and made the controller visibly lag/jitter.
            tasks.Add(new PointTask(ToolTip, target, tipGain * task2TipGainMultiplier, 0.055f));
            tasks.Add(new PointTask(EntrySidePointOnTool, DesiredConeEntryPoint(), coneGain * task2ConeGainMultiplier, 0.070f));
        }
        else if (mode == RCMMode.EntryRCM_TipConeAroundTarget)
        {
            // Task 4: trocar-side pivot. The needle keeps the RCM fixed at the entry/trocar,
            // while the tip traces a small circle around the deep target.
            // The cone target moves continuously, so without extra RCM priority the weighted
            // DLS solve lets some of that tracking velocity leak into the RCM error. We soften
            // the cone-tracking gain slightly and boost the RCM gain to keep the pivot locked.
            tasks.Add(new PointTask(ToolTip, DesiredTipConeAroundTarget(), tipGain * task4TipGainMultiplier, 0.12f));
            tasks.Add(new PointTask(RCMPoint, entry, rcmGain * task4RcmGainMultiplier, 0.06f));
        }
        /*else if (mode == RCMMode.InsertionSequence)
        {
            // Task 3 is now a true entry-through-trocar sequence:
            // 1) stay above/outside the entry while aligning the needle axis with entry-target,
            // 2) move the tip exactly to the entry point,
            // 3) lock the RCM at the entry and advance the tip to the target.
            // The extra guide point keeps the whole shaft collinear with the surgical corridor,
            // so the needle cannot visually enter from below or through the base.
            Vector3 desiredTip = CurrentDesiredInsertionTip();
            tasks.Add(new PointTask(ToolTip, desiredTip, tipGain, 0.13f));
            tasks.Add(new PointTask(InsertionAxisGuidePoint, DesiredInsertionAxisGuidePoint(desiredTip), insertionAxisGain, 0.13f));

            if (insertionPhase == InsertionPhase.InsertToTarget || insertionPhase == InsertionPhase.Done)
                tasks.Add(new PointTask(RCMPoint, entry, rcmGain, 0.060f));
        }
        */
        else if (mode == RCMMode.InsertionSequence)
        {
            Vector3 desiredTip = CurrentDesiredInsertionTip();

            float phaseTipGain = tipGain;
            float phaseAxisGain = insertionAxisGain;
            float phaseRcmGain = rcmGain;

            if (insertionPhase == InsertionPhase.InsertToTarget || insertionPhase == InsertionPhase.Done)
            {
                // Durante l'inserzione vera, il fulcro RCM deve essere più importante
                // della velocità con cui la punta raggiunge il target.
                phaseTipGain = tipGain * 0.85f;
                phaseAxisGain = insertionAxisGain * 0.65f;
                phaseRcmGain = rcmGain * 1.6f;
            }

            tasks.Add(new PointTask(ToolTip, desiredTip, phaseTipGain, 0.10f));
            tasks.Add(new PointTask(
                InsertionAxisGuidePoint,
                DesiredInsertionAxisGuidePoint(desiredTip),
                phaseAxisGain,
                0.10f
            ));

            // Poco prima di finire PierceEntry, inizia già a stabilizzare il trocar.
            if (insertionPhase == InsertionPhase.PierceEntry && insertionProgress > 0.80f)
            {
                tasks.Add(new PointTask(RCMPoint, entry, rcmGain * 0.75f, 0.045f));
            }

            if (insertionPhase == InsertionPhase.InsertToTarget || insertionPhase == InsertionPhase.Done)
            {
                tasks.Add(new PointTask(RCMPoint, entry, phaseRcmGain, 0.035f));
            }
        }

        return tasks;
    }

    private void UpdateInsertionPhase(float dt)
    {
        Vector3 tip = ToolTip(GetQRad());
        Vector3 outsideTip = PreEntryTip();
        Vector3 entry = EntryPosition();
        Vector3 target = TargetPosition();

        if (insertionPhase == InsertionPhase.ApproachEntry)
        {
            // First reach a pose above/outside the trocar, with the needle already aligned.
            insertionProgress = 0f;
            if ((tip - outsideTip).magnitude < insertionStartErrorThreshold)
            {
                insertionPhase = InsertionPhase.PierceEntry;
                insertionProgress = 0f;
            }
        }
        else if (insertionPhase == InsertionPhase.PierceEntry)
        {
            // The tip now travels from the outside point to the trocar point.
            // No RCM is imposed yet, because the shaft is still outside the body.
            insertionProgress = Mathf.Clamp01(insertionProgress + insertionProgressSpeed * 1.60f * dt);
            if (insertionProgress >= 0.999f && (tip - entry).magnitude < insertionStartErrorThreshold)
            {
                insertionPhase = InsertionPhase.InsertToTarget;
                insertionProgress = 0f;
                lambda = 0.995f;
            }
        }
        else if (insertionPhase == InsertionPhase.InsertToTarget)
        {
            // From here on, RCMPoint is constrained to the trocar.
            insertionProgress = Mathf.Clamp01(insertionProgress + insertionProgressSpeed * dt);
            if (insertionProgress >= 0.999f && (tip - target).magnitude < 0.025f)
                insertionPhase = InsertionPhase.Done;
        }
    }

    private double[] BuildNullspaceVelocity(float[] q, float lam)
    {
        double[] w = new double[NX];

        // Keep joints away from limits.
        for (int i = 0; i < N; ++i)
        {
            float min = dh[i].qMinDeg * Mathf.Deg2Rad;
            float max = dh[i].qMaxDeg * Mathf.Deg2Rad;
            float mid = 0.5f * (min + max);
            float half = Mathf.Max(0.01f, 0.5f * (max - min));
            w[i] += -jointLimitNullGain * ((q[i] - mid) / (half * half));
        }

        float desiredLambda = DesiredLambdaForCurrentMode();
        w[6] += lambdaNullGain * (desiredLambda - lam);

        if (useSkullAvoidance && skull != null)
        {
            double[] grad = NumericGradientSkullCost(q, lam);
            for (int i = 0; i < N; ++i) w[i] += -skullAvoidanceNullGain * grad[i];
            // lambda can also help keep the tool through the permitted corridor.
            w[6] += -0.25f * skullAvoidanceNullGain * grad[6];
        }

        return w;
    }

    private double[] NumericGradientSkullCost(float[] q, float lam)
    {
        double[] grad = new double[NX];
        float epsQ = 1e-3f;
        float epsL = 1e-3f;
        for (int i = 0; i < NX; ++i)
        {
            float[] qp = Copy(q);
            float[] qm = Copy(q);
            float lp = lam;
            float lm = lam;
            if (i < N)
            {
                qp[i] += epsQ;
                qm[i] -= epsQ;
                grad[i] = (SkullCost(qp, lam) - SkullCost(qm, lam)) / (2.0 * epsQ);
            }
            else
            {
                lp = Mathf.Clamp(lam + epsL, 0.02f, 0.995f);
                lm = Mathf.Clamp(lam - epsL, 0.02f, 0.995f);
                grad[i] = (SkullCost(q, lp) - SkullCost(q, lm)) / Math.Max(1e-8, (lp - lm));
            }
        }
        return grad;
    }

    private double SkullCost(float[] q, float lam)
    {
        if (skull == null) return 0.0;
        Matrix4x4[] frames = ForwardFrames(q);
        List<Vector3> samples = new List<Vector3>();

        for (int i = 0; i < frames.Length; ++i)
            samples.Add(Pos(frames[i]));

        for (int i = 0; i < frames.Length - 1; ++i)
        {
            Vector3 a = Pos(frames[i]);
            Vector3 b = Pos(frames[i + 1]);
            samples.Add(Vector3.Lerp(a, b, 0.33f));
            samples.Add(Vector3.Lerp(a, b, 0.66f));
        }

        // Tool samples: allowed only close to the planned surgical corridor.
        Vector3 baseP = ToolBase(q);
        Vector3 tip = ToolTip(q);
        for (int k = 0; k <= 8; ++k)
            samples.Add(Vector3.Lerp(baseP, tip, k / 8.0f));

        Vector3 center = skull.position;
        double cost = 0.0;
        foreach (Vector3 p in samples)
        {
            if (IsAllowedNeedleCorridor(p)) continue;
            float signedDistance = (p - center).magnitude - skullRadius;
            float violation = skullSafetyMargin - signedDistance;
            if (violation > 0f) cost += violation * violation;
        }
        return cost;
    }

    private float ComputeMaxSkullViolation(float[] q, float lam)
    {
        if (skull == null) return 0f;
        Matrix4x4[] frames = ForwardFrames(q);
        float maxV = 0f;
        Vector3 center = skull.position;
        List<Vector3> samples = new List<Vector3>();
        for (int i = 0; i < frames.Length; ++i) samples.Add(Pos(frames[i]));
        for (int i = 0; i < frames.Length - 1; ++i)
        {
            samples.Add(Vector3.Lerp(Pos(frames[i]), Pos(frames[i + 1]), 0.33f));
            samples.Add(Vector3.Lerp(Pos(frames[i]), Pos(frames[i + 1]), 0.66f));
        }
        Vector3 baseP = ToolBase(q);
        Vector3 tip = ToolTip(q);
        for (int k = 0; k <= 12; ++k) samples.Add(Vector3.Lerp(baseP, tip, k / 12.0f));

        foreach (Vector3 p in samples)
        {
            if (IsAllowedNeedleCorridor(p)) continue;
            float signedDistance = (p - center).magnitude - skullRadius;
            float violation = Mathf.Max(0f, -signedDistance);
            maxV = Mathf.Max(maxV, violation);
        }
        return maxV;
    }

    private bool IsAllowedNeedleCorridor(Vector3 p)
    {
        Vector3 entry = EntryPosition();
        Vector3 target = TargetPosition();
        Vector3 axis = target - entry;
        float len = axis.magnitude;
        if (len < 1e-5f) return false;
        Vector3 u = axis / len;
        float s = Vector3.Dot(p - entry, u);
        if (s < -0.06f || s > len + 0.08f) return false;
        Vector3 closest = entry + s * u;
        return (p - closest).magnitude <= allowedNeedleCorridorRadius;
    }

    private double[,] NumericJacobian(Func<float[], float, Vector3> f, float[] q, float lam)
    {
        double[,] J = new double[3, NX];
        float epsQ = 1e-3f;
        float epsL = 1e-3f;
        for (int i = 0; i < NX; ++i)
        {
            float[] qp = Copy(q);
            float[] qm = Copy(q);
            float lp = lam;
            float lm = lam;
            float denom;
            if (i < N)
            {
                qp[i] += epsQ;
                qm[i] -= epsQ;
                denom = 2f * epsQ;
            }
            else
            {
                lp = Mathf.Clamp(lam + epsL, 0.02f, 0.995f);
                lm = Mathf.Clamp(lam - epsL, 0.02f, 0.995f);
                denom = Mathf.Max(1e-8f, lp - lm);
            }
            Vector3 fp = f(qp, lp);
            Vector3 fm = f(qm, lm);
            Vector3 col = (fp - fm) / denom;
            J[0, i] = col.x;
            J[1, i] = col.y;
            J[2, i] = col.z;
        }
        return J;
    }

    private Matrix4x4[] ForwardFrames(float[] q)
    {
        Matrix4x4[] frames = new Matrix4x4[N + 1];
        Matrix4x4 T = transform.localToWorldMatrix;
        frames[0] = T;
        for (int i = 0; i < N; ++i)
        {
            T = T * DHMatrix(dh[i], q[i]);
            frames[i + 1] = T;
        }
        return frames;
    }

    private Matrix4x4 DHMatrix(DHLink link, float qRad)
    {
        float theta = qRad + link.thetaOffsetDeg * Mathf.Deg2Rad;
        float alpha = link.alphaDeg * Mathf.Deg2Rad;
        float ct = Mathf.Cos(theta);
        float st = Mathf.Sin(theta);
        float ca = Mathf.Cos(alpha);
        float sa = Mathf.Sin(alpha);

        Matrix4x4 A = Matrix4x4.identity;
        A.m00 = ct; A.m01 = -st * ca; A.m02 = st * sa; A.m03 = link.a * ct;
        A.m10 = st; A.m11 = ct * ca; A.m12 = -ct * sa; A.m13 = link.a * st;
        A.m20 = 0f; A.m21 = sa; A.m22 = ca; A.m23 = link.d;
        A.m30 = 0f; A.m31 = 0f; A.m32 = 0f; A.m33 = 1f;
        return A;
    }

    private Vector3 ToolBase(float[] q)
    {
        Matrix4x4[] frames = ForwardFrames(q);
        return Pos(frames[N]);
    }

    private Vector3 ToolAxis(float[] q)
    {
        Matrix4x4[] frames = ForwardFrames(q);
        Vector3 z = new Vector3(frames[N].m02, frames[N].m12, frames[N].m22);
        if (z.sqrMagnitude < 1e-8f) return Vector3.forward;
        return z.normalized;
    }

    private Vector3 PointOnTool(float[] q, float s)
    {
        s = Mathf.Clamp01(s);
        return ToolBase(q) + ToolAxis(q) * (toolLength * s);
    }

    private Vector3 ToolTip(float[] q, float lam) { return ToolTip(q); }
    private Vector3 ToolTip(float[] q) { return PointOnTool(q, 1f); }
    private Vector3 RCMPoint(float[] q, float lam) { return PointOnTool(q, lam); }

    private Vector3 EntrySidePointOnTool(float[] q, float lam)
    {
        // For task 2 the tip is fixed at the target and the tool rotates around it.
        // The entry-side point is therefore selected at a fixed distance from the tip,
        // approximately equal to the target-entry depth. This avoids coupling the
        // cone task to lambda and removes a major source of lag.
        return PointOnTool(q, EntrySideToolParameterFromTip());
    }

    private float EntrySideToolParameterFromTip()
    {
        float depth = Vector3.Distance(EntryPosition(), TargetPosition()) + entrySidePointSafetyOffset;
        return Mathf.Clamp01(1.0f - depth / Mathf.Max(0.05f, toolLength));
    }

    private Vector3 InsertionAxisGuidePoint(float[] q, float lam)
    {
        return PointOnTool(q, insertionAxisGuideS);
    }

    private Vector3 DesiredInsertionAxisGuidePoint(Vector3 desiredTip)
    {
        Vector3 dir = SafeDirection(EntryPosition(), TargetPosition());
        float distanceBehindTip = toolLength * (1.0f - insertionAxisGuideS);
        return desiredTip - dir * distanceBehindTip;
    }

    private Vector3 DesiredConeEntryPoint()
    {
        Vector3 entry = EntryPosition();
        Vector3 target = TargetPosition();
        Vector3 axis = SafeDirection(target, entry); // cone axis from target to nominal entry
        float depth = Vector3.Distance(target, entry);
        float radius = depth * Mathf.Tan(coneHalfAngleDeg * Mathf.Deg2Rad);
        Vector3 b1 = Vector3.Cross(axis, Vector3.up);
        if (b1.sqrMagnitude < 1e-6f) b1 = Vector3.Cross(axis, Vector3.right);
        b1.Normalize();
        Vector3 b2 = Vector3.Cross(axis, b1).normalized;
        float angle = animateCone ? ConeLocalTime() * coneAngularSpeedDeg * Mathf.Deg2Rad : 0f;
        return entry + radius * (Mathf.Cos(angle) * b1 + Mathf.Sin(angle) * b2);
    }

    private Vector3 DesiredTipConeAroundTarget()
    {
        Vector3 entry = EntryPosition();
        Vector3 target = TargetPosition();
        Vector3 axis = SafeDirection(entry, target); // nominal insertion direction from trocar to target
        float radius = tipTargetConeRadius > 0f
            ? tipTargetConeRadius
            : Vector3.Distance(entry, target) * Mathf.Tan(coneHalfAngleDeg * Mathf.Deg2Rad);
        Vector3 b1 = Vector3.Cross(axis, Vector3.up);
        if (b1.sqrMagnitude < 1e-6f) b1 = Vector3.Cross(axis, Vector3.right);
        b1.Normalize();
        Vector3 b2 = Vector3.Cross(axis, b1).normalized;
        // Task 4's cone target uses its own (slower) angular speed: it competes with the
        // entry-RCM task, unlike Task 2's cone which has no simultaneous RCM lock to disturb.
        float angle = animateCone ? ConeLocalTime() * coneAngularSpeedTask4Deg * Mathf.Deg2Rad : 0f;
        return target + radius * (Mathf.Cos(angle) * b1 + Mathf.Sin(angle) * b2);
    }

    private Vector3 CurrentDesiredInsertionTip()
    {
        Vector3 outsideTip = PreEntryTip();
        Vector3 entry = EntryPosition();
        Vector3 target = TargetPosition();

        if (insertionPhase == InsertionPhase.PierceEntry)
            return Vector3.Lerp(outsideTip, entry, insertionProgress);
        if (insertionPhase == InsertionPhase.InsertToTarget || insertionPhase == InsertionPhase.Done)
            return Vector3.Lerp(entry, target, insertionProgress);
        return outsideTip;
    }

    private Vector3 PreEntryTip()
    {
        Vector3 entry = EntryPosition();
        Vector3 target = TargetPosition();
        Vector3 dir = SafeDirection(entry, target);
        return entry - dir * Mathf.Max(0.02f, outsideEntryDistance);
    }

    private Vector3 InsertionStartTip()
    {
        // Kept for compatibility with older inspector labels/logs. In the new sequence
        // the actual start is above/outside the entry, not already inside the patient.
        return PreEntryTip();
    }

    private float DesiredEntryLambdaForTip(Vector3 desiredTip)
    {
        float depth = Vector3.Distance(EntryPosition(), desiredTip);
        return Mathf.Clamp(1.0f - depth / Mathf.Max(toolLength, 0.05f), 0.05f, 0.995f);
    }

    private float DesiredLambdaForCurrentMode()
    {
        if (mode == RCMMode.TargetRCM_EntryCone)
            return NominalTargetLambda();
        if (mode == RCMMode.EntryRCM_TipConeAroundTarget)
            return DesiredEntryLambdaForTip(DesiredTipConeAroundTarget());
        if (mode == RCMMode.EntryRCM_TipTarget)
            return DesiredEntryLambdaForTip(TargetPosition());
        if (mode == RCMMode.InsertionSequence)
        {
            if (insertionPhase == InsertionPhase.InsertToTarget || insertionPhase == InsertionPhase.Done)
                return DesiredEntryLambdaForTip(CurrentDesiredInsertionTip());
            return 0.995f;
        }
        return lambda;
    }

    private Vector3 EntryPosition()
    {
        return entryPoint != null ? entryPoint.position : new Vector3(0.70f, 1.05f, 0.20f);
    }

    private Vector3 TargetPosition()
    {
        return targetPoint != null ? targetPoint.position : new Vector3(0.80f, 0.67f, 0.20f);
    }

    private Vector3 SafeDirection(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        if (d.sqrMagnitude < 1e-8f) return Vector3.forward;
        return d.normalized;
    }

    private float NominalEntryLambda()
    {
        // Before the tip reaches the entry point the RCM is not physically meaningful on
        // the inserted part of the needle. Keep lambda close to the tip; when insertion
        // starts, DesiredLambdaForCurrentMode moves it to the trocar position.
        return 0.995f;
    }

    private float NominalTargetLambda()
    {
        // In task 2 the physical tip is kept on the target and the target also acts as the RCM.
        // This means lambda should be very close to the tip end of the needle.
        return 0.995f;
    }

    private Vector3 Pos(Matrix4x4 T)
    {
        return new Vector3(T.m03, T.m13, T.m23);
    }

    private float[] GetQRad()
    {
        float[] q = new float[N];
        for (int i = 0; i < N; ++i) q[i] = qDeg[i] * Mathf.Deg2Rad;
        return q;
    }

    private float[] Copy(float[] src)
    {
        float[] dst = new float[src.Length];
        Array.Copy(src, dst, src.Length);
        return dst;
    }

    private struct PointTask
    {
        public Func<float[], float, Vector3> func;
        public Vector3 desired;
        public float gain;
        public float maxErrorForVelocity;
        public PointTask(Func<float[], float, Vector3> func, Vector3 desired, float gain, float maxErrorForVelocity)
        {
            this.func = func;
            this.desired = desired;
            this.gain = gain;
            this.maxErrorForVelocity = maxErrorForVelocity;
        }
    }

    // ---------- Linear algebra: damped least squares ----------

    [Tooltip("If the raw (pre-saturation) joint command from the DLS solve would exceed " +
             "maxJointSpeedDeg by more than this factor, treat it as a near-singular configuration " +
             "and re-solve with boosted damping instead of letting the per-joint clamp distort the " +
             "commanded direction (which is what produced the flat-topped ~80 deg/s plateaus and " +
             "the resulting T2 tracking blow-up). 1.0 = trigger as soon as any joint would saturate.")]
    public float singularityGuardTriggerRatio = 1.0f;
    [Tooltip("How strongly to boost damping when the singularity guard triggers: " +
             "boostedDamping = damping * sqrt(overshootRatio) * this factor.")]
    public float singularityGuardBoostFactor = 1.2f;

    private double[] SolveTask2WithStrictPriorityAndGuard(
        double[,] stackedJ,
        double[] stackedY,
        float baseDamping,
        out float usedDamping)
    {
        double[] xdot = SolveTask2RespectingJoint4(stackedJ, stackedY, baseDamping);
        double worstRatio = WorstJointSpeedRatio(xdot);

        usedDamping = baseDamping;
        if (worstRatio > singularityGuardTriggerRatio)
        {
            usedDamping = baseDamping * (float)Math.Sqrt(worstRatio) * singularityGuardBoostFactor;
            xdot = SolveTask2RespectingJoint4(stackedJ, stackedY, usedDamping);
        }
        return xdot;
    }

    private double[] SolveTask2RespectingJoint4(double[,] J, double[] y, float damp)
    {
        double[] xdot = SolveTask2WithStrictPriority(J, y, damp);
        lastTask2Joint4LimitActive = Task2Joint4CommandWouldCrossSoftLimit(xdot[3]);
        return lastTask2Joint4LimitActive ? SolveTask2WithFixedJoint4(J, y, damp) : xdot;
    }

    private bool Task2Joint4CommandWouldCrossSoftLimit(double q4dot)
    {
        float margin = Mathf.Clamp(task2Joint4SoftLimitMarginDeg, 0f,
            0.45f * (dh[3].qMaxDeg - dh[3].qMinDeg));
        float lowerSoftLimit = dh[3].qMinDeg + margin;
        float upperSoftLimit = dh[3].qMaxDeg - margin;
        float q4 = qDeg[3];
        return (q4 <= lowerSoftLimit && q4dot < 0.0) ||
               (q4 >= upperSoftLimit && q4dot > 0.0);
    }

    private double[] SolveTask2WithFixedJoint4(double[,] stackedJ, double[] stackedY, double damp)
    {
        const int fixedJoint = 3; // q4, zero-based.
        int rows = stackedJ.GetLength(0);
        int reducedVariables = stackedJ.GetLength(1) - 1;
        double[,] reducedJ = new double[rows, reducedVariables];

        for (int r = 0; r < rows; ++r)
        {
            int reducedColumn = 0;
            for (int c = 0; c < stackedJ.GetLength(1); ++c)
            {
                if (c == fixedJoint) continue;
                reducedJ[r, reducedColumn++] = stackedJ[r, c];
            }
        }

        // Solve without q4 so the Cartesian command is redistributed before integration.
        double[] reducedVelocity = SolveTask2WithStrictPriority(reducedJ, stackedY, damp);
        double[] fullVelocity = new double[NX];
        int reducedIndex = 0;
        for (int c = 0; c < NX; ++c)
        {
            if (c == fixedJoint)
            {
                fullVelocity[c] = 0.0;
                continue;
            }
            fullVelocity[c] = reducedVelocity[reducedIndex++];
        }
        return fullVelocity;
    }

    private double[] SolveTask2WithStrictPriority(double[,] stackedJ, double[] stackedY, double damp)
    {
        // Task 2 row order is tip first, entry-cone second.
        int variables = stackedJ.GetLength(1);
        double[,] tipJ = new double[3, variables];
        double[,] coneJ = new double[3, variables];
        double[] tipY = new double[3];
        double[] coneY = new double[3];
        for (int r = 0; r < 3; ++r)
        {
            tipY[r] = stackedY[r];
            coneY[r] = stackedY[r + 3];
            for (int c = 0; c < variables; ++c)
            {
                tipJ[r, c] = stackedJ[r, c];
                coneJ[r, c] = stackedJ[r + 3, c];
            }
        }

        double[] tipVelocity = DampedLeastSquares(tipJ, tipY, damp);

        // N_tip = I - J_tip# J_tip.
        double[,] tipNullspace = new double[variables, variables];
        for (int col = 0; col < variables; ++col)
        {
            double[] basis = new double[variables];
            basis[col] = 1.0;
            double[] projected = ProjectIntoDampedNullspace(tipJ, basis, damp);
            for (int row = 0; row < variables; ++row) tipNullspace[row, col] = projected[row];
        }

        double[] coneResidual = new double[3];
        double[] coneVelocityFromTip = MatVec(coneJ, tipVelocity);
        for (int r = 0; r < 3; ++r) coneResidual[r] = coneY[r] - coneVelocityFromTip[r];

        double[,] coneInTipNullspace = Multiply(coneJ, tipNullspace);
        double[] freeVelocity = DampedLeastSquares(coneInTipNullspace, coneResidual, damp);
        double[] coneVelocity = MatVec(tipNullspace, freeVelocity);

        double[] xdot = new double[variables];
        for (int i = 0; i < variables; ++i) xdot[i] = tipVelocity[i] + coneVelocity[i];

        // Correct the small primary-task leakage introduced by damped projection.
        double[] achievedTipVelocity = MatVec(tipJ, xdot);
        double[] tipResidual = new double[3];
        for (int r = 0; r < 3; ++r) tipResidual[r] = tipY[r] - achievedTipVelocity[r];
        double[] tipCorrection = DampedLeastSquares(tipJ, tipResidual, damp);
        for (int i = 0; i < variables; ++i) xdot[i] += tipCorrection[i];
        return xdot;
    }

    private double[] ProjectIntoDampedNullspace(double[,] J, double[] velocity, double damp)
    {
        double[] taskVelocity = MatVec(J, velocity);
        double[] projectedTaskVelocity = DampedLeastSquares(J, taskVelocity, damp);
        double[] result = new double[velocity.Length];
        for (int i = 0; i < velocity.Length; ++i)
            result[i] = velocity[i] - projectedTaskVelocity[i];
        return result;
    }

    private double[,] Multiply(double[,] a, double[,] b)
    {
        int rows = a.GetLength(0);
        int inner = a.GetLength(1);
        int cols = b.GetLength(1);
        double[,] result = new double[rows, cols];
        for (int r = 0; r < rows; ++r)
            for (int c = 0; c < cols; ++c)
                for (int k = 0; k < inner; ++k)
                    result[r, c] += a[r, k] * b[k, c];
        return result;
    }

    private double WorstJointSpeedRatio(double[] xdot)
    {
        float maxJointSpeed = maxJointSpeedDeg * Mathf.Deg2Rad;
        double worstRatio = 0.0;
        for (int i = 0; i < N; ++i)
            worstRatio = Math.Max(worstRatio, Math.Abs(xdot[i]) / Math.Max(1e-6, maxJointSpeed));
        return worstRatio;
    }

    private double[] SolveWithSingularityGuard(double[,] J, double[] y, float baseDamping, out float usedDamping)
    {
        double[] xdot = DampedLeastSquares(J, y, baseDamping);
        double worstRatio = WorstJointSpeedRatio(xdot);

        usedDamping = baseDamping;
        if (worstRatio > singularityGuardTriggerRatio)
        {
            // The raw command needs more speed than any joint can actually deliver: this is the
            // signature of a near-singular Jacobian, not just a fast task. Re-solving with more
            // damping trades some tracking speed for a numerically well-behaved direction, instead
            // of saturating each joint independently and ending up going the wrong way overall.
            usedDamping = baseDamping * (float)Math.Sqrt(worstRatio) * singularityGuardBoostFactor;
            xdot = DampedLeastSquares(J, y, usedDamping);
        }
        return xdot;
    }

    private double[] DampedLeastSquares(double[,] J, double[] y, double damp)
    {
        int m = J.GetLength(0);
        int n = J.GetLength(1);
        double[,] A = new double[m, m];
        for (int r = 0; r < m; ++r)
        {
            for (int c = 0; c < m; ++c)
            {
                double s = 0.0;
                for (int k = 0; k < n; ++k) s += J[r, k] * J[c, k];
                A[r, c] = s;
            }
            A[r, r] += damp * damp;
        }
        double[] z = SolveLinearSystem(A, y);
        double[] x = new double[n];
        for (int c = 0; c < n; ++c)
        {
            double s = 0.0;
            for (int r = 0; r < m; ++r) s += J[r, c] * z[r];
            x[c] = s;
        }
        return x;
    }

    private double[] MatVec(double[,] A, double[] x)
    {
        int rows = A.GetLength(0);
        int cols = A.GetLength(1);
        double[] y = new double[rows];
        for (int r = 0; r < rows; ++r)
        {
            double s = 0.0;
            for (int c = 0; c < cols; ++c) s += A[r, c] * x[c];
            y[r] = s;
        }
        return y;
    }

    private double[] SolveLinearSystem(double[,] AIn, double[] bIn)
    {
        int n = bIn.Length;
        double[,] A = new double[n, n];
        double[] b = new double[n];
        Array.Copy(AIn, A, AIn.Length);
        Array.Copy(bIn, b, bIn.Length);

        for (int k = 0; k < n; ++k)
        {
            int pivot = k;
            double maxAbs = Math.Abs(A[k, k]);
            for (int i = k + 1; i < n; ++i)
            {
                double v = Math.Abs(A[i, k]);
                if (v > maxAbs) { maxAbs = v; pivot = i; }
            }
            if (maxAbs < 1e-10)
            {
                A[k, k] += 1e-6;
                maxAbs = Math.Abs(A[k, k]);
            }
            if (pivot != k)
            {
                for (int j = k; j < n; ++j)
                {
                    double tmp = A[k, j];
                    A[k, j] = A[pivot, j];
                    A[pivot, j] = tmp;
                }
                double tb = b[k]; b[k] = b[pivot]; b[pivot] = tb;
            }

            double diag = A[k, k];
            for (int j = k; j < n; ++j) A[k, j] /= diag;
            b[k] /= diag;

            for (int i = 0; i < n; ++i)
            {
                if (i == k) continue;
                double factor = A[i, k];
                if (Math.Abs(factor) < 1e-14) continue;
                for (int j = k; j < n; ++j) A[i, j] -= factor * A[k, j];
                b[i] -= factor * b[k];
            }
        }
        return b;
    }

    private double Clamp(double v, double min, double max)
    {
        return Math.Max(min, Math.Min(max, v));
    }

    // ---------- Visuals ----------

    public void BuildVisualRobot()
    {
        linkMat = MakeMaterial("ROSA_Link_BlueWhite", new Color(0.78f, 0.86f, 0.95f, 1f), false);
        jointMat = MakeMaterial("ROSA_Joint_Dark", new Color(0.08f, 0.09f, 0.11f, 1f), false);
        wristMat = MakeMaterial("ROSA_Wrist_Blue", new Color(0.1f, 0.25f, 0.75f, 1f), false);
        shaftMat = MakeMaterial("Needle_Black", new Color(0.005f, 0.005f, 0.005f, 1f), false);
        tipMat = MakeMaterial("Needle_Tip_Red", new Color(1.0f, 0.03f, 0.02f, 1f), false);
        rcmMat = MakeMaterial("RCM_Green", new Color(0.0f, 1.0f, 0.35f, 1f), false);
        coneMat = MakeMaterial("Cone_Desired_Yellow", new Color(1.0f, 0.85f, 0.1f, 1f), false);
        lineMat = MakeMaterial("Nominal_Line", new Color(0.0f, 0.55f, 1.0f, 0.35f), true);

        GameObject visuals = new GameObject("Procedural_ROSA_Visuals");
        visuals.transform.SetParent(transform);

        for (int i = 0; i <= N; ++i)
        {
            GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "Joint_" + i;
            s.transform.SetParent(visuals.transform);
            s.transform.localScale = Vector3.one * (i == 0 ? 0.12f : 0.075f);
            s.GetComponent<Renderer>().material = i < 4 ? jointMat : wristMat;
            DestroyCollider(s);
            jointSpheres.Add(s);
        }

        for (int i = 0; i < N; ++i)
        {
            GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.name = "Link_" + i;
            c.transform.SetParent(visuals.transform);
            c.GetComponent<Renderer>().material = linkMat;
            DestroyCollider(c);
            linkCylinders.Add(c);
        }

        shaftCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shaftCylinder.name = "Needle_Shaft_Black";
        shaftCylinder.transform.SetParent(visuals.transform);
        shaftCylinder.GetComponent<Renderer>().material = shaftMat;
        DestroyCollider(shaftCylinder);

        tipSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tipSphere.name = "Needle_Tip_Red";
        tipSphere.transform.SetParent(visuals.transform);
        tipSphere.transform.localScale = Vector3.one * 0.01f;
        tipSphere.GetComponent<Renderer>().material = tipMat;
        DestroyCollider(tipSphere);

        rcmSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        rcmSphere.name = "Current_RCM_Point";
        rcmSphere.transform.SetParent(visuals.transform);
        rcmSphere.transform.localScale = Vector3.one * 0.032f;
        rcmSphere.GetComponent<Renderer>().material = rcmMat;
        DestroyCollider(rcmSphere);

        coneDesiredSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        coneDesiredSphere.name = "Desired_Entry_Cone_Point";
        coneDesiredSphere.transform.SetParent(visuals.transform);
        coneDesiredSphere.transform.localScale = Vector3.one * 0.008f;
        coneDesiredSphere.GetComponent<Renderer>().material = coneMat;
        DestroyCollider(coneDesiredSphere);

        nominalLineCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        nominalLineCylinder.name = "Entry_Target_Nominal_Line";
        nominalLineCylinder.transform.SetParent(visuals.transform);
        nominalLineCylinder.GetComponent<Renderer>().material = lineMat;
        DestroyCollider(nominalLineCylinder);

        UpdateVisualRobot();
    }

    private void UpdateVisualRobot()
    {
        if (jointSpheres.Count == 0) return;
        float[] q = GetQRad();
        Matrix4x4[] frames = ForwardFrames(q);
        for (int i = 0; i <= N; ++i)
        {
            jointSpheres[i].transform.position = Pos(frames[i]);
        }
        for (int i = 0; i < N; ++i)
        {
            DrawCylinderBetween(linkCylinders[i].transform, Pos(frames[i]), Pos(frames[i + 1]), linkRadius);
        }

        Vector3 baseP = ToolBase(q);
        Vector3 tip = ToolTip(q);
        DrawCylinderBetween(shaftCylinder.transform, baseP, tip, toolVisualRadius);
        tipSphere.transform.position = tip;

        Vector3 rcm = RCMPoint(q, lambda);
        rcmSphere.transform.position = rcm;

        Vector3 desiredCone = DesiredConeEntryPoint();
        Vector3 desiredTipCone = DesiredTipConeAroundTarget();
        if (mode == RCMMode.EntryRCM_TipConeAroundTarget)
            coneDesiredSphere.transform.position = desiredTipCone;
        else
            coneDesiredSphere.transform.position = desiredCone;
        coneDesiredSphere.SetActive(mode == RCMMode.TargetRCM_EntryCone || mode == RCMMode.EntryRCM_TipConeAroundTarget);

        DrawCylinderBetween(nominalLineCylinder.transform, EntryPosition(), TargetPosition(), 0.006f);

        lastTip = tip;
        lastRCM = rcm;
        lastEntrySide = EntrySidePointOnTool(q, lambda);
        lastConeDesired = desiredCone;
        lastTipConeDesired = desiredTipCone;
        lastSkullViolation = ComputeMaxSkullViolation(q, lambda) * 1000f;
    }

    private void DrawCylinderBetween(Transform t, Vector3 a, Vector3 b, float radius)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 1e-5f)
        {
            t.gameObject.SetActive(false);
            return;
        }
        t.gameObject.SetActive(true);
        t.position = 0.5f * (a + b);
        t.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        t.localScale = new Vector3(radius, 0.5f * len, radius);
    }

    private Material MakeMaterial(string name, Color color, bool transparent)
    {
        Material mat = new Material(FindBestShader());
        mat.name = name;

        // Works in Built-in Render Pipeline and URP.
        // If everything is pink/magenta, Unity is using an unsupported shader.
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        if (transparent)
        {
            // Built-in Standard transparency.
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

    // ---------- Logging and overlay ----------

    private string ActiveTaskLabel()
    {
        if (mode == RCMMode.EntryRCM_TipTarget) return "T1_entry_rcm_tip_target";
        if (mode == RCMMode.TargetRCM_EntryCone) return "T2_target_rcm_entry_cone";
        if (mode == RCMMode.InsertionSequence) return "T3_safe_insertion";
        if (mode == RCMMode.EntryRCM_TipConeAroundTarget) return "T4_entry_rcm_tip_cone";
        return "Hold";
    }

    private float PointToSegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-8f) return Vector3.Distance(p, a);
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        Vector3 c = a + t * ab;
        return Vector3.Distance(p, c);
    }

    private float ComputeMaxToolLineDistance(float[] q)
    {
        Vector3 entry = EntryPosition();
        Vector3 target = TargetPosition();
        Vector3 baseP = ToolBase(q);
        Vector3 tip = ToolTip(q);
        float maxDist = 0f;
        for (int k = 0; k <= 24; ++k)
        {
            Vector3 p = Vector3.Lerp(baseP, tip, k / 24.0f);
            maxDist = Mathf.Max(maxDist, PointToSegmentDistance(p, entry, target));
        }
        return maxDist;
    }

    private float ComputeToolAxisAngleDeg(float[] q)
    {
        Vector3 surgicalAxis = SafeDirection(EntryPosition(), TargetPosition());
        Vector3 toolAxis = ToolAxis(q);
        return Vector3.Angle(toolAxis, surgicalAxis);
    }

    private float ActiveTaskErrorMm(float tipErr, float entryErr, float targetErr, float task2ConeErr, float task4ConeErr)
    {
        if (mode == RCMMode.EntryRCM_TipTarget) return Mathf.Max(tipErr, entryErr);
        if (mode == RCMMode.TargetRCM_EntryCone) return Mathf.Max(tipErr, task2ConeErr);
        if (mode == RCMMode.InsertionSequence)
        {
            if (insertionPhase == InsertionPhase.InsertToTarget || insertionPhase == InsertionPhase.Done)
                return Mathf.Max(tipErr, entryErr);
            return tipErr;
        }
        if (mode == RCMMode.EntryRCM_TipConeAroundTarget) return Mathf.Max(entryErr, task4ConeErr);
        return 0f;
    }

    public void StartLogging()
    {
        StopLogging();

        string dir = Path.Combine(Application.dataPath, "..", "RCM_logs");
        Directory.CreateDirectory(dir);
        csvPath = Path.Combine(dir, "project4_multircm_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".csv");

        // FileShare.ReadWrite lets you inspect/copy the CSV while Unity is still running.
        FileStream fs = new FileStream(csvPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        csvWriter = new StreamWriter(fs, Encoding.UTF8);
        csvWriter.AutoFlush = true;
        logTimer = 0f;

        // Presentation-oriented CSV: one row = one sampled controller state.
        // It contains both raw state variables and already-computed errors in millimetres,
        // so the Python script can generate clean plots and slide-ready tables directly.
        string header =
            "time_s,dt_s,fps,task,mode,phase,insertion_progress,lambda," +
            "tip_x,tip_y,tip_z,rcm_x,rcm_y,rcm_z,entry_side_x,entry_side_y,entry_side_z," +
            "desired_tip_x,desired_tip_y,desired_tip_z,desired_entry_cone_x,desired_entry_cone_y,desired_entry_cone_z,desired_tip_cone_x,desired_tip_cone_y,desired_tip_cone_z," +
            "entry_x,entry_y,entry_z,target_x,target_y,target_z," +
            "tip_target_error_mm,entry_rcm_error_mm,target_rcm_error_mm,task2_entry_cone_error_mm,task4_tip_cone_error_mm,active_task_error_mm," +
            "tip_line_distance_mm,shaft_line_max_distance_mm,tool_axis_angle_deg,skull_violation_mm," +
            "qdot_norm_rad_s,task2_q4_limit_active,lambdadot,q1_deg,q2_deg,q3_deg,q4_deg,q5_deg,q6_deg,q1dot_deg_s,q2dot_deg_s,q3dot_deg_s,q4dot_deg_s,q5dot_deg_s,q6dot_deg_s," +
            "entry_rcm_ok,tip_target_ok,skull_ok_0mm";
        // Threshold values actually used (see entryRcmOkThresholdMm / tipTargetOkThresholdMm
        // in the inspector) are written once at the top of the file as a comment line, since the
        // header itself no longer hardcodes them in its column names.
        csvWriter?.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "# entry_rcm threshold = {0:F2} mm, tip_target threshold = {1:F2} mm, skull threshold = 0 mm",
            entryRcmOkThresholdMm, tipTargetOkThresholdMm));
        csvWriter.WriteLine(header);
        Debug.Log("RCM presentation CSV log saved to: " + csvPath);
    }

    public void StopLogging()
    {
        if (csvWriter != null)
        {
            csvWriter.Flush();
            csvWriter.Close();
            csvWriter = null;
        }
    }

    private void AppendCsv(StringBuilder sb, string value)
    {
        sb.Append(value);
        sb.Append(',');
    }

    private void AppendCsv(StringBuilder sb, float value, string format = "F3")
    {
        sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:" + format + "}", value);
        sb.Append(',');
    }

    private void AppendCsv(StringBuilder sb, double value, string format = "F5")
    {
        sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:" + format + "}", value);
        sb.Append(',');
    }

    private void AppendCsvVec(StringBuilder sb, Vector3 v)
    {
        AppendCsv(sb, v.x, "F5");
        AppendCsv(sb, v.y, "F5");
        AppendCsv(sb, v.z, "F5");
    }

    private void AppendLogIfNeeded(float dt)
    {
        if (string.IsNullOrEmpty(csvPath)) StartLogging();
        logTimer += dt;
        if (logTimer < logPeriod) return;
        logTimer = 0f;

        float[] q = GetQRad();
        Vector3 tip = lastTip;
        Vector3 entry = EntryPosition();
        Vector3 target = TargetPosition();
        Vector3 rcm = lastRCM;
        Vector3 desiredTip = mode == RCMMode.InsertionSequence ? CurrentDesiredInsertionTip() : target;

        float tipErr = Vector3.Distance(tip, target) * 1000f;
        float entryErr = Vector3.Distance(rcm, entry) * 1000f;
        float targetErr = Vector3.Distance(rcm, target) * 1000f;
        float task2ConeErr = Vector3.Distance(lastEntrySide, lastConeDesired) * 1000f;
        float task4ConeErr = Vector3.Distance(lastTip, lastTipConeDesired) * 1000f;
        float activeErr = ActiveTaskErrorMm(tipErr, entryErr, targetErr, task2ConeErr, task4ConeErr);
        float tipLineDistance = PointToSegmentDistance(tip, entry, target) * 1000f;
        float shaftLineMaxDistance = ComputeMaxToolLineDistance(q) * 1000f;
        float toolAxisAngle = ComputeToolAxisAngleDeg(q);

        double qdotNorm = 0.0;
        for (int i = 0; i < N; ++i) qdotNorm += lastXdot[i] * lastXdot[i];
        qdotNorm = Math.Sqrt(qdotNorm);

        float safeDt = Mathf.Max(dt, 1e-5f);
        StringBuilder sb = new StringBuilder(1024);
        AppendCsv(sb, Time.time, "F4");
        AppendCsv(sb, safeDt, "F5");
        AppendCsv(sb, 1.0f / safeDt, "F2");
        AppendCsv(sb, ActiveTaskLabel());
        AppendCsv(sb, mode.ToString());
        AppendCsv(sb, insertionPhase.ToString());
        AppendCsv(sb, insertionProgress, "F4");
        AppendCsv(sb, lambda, "F5");

        AppendCsvVec(sb, tip);
        AppendCsvVec(sb, rcm);
        AppendCsvVec(sb, lastEntrySide);
        AppendCsvVec(sb, desiredTip);
        AppendCsvVec(sb, lastConeDesired);
        AppendCsvVec(sb, lastTipConeDesired);
        AppendCsvVec(sb, entry);
        AppendCsvVec(sb, target);

        AppendCsv(sb, tipErr, "F3");
        AppendCsv(sb, entryErr, "F3");
        AppendCsv(sb, targetErr, "F3");
        AppendCsv(sb, task2ConeErr, "F3");
        AppendCsv(sb, task4ConeErr, "F3");
        AppendCsv(sb, activeErr, "F3");
        AppendCsv(sb, tipLineDistance, "F3");
        AppendCsv(sb, shaftLineMaxDistance, "F3");
        AppendCsv(sb, toolAxisAngle, "F3");
        AppendCsv(sb, lastSkullViolation, "F3");
        AppendCsv(sb, qdotNorm, "F5");
        AppendCsv(sb, lastTask2Joint4LimitActive ? "1" : "0");
        AppendCsv(sb, lastXdot[6], "F5");

        for (int i = 0; i < N; ++i) AppendCsv(sb, qDeg[i], "F3");
        for (int i = 0; i < N; ++i) AppendCsv(sb, (float)(lastXdot[i] * Mathf.Rad2Deg), "F3");

        AppendCsv(sb, entryErr <= entryRcmOkThresholdMm ? "1" : "0");
        AppendCsv(sb, tipErr <= tipTargetOkThresholdMm ? "1" : "0");
        AppendCsv(sb, lastSkullViolation <= 0.0f ? "1" : "0");

        if (sb.Length > 0 && sb[sb.Length - 1] == ',') sb.Length = sb.Length - 1;
        sb.AppendLine();
        if (csvWriter == null) StartLogging();
        csvWriter.Write(sb.ToString());
    }

    private void OnGUI()
    {
        if (!showOverlay) return;
        float x = Screen.width - 420f;
        Rect box = new Rect(x, 10, 405, 318);
        GUI.Box(box, "Project 4 — Multi-RCM ROSA controller");
        float y = 38f;
        Label(x, ref y, "Mode: " + mode + "    Phase: " + insertionPhase + "    autoRun: " + autoRun);
        Label(x, ref y, string.Format("lambda: {0:F3}   nominal entry: {1:F3}   nominal target: {2:F3}", lambda, NominalEntryLambda(), NominalTargetLambda()));
        Label(x, ref y, string.Format("Tip→target: {0:F1} mm", Vector3.Distance(lastTip, TargetPosition()) * 1000f));
        Label(x, ref y, string.Format("RCM→entry: {0:F1} mm    RCM→target: {1:F1} mm", Vector3.Distance(lastRCM, EntryPosition()) * 1000f, Vector3.Distance(lastRCM, TargetPosition()) * 1000f));
        Label(x, ref y, string.Format("Thresholds: entry-RCM <= {0:F1} mm, tip/target <= {1:F1} mm", entryRcmOkThresholdMm, tipTargetOkThresholdMm));
        Label(x, ref y, string.Format("Task 2 entry-cone error: {0:F1} mm", Vector3.Distance(lastEntrySide, lastConeDesired) * 1000f));
        Label(x, ref y, string.Format("Task 4 tip-cone error: {0:F1} mm", Vector3.Distance(lastTip, lastTipConeDesired) * 1000f));
        Label(x, ref y, skull != null && useSkullAvoidance ? string.Format("Skull violation outside corridor: {0:F1} mm", lastSkullViolation) : "Skull: disabled for current tuning");
        Label(x, ref y, "Task 3 now: above entry -> trocar -> target, with shaft-axis guide.");
        Label(x, ref y, "Keys: 1 Entry-RCM, 2 tip fixed+target cone, 3 insertion, 4 entry fixed+tip cone, 0 hold");
        Label(x, ref y, "Task 2 q4 active-set: " + lastTask2Joint4LimitActive);
        Label(x, ref y, "Space pause, C cone on/off, R reset, L CSV log");
        Label(x, ref y, "DH table is in the inspector and can be replaced with real ROSA DH.");
    }

    private void Label(float x, ref float y, string text)
    {
        GUI.Label(new Rect(x + 12f, y, 380f, 20f), text);
        y += 22f;
    }
}
