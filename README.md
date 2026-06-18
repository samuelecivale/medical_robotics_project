# Project 4 — Multi-RCM for a ROSA-like Neurosurgical Robot

## Overview

This Unity project implements a simplified but complete simulation of **Project 4 — Multi-RCM for the ROSA robot**.

The goal of the project is to reproduce, in a 3D Unity environment, the main kinematic idea behind multi-RCM control for neurosurgical robotic procedures. In the standard ROSA neurosurgical setup, the **Remote Center of Motion** is located at the skull entry point, also called the trocar or entry point. During some phases of the surgical procedure, however, it can be useful to impose a second RCM-like behavior at the deep target, allowing a small conical motion at the entry side.

The implementation provides:

* a procedural 6-DOF ROSA-like manipulator;
* a replaceable Denavit-Hartenberg kinematic model;
* a virtual surgical entry point;
* a deep target point;
* a needle/tool attached to the robot end-effector;
* an entry-RCM insertion task;
* a target-RCM conical task;
* an entry-RCM tip-cone task;
* keyboard controls and camera overlay;
* real-time visualization of the robot, needle, entry point, target point and nominal entry-target line.

The project does not use an official ROSA mesh. Instead, it builds a simplified ROSA-like 6-DOF serial manipulator procedurally in Unity. This makes the project self-contained and allows the DH parameters to be replaced later with more accurate values if a real robot model becomes available.

---

## Theoretical reference

The project is inspired by the RCM formulation proposed in:

**Aghakhani, Geravand, Shahriari, Vendittelli, Oriolo — “Task Control with Remote Center of Motion Constraint for Minimally Invasive Robotic Surgery”, ICRA 2013.**

The central idea is to model the RCM point as a variable point lying on the tool axis. In this project, the RCM point is written as:

```text
p_RCM(q, λ) = p_tool_base(q) + λ · L_tool · z_tool(q)
```

where:

* `q` is the vector of robot joint variables;
* `λ` is a scalar penetration variable;
* `L_tool` is the needle/tool length;
* `z_tool(q)` is the tool axis direction;
* `p_tool_base(q)` is the base point of the tool;
* `p_RCM(q, λ)` is the point on the needle constrained to coincide with the desired RCM.

This follows the idea that the RCM point is not fixed on the tool, but can move along the tool axis as the penetration changes.

The controller is implemented as an extended kinematic task:

```text
[ task velocity ]   [ J_task   0 ] [ q_dot      ]
[ RCM velocity  ] = [ J_RCM      ] [ lambda_dot ]
```

The system is solved using a damped least-squares pseudoinverse. A null-space contribution is also used to keep the penetration variable close to a desired value and to avoid poor joint configurations.

---

## Why the skull model was removed

An earlier version of the project included a simplified skull/no-go sphere and a skull-avoidance term. This was later removed intentionally.

The reason is that, for the purpose of this exam project, the skull model was considered **over-engineered** with respect to the actual goal of the assignment. The project is about implementing and demonstrating **multi-RCM kinematic control**, not about developing a full anatomical collision-avoidance planner.

The reference paper also focuses on the mathematical and control formulation of the RCM constraint. It assumes the trocar/entry point is known and uses it as a kinematic constraint. It does not require a detailed skull model or a full collision avoidance system around the patient anatomy.

Keeping the skull introduced additional complexity that made the simulation harder to debug and less clear visually. In particular, it mixed three different problems:

1. RCM constraint satisfaction;
2. needle insertion through the trocar;
3. collision avoidance with a simplified anatomical obstacle.

For this project, the priority is to clearly show that:

* the needle enters through the entry point;
* the RCM can be imposed at the entry point;
* the RCM can be shifted conceptually to the target point;
* small conical motions can be generated while respecting the chosen RCM behavior.

Therefore, the final version removes the skull and keeps only the geometric elements strictly needed to explain and test the double-RCM controller: the robot, the needle, the trocar/entry point, the target point and the entry-target line.

This makes the simulation cleaner, more robust, and more aligned with the expected learning objectives of the project.

---

## Implemented tasks

The simulation provides multiple task modes, selectable from the keyboard.

### Task 1 — Entry-RCM with tip target

The robot controls the needle so that:

* the RCM point remains fixed at the entry point;
* the needle tip moves toward the deep target.

This corresponds to the standard neurosurgical insertion idea: the needle/tool must pass through the trocar while reaching the internal target.

---

### Task 2 — Target-RCM with conical motion at the entry side

In this mode:

* the needle tip stays fixed on the target;
* the target behaves as the effective RCM;
* a point on the entry side of the needle performs a small conical motion.

This reproduces the idea described in the project statement: in some phases, it can be useful to place the RCM at the target while allowing a small conical motion at the entry side.

---

### Task 3 — Insertion sequence through the entry point

This is the main surgical insertion sequence.

The desired behavior is:

