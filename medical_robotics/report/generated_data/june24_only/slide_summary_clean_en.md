# Clean slide-ready results

## Main message
- The analyzer now avoids the confusing all-errors plot.
- Each figure focuses on one task metric, with its own threshold and maximum value.
- This makes the plots easier to explain during the presentation.

## Key numbers
- Task 1 — max Entry-RCM error: 0.0 mm; max tip-target error: 0.1 mm.
- Task 2 — max entry-cone error: 18.7 mm; max tip-target error: 3.6 mm.
- Task 3 — max insertion Entry-RCM error: 8.6 mm; max skull violation: 0.0 mm; final target error: 0.2 mm.
- Task 4 — max Entry-RCM error: 10.2 mm; max tip-cone error: 42.0 mm.

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
- PASS: T1 entry RCM stability — max entry_rcm_error_mm = 0.0 mm, threshold 10.0 mm.
- PASS: T1 tip reaches target — max tip_target_error_mm = 0.1 mm, threshold 25.0 mm.
- PASS: T2 entry-cone tracking — max task2_entry_cone_error_mm = 18.7 mm, threshold 50.0 mm.
- PASS: T2 tip fixed at target — max tip_target_error_mm = 3.6 mm, threshold 25.0 mm.
- PASS: T3 insertion Entry-RCM — max entry_rcm_error_mm = 8.6 mm, threshold 10.0 mm.
- PASS: T3 final target — max tip_target_error_mm = 13.7 mm, threshold 25.0 mm.
- PASS: T3 skull/corridor safety — max skull_violation_mm = 0.0 mm, threshold 0.0 mm.
- FAIL: T4 Entry-RCM stability — max entry_rcm_error_mm = 10.2 mm, threshold 10.0 mm.
- PASS: T4 tip-cone tracking — max task4_tip_cone_error_mm = 42.0 mm, threshold 50.0 mm.