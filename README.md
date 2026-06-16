# Project 4 — Multi-RCM Control for a ROSA-like Neurosurgical Robot in Unity

## Overview

This repository contains a Unity implementation of a simplified ROSA-like robotic system for neurosurgical Remote Center of Motion (RCM) control.

The project addresses **Project 4 — Multi-RCM for the ROSA robot**. The goal is to build a 3D and kinematic model of a ROSA-inspired manipulator and implement a double-RCM kinematic controller for neurosurgical procedures.

The surgical scenario is based on:

- an **entry point** on the skull, representing the trocar / burr-hole constraint;
- an internal **target point** inside the skull;
- a simplified skull model used for visualization and collision / violation monitoring;
- a needle-like surgical tool attached to a 6-DOF serial robotic arm.

The controller supports three main demonstration tasks:

1. **Target RCM + small conical motion at the entry side**
2. **Safe insertion sequence through the skull entry point**
3. **Entry RCM + small conical motion of the tip around the target**

The implementation focuses on showing that the robot can satisfy RCM constraints, reach the surgical target, avoid skull violations during approach/insertion, and generate visible conical motions when required by the assignment.

---

## Repository structure

Recommended structure:

```text
Assets/
└── Scripts/
    ├── AutoROSABuilder.cs
    ├── DoubleRCMUnityController2.cs
    ├── MainCameraKeyboardFreeFly.cs
    └── CameraControlsOverlay.cs

RCM_logs/
└── *.csv

analysis/
└── analyze_rcm_logs.py

README.md
```

### Main scripts

| File | Purpose |
|---|---|
| `AutoROSABuilder.cs` | Automatically builds the ROSA-like arm, tool, skull, entry point, target point, camera and scene objects. It also attaches and configures the controller. |
| `DoubleRCMUnityController2.cs` | Main kinematic controller. Implements Entry RCM, Target RCM, Double RCM, insertion sequence, conical motions, skull avoidance and CSV logging. |
| `MainCameraKeyboardFreeFly.cs` | Keyboard-only free-fly camera for inspecting the scene during Play mode. |
| `CameraControlsOverlay.cs` | Left-side overlay showing the camera controls. |
| `analyze_rcm_logs.py` | Python script used to evaluate the generated CSV logs and produce quantitative reports and plots. |

---

## Scene generation

The scene is generated automatically by `AutoROSABuilder.cs`.

The generated scene contains:

- a simplified 6-DOF ROSA-like manipulator;
- a black surgical shaft;
- a red physical tool tip;
- a red skull entry point;
- a green internal target point;
- a semi-transparent skull ellipsoid;
- a controller component attached to the generated robot root;
- a camera and lights.

Current surgical reference positions are:

```csharp
entryPosition  = new Vector3(0.80f, 0.70f, 0.055f);
targetPosition = new Vector3(0.865f, 0.585f, 0.055f);

skullCenterOffsetFromEntry = new Vector3(0.060f, -0.135f, 0.025f);
initialSkullClearanceMm = 18.0f;
```

The target is intentionally placed inside the skull, while the entry point represents the external trocar / skull entry constraint.

---

## Unity setup

1. Create or open the Unity project.
2. Put the scripts inside:

```text
Assets/Scripts/
```

3. Create an empty GameObject in the scene, for example:

```text
ROSA_Builder
```

4. Attach:

```text
AutoROSABuilder.cs
```

5. Enable:

```text
Build On Start = true
Rebuild Every Start = true
Add Controller = true
```

6. Press Play.

The ROSA-like robot, tool, skull, entry point and target point should be created automatically.

---

## Important Unity note: avoid duplicate scripts

Unity compiles every `.cs` file inside `Assets/`.

Do not keep old versions of the same scripts inside folders such as:

```text
Assets/Scripts/old/
Assets/Scripts/backup/
Assets/Scripts/task_3_funziona_2_4_no/
```

If Unity finds two files containing the same class name, for example:

```csharp
public class AutoROSABuilder : MonoBehaviour
public class DoubleRCMUnityController2 : MonoBehaviour
```

it will generate duplicate-definition errors.

To keep old files, move them outside `Assets/`, for example:

```text
_script_backups/
```

or rename them from `.cs` to `.txt`.

---

## Camera controls

The project includes a keyboard-only free-fly camera.

Attach this script to the Main Camera:

```text
MainCameraKeyboardFreeFly.cs
```

Recommended camera settings:

```text
Projection = Perspective
Field of View = 50
Near Clip Plane = 0.01
Far Clip Plane = 100
```

Controls:

```text
W / S              forward / backward
A / D              left / right
Q / E              down / up
Arrow Left/Right   yaw left / right
Arrow Up/Down      look up / down
Shift              faster movement
Ctrl               slower movement
F                  reset view
```

The camera controls can also be displayed using:

```text
CameraControlsOverlay.cs
```

Attach it to the Main Camera or to any active GameObject in the scene.

---

## Controller controls

During Play mode, the main controller supports the following keyboard shortcuts:

