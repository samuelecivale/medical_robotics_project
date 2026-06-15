# ROSA-inspired Multi-RCM Surgical Robot Simulation

This project implements a Unity-based simulation of a **ROSA-inspired robotic arm** for minimally invasive surgical insertion tasks under **Remote Center of Motion (RCM)** constraints.

The simulation is based on the RCM formulation described in the paper:

> *Task Control with Remote Center of Motion Constraint for Minimally Invasive Robotic Surgery*
> N. Aghakhani, M. Geravand, N. Shahriari, M. Vendittelli, G. Oriolo

The main goal of the project is to demonstrate how a robotic arm can guide a surgical needle through a fixed skull entry point while reaching an internal target, keeping the entry point approximately fixed as a Remote Center of Motion.

---

## Project overview

The scene contains:

* a simplified ROSA-inspired 6-DOF robotic arm;
* a surgical tool / needle attached to the end-effector;
* a skull-like transparent ellipsoid;
* an external entry point;
* an internal target point;
* a paper-inspired RCM controller;
* a safe insertion sequence.

The current version focuses on one main working task:

```text
Safe insertion sequence:
1. Move the needle tip to a pre-entry point outside the skull.
2. Align the needle axis with the entry-target direction.
3. Reach the entry point with the correct orientation.
4. Insert the needle while keeping the entry point as RCM.
5. Reach the internal target.
```

The old static Task 1 was removed because it became redundant with the final phase of the insertion sequence. Forced insertion was also removed to avoid unrealistic transitions.

---

## Theoretical background

The controller is inspired by the RCM formulation introduced in the reference paper.

The RCM point is modeled as a variable point along the surgical tool segment:

```text
p_RCM = p_i + lambda * (p_{i+1} - p_i)
```

where:

* `p_i` is the base point of the selected segment;
* `p_{i+1}` is the distal point of the selected segment;
* `lambda` is the penetration parameter;
* `lambda = 0` corresponds to the proximal point;
* `lambda = 1` corresponds to the distal point.

The important idea is that `lambda` is not only a geometric parameter but also an optimization variable. This allows the controller to represent the sliding motion of the tool through the entry point during insertion.

The implemented controller therefore follows the structure:

```text
Task error = [ tip target error ;
               entry RCM error ;
               axis alignment error ]
```

During insertion, the controller tries to:

```text
tip -> target
p_RCM -> entry
tool axis -> entry-target direction
```

---

## Current implementation status

The latest version includes:

* ROSA-inspired automatic robot builder;
* improved visual mesh setup;
* cleaner robot aesthetics;
* transparent skull mesh;
* red entry point;
* green target point;
* black surgical shaft;
* red real needle tip;
* safer entry-target geometry;
* pre-entry phase;
* alignment gate before insertion;
* paper-inspired RCM formulation with lambda;
* analytic Jacobian for the main insertion sequence;
* pause/resume support;
* reduced visual debug clutter;
* improved overlay information;
* disabled forced insertion shortcut;
* safer skull violation monitoring.

---

## Main scripts

The project mainly uses two scripts:

```text
Assets/Scripts/AutoROSABuilder.cs
Assets/Scripts/DoubleRCMUnityController2.cs
```

### AutoROSABuilder.cs

This script automatically builds the full simulation scene.

It creates:

* robot base;
* vertical column;
* shoulder link;
* upper arm;
* forearm;
* wrist links;
* tool frame;
* surgical needle;
* tool tip;
* entry point;
* target point;
* transparent skull;
* light and camera;
* controller component.

It also connects the generated robot to `DoubleRCMUnityController2`.

### DoubleRCMUnityController2.cs

This script controls the robot.

It implements:

* the RCM constraint;
* the insertion sequence;
* the analytic Jacobian solver for the main task;
* lambda optimization;
* entry-target axis alignment;
* pause/resume;
* keyboard shortcuts;
* overlay debug values;
* skull violation monitoring.

---

## Unity setup instructions

### 1. Create or open the Unity project

Open the Unity project containing the medical robotics simulation.

Recommended structure:

```text
Assets/
└── Scripts/
    ├── AutoROSABuilder.cs
    └── DoubleRCMUnityController2.cs
```

---

### 2. Add the scripts to Unity

In the Unity Project panel:

1. Open the `Assets` folder.
2. Create a folder named `Scripts` if it does not already exist.
3. Drag the following files into `Assets/Scripts/` one by one:

```text
AutoROSABuilder.cs
DoubleRCMUnityController2.cs
```

Wait for Unity to compile the scripts.

If Unity reports compilation errors, fix them before continuing. The scene will not work if there are compiler errors.

---

### 3. Create the robot root object

In the Hierarchy panel:

1. Right click inside the Hierarchy.
2. Select:

```text
Create Empty
```

3. Rename the object to:

