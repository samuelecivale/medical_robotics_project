# Report Generation Notes

This file documents how `report/report.tex` / `report/report.pdf` were produced, exactly
what was inspected, what (if anything) was changed in the analysis code, and how the
numerical claims in the report were verified against the selected experimental log.

## Final experimental source

```
RCM_logs/project4_multircm_20260714_193548_422.csv
```

2696 samples, `time_s` range `[0.0696, 157.258]` s (duration 157.188 s). This is the
**only** file used for every statistic, table, figure, and pass/fail claim in the report.
The file itself was never modified.

## Excluded experimental logs

All other `RCM_logs/project4_multircm_*.csv` files present in the repository were
excluded from the quantitative analysis, including but not limited to:

```
RCM_logs/project4_multircm_20260619_222549_881.csv
RCM_logs/project4_multircm_20260624_092806_475.csv
RCM_logs/project4_multircm_20260711_164931_583.csv
RCM_logs/project4_multircm_20260713_121038_210.csv   (and 8 more from 2026-07-13)
RCM_logs/project4_multircm_20260714_191638_723.csv
RCM_logs/project4_multircm_20260714_192703_100.csv
RCM_logs/project4_multircm_20260714_192914_035.csv
RCM_logs/project4_multircm_20260714_193131_160.csv
RCM_logs/project4_multircm_20260714_193531_895.csv
RCM_logs/project4_multircm_20260714_194542_200.csv
RCM_logs/project4_multircm_20260714_194742_070.csv
RCM_logs/project4_multircm_20260714_195022_983.csv
```

None of these were used for statistics, plots, tables, or claims, and no "pick the newest
file" or other automatic-selection logic was used anywhere in the pipeline: the selected
CSV path is hard-coded (as a Python string constant) in
`report/scripts/generate_final_run_metrics.py`.

Pre-existing folders `report/generated_data/{june19_only,june24_only,july13_v2,july14}/`
and `report/generated_figures/{june19_only,june24_only,july13_v2,july14}/` are earlier
analysis runs from prior development iterations (some on different logs, one — `july14`
— on a `combined_logs.csv` whose sole `log_file` value happens to already be the selected
CSV). None of these pre-existing folders were read as a numeric source for this report;
everything was regenerated fresh into `july14_193548_422/` folders as instructed.

## Files inspected

**C# scripts** (all project-authored scripts in the repository; Unity-generated folders
`Library/`, `Logs/`, `Temp/`, `UserSettings/`, `obj/`, `bin/` were not inspected, per the
task brief):
- `Assets/Scripts/ROSADoubleRCMController.cs` (1656 lines) — read in full (two passes,
  lines 1–1169 and 1170–1656).
- `Assets/Scripts/Project4SceneBuilder.cs` (201 lines) — read in full.
- `Assets/Scripts/FreeFlyCameraKeyboard.cs` (139 lines) — read in full.
- `Assets/Editor/RCMDiagnosticPlayerBuilder.cs` (28 lines) — read in full.
- `Assets/TutorialInfo/Scripts/{Readme.cs,Editor/ReadmeEditor.cs}` — located via search,
  confirmed to be Unity's default "Readme" template boilerplate, not project logic; not
  discussed in the report.

**Python scripts:**
- `analyze_rcm_logs_clean.py` (repository root, 698 lines) — read in full; used
  unmodified (see "Code changes" below).
- `RCM_logs/analyze_rcm_logs_clean.py` (577 lines) — diffed against the root copy;
  confirmed to be an older, shorter duplicate (no settle-time exclusion, no joint-angle /
  nullspace / conditioning diagnostic plots). Not used for this report; noted in the
  report (Appendix A) as historical.
- `report/scripts/generate_final_run_metrics.py` — new wrapper written for this report
  (see "Code changes").

