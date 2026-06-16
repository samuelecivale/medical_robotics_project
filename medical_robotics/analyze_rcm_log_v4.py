#!/usr/bin/env python3
"""
Analyze Unity RCM logs for the ROSA-inspired Multi-RCM project.

Version: safety-semantics v4

Key idea
--------
The needle is SUPPOSED to enter the skull during insertion.
So a non-zero needle/skull value is not automatically an error.

The script reports three different safety quantities:

1. max_intentional_needle_penetration_mm
   Needle inside the skull during valid insertion/post-insertion context.
   This is expected and is NOT a failure.

2. max_unsafe_needle_violation_mm
   Needle/skull interaction before a valid insertion context.
   This is the quantity used for the safety pass/fail.

3. max_arm_skull_violation_mm
   Robot arm inside the skull.
   This should stay close to zero.

Correct evaluation workflow
---------------------------
1. Press 3 and wait until the insertion sequence reaches Done.
2. Without resetting, press 2 for the Target-RCM cone demo.
3. Without resetting, press 4 for the Entry-RCM tip-cone demo.
4. Stop Play Mode.
5. Run this script on the generated CSV.

Examples
--------
python analyze_rcm_log.py
python analyze_rcm_log.py --csv RCM_logs/paper_rcm_log_20260616_110935_960_8218.csv
"""

from __future__ import annotations

import argparse
import math
import sys
from pathlib import Path
from typing import Dict, Iterable, Optional

import pandas as pd
import matplotlib.pyplot as plt


DEFAULT_LOG_DIR = Path("RCM_logs")


# ---------------------------------------------------------------------
# Column aliases
# ---------------------------------------------------------------------

COLUMN_ALIASES = {
    "time": [
        "time", "time_s", "t", "Time", "Time [s]",
    ],
    "mode": [
        "mode", "control_mode", "rcm_mode", "Mode", "task", "Task",
    ],
    "phase": [
        "phase", "insertion_phase", "InsertionPhase", "Phase",
    ],
    "evaluation_stage": [
        "evaluation_stage", "stage", "test_stage",
    ],

    # Entry / RCM errors
    "entry_error_mm": [
        "entry_error_mm",
        "entry_rcm_error_mm",
        "entryRCM_error_mm",
        "entry_rcm_formula_error_mm",
        "entryRCMFormulaErrorMm",
        "entry_r_cm_error_mm",
        "entryRcmErrorMm",
        "entryRCMErrorMm",
        "rcm_error_mm",
        "RCM_error_mm",
        "rcmErrorMm",
    ],

    # Target / tip errors
    "target_error_mm": [
        "target_error_mm",
        "target_tip_error_mm",
        "target_rcm_or_tip_error_mm",
        "target_rcm_error_mm",
        "targetRCM_error_mm",
        "targetRcmErrorMm",
        "targetRCMErrorMm",
        "final_target_tip_error_mm",
        "targetTipErrorMm",
        "tip_target_error_mm",
        "tipTargetErrorMm",
        "target_error",
    ],

    # Additional diagnostic quantities
    "tip_entry_error_mm": [
        "tip_entry_error_mm",
        "tipEntryErrorMm",
        "tip_to_entry_error_mm",
        "tipEntry_error_mm",
    ],
    "axis_error_deg": [
        "axis_error_deg",
        "entry_axis_error_deg",
        "entry_target_axis_error_deg",
        "entryTargetAxisErrorDeg",
        "axisErrorDeg",
    ],
    "entry_axis_error_mm": [
        "entry_axis_error_mm",
        "entryAxisErrorMm",
        "entry_to_axis_error_mm",
    ],

    # Safety
    "needle_skull_metric_mm": [
        # New preferred names
        "needle_skull_metric_mm",
        "raw_needle_skull_penetration_mm",
        "intentional_needle_penetration_mm",
        "needle_skull_interaction_mm",

        # Old names; these may mean either raw interaction or unsafe violation depending on controller.
        "needle_skull_violation_mm",
        "skull_violation_mm",
        "skullViolationMm",
        "needleSkullViolationMm",
        "needle_skull_error_mm",
    ],
    "unsafe_needle_violation_mm": [
        "unsafe_needle_skull_violation_mm",
        "unsafe_needle_violation_mm",
        "needle_unsafe_violation_mm",
    ],
    "arm_skull_violation_mm": [
        "arm_skull_violation_mm",
        "armSkullViolationMm",
        "arm_violation_mm",
    ],

    # Lambda and geometry
    "lambda": [
        "lambda", "entry_lambda", "entryLambda",
    ],
    "target_lambda": [
        "target_lambda", "targetLambda",
    ],

    # Cone
    "entry_cone_angle_deg": [
        "entry_cone_angle_deg", "entryConeAngleDeg",
    ],
    "entry_cone_violation_deg": [
        "entry_cone_violation_deg", "entryConeViolationDeg",
    ],

    # Positions, used for fallback metric reconstruction
    "tool_x": ["tool_x", "toolBase_x", "tool_base_x", "base_tool_x"],
    "tool_y": ["tool_y", "toolBase_y", "tool_base_y", "base_tool_y"],
    "tool_z": ["tool_z", "toolBase_z", "tool_base_z", "base_tool_z"],

    "tip_x": ["tip_x", "toolTip_x", "tool_tip_x"],
    "tip_y": ["tip_y", "toolTip_y", "tool_tip_y"],
    "tip_z": ["tip_z", "toolTip_z", "tool_tip_z"],

    "entry_x": ["entry_x", "entryPoint_x", "entry_point_x"],
    "entry_y": ["entry_y", "entryPoint_y", "entry_point_y"],
    "entry_z": ["entry_z", "entryPoint_z", "entry_point_z"],

    "target_x": ["target_x", "targetPoint_x", "target_point_x"],
    "target_y": ["target_y", "targetPoint_y", "target_point_y"],
    "target_z": ["target_z", "targetPoint_z", "target_point_z"],
}


