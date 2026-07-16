#!/usr/bin/env python3
"""
Wrapper analysis script for the FINAL report of the Multi-RCM Unity project.

Purpose
-------
The repository's existing analyzer (analyze_rcm_logs_clean.py) accepts either a
directory (and then globs *all* CSVs inside it) or an explicit file/glob. For the
final report only ONE experimental run must be analysed:

    RCM_logs/project4_multircm_20260714_193548_422.csv

This script does NOT modify analyze_rcm_logs_clean.py. It instead:
  1. Calls it as a subprocess with the single selected CSV as the only input,
     so its internal `discover_csvs` never has a chance to glob a directory.
  2. Splits its output (figures vs. tables) into the two report-mandated folders.
  3. Performs an additional, finer-grained statistical pass directly on the
     selected CSV (RMS, std, median, p95 -- which the existing analyzer does not
     compute) to produce final_metrics_table.csv and summary_overall.csv.

Nothing here alters RCM_logs/project4_multircm_20260714_193548_422.csv.
"""
from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import numpy as np
import pandas as pd

REPO_ROOT = Path(__file__).resolve().parents[2]
SELECTED_CSV = REPO_ROOT / "RCM_logs" / "project4_multircm_20260714_193548_422.csv"
SOURCE_LOG = "RCM_logs/project4_multircm_20260714_193548_422.csv"

DATA_OUT = REPO_ROOT / "report" / "generated_data" / "july14_193548_422"
FIG_OUT = REPO_ROOT / "report" / "generated_figures" / "july14_193548_422"

ANALYZER = REPO_ROOT / "analyze_rcm_logs_clean.py"

ENTRY_RCM_THRESHOLD_MM = 2.00   # entryRcmOkThresholdMm in ROSADoubleRCMController.cs, also in CSV comment line
TIP_TARGET_THRESHOLD_MM = 3.00  # tipTargetOkThresholdMm in ROSADoubleRCMController.cs, also in CSV comment line
SKULL_THRESHOLD_MM = 0.00       # skull threshold in CSV comment line (skull avoidance disabled at scene-build time)
SETTLE_S = 1.00                 # settle-time exclusion, same convention as analyze_rcm_logs_clean.py pass_fail_summary


def run_existing_analyzer() -> None:
    """Runs the unmodified analyze_rcm_logs_clean.py on ONLY the selected CSV."""
    if DATA_OUT.exists():
        shutil.rmtree(DATA_OUT)
    DATA_OUT.mkdir(parents=True, exist_ok=True)

    cmd = [
        sys.executable, str(ANALYZER),
        str(SELECTED_CSV),
        "--out", str(DATA_OUT),
        "--entry-threshold-mm", str(ENTRY_RCM_THRESHOLD_MM),
        "--tip-threshold-mm", str(TIP_TARGET_THRESHOLD_MM),
        "--cone-threshold-mm", "50.0",  # script default; NOT a controller-defined clinical threshold, see notes
        "--skull-threshold-mm", str(SKULL_THRESHOLD_MM),
        "--settle-time-s", str(SETTLE_S),
    ]
    print("Running:", " ".join(cmd))
    subprocess.run(cmd, check=True, cwd=str(REPO_ROOT))

    # Verify the analyzer only ingested the selected CSV (guards against silent misconfiguration).
    combined = pd.read_csv(DATA_OUT / "combined_logs.csv")
    logs = set(combined["log_file"].unique())
    assert logs == {SELECTED_CSV.name}, f"Unexpected source logs in combined_logs.csv: {logs}"

    # Split outputs: figures -> generated_figures/, tables/markdown stay in generated_data/.
    if FIG_OUT.exists():
        shutil.rmtree(FIG_OUT)
    FIG_OUT.mkdir(parents=True, exist_ok=True)
    for png in DATA_OUT.glob("*.png"):
        shutil.move(str(png), str(FIG_OUT / png.name))
    print(f"Moved {len(list(FIG_OUT.glob('*.png')))} figures to {FIG_OUT}")


