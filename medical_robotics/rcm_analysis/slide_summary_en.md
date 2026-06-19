# Slide-ready results

## Main message
- The controller evaluates four kinematic behaviours: Entry-RCM, Target-RCM with entry cone, safe insertion, and tip cone with Entry-RCM.
- The generated figures support the presentation with tracking errors, RCM stability, insertion-line alignment and skull/corridor safety.

## Key numbers
- Task 1 — max Entry-RCM error: 0.0 mm; max tip-target error: 0.2 mm.
- Task 2 — max entry-cone error: 25.5 mm; max tip-target error: 14.3 mm.
- Task 3 — max insertion RCM error: 8.9 mm; max skull violation: 0.0 mm; final target error: 0.3 mm.
- Task 4 — max Entry-RCM error: 9.0 mm; max tip-cone error: 27.7 mm.

## Suggested figures
- 00_task_timeline.png: executed task sequence.
- 01_tracking_errors.png: global error comparison.
- 02_insertion_detail.png: safety and alignment during insertion.
- 03_task2_cone.png and 04_task4_cone.png: cone tracking quality.

## Automatic checks
- PASS: T1 entry RCM stability — max entry_rcm_error_mm = 0.0 mm, threshold 10.0 mm.
- PASS: T1 tip reaches target — max tip_target_error_mm = 0.2 mm, threshold 25.0 mm.
- PASS: T2 cone tracking — max task2_entry_cone_error_mm = 25.5 mm, threshold 50.0 mm.
- PASS: T2 tip fixed at target — max tip_target_error_mm = 14.3 mm, threshold 25.0 mm.
- PASS: T3 insertion entry RCM — max entry_rcm_error_mm = 8.9 mm, threshold 10.0 mm.
- PASS: T3 final target — max tip_target_error_mm = 14.2 mm, threshold 25.0 mm.
- PASS: T3 skull/corridor safety — max skull_violation_mm = 0.0 mm, threshold 0.0 mm.
- PASS: T4 entry RCM stability — max entry_rcm_error_mm = 9.0 mm, threshold 10.0 mm.
- PASS: T4 tip cone tracking — max task4_tip_cone_error_mm = 27.7 mm, threshold 50.0 mm.