SUCCESS_THRESHOLDS = {
    "T3_final_target_error_mm": 10.0,
    "T3_final_entry_rcm_error_mm": 15.0,
    "T3_mean_entry_rcm_during_insertion_mm": 15.0,
    "max_arm_skull_violation_mm": 0.5,
    "max_unsafe_needle_violation_mm": 0.5,
    "T2_mean_target_rcm_error_mm": 15.0,
    "T4_mean_entry_rcm_error_mm": 15.0,
}


# ---------------------------------------------------------------------
# Utility
# ---------------------------------------------------------------------

def find_latest_csv(log_dir: Path) -> Path:
    if not log_dir.exists():
        raise FileNotFoundError(f"Log directory not found: {log_dir}")

    csv_files = sorted(log_dir.glob("*.csv"), key=lambda p: p.stat().st_mtime, reverse=True)

    if not csv_files:
        raise FileNotFoundError(
            f"No CSV logs found in {log_dir}. Run Unity first, stop Play Mode, then run this script."
        )

    return csv_files[0]


def first_existing_column(df: pd.DataFrame, candidates: Iterable[str]) -> Optional[str]:
    for col in candidates:
        if col in df.columns:
            return col
    return None


def is_missing_or_all_nan(df: pd.DataFrame, col: str) -> bool:
    return col not in df.columns or df[col].dropna().empty


def standardize_columns(df: pd.DataFrame) -> pd.DataFrame:
    out = df.copy()

    for standard_name, candidates in COLUMN_ALIASES.items():
        found = first_existing_column(out, candidates)
        if found is not None and found != standard_name:
            out[standard_name] = out[found]

    if "time" not in out.columns:
        out["time"] = range(len(out))

    if "mode" not in out.columns:
        out["mode"] = "Unknown"

    if "phase" not in out.columns:
        out["phase"] = "Unknown"

    if "entry_error_mm" not in out.columns:
        out["entry_error_mm"] = math.nan

    if "target_error_mm" not in out.columns:
        out["target_error_mm"] = math.nan

    if "needle_skull_metric_mm" not in out.columns:
        out["needle_skull_metric_mm"] = 0.0

    if "unsafe_needle_violation_mm" not in out.columns:
        out["unsafe_needle_violation_mm"] = math.nan

    if "arm_skull_violation_mm" not in out.columns:
        out["arm_skull_violation_mm"] = 0.0

    numeric_cols = [
        "time",
        "entry_error_mm",
        "target_error_mm",
        "tip_entry_error_mm",
        "axis_error_deg",
        "entry_axis_error_mm",
        "needle_skull_metric_mm",
        "unsafe_needle_violation_mm",
        "arm_skull_violation_mm",
        "lambda",
        "target_lambda",
        "entry_cone_angle_deg",
        "entry_cone_violation_deg",
        "tool_x", "tool_y", "tool_z",
        "tip_x", "tip_y", "tip_z",
        "entry_x", "entry_y", "entry_z",
        "target_x", "target_y", "target_z",
    ]

    for col in numeric_cols:
        if col in out.columns:
            out[col] = pd.to_numeric(out[col], errors="coerce")

    out["mode"] = out["mode"].astype(str)
    out["phase"] = out["phase"].astype(str)

    out = reconstruct_missing_errors_from_positions(out)

    return out