def load_df() -> pd.DataFrame:
    df = pd.read_csv(SELECTED_CSV, comment="#")
    df["seg_change"] = (df["task"] != df["task"].shift()) | (df["phase"] != df["phase"].shift())
    df["seg_id"] = df["seg_change"].cumsum()
    df["local_t"] = df.groupby("seg_id")["time_s"].transform(lambda s: s - s.min())
    return df


def stats_row(series: pd.Series) -> dict:
    s = pd.to_numeric(series, errors="coerce").dropna()
    if s.empty:
        return dict(n_samples=0, mean="--", rms="--", std="--", median="--", p95="--", max="--", final="--")
    rms = float(np.sqrt(np.mean(np.square(s.to_numpy()))))
    return dict(
        n_samples=int(s.size),
        mean=round(float(s.mean()), 4),
        rms=round(rms, 4),
        std=round(float(s.std(ddof=0)), 4),
        median=round(float(s.median()), 4),
        p95=round(float(s.quantile(0.95)), 4),
        max=round(float(s.max()), 4),
        final=round(float(s.iloc[-1]), 4),
    )


def build_final_metrics_table(df: pd.DataFrame) -> pd.DataFrame:
    rows = []

    def add(task_label, phase_label, mask, metric, unit, threshold=None, note=None):
        if metric not in df.columns:
            return
        sub = df.loc[mask]
        st = stats_row(sub[metric])
        pass_fail = "--"
        if threshold is not None and st["n_samples"] > 0 and st["max"] != "--":
            pass_fail = "PASS" if st["max"] <= threshold else "FAIL"
        rows.append({
            "task": task_label,
            "phase": phase_label,
            "metric": metric,
            "unit": unit,
            "n_samples": st["n_samples"],
            "mean": st["mean"],
            "rms": st["rms"],
            "std": st["std"],
            "median": st["median"],
            "p95": st["p95"],
            "max": st["max"],
            "final": st["final"],
            "threshold": threshold if threshold is not None else "--",
            "pass_fail": pass_fail,
            "note": note or "",
            "source_log": SOURCE_LOG,
        })

    t3 = df["task"].eq("T3_safe_insertion")
    t1 = df["task"].eq("T1_entry_rcm_tip_target")
    t2 = df["task"].eq("T2_target_rcm_entry_cone")
    t4 = df["task"].eq("T4_entry_rcm_tip_cone")

    # ---------------- Task 3: Safe insertion ----------------
    approach = t3 & df["phase"].eq("ApproachEntry")
    pierce = t3 & df["phase"].eq("PierceEntry")
    insert = t3 & df["phase"].eq("InsertToTarget")
    done3 = t3 & df["phase"].eq("Done")
    insert_settled = insert & (df["local_t"] >= SETTLE_S)
    done3_settled = done3 & (df["local_t"] >= SETTLE_S)

    add("T3_safe_insertion", "ApproachEntry", approach, "tip_line_distance_mm", "mm",
        note="Geometric diagnostic (distance of the tip from the entry-target line); no RCM task active yet.")
    add("T3_safe_insertion", "ApproachEntry", approach, "tool_axis_angle_deg", "deg",
        note="Angle between tool axis and the nominal entry-target line.")
    add("T3_safe_insertion", "PierceEntry", pierce, "tip_line_distance_mm", "mm")
    add("T3_safe_insertion", "PierceEntry", pierce, "tool_axis_angle_deg", "deg")
    add("T3_safe_insertion", "InsertToTarget (full phase)", insert, "entry_rcm_error_mm", "mm",
        note="Includes the transient right after the PierceEntry->InsertToTarget switch (RCM task just engaged).")
    add("T3_safe_insertion", "InsertToTarget (settled, t>=1s)", insert_settled, "entry_rcm_error_mm", "mm",
        threshold=ENTRY_RCM_THRESHOLD_MM,
        note="Excludes first 1 s after phase entry (mode-switch transient), per analyze_rcm_logs_clean.py convention.")
    add("T3_safe_insertion", "Done (full phase)", done3, "entry_rcm_error_mm", "mm")
    add("T3_safe_insertion", "Done (settled, t>=1s)", done3_settled, "entry_rcm_error_mm", "mm",
        threshold=ENTRY_RCM_THRESHOLD_MM)
    add("T3_safe_insertion", "Done (full phase)", done3, "tip_target_error_mm", "mm",
        note="Final needle-tip-to-target accuracy; 'final' column = last logged sample of the run in this phase.")
    add("T3_safe_insertion", "Done (settled, t>=1s)", done3_settled, "tip_target_error_mm", "mm",
        threshold=TIP_TARGET_THRESHOLD_MM)
    add("T3_safe_insertion", "InsertToTarget+Done", insert | done3, "shaft_line_max_distance_mm", "mm",
        note="Maximum distance of any sampled point on the shaft from the entry-target line (corridor diagnostic).")
    add("T3_safe_insertion", "InsertToTarget+Done", insert | done3, "tool_axis_angle_deg", "deg")
    add("T3_safe_insertion", "all phases", t3, "skull_violation_mm", "mm", threshold=SKULL_THRESHOLD_MM,
        note="Skull avoidance is disabled in Project4SceneBuilder (useSkullAvoidance=false, no skull object); "
             "this channel is identically zero and is NOT evidence of collision safety.")

    # ---------------- Task 2: Target-centred RCM with entry cone ----------------
    t2_settled = t2 & (df["local_t"] >= SETTLE_S)
    add("T2_target_rcm_entry_cone", "Done (full task)", t2, "task2_entry_cone_error_mm", "mm",
        note="Tracking error of the entry-side point vs. the commanded rotating cone reference (active control error).")
    add("T2_target_rcm_entry_cone", "Done (settled, t>=1s)", t2_settled, "task2_entry_cone_error_mm", "mm")
    add("T2_target_rcm_entry_cone", "Done (full task)", t2, "tip_target_error_mm", "mm",
        note="Tip-to-target distance; tip is the primary regulated task in Task 2.")
    add("T2_target_rcm_entry_cone", "Done (settled, t>=1s)", t2_settled, "tip_target_error_mm", "mm",
        threshold=TIP_TARGET_THRESHOLD_MM)
    add("T2_target_rcm_entry_cone", "Done (full task)", t2, "target_rcm_error_mm", "mm",
        note="Diagnostic only: no explicit RCM->target task exists in Task 2 (removed, see BuildTasks() comment); "
             "this tracks lambda~1 placing RCM near the tip, so it largely mirrors tip_target_error_mm.")

    # ---------------- Task 4: Entry-centred RCM with tip cone ----------------
    t4_settled = t4 & (df["local_t"] >= SETTLE_S)
    add("T4_entry_rcm_tip_cone", "Done (full task)", t4, "entry_rcm_error_mm", "mm")
    add("T4_entry_rcm_tip_cone", "Done (settled, t>=1s)", t4_settled, "entry_rcm_error_mm", "mm",
        threshold=ENTRY_RCM_THRESHOLD_MM)
    add("T4_entry_rcm_tip_cone", "Done (full task)", t4, "task4_tip_cone_error_mm", "mm",
        note="Tracking error of the tip vs. the commanded rotating cone reference around the target (active control error).")
    add("T4_entry_rcm_tip_cone", "Done (settled, t>=1s)", t4_settled, "task4_tip_cone_error_mm", "mm")
    add("T4_entry_rcm_tip_cone", "Done (full task)", t4, "tip_target_error_mm", "mm",
        note="NOT an error: the tip deliberately traces a circle of commanded radius tipTargetConeRadius=13.8 mm "
             "around the target; this column reports that radius as tracked, not a regulation error.")

    # ---------------- Task 1: brief manual demonstration ----------------
    add("T1_entry_rcm_tip_target", "Done (full task)", t1, "entry_rcm_error_mm", "mm",
        note="Short (~3.9 s) manually triggered segment; dominated by the transition transient, not steady state.")
    add("T1_entry_rcm_tip_target", "Done (full task)", t1, "tip_target_error_mm", "mm")

    # ---------------- Whole-run controller/runtime diagnostics ----------------
    add("ALL", "whole run", pd.Series(True, index=df.index), "qdot_norm_rad_s", "rad/s",
        note="Norm of the commanded (post-saturation) joint-velocity vector.")
    add("ALL", "whole run", pd.Series(True, index=df.index), "lambda", "-",
        note="Penetration coordinate along the tool axis (dimensionless, 0=tool base, 1=tip).")
    add("ALL", "whole run", pd.Series(True, index=df.index), "lambdadot", "1/s",
        note="Commanded rate of change of lambda (post-saturation).")
    add("ALL", "whole run", pd.Series(True, index=df.index), "fps", "Hz",
        note="Unity editor frame rate during logging; NOT a real-time/hardware guarantee.")

    return pd.DataFrame(rows)