```text
RobotRoot
```

This object will act as the root object for the automatic builder.

---

### 4. Attach AutoROSABuilder

Select `RobotRoot`.

In the Inspector panel on the right:

1. Click:

```text
Add Component
```

2. Search for:

```text
AutoROSABuilder
```

3. Add it to `RobotRoot`.

Do not manually add `DoubleRCMUnityController2` to `RobotRoot`.

`AutoROSABuilder` automatically creates the generated robot and attaches `DoubleRCMUnityController2` to the generated robot root.

---

### 5. Configure AutoROSABuilder

With `RobotRoot` selected, check the `AutoROSABuilder` component in the Inspector.

Recommended settings:

```text
Build On Start: enabled
Rebuild Every Start: enabled
Add Controller: enabled
Use Demo Start Pose: enabled
Auto Orient Base To Entry: enabled
Auto Clear Initial Skull Arm Overlap: enabled
Validate Workspace At Build: enabled
Use Enhanced Materials: enabled
Use Mesh Collider For Skull: enabled
```

The builder will automatically generate the scene at runtime.

---

### 6. Start the simulation

Press Play in Unity.

At runtime, the builder creates a generated object such as:

```text
ROSA_DoubleRCM_Generated
```

Inside it, Unity creates:

```text
Mobile_Base
Joint_0_BaseYaw
Joint_1_Shoulder
Joint_2_UpperArm
Joint_3_Elbow
Joint_4_WristPitch
Joint_5_ToolAxis
ToolFrame
ToolTip
EntryPoint
TargetPoint
Transparent_Skull
```

The controller should start automatically.

---

## Controls

The current keyboard controls are:

```text
3      Start / restart safe insertion sequence
2      Target-RCM cone demonstration
4      Entry-RCM tip-cone demonstration
Space  Pause / resume IK solver
R      Reset robot pose
H      Show / hide overlay
C      Enable / disable cone animation
```

The old Task 1 shortcut has been disabled.

The forced insertion shortcut has also been disabled.

This is intentional: insertion should only start when the robot reaches the entry point with a valid axis alignment.

---

## Main task: safe insertion sequence

Press:

```text
3
```

The robot performs the safe insertion sequence.

The sequence is divided into three conceptual phases.

### Phase 1: pre-entry approach

The needle tip moves to a point outside the skull, slightly before the entry point.

This avoids approaching the skull from a bad or lateral direction.

### Phase 2: alignment at entry

The needle tip reaches the entry point while the shaft aligns with the direction:

```text
entry -> target
```

The controller does not start insertion immediately when the tip touches the entry.

It waits until the alignment gate is satisfied.

The gate checks:

```text
tip-entry distance
axis angle error
entry-axis distance
```

Only when these values are acceptable does the controller start insertion.

### Phase 3: RCM insertion

The entry point becomes the active RCM.

The controller tries to keep:

```text
p_RCM = entry point
```

while moving the tip toward:

```text
target point
```

The `lambda` parameter is optimized to let the RCM point slide along the tool shaft during insertion.

---

## Overlay information

The overlay shows useful runtime information such as:

```text
current mode
current insertion phase
tip-entry error
entry RCM error
target tip error
axis alignment error
lambda
skull violation
pause state
gate status
```

If the robot reaches the entry but does not start insertion, check the gate values.

If the gate says:

```text
WAIT
```

then the tip is close enough to the entry, but the tool is not yet aligned well enough to safely insert.

If the gate says:

```text
OK
```

then insertion can start.

---

## Hiding debug lines and yellow gizmos

If yellow debug lines appear around the robot, entry point, target point, or skull, they are likely Unity Gizmos.

To hide them quickly:

1. Open the Scene view or Game view.
2. Find the `Gizmos` button in the top-right area.
3. Disable `Gizmos`.

Alternatively, in the controller Inspector, disable:

```text
Show Debug Gizmos
```

if this option is present.

For clean presentation videos, it is recommended to disable Gizmos.

---

## Skull safety

The skull is represented as a transparent ellipsoid.

The controller monitors possible skull violations and tries to prevent the robot arm from intersecting the skull.

Important design choice:

```text
The needle is allowed to enter only through the surgical corridor.
The robot arm and distal carrier should remain outside the skull.
```

This makes the behavior more realistic than simply allowing the whole end-effector to pass through the skull.

The current version avoids excessive safety terms inside the IK solver because this previously made the simulation slow and unstable. Instead, the main task is handled more directly and skull violation is monitored during the motion.

---

## Why Task 1 was removed

The original Task 1 was:

```text
Entry-RCM + tip target
```

After improving the insertion sequence, this became almost identical to the final insertion phase of Task 3.

Keeping both tasks made the demo confusing.

The project now focuses on:

```text
Task 3 = main safe insertion sequence
Task 2 = Target-RCM cone demonstration
Task 4 = Entry-RCM tip-cone demonstration
```

This makes the presentation cleaner and easier to explain.

---

## Why forced insertion was removed

The previous force insertion shortcut allowed the user to jump directly into insertion.

This was unsafe and unrealistic because the robot could start inserting even if:

* the tip was not correctly placed at the entry;
* the needle axis was not aligned with the target;
* the robot was in a poor configuration;
* the RCM constraint could not be properly maintained.

The updated version requires the controller to pass through the pre-entry and alignment phases before insertion.

---

## Visual and aesthetic improvements

The latest update also improves the visual quality of the simulation.

Main graphical changes:

* more ROSA-like visual structure;
* improved base and column;
* clearer arm links;
* blue joint spheres;
* white and gray robot links;
* darker distal tool mount;
* black surgical needle shaft;
* red physical needle tip;
* red entry point;
* green internal target point;
* transparent skull material;
* better light and camera creation;
* cleaner generated hierarchy;
* reduced debug clutter.

These changes make the simulation easier to understand during demonstrations and presentations.

---

## Troubleshooting

### The robot does not appear

Check that:

```text
Build On Start = true
Add Controller = true
```

Also make sure there are no compiler errors in Unity.

---

### The robot appears twice

Delete old generated roots from the Hierarchy, such as:

```text
ROSA_PaperRCM_Generated
ROSA_DoubleRCM_Generated
```

Then press Play again.

---

### The robot moves slowly or in steps

Possible causes:

* too many debug gizmos enabled;
* CSV logging enabled;
* Unity editor running slowly;
* too many collision checks;
* old controller still active in the scene.

Recommended fixes:

```text
Disable Gizmos
Disable CSV logging if not needed
Keep only one AutoROSABuilder in the scene
Remove old PaperRCMController components
Remove old generated robot roots
```

---

### The robot reaches the entry but does not insert

This usually means the alignment gate is not satisfied.

Check the overlay.

If the gate says:

```text
WAIT
```

then the robot is still trying to align the needle axis with the target direction.

The insertion starts only when the entry point is reached with a valid orientation.

---

### The robot cannot reach the skull or target

The builder is a simplified ROSA-like model, not the exact real ROSA robot.

If the robot cannot reach the target, adjust:

```text
robotBaseOffset
entryPosition
targetPosition
upperArmLength
forearmLength
wristLength
toolLength
```

The current configuration was chosen to make the surgical task reachable and visually understandable.

---

### The robot intersects the skull

Check:

```text
armSafetyMargin
needleSafetyMargin
surgical corridor radius
entry-target alignment
```

The needle should enter only through the entry-target corridor. The arm should remain outside the skull.

---

## Recommended presentation explanation

A concise explanation for the project presentation could be:

```text
We implemented a Unity simulation of a ROSA-inspired surgical robot performing a minimally invasive insertion task under a Remote Center of Motion constraint. The RCM is modeled following the paper formulation as a variable point along the surgical tool segment, controlled through the lambda parameter. The main task is a safe insertion sequence: the robot first approaches a pre-entry point, aligns the needle with the entry-target direction, and then inserts the tool while keeping the entry point fixed as RCM. The scene is automatically generated by AutoROSABuilder and includes a simplified ROSA-like arm, a transparent skull, an entry point, a target point, and a surgical needle.
```

---

## Suggested Git commit

Recommended commit title:

```text
Implement paper-based RCM insertion workflow with ROSA-like scene builder and safer skull interaction
```

Alternative shorter commit title:

```text
Refactor ROSA RCM demo with safe insertion sequence and improved visual setup
```

Suggested commit body:

```text
- Reworked the Unity scene generation through AutoROSABuilder.
- Integrated DoubleRCMUnityController2 as the main controller.
- Added a ROSA-inspired 6-DOF robot visual model.
- Improved robot mesh aesthetics, materials, colors, skull transparency, and tool visualization.
- Implemented a safer insertion sequence with pre-entry, alignment, and RCM insertion phases.
- Removed the redundant Task 1 and disabled forced insertion.
- Added paper-inspired link-based RCM formulation using lambda.
- Improved target and entry positioning for a more reachable insertion geometry.
- Added alignment gating before insertion.
- Reduced unstable collision terms inside the IK loop.
- Improved skull violation monitoring and presentation/debug overlay.
- Added clearer keyboard controls and setup workflow.
```

---

## Current project status

The current project demonstrates a working ROSA-inspired RCM insertion workflow in Unity.

The simulation is not intended to be an exact digital twin of the real ROSA robot. Instead, it is a visual and control-oriented prototype showing how a surgical robot can perform an insertion task while respecting a Remote Center of Motion constraint.

The most stable and meaningful demo is:

```text
Press 3 -> Safe insertion sequence
```