def reconstruct_missing_errors_from_positions(df: pd.DataFrame) -> pd.DataFrame:
    """
    If the CSV has positions but not explicit errors, reconstruct:
    - target_error_mm from tip and target positions;
    - entry_error_mm from tool base, tip, entry and lambda.
    """
    out = df.copy()

    has_tip_target = all(c in out.columns for c in ["tip_x", "tip_y", "tip_z", "target_x", "target_y", "target_z"])

    if is_missing_or_all_nan(out, "target_error_mm") and has_tip_target:
        dx = out["target_x"] - out["tip_x"]
        dy = out["target_y"] - out["tip_y"]
        dz = out["target_z"] - out["tip_z"]
        out["target_error_mm"] = (dx * dx + dy * dy + dz * dz).pow(0.5) * 1000.0

    has_rcm_geometry = all(
        c in out.columns
        for c in ["tool_x", "tool_y", "tool_z", "tip_x", "tip_y", "tip_z", "entry_x", "entry_y", "entry_z", "lambda"]
    )

    if is_missing_or_all_nan(out, "entry_error_mm") and has_rcm_geometry:
        rcm_x = out["tool_x"] + out["lambda"] * (out["tip_x"] - out["tool_x"])
        rcm_y = out["tool_y"] + out["lambda"] * (out["tip_y"] - out["tool_y"])
        rcm_z = out["tool_z"] + out["lambda"] * (out["tip_z"] - out["tool_z"])

        dx = out["entry_x"] - rcm_x
        dy = out["entry_y"] - rcm_y
        dz = out["entry_z"] - rcm_z
        out["entry_error_mm"] = (dx * dx + dy * dy + dz * dz).pow(0.5) * 1000.0

    return out


def normalize_time(df: pd.DataFrame) -> pd.DataFrame:
    out = df.copy()
    if "time" in out.columns and not out["time"].dropna().empty:
        out["time"] = out["time"] - out["time"].dropna().iloc[0]
    return out


def infer_stage(df: pd.DataFrame) -> pd.DataFrame:
    out = df.copy()

    if "evaluation_stage" in out.columns:
        out["stage"] = out["evaluation_stage"].astype(str)
        return out

    def map_mode_to_stage(mode: str) -> str:
        m = mode.lower().replace("-", "_").replace(" ", "_")

        # Order matters: EntryRCM_TipCone contains "entry", not target.
        if "entryrcm_tipcone" in m or "entry_tip_cone" in m or "entrytipcone" in m:
            return "T4_entry_rcm_tip_cone_after_insertion"

        if "targetrcm_entrycone" in m or "target_rcm_entry_cone" in m or "target" in m:
            return "T2_target_rcm_cone_after_insertion"

        if "insertionsequence" in m or "safe_insertion" in m or "double" in m or "insertion" in m:
            return "T3_safe_insertion"

        return "Unknown"

    out["stage"] = out["mode"].map(map_mode_to_stage)
    return out


def mark_valid_post_insertion_context(df: pd.DataFrame) -> pd.DataFrame:
    out = df.copy()
    out["valid_post_insertion_context"] = False

    done_mask = (
        out["stage"].eq("T3_safe_insertion")
        & out["phase"].str.contains("Done", case=False, na=False)
    )

    if done_mask.any():
        first_done_index = done_mask[done_mask].index[0]
        out.loc[out.index >= first_done_index, "valid_post_insertion_context"] = True

    return out