**Scene / project files:**
- `Assets/Scenes/Scena principale.unity` (540 lines) — read in full. Confirmed the scene
  contains a single authored `Project4SceneBuilder` MonoBehaviour with serialized fields
  `buildOnStart=1, addFreeFlyCamera=1, createSkull=1, entryPoint=(0.624,1.629,0.161),
  targetPoint=(0.703,1.55,0.161), skullRadius=0.118`. Note `createSkull=1` here overrides
  the C# field default of `false` in `Project4SceneBuilder.cs` — a skull sphere **is**
  instantiated in the actual scene, even though the controller's `useSkullAvoidance` flag
  is separately hard-coded to `false` in `Project4SceneBuilder.Build()`. Both facts are
  reported explicitly in report Sections 4.1 and 11. The scene also contains two dangling
  `MonoBehaviour` references (`MainCameraKeyboardFreeFly`, `CameraControlsOverlay`) with
  no corresponding `.cs` file anywhere in the repository (orphaned/legacy script
  references on the Main Camera GameObject); these do not correspond to any executable
  code and are not discussed in the report body.

**Figures / images:**
- `PresentationAssets/unity_scene_wide.jpg` — inspected visually, used as the Unity
  overview figure (report Fig. 1).
- `Recordings/video_intera_pipeline.mp4` (65 MB) — file located and its role understood
  (a recording of the full pipeline); not decoded frame-by-frame for this report (no
  frame was extracted), and, per the task brief, it was in any case never intended to be
  used as quantitative evidence.
- All 22 PNGs generated by the analysis wrapper into
  `report/generated_figures/july14_193548_422/` were inspected visually before selection.

**Data files:**
- `RCM_logs/project4_multircm_20260714_193548_422.csv` — full column list inspected;
  segmented by `(task, phase)`; every numeric claim in the report traces to this file.
- Pre-existing `report/generated_data/july14/*` and `RCM_analysis_v2/`,
  `RCM_analysis_clean/`, `rcm_analysis/` folders were listed and spot-checked for
  provenance/format understanding only; none of their numeric content was copied into
  the report.

**Paper:**
- `ICRA13_RCM.pdf` — read in full (6 pages), including all figures and the reference
  list. Bibliographic data (title, authors, venue, dates, page numbers) verified both
  from the PDF's embedded metadata (`pdfinfo`) and from the rendered title page/footer,
  which agree.

## Analysis procedure

1. **Task/phase segmentation**: computed directly from the `task` and `phase` columns of
   the selected CSV by detecting contiguous runs (`task != task.shift() or phase !=
   phase.shift()`), not reused from any other run's segmentation or plots. Result:
   7 segments (`T3_safe_insertion/ApproachEntry`, `.../PierceEntry`,
   `.../InsertToTarget`, `.../Done`, `T1_entry_rcm_tip_target`, `T2_target_rcm_entry_cone`,
   `T4_entry_rcm_tip_cone` — the last three all logged with `phase=Done`, which is a
   leftover/unused value for non-Task-3 modes, documented explicitly in the report).
2. **Filters / inactive-channel handling**: `tip_target_error_mm` is not reported as a
   meaningful metric during `ApproachEntry`/`PierceEntry` (the tip is not tracking the
   deep target during those phases); `skull_violation_mm` and diagnostic Jacobian-
   conditioning columns are reported only where genuinely logged (the latter are absent
   from the current CSV schema entirely, confirmed by header inspection, and the
   corresponding analyzer plot functions were confirmed to silently no-op).
3. **Settle-time exclusion**: following `analyze_rcm_logs_clean.py`'s own convention
   (`--settle-time-s`, default 1.0 s), pass/fail statistics for entry-RCM and tip-target
   errors exclude the first 1 s after each task/phase switch. Both the full-phase
   (transient-inclusive) maximum and the settled statistic are reported side by side in
   `final_metrics_table.csv` and in the report text, per the task brief.
4. **Unit conversions**: all positional errors are already logged in millimetres by the
   C# controller (`* 1000f` conversions in `AppendLogIfNeeded`); no additional conversion
   was applied. Angles are already logged in degrees. The report additionally derives, by
   hand from the logged `entry_{x,y,z}`/`target_{x,y,z}` columns and from documented
   controller constants, two geometric quantities not directly present as CSV columns:
   entry-target depth (111.7 mm) and the Task-2 commanded cone radius
   (`depth * tan(4.5°)` = 8.79 mm); both are re-derived and checked in the verification
   table below.