def build_summary_overall(df: pd.DataFrame) -> pd.DataFrame:
    rows = []
    for task, g in df.groupby("task", sort=False):
        g = g.sort_values("time_s")
        st_active = stats_row(g["active_task_error_mm"])
        st_qdot = stats_row(g["qdot_norm_rad_s"])
        st_fps = stats_row(g["fps"])
        rows.append({
            "task": task,
            "n_samples": len(g),
            "t_start_s": round(float(g["time_s"].min()), 3),
            "t_end_s": round(float(g["time_s"].max()), 3),
            "duration_s": round(float(g["time_s"].max() - g["time_s"].min()), 3),
            "active_task_error_mean_mm": st_active["mean"],
            "active_task_error_rms_mm": st_active["rms"],
            "active_task_error_max_mm": st_active["max"],
            "qdot_norm_mean_rad_s": st_qdot["mean"],
            "qdot_norm_max_rad_s": st_qdot["max"],
            "fps_mean": st_fps["mean"],
            "fps_min": round(float(pd.to_numeric(g["fps"], errors="coerce").dropna().min()), 2),
            "lambda_start": round(float(g["lambda"].iloc[0]), 4),
            "lambda_end": round(float(g["lambda"].iloc[-1]), 4),
            "source_log": SOURCE_LOG,
        })
    order = ["T3_safe_insertion", "T1_entry_rcm_tip_target", "T2_target_rcm_entry_cone", "T4_entry_rcm_tip_cone"]
    out = pd.DataFrame(rows)
    out["order"] = out["task"].map({t: i for i, t in enumerate(order)}).fillna(999)
    out = out.sort_values("order").drop(columns=["order"]).reset_index(drop=True)
    return out


def main() -> None:
    assert SELECTED_CSV.exists(), f"Selected CSV not found: {SELECTED_CSV}"
    run_existing_analyzer()

    df = load_df()

    final_table = build_final_metrics_table(df)
    final_table.to_csv(DATA_OUT / "final_metrics_table.csv", index=False)
    final_table.to_csv(REPO_ROOT / "report" / "final_metrics_table.csv", index=False)
    print(f"Wrote final_metrics_table.csv with {len(final_table)} rows")

    summary_overall = build_summary_overall(df)
    summary_overall.to_csv(DATA_OUT / "summary_overall.csv", index=False)
    print(f"Wrote summary_overall.csv with {len(summary_overall)} rows")

    # Sanity: summary_by_task_phase.csv and pass_fail_checks.csv already written by the analyzer above.
    assert (DATA_OUT / "summary_by_task_phase.csv").exists()
    assert (DATA_OUT / "pass_fail_checks.csv").exists()
    print("Done. Data:", DATA_OUT, "Figures:", FIG_OUT)


if __name__ == "__main__":
    main()