def add_safety_semantics(df: pd.DataFrame) -> pd.DataFrame:
    """
    Separate expected needle penetration from unsafe needle violation.

    If the controller already logs unsafe_needle_violation_mm, use it.
    Otherwise infer unsafe violation from context:
    - T3 InsertToTarget or Done: needle inside skull is intentional.
    - T2/T4 after T3 Done: needle inside skull is intentional.
    - Before that: needle/skull metric is unsafe.
    """
    out = df.copy()

    intentional_context = (
        (
            out["stage"].eq("T3_safe_insertion")
            & out["phase"].str.contains("Insert|Done", case=False, na=False, regex=True)
        )
        |
        (
            out["stage"].isin([
                "T2_target_rcm_cone_after_insertion",
                "T4_entry_rcm_tip_cone_after_insertion",
            ])
            & out["valid_post_insertion_context"].eq(True)
        )
    )

    out["intentional_needle_inside_skull"] = intentional_context

    out["intentional_needle_penetration_mm"] = out["needle_skull_metric_mm"].where(
        intentional_context,
        0.0,
    )

    if "unsafe_needle_violation_mm" in out.columns and not out["unsafe_needle_violation_mm"].dropna().empty:
        out["unsafe_needle_violation_mm"] = out["unsafe_needle_violation_mm"].fillna(0.0)
    else:
        out["unsafe_needle_violation_mm"] = out["needle_skull_metric_mm"].where(
            ~intentional_context,
            0.0,
        )

    return out


def stage_df(df: pd.DataFrame, stage: str) -> pd.DataFrame:
    return df[df["stage"].eq(stage)].copy()


def phase_contains(df: pd.DataFrame, pattern: str) -> pd.DataFrame:
    return df[df["phase"].str.contains(pattern, case=False, na=False)].copy()


def safe_mean(series: pd.Series) -> float:
    clean = series.dropna()
    return float(clean.mean()) if len(clean) else math.nan


def safe_max(series: pd.Series) -> float:
    clean = series.dropna()
    return float(clean.max()) if len(clean) else math.nan


def safe_last(series: pd.Series) -> float:
    clean = series.dropna()
    return float(clean.iloc[-1]) if len(clean) else math.nan


def safe_min(series: pd.Series) -> float:
    clean = series.dropna()
    return float(clean.min()) if len(clean) else math.nan


def fmt(value: float, digits: int = 3) -> str:
    if value is None:
        return "NA"
    if isinstance(value, float) and math.isnan(value):
        return "NA"
    return f"{value:.{digits}f}"


