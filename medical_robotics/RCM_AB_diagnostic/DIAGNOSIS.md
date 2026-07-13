# Task 2 q4 pinning — diagnostic result

## Verdict

The q4 pinning is driven by the **primary Task 2 solve**, not by the projected
nullspace contribution. The evidence is consistent with the controller crossing a
near-singular / multiple-IK-branch region of the primary two-point task.

No gain, damping, speed limit, or threshold was changed as a proposed fix.

## Bug-observed Editor run

Source CSV: `bug_observed_editor/bug.csv`

- Task 2 local time 30.013 s: q4 = -149.302 deg, tip error = 8.498 mm,
  raw primary qdot norm = 0.36112 rad/s, nullspace contribution = 0.00754 rad/s.
- Task 2 local time 30.981 s: q4 = -169.287 deg, tip error = 5.249 mm,
  raw primary qdot norm = 0.40343 rad/s, nullspace contribution = 0.00707 rad/s.
- q4 first reaches -170 deg at 31.041 s local.
- Task 2 local time 32.004 s: q4 = -170 deg, tip error = 42.717 mm,
  raw primary qdot norm = 0.80304 rad/s, nullspace contribution = 0.00642 rad/s.
- Peak raw primary norm = 0.97109 rad/s at 32.543 s; the nullspace contribution
  at that frame is only 0.00634 rad/s.
- Final recorded sample: tip error = 65.491 mm, raw primary norm = 0.90475 rad/s,
  nullspace contribution = 0.00621 rad/s.
- `boosted_damping` remains 0.055 throughout Task 2; the singularity guard does
  not activate in this episode.

Plots:

- `bug_observed_editor/plots/05_T2_tip_target_error.png`
- `bug_observed_editor/plots/14_joint_angles_diagnostic.png`
- `bug_observed_editor/plots/15_nullspace_vs_task_diagnostic.png`

## Matched A/B run

Both runs use the posture history observed in the bug run: Task 3 for 27 s,
Task 1 for 5.6 s, then Task 2 for 120 s at deterministic 15 Hz.

| Metric | Nullspace ON | Nullspace OFF |
|---|---:|---:|
| Max tip-target error | 13.565 mm | 13.841 mm |
| q4 minimum | -92.024 deg | -96.132 deg |
| Peak raw primary qdot norm | 0.57356 rad/s | 0.57349 rad/s |
| Peak nullspace contribution | 0.00482 rad/s | 0 rad/s |
| Samples with tip error > 10 mm | 300 | 317 |

Disabling the nullspace does not remove or attenuate the periodic primary-task
degradation; it is marginally worse in this deterministic pair.

Inputs and plots:

- `../RCM_AB_diagnostic_matched/control/control.csv`
- `../RCM_AB_diagnostic_matched/nullspace_off/nullspace_off.csv`
- `../RCM_AB_diagnostic_matched/control/plots/05_T2_tip_target_error.png`
- `../RCM_AB_diagnostic_matched/nullspace_off/plots/05_T2_tip_target_error.png`

## Primary Jacobian conditioning

Task 2 has one structurally zero singular value because two points on the same
rigid shaft provide five independent pose constraints; rotation about the shaft
axis remains free. Therefore the useful diagnostic is the smallest **nonzero**
singular value and the effective condition number on the controllable subspace.

In the matched run:

- At Task 2 start: sigma_min_nonzero = 0.0183542, effective condition = 137.442.
- At 18.867 s local: sigma_min_nonzero = 0.0000302, effective condition = 78690.561.
- At 31.000 s local, immediately around the cone phase where the Editor run pins
  q4: sigma_min_nonzero = 0.0001069, effective condition = 22210.138.
- The conditioning spikes repeat with the cone motion.

This abrupt loss of conditioning before the branch degradation supports a
near-singular / IK-branch transition. It is not consistent with a flat or spiking
nullspace term driving q4 into its limit.

Conditioning evidence:

- `../RCM_AB_diagnostic_matched/conditioning/control_with_effective_conditioning.csv`
- `../RCM_AB_diagnostic_matched/conditioning/plots/16_task2_jacobian_conditioning.png`