```text
2      Target RCM + entry-side cone
3      Safe insertion sequence
4      Entry RCM + tip cone around target
C      Toggle target-cone animation
R      Reset robot pose
Space  Pause / resume IK solving
H      Show / hide controller overlay
```

---

## Implemented demonstration tasks


### Task 2 — Target RCM + conical motion at entry side

The target point is treated as the RCM.  
The shaft is allowed to perform a small conical motion on the entry side.

Main objective:

```text
Keep the internal target fixed as RCM while showing a small visible cone at the entry/trocar side.
```

Important note:

In this task, the entry-side point is intentionally moving inside a cone. Therefore, the entry-side distance from the nominal entry point should not be interpreted as a failure of the entry RCM. The active RCM constraint in this task is the target point.

Current configuration:

```csharp
controller.targetModeTargetRCMWeight = 5.5f;
controller.targetModeEntryConeWeight = 1.6f;

controller.useEntryConeInTargetMode = true;
controller.animateTargetConeDemo = true;

controller.entryConeHalfAngleDeg = 7.0f;
controller.entryConeMotionFraction = 0.75f;
controller.entryConeFrequencyHz = 0.18f;
```

---

### Task 3 — Safe insertion sequence

The insertion sequence is divided into phases:

```text
ApproachEntry
InsertToTarget
Done
```

The controller first moves the tool toward a pre-entry pose, then aligns the shaft with the entry-target direction, and finally inserts the tool toward the target while maintaining the trocar RCM.

Main objective:

```text
Reach the internal target through the skull entry point with zero skull violation.
```

The controller uses:

- pre-entry approach;
- alignment gate before insertion;
- entry RCM preservation during insertion;
- target reaching;
- needle/skull avoidance;
- arm/skull avoidance;
- hard skull safety backtracking;
- phase-specific speed limits for smoother approach.

Current relevant configuration:

```csharp
controller.useInsertionSequence = true;
controller.useProgressiveStraightInsertion = true;

controller.preEntryDistanceMm = 135.0f;
controller.preEntryReachedThresholdMm = 12.0f;
controller.insertionStartAxisThresholdDeg = 5.0f;
controller.insertionStartAxisDistanceThresholdMm = 4.0f;

controller.requireAlignedPoseBeforeInsertion = true;

controller.enforceZeroSkullViolation = true;
controller.hardSkullSafetyMargin = 0.0f;
controller.hardSafetyBacktrackingSteps = 6;
controller.hardSafetyToleranceMm = 0.0f;
```

---

### Task 4 — Entry RCM + tip cone around target

The entry point is treated as the trocar RCM.  
The physical tip moves around the internal target with a small conical motion.

Main objective:

```text
Keep the entry/trocar point fixed while the tool tip performs a small visible cone around the target.
```

Current configuration:

```csharp
controller.entryTipConeRCMWeight = 5.8f;
controller.entryTipConeTipWeight = 2.4f;
controller.entryTipConeAxisWeight = 1.0f;

controller.useTipConeAroundTargetMode = true;

controller.tipConeHalfAngleDeg = 4.0f;
controller.tipConeMotionFraction = 0.85f;
controller.tipConeFrequencyHz = 0.18f;
```

The cone was intentionally kept small to preserve the entry RCM and keep the target-side distance below approximately 10 mm while still remaining visible.

---

## CSV logging

CSV logging is implemented inside `DoubleRCMUnityController2.cs`.

Recommended settings:

```csharp
controller.logToCsv = true;
controller.useTimestampedLogFile = true;
controller.logEverySeconds = 0.02f;
```

Logs are saved in:

```text
RCM_logs/
```

Each CSV contains time series data such as:

```text
time
dt
fps
mode
phase
tip_entry_error_mm
entry_rcm_error_mm
target_rcm_or_tip_error_mm
final_target_tip_error_mm
insertion_intermediate_error_mm
entry_cone_angle_deg
entry_cone_violation_deg
skull_violation_mm
arm_skull_violation_mm
total_skull_violation_mm
entry_lambda
target_lambda
tool position
tip position
entry position
target position
joint angles
```

---

## Quantitative evaluation

The project can be evaluated using:

```text
analyze_rcm_logs.py
```

Example command:

```bash
python3 analyze_rcm_logs.py RCM_logs/*.csv \
  --out rcm_analysis \
  --entry-max-mm 10 \
  --target-max-mm 10 \
  --final-target-max-mm 8 \
  --skull-max-mm 0 \
  --min-fps 30 \
  --cone-min-angle-deg 3 \
  --cone-min-radius-mm 8
```

The script generates:

```text
rcm_analysis/
├── rcm_analysis_report.txt
├── rcm_summary_by_task_phase.csv
├── *_derived.csv
├── *_errors_time.png
├── *_skull_violation_time.png
├── *_fps_time.png
├── *_cone_angle_time.png
├── *_cone_radius_time.png
├── *_smoothness_time.png
├── *_Task2_TargetRCM_EntryCone_cone_plane.png
└── *_Task4_EntryRCM_TipCone_cone_plane.png
```

---

## Recommended evaluation procedure

Before each evaluation, clear old logs:

```bash
mkdir -p RCM_logs_old
mv RCM_logs/*.csv RCM_logs_old/
```

Then run the simulation:

```text
Play
Press 2 and record for about 25 seconds
Press 3 and wait until Done
Press 4 and record for about 25 seconds
Stop
```

Then run:

```bash
rm -rf rcm_analysis

python3 analyze_rcm_logs.py RCM_logs/*.csv \
  --out rcm_analysis \
  --entry-max-mm 10 \
  --target-max-mm 10 \
  --final-target-max-mm 8 \
  --skull-max-mm 0 \
  --min-fps 30 \
  --cone-min-angle-deg 3 \
  --cone-min-radius-mm 8
```

Open the report:

```bash
open rcm_analysis/rcm_analysis_report.txt
```

---

## Acceptance criteria

Recommended quantitative thresholds:

| Metric | Acceptance threshold |
|---|---:|
| Entry RCM max error, when entry is the active RCM | < 10 mm |
| Target RCM max error, when target is the active RCM | < 10 mm |
| Final target error in insertion | < 8 mm |
| Skull violation | 0 mm |
| Arm-skull violation | 0 mm |
| FPS p05 | > 30 FPS |
| Cone angle p95 in Task 2 / Task 4 | > 3 deg |
| Cone radius p95 in Task 2 / Task 4 | > 8 mm |

Task-specific interpretation:

### Task 2

Passes if:

```text
target RCM error < 10 mm
entry-side cone angle p95 > 3 deg
entry-side cone radius p95 > 8 mm
skull violation = 0 mm
```

The entry-side distance from the nominal entry point is expected because Task 2 intentionally demonstrates a cone on the entry side.

### Task 3

Passes if:

```text
final target error < 8 mm
entry RCM final / p95 during insertion < 10 mm
skull violation = 0 mm
arm skull violation = 0 mm
phase reaches Done
```

Large raw max errors during task switching should be treated as transients and evaluated separately from the steady-state insertion phase.

### Task 4

Passes if:

```text
entry RCM error < 10 mm
tip cone angle p95 > 3 deg
tip cone radius p95 > 8 mm
skull violation = 0 mm
```

The final target-tip distance is not necessarily a failure in Task 4, because the tip is intentionally moving around the target on a cone.

---

## Latest measured results

Using the current configuration, the latest log analysis produced the following relevant results.

### Task 2 — Target RCM + entry-side cone

```text
target_rcm_or_tip_error max ≈ 0.538 mm
cone angle p95 ≈ 5.280 deg
cone radius p95 ≈ 14.839 mm
skull violation max = 0 mm
```

Interpretation:

```text
Task 2 passes.
The target RCM is very accurate and the entry-side conical motion is clearly visible.
```

### Task 3 — Safe insertion

```text
final target error ≈ 2.180 mm
entry RCM final error ≈ 1.010 mm
skull violation max = 0 mm
arm skull violation max = 0 mm
```

Interpretation:

```text
Task 3 passes.
The tool reaches the internal target with zero skull violation.
The large raw maximum errors in the report are task-switching / initialization transients, not the final insertion behavior.
```

### Task 4 — Entry RCM + tip cone

```text
entry RCM max error ≈ 2.431 mm
tip cone angle p95 ≈ 3.498 deg
tip cone radius p95 ≈ 9.809 mm
skull violation max = 0 mm
```

Interpretation:

```text
Task 4 passes.
The trocar remains stable and the conical motion remains visible while staying smaller than the previous 13 mm radius.
```

---

## Known limitations

1. **Simplified ROSA geometry**

The robot is ROSA-inspired, not a manufacturer-accurate mechanical replica. The project focuses on kinematic behavior and RCM control rather than CAD-level fidelity.

2. **Numerical IK**

The controller uses numerical damped least-squares IK. This makes the implementation flexible but can create transient spikes during task switching or reinitialization.

3. **Task-switching transients**

Some CSV reports show large max errors immediately after switching between tasks. These should be separated from steady-state performance by evaluating each task independently or by discarding the first seconds after a mode change.

4. **Simplified skull model**

The skull is represented as an ellipsoid-like collider/visual object. This is sufficient for collision and violation monitoring in the project, but it is not patient-specific anatomy.

5. **Visual cone vs clinical motion**

The conical motions in Task 2 and Task 4 are deliberately tuned to be visible in Unity while keeping the RCM error bounded. They are demonstration motions rather than clinically optimized trajectories.

---

## Conclusion

The project satisfies the main requirements of Project 4:

- it builds a 3D and kinematic ROSA-like robot model in Unity;
- it implements entry RCM and target RCM constraints;
- it demonstrates double-RCM behavior through kinematic control;
- it performs a safe insertion sequence through the skull entry point;
- it supports target-RCM and entry-RCM conical motion demonstrations;
- it logs quantitative CSV data for objective evaluation;
- it achieves zero skull violation in the tested runs;
- it keeps the trocar stable during the active entry-RCM tasks.

Overall, the implementation provides both a visual Unity demonstration and a quantitative evaluation pipeline for assessing Multi-RCM behavior in a simplified ROSA neurosurgical scenario.