def compute_metrics(df: pd.DataFrame) -> Dict[str, float | str | int | bool]:
    metrics: Dict[str, float | str | int | bool] = {}

    metrics["samples"] = int(len(df))
    metrics["duration_s"] = safe_last(df["time"])
    metrics["modes"] = ", ".join(sorted(df["mode"].dropna().unique()))
    metrics["phases"] = ", ".join(sorted(df["phase"].dropna().unique()))
    metrics["stages"] = ", ".join(sorted(df["stage"].dropna().unique()))

    metrics["max_intentional_needle_penetration_mm"] = safe_max(df["intentional_needle_penetration_mm"])
    metrics["max_unsafe_needle_violation_mm"] = safe_max(df["unsafe_needle_violation_mm"])
    metrics["max_arm_skull_violation_mm"] = safe_max(df["arm_skull_violation_mm"])

    # T3 main safe insertion
    t3 = stage_df(df, "T3_safe_insertion")
    if len(t3) > 0:
        insert = phase_contains(t3, "Insert")
        done = phase_contains(t3, "Done")
        insertion_eval = insert if len(insert) > 0 else done

        if len(insertion_eval) == 0:
            start = int(len(t3) * 0.8)
            insertion_eval = t3.iloc[start:].copy()

        metrics["T3_samples"] = int(len(t3))
        metrics["T3_done_reached"] = bool(len(done) > 0)

        metrics["T3_final_entry_rcm_error_mm"] = safe_last(t3["entry_error_mm"])
        metrics["T3_final_target_error_mm"] = safe_last(t3["target_error_mm"])

        metrics["T3_mean_entry_rcm_during_insertion_mm"] = safe_mean(insertion_eval["entry_error_mm"])
        metrics["T3_max_entry_rcm_during_insertion_mm"] = safe_max(insertion_eval["entry_error_mm"])
        metrics["T3_mean_target_error_during_insertion_mm"] = safe_mean(insertion_eval["target_error_mm"])

        if "tip_entry_error_mm" in t3.columns:
            approach = phase_contains(t3, "Approach")
            if len(approach) > 0:
                metrics["T3_min_tip_entry_error_during_approach_mm"] = safe_min(approach["tip_entry_error_mm"])

        if "axis_error_deg" in t3.columns:
            align = phase_contains(t3, "Align")
            if len(align) > 0:
                metrics["T3_final_axis_error_before_insertion_deg"] = safe_last(align["axis_error_deg"])
                metrics["T3_mean_axis_error_during_alignment_deg"] = safe_mean(align["axis_error_deg"])

    # T2 target-RCM cone after insertion
    t2 = stage_df(df, "T2_target_rcm_cone_after_insertion")
    t2_valid = t2[t2["valid_post_insertion_context"]].copy()

    if len(t2) > 0:
        metrics["T2_samples"] = int(len(t2))
        metrics["T2_valid_after_T3_done"] = bool(len(t2_valid) > 0)

        eval_t2 = t2_valid if len(t2_valid) > 0 else t2

        metrics["T2_mean_target_rcm_error_mm"] = safe_mean(eval_t2["target_error_mm"])
        metrics["T2_final_target_rcm_error_mm"] = safe_last(eval_t2["target_error_mm"])
        metrics["T2_max_target_rcm_error_mm"] = safe_max(eval_t2["target_error_mm"])

        if "entry_cone_violation_deg" in eval_t2.columns:
            metrics["T2_mean_entry_cone_violation_deg"] = safe_mean(eval_t2["entry_cone_violation_deg"])
            metrics["T2_max_entry_cone_violation_deg"] = safe_max(eval_t2["entry_cone_violation_deg"])

    # T4 entry-RCM tip cone after insertion
    t4 = stage_df(df, "T4_entry_rcm_tip_cone_after_insertion")
    t4_valid = t4[t4["valid_post_insertion_context"]].copy()

    if len(t4) > 0:
        metrics["T4_samples"] = int(len(t4))
        metrics["T4_valid_after_T3_done"] = bool(len(t4_valid) > 0)

        eval_t4 = t4_valid if len(t4_valid) > 0 else t4

        metrics["T4_mean_entry_rcm_error_mm"] = safe_mean(eval_t4["entry_error_mm"])
        metrics["T4_final_entry_rcm_error_mm"] = safe_last(eval_t4["entry_error_mm"])
        metrics["T4_max_entry_rcm_error_mm"] = safe_max(eval_t4["entry_error_mm"])

        # Not expected to be zero in cone motion.
        metrics["T4_mean_tip_target_distance_mm"] = safe_mean(eval_t4["target_error_mm"])
        metrics["T4_final_tip_target_distance_mm"] = safe_last(eval_t4["target_error_mm"])

    metrics["PASS_skull_safety"] = (
        metrics.get("max_arm_skull_violation_mm", math.inf) <= SUCCESS_THRESHOLDS["max_arm_skull_violation_mm"]
        and metrics.get("max_unsafe_needle_violation_mm", math.inf) <= SUCCESS_THRESHOLDS["max_unsafe_needle_violation_mm"]
    )

    metrics["PASS_T3_safe_insertion"] = (
        bool(metrics.get("T3_done_reached", False))
        and metrics.get("T3_final_target_error_mm", math.inf) <= SUCCESS_THRESHOLDS["T3_final_target_error_mm"]
        and metrics.get("T3_final_entry_rcm_error_mm", math.inf) <= SUCCESS_THRESHOLDS["T3_final_entry_rcm_error_mm"]
        and metrics.get("T3_mean_entry_rcm_during_insertion_mm", math.inf) <= SUCCESS_THRESHOLDS["T3_mean_entry_rcm_during_insertion_mm"]
    )

    if "T2_samples" in metrics:
        metrics["PASS_T2_post_target_cone"] = (
            bool(metrics.get("T2_valid_after_T3_done", False))
            and metrics.get("T2_mean_target_rcm_error_mm", math.inf) <= SUCCESS_THRESHOLDS["T2_mean_target_rcm_error_mm"]
        )

    if "T4_samples" in metrics:
        metrics["PASS_T4_post_entry_tip_cone"] = (
            bool(metrics.get("T4_valid_after_T3_done", False))
            and metrics.get("T4_mean_entry_rcm_error_mm", math.inf) <= SUCCESS_THRESHOLDS["T4_mean_entry_rcm_error_mm"]
        )

    return metrics