5. **Statistics**: sample count, mean, RMS, standard deviation (population, ddof=0),
   median, 95th percentile, maximum, and final (last-sample) value, computed with
   `numpy`/`pandas` directly on the filtered subset for each task/phase/metric row.
6. **Pass/fail logic**: `max <= threshold` on the (optionally settled) subset, using only
   thresholds that exist in the controller source or its CSV comment header
   (`entryRcmOkThresholdMm = 2.0 mm`, `tipTargetOkThresholdMm = 3.0 mm`, skull
   `= 0.0 mm`); no threshold was invented. The 50 mm cone-tracking reference line is
   explicitly flagged in the report as a script-default (`analyze_rcm_logs_clean.py`
   `--cone-threshold-mm`), not a clinical/controller-defined threshold, and cone-tracking
   rows are reported with threshold `"--"` (no pass/fail verdict) rather than being
   silently scored against it.
7. **Figure generation**: the unmodified `analyze_rcm_logs_clean.py` was run once, with
   the selected CSV as its *only* input file, producing 22 PNGs; 11 of these were
   selected for inclusion in the report body (see "Figures used" below), following the
   task brief's "select, don't include everything" instruction.

## Code changes

**No changes were made to `analyze_rcm_logs_clean.py` (repository root) or to
`RCM_logs/analyze_rcm_logs_clean.py`.** Both scripts are used/referenced exactly as they
already existed in the repository.

One new file was added:

- **`report/scripts/generate_final_run_metrics.py`** (new). This wrapper:
  1. Invokes the unmodified `analyze_rcm_logs_clean.py` as a subprocess, passing the
     single selected CSV path as its only input file (never a directory or glob), with
     `--out report/generated_data/july14_193548_422`, and with the analyzer's own
     `--entry-threshold-mm 2.0 --tip-threshold-mm 3.0 --skull-threshold-mm 0.0` flags
     set to the controller's actual thresholds and `--settle-time-s 1.0` (the analyzer's
     own default).
  2. Asserts that `combined_logs.csv`'s `log_file` column contains only the selected
     CSV's filename (a guard against accidentally analyzing more than one file).
  3. Moves the 22 generated `.png` files from `generated_data/july14_193548_422/` into
     `report/generated_figures/july14_193548_422/`, leaving only tables/markdown in
     `generated_data/`.
  4. Computes `summary_overall.csv` and `final_metrics_table.csv` directly from the
     selected CSV with `pandas`/`numpy` (RMS, std, median, p95 — statistics the existing
     analyzer does not compute), and copies `final_metrics_table.csv` to
     `report/final_metrics_table.csv` as required by the task brief.

This script does not alter, overwrite, or otherwise modify
`RCM_logs/project4_multircm_20260714_193548_422.csv`.

**LaTeX**: `report/report.tex` was backed up verbatim to
`report/report_backup_before_final.tex` before any edit, then rewritten in full following
the mandated report structure. `report/generated_figures.zip` (a leftover archive from a
prior report iteration) and the pre-existing `report/generated_data/{...}` /
`report/generated_figures/{...}` sub-folders from earlier runs were left untouched.

## Figures used

All included in `report/report.tex`, sourced as follows:

| Figure (report) | File | Source |
|---|---|---|
| Fig. 1 (Unity overview) | `PresentationAssets/unity_scene_wide.jpg` | Pre-existing project asset; illustrative only, not tied to numeric results |
| Fig. 2 (RCM geometry) | drawn in-document with TikZ | Conceptual diagram, no external image |
| Fig. 3 (task timeline) | `generated_figures/july14_193548_422/00_task_timeline.png` | Selected CSV |
| Fig. 4 (Task 2 cone error) | `.../04_T2_entry_cone_error.png` | Selected CSV |
| Fig. 5 (Task 2 tip error) | `.../05_T2_tip_target_error.png` | Selected CSV |
| Fig. 6/7 (Task 3 entry-RCM / tip-target) | `.../02_T3_entry_rcm_error.png`, `.../02b_T3_tip_target_error.png` | Selected CSV |
| Fig. 8 (Task 3 skull violation) | `.../03_T3_skull_violation.png` | Selected CSV |
| Fig. 9a/b (Task 4 entry-RCM / tip-cone) | `.../06_T4_entry_rcm_error.png`, `.../07_T4_tip_cone_error.png` | Selected CSV |
| Fig. 10 (qdot norm) | `.../12_qdot_norm_rad_s.png` | Selected CSV |

