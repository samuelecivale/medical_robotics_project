# Multi-RCM for a ROSA-Inspired Neurosurgical Robot

Unity project developed for the **Medical Robotics** course.

This project implements a simplified **ROSA-inspired neurosurgical robotic system** and a kinematic controller for a **multi-RCM neurosurgical task**.

The assignment focuses on the ROSA robot use case for neurosurgery. In the real system, the Remote Center of Motion (RCM) is normally located at the **skull entry point**. In some phases of the procedure, however, it can be useful to consider the **internal target point** as an additional constraint and to allow a small controlled conical motion on the entry side.

This implementation focuses on:

- building a simplified 3D and kinematic model of a ROSA-inspired robot in Unity;
- defining an entry point on the skull surface;
- defining a target point inside the skull;
- implementing Entry-RCM, Target-RCM, Double-RCM and insertion behaviours;
- implementing small conical motion demonstrations;
- separating skull avoidance for the robot arm and the surgical needle;
- visualizing the main control errors during simulation.

---

## Project Overview

The implemented Unity scene contains:

- a fixed base;
- a vertical column;
- a compact serial robotic arm;
- a wrist / distal tool guide;
- a surgical needle/tool axis;
- a simplified transparent skull;
- an entry point on the skull surface;
- a target point inside the skull;
- an on-screen debug overlay with the main RCM and avoidance quantities.

The robot is **not an exact CAD reconstruction of the commercial ROSA robot**. It is a **ROSA-inspired kinematic model**, built with Unity primitives, designed to reproduce the functional elements required by the assignment.

---
## Demo Video

The following video shows the current Unity demo, including the ROSA-inspired robot, the skull entry point, the internal target, the insertion sequence, and the RCM/skull-avoidance overlay.

[▶ Watch Movie_002.mp4](Recordings/Movie_002.mp4)
---

## Main Files

The current implementation is mainly contained in two scripts:

```text
Assets/Scripts/AutoROSABuilder.cs
Assets/Scripts/DoubleRCMUnityController2.cs
```

### `AutoROSABuilder.cs`

This script automatically builds the scene at runtime.

It creates:

- the ROSA-inspired robot base, column, links and joints;
- the distal support and surgical needle;
- the transparent skull;
- the entry point;
- the target point;
- the camera and light setup;
- the controller component and its default parameters.

It also defines the scene geometry, including the entry-target configuration and the placement of the robot with respect to the skull.

### `DoubleRCMUnityController2.cs`

This script implements the kinematic controller.

It includes:

- numerical inverse kinematics;
- damped least-squares joint update;
- link-based RCM formulation;
- insertion sequence control;
- Entry-RCM mode;
- Target-RCM mode;
- Double-RCM mode;
- Entry-RCM with tip cone mode;
- skull avoidance for the robot arm;
- needle-skull avoidance before insertion;
- safe insertion corridor logic;
- on-screen overlay for debugging.

---

## Implemented Modes

During Play Mode, the demo supports several behaviours selectable from the keyboard.

| Key | Mode | Description |
|---|---|---|
| `1` | Entry-RCM | The entry point is treated as the trocar/RCM and the tip is driven toward the target. |
| `2` | Target-RCM + entry cone | The target is treated as the RCM and the entry side is allowed to move inside a small cone. |
| `3` | Insertion sequence | The needle first reaches the entry point, then advances toward the internal target. |
| `4` | Entry-RCM + tip cone | The entry is kept as RCM while the tip performs a small conical motion around the target. |
| `I` | Force insertion | Forces the insertion phase. |
| `C` | Toggle cone animation | Enables/disables the conical demo motion. |
| `R` | Reset | Resets the robot pose and simulation phase. |
| `Space` | Pause/resume IK | Pauses or resumes the IK solver. |
| `H` | Overlay | Shows/hides the debug overlay. |

---

## Insertion Sequence

The main demonstration is the full insertion sequence, selected with key `3`.

### 1. Approach Entry

The physical needle tip is driven toward the red entry point on the skull surface.

During this phase:

- the needle should not cut through the skull;
- the arm links are kept outside the skull;
- the tool axis is weakly aligned with the entry-target direction.

### 2. Insert to Target

After the tip reaches the entry point, the system switches to insertion.

During this phase:

- the entry point becomes the trocar/RCM;
- the needle advances toward the internal green target;
- the entry point slides along the needle shaft through the link-based RCM parameter;
- the needle is allowed to enter the skull only along the intended entry-target corridor;
- the robot arm must remain outside the skull.

---

## Link-Based RCM Formulation

The entry RCM is represented using a point on a kinematic segment:

```text
p_RCM = p_i + lambda (p_{i+1} - p_i)
```

where:

- `p_i` and `p_{i+1}` are the endpoints of the selected segment;
- `lambda` is a scalar parameter in `[0, 1]`;
- for the surgical needle segment, `lambda = 1` corresponds to the physical tip;
- as the needle advances, the entry point moves backward along the shaft.

For the insertion sequence, the controller updates `entryLambda` according to the insertion depth. This prevents the entry point from remaining attached to the physical tip after insertion starts.

---

## Skull and Needle Avoidance

The current implementation separates the avoidance logic into two parts.

### Arm-skull avoidance

The robot arm links are sampled and penalized when they enter or get too close to the skull volume. This prevents the large robot links and joints from intersecting the skull during approach and insertion.

### Needle-skull avoidance

The needle has different behaviour depending on the phase.

Before insertion:

- the needle should remain outside the skull;
- if it intersects the skull away from the entry point, it is pushed away.

