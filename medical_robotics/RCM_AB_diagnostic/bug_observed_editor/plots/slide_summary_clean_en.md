# Clean slide-ready results

## Main message
- The analyzer now avoids the confusing all-errors plot.
- Each figure focuses on one task metric, with its own threshold and maximum value.
- This makes the plots easier to explain during the presentation.

## Key numbers
- Task 1 — max Entry-RCM error: 0.0 mm; max tip-target error: 0.1 mm.
- Task 2 — max entry-cone error: 33.9 mm; max tip-target error: 65.5 mm.
- Task 3 — max insertion Entry-RCM error: 9.4 mm; max skull violation: 0.0 mm; final target error: 0.1 mm.
- Task 4 — max Entry-RCM error: n/a; max tip-cone error: n/a.

## Recommended figures
- 00_task_timeline.png
- 01_active_task_error_only.png
- 02_T3_entry_rcm_error.png
- 03_T3_skull_violation.png
- 04_T2_entry_cone_error.png
- 05_T2_tip_target_error.png
- 06_T4_entry_rcm_error.png
- 07_T4_tip_cone_error.png
- 99_validation_summary_normalized.png

## Automatic checks
- PASS: T1 entry RCM stability — max entry_rcm_error_mm = 0.0 mm, threshold 2.0 mm.
- PASS: T1 tip reaches target — max tip_target_error_mm = 0.1 mm, threshold 3.0 mm.
- PASS: T2 entry-cone tracking — max task2_entry_cone_error_mm = 33.9 mm, threshold 50.0 mm.
- FAIL: T2 tip fixed at target — max tip_target_error_mm = 65.5 mm, threshold 3.0 mm.
- PASS: T3 insertion Entry-RCM — max entry_rcm_error_mm = 0.4 mm, threshold 2.0 mm.
- PASS: T3 final target — max tip_target_error_mm = 2.9 mm, threshold 3.0 mm.
- PASS: T3 skull/corridor safety — max skull_violation_mm = 0.0 mm, threshold 0.0 mm.