Every `generated_figures/july14_193548_422/*.png` file was produced in this session by
`report/scripts/generate_final_run_metrics.py` calling the unmodified
`analyze_rcm_logs_clean.py` on exactly the selected CSV (verified via the `log_file`
assertion in the wrapper, see "Code changes").

## Numerical verification

Two independent verification passes were run (outside of, and after, generating
`final_metrics_table.csv`), each recomputing values directly from
`RCM_logs/project4_multircm_20260714_193548_422.csv` with a fresh `pandas` script using
its own segmentation logic (mirroring, but not calling, the wrapper script). At least
ten values are tabulated below; two real transcription errors were found and corrected
in `report.tex` (marked **FIXED**).

| # | Report location | Task | Phase | Metric | Reported value | Recomputed value | Unit | Result |
|---|---|---|---|---|---|---|---|---|
| 1 | Abstract / §7.1 | -- | whole run | sample count | 2696 | 2696 | samples | OK |
| 2 | §7.1 | -- | whole run | duration | 157.258 (end) / 157.188 (span) | 157.188 | s | OK |
| 3 | §8.3, Table 4 | T3 | InsertToTarget, settled | entry_rcm_error_mm mean | 0.179 | 0.179 | mm | OK |
| 4 | §8.3, Fig. 6/7 caption, Table 4 | T3 | InsertToTarget, settled | entry_rcm_error_mm max | 0.235 (initial draft) | **0.237** | mm | **FIXED** (draft value was the p95, not the max) |
| 5 | §8.3, Table 4 | T3 | Done, settled | entry_rcm_error_mm mean / max | 0.091 / 0.151 | 0.091 / 0.151 | mm | OK |
| 6 | §8.3 | T3 | Done | tip_target_error_mm final (last sample) | 0.467 | 0.467 | mm | OK |
| 7 | §8.3, Table 4 | T3 | Done, settled | tip_target_error_mm mean / max | 0.752 / 1.498 | 0.752 / 1.498 | mm | OK |
| 8 | §8.2, Table 4 | T2 | full task | task2_entry_cone_error_mm mean / max | 4.956 / 13.665 | 4.956 / 13.665 | mm | OK |
| 9 | §8.2 | T2 | settled | tip_target_error_mm mean / max | 0.005 / 0.009 | 0.005 / 0.009 | mm | OK |
| 10 | §5.2 footnote / §8.2 | T2 | full task | target_rcm_error_mm mean / std | 1.42 / 0.003 | 1.42 / 0.003 | mm | OK |
| 11 | §8.4, Table 4 | T4 | settled | entry_rcm_error_mm mean / max | 0.353 / 0.834 | 0.353 / 0.834 | mm | OK |
| 12 | §8.4, Table 4 | T4 | settled | task4_tip_cone_error_mm mean / max | 3.194 / 7.109 | 3.194 / 7.109 | mm | OK |
| 13 | §8.4 | T4 | full task | tip_target_error_mm (radius tracked) mean | 13.03 | 13.03 | mm | OK |
| 14 | §8.5 | -- | whole run | qdot_norm_rad_s mean / max | 0.063 / 1.25 | 0.063 / 1.25 | rad/s | OK |
| 15 | §8.5 | -- | whole run | lambda min | 0.603 (initial draft) | **0.601** | -- | **FIXED** |
| 16 | §8.5 | -- | whole run | fps min / max / mean | 5.8 / 213.0 / 58.4 | 5.82→5.8 / 213.04→213.0 / 58.37→58.4 | Hz | OK (report already rounds to 1 dp) |
| 17 | §6.1 | T2 | -- | entry-target depth $d_{ET}$ | 111.7 | 111.7 | mm | OK (hand-derived from entry/target columns) |
| 18 | §6.1 | T2 | -- | commanded cone radius $r_2$ | 8.79 | 8.79 | mm | OK ($d_{ET}\tan 4.5°$) |
| 19 | §5.6 | -- | -- | max joint speed in rad/s | 1.396 | 1.396 | rad/s | OK ($80°\times\pi/180$) |
| 20 | §8.3 | T3 | InsertToTarget+Done | tool_axis_angle_deg median / p95 | 1.08 / 10.6 | 1.08 / 10.6 | deg | OK |
| 21 | §8.3 | T3 | InsertToTarget+Done | shaft_line_max_distance_mm max | 286.3 | 286.3 | mm | OK |
| 22 | §8.3 | T3 | all phases | skull_violation_mm max / n | 0.0 / 490 | 0.0 / 490 | mm / samples | OK |
| 23 | §8.6, Table 4 | T1 | full task | entry_rcm_error_mm mean / max | 0.076 / 0.088 | 0.076 / 0.088 | mm | OK |

