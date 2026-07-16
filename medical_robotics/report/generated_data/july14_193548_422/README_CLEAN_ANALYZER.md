# Clean Multi-RCM log analysis

This output folder was generated with `analyze_rcm_logs_clean.py`.

Unlike the previous analyzer, this version does **not** put all errors into a single plot.  
Each task metric is plotted separately, with a threshold line and a max-value box.

## Recommended images for the presentation

- `00_task_timeline.png` — executed task sequence.
- `01_active_task_error_only.png` — only the active controller error, not all errors together.
- `02_T3_entry_rcm_error.png` — Entry-RCM stability during safe insertion.
- `03_T3_skull_violation.png` — skull/corridor safety.
- `04_T2_entry_cone_error.png` — cone tracking in Task 2.
- `05_T2_tip_target_error.png` — tip stability in Task 2.
- `06_T4_entry_rcm_error.png` — Entry-RCM stability in Task 4.
- `07_T4_tip_cone_error.png` — tip-cone tracking in Task 4.
- `99_validation_summary_normalized.png` — compact pass/fail-style summary normalized by thresholds.

## Tables

- `pass_fail_checks.csv` — validation checks.
- `summary_by_task_phase.csv` — detailed task/phase statistics.
- `combined_logs.csv` — merged input logs.
- `slide_summary_clean_en.md` — text ready to paste into slides or notes.