During insertion:

- the needle is allowed to enter the skull;
- it should do so only through the intended entry-target corridor.

This distinction is important because the surgical needle must enter the skull, while the robot arm must not.

---

## Debug Overlay

The simulation includes an on-screen overlay placed on the right side of the screen.

The overlay displays:

- current mode;
- current insertion phase;
- IK solving status;
- entry RCM error;
- target/tip error;
- insertion progress;
- intermediate target error;
- entry-target axis error;
- cone angle and cone violation;
- needle skull violation;
- arm skull violation;
- joint limit status;
- cone animation status.

---

## How to Run the Project

### 1. Open the Unity project

Open the Unity project folder from Unity Hub.

### 2. Create a root object

In the Unity Hierarchy:

```text
Right click -> Create Empty
```

Rename the object, for example:

```text
RobotRoot
```

### 3. Add the builder script

Select `RobotRoot` and add the component:

```text
AutoROSABuilder
```

Make sure the following options are enabled:

```text
Build On Start = true
Rebuild Every Start = true
Add Controller = true
```

### 4. Press Play

When Play Mode starts, the builder automatically creates the scene and attaches the controller.

---

## Suggested Test Procedure

```text
1. Press Play.
2. Observe the initial approach to the entry point.
3. Press 3 to restart the insertion sequence if needed.
4. Check that the tip reaches the red entry point.
5. Observe the insertion toward the green target.
6. Check the overlay values:
   - Entry RCM error should remain small.
   - Target/tip error should decrease near the end.
   - Arm skull violation should remain close to zero.
7. Press 2 to test target-RCM with entry cone.
8. Press 4 to test entry-RCM with tip cone.
9. Press R to reset the simulation.
```

---

## Current Parameters and Tuning Notes

The demo uses approximate values tuned for visual stability.

Important controller parameters include:

```text
entryApproachTipWeight
preAlignEntryAxisWeight
insertionEntryWeight
insertionTargetWeight
insertionAxisWeight
insertionProgressSpeed
damping
maxDeltaDegPerIteration
ikStepScale
armSkullAvoidanceWeight
armSafetyMargin
armAvoidanceSamplesPerSegment
needleSkullAvoidanceWeight
needleSafetyMargin
needleAvoidanceSamples
needleInsertionCorridorRadiusMm
```

The final tuning separates two requirements:

- the arm must avoid the skull strongly;
- the needle must be allowed to enter only through the intended entry-target direction.

---

## Repository Structure

The most relevant files are:

```text
Assets/
└── Scripts/
    ├── AutoROSABuilder.cs
    └── DoubleRCMUnityController2.cs
```

Generated Unity folders such as `Library/`, `Temp/`, `Logs/`, and `UserSettings/` are not required in the repository and should not be committed.

Recommended `.gitignore`:

```text
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
[Ll]ogs/
[Uu]ser[Ss]ettings/
*.csproj
*.sln
*.slnx
.DS_Store
RCM_logs/
```

---

## Technical Description

The controller uses an iterative numerical IK approach.

At each update:

1. the current task error vector is computed;
2. a numerical Jacobian is estimated with finite differences;
3. a damped least-squares system is solved;
4. small joint updates are applied;
5. joint limits and avoidance terms are considered.

The damped least-squares structure is:

```text
dq = J^T (J J^T + lambda^2 I)^-1 e
```

where:

- `J` is the numerical Jacobian;
- `lambda` is the damping coefficient;
- `e` is the stacked task error vector;
- `dq` is the joint update.

The task error vector can include:

- entry RCM error;
- target/tip error;
- axis alignment error;
- cone tracking error;
- arm-skull avoidance errors;
- needle-skull avoidance errors.

---

## Limitations

This project is a simplified educational simulation.

Current limitations:

- the robot is ROSA-inspired, not an exact commercial ROSA CAD model;
- the geometry is generated with Unity primitives;
- the skull is represented by a transparent ellipsoid;
- the IK is numerical and locally optimized, not a full constrained optimal controller;
- collision avoidance is approximate and based on sampled points;
- anatomical structures are simplified;
- the controller is tuned for demonstration rather than clinical realism.

Despite these simplifications, the implementation captures the core learning objectives:

- building a 3D robotic system in Unity;
- defining RCM constraints;
- implementing kinematic control;
- demonstrating entry-RCM, target-RCM and insertion behaviour;
- showing the distinction between needle insertion and arm-skull avoidance.

---

## Possible Future Improvements

Possible extensions include:

- replacing the primitive robot with a more accurate ROSA-like CAD model;
- adding more realistic joint limits;
- using a constrained optimization-based controller;
- adding proper collision geometry for each robot link;
- improving the anatomical model of the skull;
- plotting overlay/logged errors over time;
- adding a more realistic surgical trajectory planner;
- improving the conical motion demonstration around the target and entry constraints.

---

## Short Description for the Presentation

This project presents a Unity simulation of a ROSA-inspired neurosurgical robot for a multi-RCM task.

The system includes a simplified robotic arm, a surgical needle, a transparent skull, an entry point on the skull surface and an internal target point.

The controller supports Entry-RCM, Target-RCM, Double-RCM insertion and small conical motion demonstrations. It uses numerical Jacobian inverse kinematics with damped least squares, while separating the avoidance behaviour of the robot arm from the allowed insertion behaviour of the surgical needle.

---

## Authors

Project developed for the **Medical Robotics** course.

Students:

```text
Samuele Civale
Alexandru Vivian Pita
Francesca Forghieri
```