Both errors found (#4, #15) were corrected directly in `report/report.tex` (the
underlying `report/generated_data/july14_193548_422/final_metrics_table.csv`, generated
independently by the wrapper script, already contained the correct values — the errors
were transcription mistakes made while drafting prose from that table, not errors in the
generated data itself). After correction, `tectonic report.tex` was re-run and produced
zero LaTeX warnings (no overfull/underfull boxes, no missing references).

Controller-source constants cited in the report (DH table, gains, cone parameters,
thresholds) were cross-checked with a final `grep` pass over
`Assets/Scripts/ROSADoubleRCMController.cs` after the report was written; all matched
exactly (`toolLength=0.283`, `tipGain=3.5`, `rcmGain=17.0`, `damping=0.055`,
`maxJointSpeedDeg=80`, `coneHalfAngleDeg=4.5`, `coneAngularSpeedDeg=8.0`,
`coneAngularSpeedTask4Deg=7.0`, `tipTargetConeRadius=0.0138`,
`singularityGuardBoostFactor=1.2`, `entryRcmOkThresholdMm=2.0`,
`tipTargetOkThresholdMm=3.0`).

## Compilation

- Command: `cd report && tectonic report.tex`.
- Warnings: initial run produced two LaTeX errors (a stray `$` in an inline-math
  fragment; a `siunitx` `S`-column fed a non-numeric `"--"` cell) and a series of
  overfull/underfull `\hbox`/`\vbox` warnings (long unbreakable `\texttt{}` file paths
  and identifiers; one full-page comparison table not fitting in the remaining space on
  its starting page). All errors were fixed (corrected stray `$`; changed the offending
  table column from an `S` column to plain text; inserted `\allowbreak` after `/` and
  `_` inside long `\texttt`/`\code` paths; moved the comparison table to a dedicated
  page with `\footnotesize`). Final compilation run: **zero errors, zero warnings**.
- Final PDF path: `report/report.pdf`.
- Final page count: **28 pages**.
- Final PDF file size: **≈909.8 KB** (909776 bytes).

## Deliverables checklist

- [x] `report/report.tex`
- [x] `report/report.pdf`
- [x] `report/report_backup_before_final.tex` (pre-existing report, backed up before rewrite)
- [x] `report/REPORT_GENERATION_NOTES.md` (this file)
- [x] `report/final_metrics_table.csv`
- [x] `report/generated_data/july14_193548_422/{summary_by_task_phase.csv, summary_overall.csv, pass_fail_checks.csv, final_metrics_table.csv, combined_logs.csv, slide_summary_clean_en.md, README_CLEAN_ANALYZER.md}`
- [x] `report/generated_figures/july14_193548_422/*.png` (22 files)
- [x] `report/scripts/generate_final_run_metrics.py` (new wrapper, documented above)
- [x] `RCM_logs/project4_multircm_20260714_193548_422.csv` left unmodified