def save_metrics(metrics: Dict[str, float | str | int | bool], out_csv: Path, out_txt: Path) -> None:
    rows = [{"metric": key, "value": value} for key, value in metrics.items()]
    pd.DataFrame(rows).to_csv(out_csv, index=False)

    with out_txt.open("w", encoding="utf-8") as f:
        f.write("RCM log analysis summary\n")
        f.write("========================\n\n")
        for key, value in metrics.items():
            if isinstance(value, float):
                f.write(f"{key}: {fmt(value)}\n")
            else:
                f.write(f"{key}: {value}\n")


def plot_errors_over_time(df: pd.DataFrame, out_path: Path) -> None:
    plt.figure(figsize=(13, 6))

    plt.plot(df["time"], df["entry_error_mm"], label="Entry RCM error")
    plt.plot(df["time"], df["target_error_mm"], label="Target / tip error")
    plt.plot(df["time"], df["intentional_needle_penetration_mm"], label="Intentional needle penetration")
    plt.plot(df["time"], df["unsafe_needle_violation_mm"], label="Unsafe needle violation")
    plt.plot(df["time"], df["arm_skull_violation_mm"], label="Arm skull violation")

    if "stage" in df.columns and len(df) > 1:
        previous = df["stage"].iloc[0]
        for _, row in df.iterrows():
            current = row["stage"]
            if current != previous:
                plt.axvline(row["time"], linestyle="--", alpha=0.35)
                plt.text(
                    row["time"],
                    plt.ylim()[1] * 0.95,
                    current.replace("_", "\n"),
                    rotation=90,
                    va="top",
                    fontsize=8,
                )
                previous = current

    plt.xlabel("Time [s]")
    plt.ylabel("Error / distance [mm]")
    plt.title("RCM, target, and safety metrics over time")
    plt.legend()
    plt.grid(True)
    plt.tight_layout()
    plt.savefig(out_path, dpi=250)
    plt.close()


def plot_mean_error_by_stage(df: pd.DataFrame, out_path: Path) -> None:
    stage_stats = df.groupby("stage")[["entry_error_mm", "target_error_mm"]].mean()

    plt.figure(figsize=(10, 5))
    stage_stats.plot(kind="bar")
    plt.ylabel("Mean error [mm]")
    plt.title("Mean error by evaluation stage")
    plt.grid(axis="y")
    plt.tight_layout()
    plt.savefig(out_path, dpi=250)
    plt.close()


def plot_final_error_by_stage(df: pd.DataFrame, out_path: Path) -> None:
    final_rows = df.groupby("stage")[["entry_error_mm", "target_error_mm"]].tail(1).copy()
    final_rows["stage"] = df.loc[final_rows.index, "stage"].values
    final_rows = final_rows.set_index("stage")

    plt.figure(figsize=(10, 5))
    final_rows[["entry_error_mm", "target_error_mm"]].plot(kind="bar")
    plt.ylabel("Final error [mm]")
    plt.title("Final error by evaluation stage")
    plt.grid(axis="y")
    plt.tight_layout()
    plt.savefig(out_path, dpi=250)
    plt.close()


def plot_mean_error_by_phase_for_t3(df: pd.DataFrame, out_path: Path) -> None:
    t3 = stage_df(df, "T3_safe_insertion")

    if len(t3) == 0:
        return

    phase_stats = t3.groupby("phase")[["entry_error_mm", "target_error_mm"]].mean()

    plt.figure(figsize=(10, 5))
    phase_stats.plot(kind="bar")
    plt.ylabel("Mean error [mm]")
    plt.title("Safe insertion: mean error by phase")
    plt.grid(axis="y")
    plt.tight_layout()
    plt.savefig(out_path, dpi=250)
    plt.close()