```text
needle starts from above
→ needle aligns with the entry-target line
→ tip passes through the entry point
→ needle inserts toward the target
→ RCM remains at the entry point during insertion
```

The important point is that the needle tip must enter from the trocar/entry point. The final version is tuned so that the needle does not approach the target from below or from an unrealistic direction.

---

### Task 4 — Entry-RCM with tip cone around the target

In this mode:

* the RCM remains fixed at the entry/trocar point;
* the tip performs a small circular/conical motion around the target.

This is useful to show the opposite behavior of Task 2:

* Task 2: target fixed, entry side moves in a cone;
* Task 4: entry fixed, tip moves in a cone around the target.

---

## Unity scene structure

The scene is generated procedurally by:

```text
Project4SceneBuilder.cs
```

This script creates:

* the ROSA-like robot root object;
* the entry point marker;
* the target point marker;
* the nominal entry-target line;
* lights;
* ground plane;
* pedestal;
* camera.

The robot controller is implemented in:

```text
ROSADoubleRCMController.cs
```

The keyboard-only free camera is implemented in:

```text
FreeFlyCameraKeyboard.cs
```

---

## Main scripts

### `Project4SceneBuilder.cs`

This script builds the complete scene at runtime.

It defines the default positions of:

```csharp
entryPoint
targetPoint
robot.position
```

These values can be changed in the Inspector or directly in the script.

The current version has the skull disabled:

```csharp
createSkull = false;
```

This is intentional and part of the final project scope.

---

### `ROSADoubleRCMController.cs`

This is the main controller.

It contains:

* DH parameters;
* forward kinematics;
* numerical Jacobian computation;
* damped least-squares inverse kinematics;
* RCM point computation;
* insertion logic;
* target-RCM task;
* entry-RCM task;
* conical motion generation;
* overlay information;
* CSV logging.

The DH table is intentionally simple and replaceable. The robot is not meant to be an exact ROSA replica, but a plausible 6-DOF ROSA-like manipulator suitable for demonstrating the control strategy.

---

### `FreeFlyCameraKeyboard.cs`

This script controls the camera using only the keyboard and draws a small overlay with the available camera commands.

Controls:

```text
W / S          forward / backward
A / D          left / right
Q / E          down / up
Arrow left/right   yaw
Arrow up/down      pitch
Shift          faster movement
Ctrl           slower movement
F              reset camera
H              show/hide camera overlay
```

---

## Keyboard controls

Robot/task controls:

```text
1      Entry-RCM + tip target
2      Target-RCM + entry-side cone
3      Insertion sequence
4      Entry-RCM + tip cone around target
0      Hold
Space  Pause / resume controller
C      Enable / disable cone animation
R      Reset robot state
L      Enable / disable CSV logging
```

Camera controls:

```text
W/S    Move forward/backward
A/D    Move left/right
Q/E    Move down/up
Arrows Rotate camera
Shift  Move faster
Ctrl   Move slower
F      Reset camera
H      Toggle camera overlay
```

---

## Setup instructions

1. Create a new Unity 3D project.
2. Copy the scripts into:

```text
Assets/Scripts/
```

3. Create an empty GameObject in the scene.
4. Attach:

```text
Project4SceneBuilder.cs
```

5. Press Play.

The scene should be generated automatically.

If the keyboard controls do not work, check:

```text
Edit > Project Settings > Player > Active Input Handling
```

Set it to either:

```text
Both
```

or:

```text
Input Manager (Old)
```

because the scripts use Unity’s old `Input.GetKey` API.

---

## Notes about coordinate frames

Unity uses a **Y-up** world frame, while the DH convention usually assumes the robot vertical axis along **Z**.

For this reason, the robot root is rotated so that the DH vertical axis is mapped consistently into Unity’s vertical direction.

This is important because otherwise the robot would visually develop along the horizontal plane instead of standing upright.

---

## Current limitations

This project is a simplified academic simulation. It is not a clinically accurate surgical simulator.

Main limitations:

* the robot is ROSA-like, not an exact ROSA CAD model;
* the DH parameters are plausible but not official;
* the needle is modeled as a simple rigid cylinder;
* the skull/anatomical model is intentionally removed;
* no force control is implemented;
* no real collision detection is used;
* the controller is purely kinematic;
* Jacobians are computed numerically for simplicity.

These limitations are acceptable for the purpose of the project, because the focus is the implementation and visualization of double-RCM kinematic control.

---

## Final design choice

The final version focuses on the core objective of the assignment:

```text
implement a 3D and kinematic model of a ROSA-like robot in Unity
and demonstrate double-RCM behavior through kinematic control.
```

The skull was removed because it made the simulation unnecessarily complex and less readable for the exam. The final scene is therefore simpler, but better aligned with the theoretical contribution of the reference paper and with the learning objectives of the project.

The key result is that the simulation clearly shows:

* standard entry-point RCM;
* insertion through the trocar;
* target-side RCM behavior;
* conical motion around the selected RCM;
* a replaceable kinematic model suitable for future refinement.

