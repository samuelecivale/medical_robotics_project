# Multi-RCM for a ROSA-Inspired Neurosurgical Robot

Unity project developed for the **Medical Robotics** course.

This project implements a simplified **ROSA-inspired neurosurgical robotic system** and a kinematic controller for a **double Remote Center of Motion (RCM)** task.

The goal of the assigned project is to study a multi-RCM control strategy for a ROSA-like neurosurgical robot. In the real ROSA system, the RCM is normally located at the skull entry point. In some phases of the procedure, however, it may be useful to also consider an RCM located at the internal target point, while allowing a small conical motion around the entry point.

This Unity implementation focuses on the main functional aspects of the assignment:

- building a 3D and kinematic model of a ROSA-inspired robot;
- defining an entry point on the skull surface;
- defining a target point inside the skull;
- implementing single-RCM and double-RCM kinematic control;
- visualizing and logging the RCM errors during simulation.

---

## Project Overview

The implemented scene contains:

- a fixed base;
- a vertical column;
- a compact serial robotic arm;
- a wrist / distal tool guide;
- a surgical tool axis;
- a simplified transparent skull;
- an entry point on the skull surface;
- a target point inside the skull;
- visual debugging elements for the RCM constraints.

The robot is **not an exact CAD reconstruction of the commercial ROSA robot**. It is a **ROSA-inspired kinematic model**, designed to reproduce the functional elements required for the assignment.

---

## Implemented Features

### 1. ROSA-Inspired 3D Scene

The scene is generated automatically from code.

The main scene builder creates:

- robot base and vertical support;
- serial manipulator links and joints;
- distal surgical tool guide;
- tool frame;
- needle / surgical tool axis;
- transparent skull;
- entry point;
- target point;
- entry-target trajectory line;
- camera and lighting setup.

Relevant file:

```text
Assets/Scripts/BuildROSAStyleRCMScene.cs
```

> Note: if the file is currently named `BuildROSAStyleRCMS.cs`, it should preferably be renamed to `BuildROSAStyleRCMScene.cs`, because in Unity the C# file name should match the public MonoBehaviour class name.

---

### 2. Double RCM Kinematic Controller

The main controller is implemented in:

```text
Assets/Scripts/DoubleRCMUnityController.cs
```

The controller supports three operating modes:

| Mode | Description |
|---|---|
| Entry RCM | The tool axis is constrained to pass through the skull entry point. |
| Target RCM | The tool axis is constrained to pass through the internal target point. |
| Double RCM | Both the entry point and the target point are considered simultaneously. |

The control objective is formulated as a point-to-line error minimization problem. The controller tries to minimize the distance between each RCM point and the current surgical tool axis.

The inverse kinematics update is computed iteratively using:

- numerical Jacobian estimation;
- damped least squares;
- incremental joint rotations;
- error feedback from the selected RCM constraints.

---

### 3. Visual Debugging

The visualizer is implemented in:

```text
Assets/Scripts/RCMVisualizer.cs
```

It displays:

- the tool axis;
- the entry point;
- the target point;
- the error line from the entry point to the tool axis;
- the error line from the target point to the tool axis.

This makes it easier to understand whether the RCM constraints are being respected during the simulation.

---

### 4. Clean Robot Version

A simplified version of the robot builder is implemented in:

```text
Assets/Scripts/BuildCleanRCMRobot.cs
```

This version is useful for:

- testing the RCM controller in a cleaner scene;
- debugging the kinematic chain;
- checking the behavior of the controller without the full ROSA-inspired environment.

---

## Repository Structure

The most relevant files are:

```text
Assets/
└── Scripts/
    ├── BuildROSAStyleRCMScene.cs
    ├── DoubleRCMUnityController.cs
    ├── RCMVisualizer.cs
    └── BuildCleanRCMRobot.cs
```

Generated Unity folders such as `Library/`, `Temp/`, `Logs/`, and `UserSettings/` are not required in the repository and should not be committed.

A recommended `.gitignore` should exclude:

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

## How to Run the Project

### 1. Open the Unity Project

Open the Unity project folder from Unity Hub:

```text
medical_robotics
```

---