def print_summary(metrics: Dict[str, float | str | int | bool]) -> None:
    print("\nRCM LOG ANALYSIS SUMMARY")
    print("========================")

    keys_order = [
        "samples",
        "duration_s",
        "modes",
        "phases",
        "stages",
        "T3_done_reached",
        "T3_final_target_error_mm",
        "T3_final_entry_rcm_error_mm",
        "T3_mean_entry_rcm_during_insertion_mm",
        "T3_max_entry_rcm_during_insertion_mm",
        "T2_valid_after_T3_done",
        "T2_mean_target_rcm_error_mm",
        "T4_valid_after_T3_done",
        "T4_mean_entry_rcm_error_mm",
        "max_intentional_needle_penetration_mm",
        "max_unsafe_needle_violation_mm",
        "max_arm_skull_violation_mm",
        "PASS_skull_safety",
        "PASS_T3_safe_insertion",
        "PASS_T2_post_target_cone",
        "PASS_T4_post_entry_tip_cone",
    ]

    for key in keys_order:
        if key not in metrics:
            continue
        value = metrics[key]
        if isinstance(value, float):
            print(f"{key}: {fmt(value)}")
        else:
            print(f"{key}: {value}")

    print("\nInterpretation:")
    print("- Intentional needle penetration is expected during insertion and post-insertion demos.")
    print("- Unsafe needle violation is the actual safety failure metric for the needle.")
    print("- Arm skull violation should remain close to zero.")
    print("- T3 is the main surgical insertion task; T2 and T4 are post-insertion demos.")


def main() -> int:
    parser = argparse.ArgumentParser(description="Analyze Unity RCM CSV logs.")
    parser.add_argument("--csv", type=Path, default=None, help="Path to a specific CSV log.")
    parser.add_argument("--log-dir", type=Path, default=DEFAULT_LOG_DIR, help="Directory containing Unity CSV logs.")
    parser.add_argument("--out-dir", type=Path, default=None, help="Output directory.")
    parser.add_argument("--prefix", type=str, default=None, help="Output file prefix.")
    args = parser.parse_args()

    try:
        csv_path = args.csv if args.csv is not None else find_latest_csv(args.log_dir)
        out_dir = args.out_dir if args.out_dir is not None else csv_path.parent
        out_dir.mkdir(parents=True, exist_ok=True)

        raw_df = pd.read_csv(csv_path)

        if raw_df.empty:
            raise RuntimeError("CSV file is empty.")

        df = standardize_columns(raw_df)
        df = normalize_time(df)
        df = infer_stage(df)
        df = mark_valid_post_insertion_context(df)
        df = add_safety_semantics(df)

        prefix = args.prefix if args.prefix else csv_path.stem

        cleaned_csv = out_dir / f"{prefix}_cleaned_with_stages.csv"
        metrics_csv = out_dir / f"{prefix}_metrics.csv"
        metrics_txt = out_dir / f"{prefix}_summary.txt"

        plot_time = out_dir / f"{prefix}_errors_over_time.png"
        plot_stage_mean = out_dir / f"{prefix}_mean_error_by_stage.png"
        plot_stage_final = out_dir / f"{prefix}_final_error_by_stage.png"
        plot_t3_phase = out_dir / f"{prefix}_T3_mean_error_by_phase.png"

        df.to_csv(cleaned_csv, index=False)

        metrics = compute_metrics(df)
        save_metrics(metrics, metrics_csv, metrics_txt)

        plot_errors_over_time(df, plot_time)
        plot_mean_error_by_stage(df, plot_stage_mean)
        plot_final_error_by_stage(df, plot_stage_final)
        plot_mean_error_by_phase_for_t3(df, plot_t3_phase)

        print(f"Analyzed CSV: {csv_path}")
        print_summary(metrics)

        print("\nGenerated files:")
        print(f"- {cleaned_csv}")
        print(f"- {metrics_csv}")
        print(f"- {metrics_txt}")
        print(f"- {plot_time}")
        print(f"- {plot_stage_mean}")
        print(f"- {plot_stage_final}")
        print(f"- {plot_t3_phase}")

        return 0

    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