### 2. Create the Robot Root Object

In the Unity Hierarchy:

```text
Right click -> Create Empty
```

Rename the object:

```text
RobotRoot
```

---

### 3. Add the Scene Builder

Select `RobotRoot`, then add the component:

```text
BuildROSAStyleRCMScene
```

Then open the component menu and select:

```text
Build ROSA-like RCM Scene
```

This automatically creates the full simulation scene.

---

### 4. Run the Simulation

Press:

```text
Play
```

The robot starts solving the RCM task.

---

## Controls

During Play Mode:

```text
1 = Entry RCM mode
2 = Target RCM mode
3 = Double RCM mode
R = Reset robot pose
```

The current mode and the RCM errors are displayed on screen.

---

## CSV Logging

During simulation, the controller logs the RCM errors to a CSV file.

The file is saved in:

```text
RCM_logs/rcm_log.csv
```

This path is relative to the Unity project root.

For example:

```text
medical_robotics/RCM_logs/rcm_log.csv
```

The CSV contains:

```text
time
mode
entry_error_mm
target_error_mm
tool_x
tool_y
tool_z
entry_x
entry_y
entry_z
target_x
target_y
target_z
```

This allows quantitative comparison between:

- Entry RCM mode;
- Target RCM mode;
- Double RCM mode.

---

## Suggested Test Procedure

A useful test sequence is:

```text
1. Press Play.
2. Let the robot run in Double RCM mode for a few seconds.
3. Press 1 and observe Entry RCM behavior.
4. Press 2 and observe Target RCM behavior.
5. Press 3 and return to Double RCM mode.
6. Observe the entry and target errors on screen.
7. Stop Play Mode.
8. Inspect the generated CSV log.
```

---

## Technical Description

The RCM constraint is modeled as a point-to-line minimization problem.

For a point `p` and a tool axis defined by:

```text
a = tool frame position
d = tool frame forward direction
```

the closest point on the tool axis is:

```text
closest = a + dot(p - a, d) d
```

The RCM error vector is:

```text
e = p - closest
```

The controller computes this error for the selected RCM points.

In Double RCM mode, the stacked error vector contains both:

```text
entry point error
target point error
```

The joint update is computed using damped least squares:

```text
dq = - J^T (J J^T + lambda^2 I)^-1 e
```

where:

- `J` is the numerical Jacobian;
- `lambda` is the damping coefficient;
- `e` is the stacked RCM error vector;
- `dq` is the joint update.

---

## Limitations

This project is a simplified educational simulation.

Current limitations:

- the model is ROSA-inspired, not an exact CAD reconstruction of the real ROSA robot;
- the robot geometry is manually generated using Unity primitives;
- joint limits are not modeled in detail;
- collision avoidance is not implemented;
- the skull is represented by a simplified transparent ellipsoid;
- the controller uses a numerical Jacobian rather than a full analytical robot model.

Despite these simplifications, the implementation captures the core learning objectives of the assignment:

- building a 3D robotic system in Unity;
- defining RCM constraints;
- implementing kinematic control;
- comparing single-RCM and double-RCM behavior.

---

## Possible Future Improvements

Possible extensions include:

- adding realistic joint limits;
- adding collision checking with the skull;
- improving the ROSA-inspired geometry with a more accurate 3D model;
- adding a constrained optimization controller;
- explicitly modeling the admissible conical motion around the entry point;
- plotting the logged RCM errors over time;
- adding a more realistic surgical trajectory planning module.

---

## Short Description for the Presentation

This project presents a Unity simulation of a ROSA-inspired neurosurgical robot for a double RCM task.

The implemented system includes a simplified robotic arm, a surgical tool axis, a transparent skull, an entry point on the skull surface, and an internal target point.

The controller supports Entry RCM, Target RCM, and Double RCM modes using numerical Jacobian inverse kinematics with damped least squares.

The simulation also visualizes and logs the RCM errors, allowing qualitative and quantitative comparison of the different control modes.

---

## Authors

Project developed for the **Medical Robotics** course.

Students:

```text
[Samuele Civale]
[Alexandru Vivian Pita]
[Francesca Forghieri]
```

