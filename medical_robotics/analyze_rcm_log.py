#!/usr/bin/env python3
"""
Analyze Unity RCM logs for the ROSA-inspired Multi-RCM project.

This version separates:
- raw needle skull penetration
- unsafe needle skull violation

Why?
----
The needle is supposed to enter the skull through the surgical corridor.
Therefore, raw needle penetration into the skull is not automatically a failure.
A failure is:
- robot arm entering the skull;
- needle entering the skull before a valid insertion phase;
- needle entering outside the surgical corridor;
- skull violation during the wrong stage.

Correct evaluation workflow:
    1. Press 3 and wait until the insertion sequence reaches Done.
    2. Without resetting, press 2 for the Target-RCM cone demo.
    3. Without resetting, press 4 for the Entry-RCM tip-cone demo.
    4. Stop Play Mode.
    5. Run this script on the generated CSV.
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

COLUMN_ALIASES = {
    "time": ["time", "time_s", "t"],
    "mode": ["mode", "control_mode", "rcm_mode"],
    "phase": ["phase", "insertion_phase"],
    "evaluation_stage": ["evaluation_stage", "stage", "test_stage"],
    "entry_error_mm": [
        "entry_error_mm",
        "entry_rcm_error_mm",
        "entryRCMFormulaErrorMm",
        "entry_rcm_formula_error_mm",
    ],
    "target_error_mm": [
        "target_error_mm",
        "target_tip_error_mm",
        "target_rcm_or_tip_error_mm",
        "final_target_tip_error_mm",
        "targetTipErrorMm",
    ],
    "tip_entry_error_mm": ["tip_entry_error_mm", "tipEntryErrorMm"],
    "axis_error_deg": ["axis_error_deg", "entry_target_axis_error_deg", "entryTargetAxisErrorDeg"],
    "entry_axis_error_mm": ["entry_axis_error_mm", "entryAxisErrorMm"],
    "needle_skull_violation_mm": [
        "needle_skull_violation_mm",
        "skull_violation_mm",
        "skullViolationMm",
    ],
    "arm_skull_violation_mm": ["arm_skull_violation_mm", "armSkullViolationMm"],
    "lambda": ["lambda", "entry_lambda", "entryLambda"],
    "entry_cone_angle_deg": ["entry_cone_angle_deg", "entryConeAngleDeg"],
    "entry_cone_violation_deg": ["entry_cone_violation_deg", "entryConeViolationDeg"],
}

SUCCESS_THRESHOLDS = {
    "T3_final_target_error_mm": 10.0,
    "T3_final_entry_rcm_error_mm": 15.0,
    "T3_mean_entry_rcm_during_insertion_mm": 15.0,
    "max_arm_skull_violation_mm": 0.5,
    "max_unsafe_needle_skull_violation_mm": 0.5,
    "T2_mean_target_rcm_error_mm": 15.0,
    "T4_mean_entry_rcm_error_mm": 15.0,
}


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

    if "needle_skull_violation_mm" not in out.columns:
        out["needle_skull_violation_mm"] = 0.0

    if "arm_skull_violation_mm" not in out.columns:
        out["arm_skull_violation_mm"] = 0.0

    numeric_cols = [
        "time",
        "entry_error_mm",
        "target_error_mm",
        "tip_entry_error_mm",
        "axis_error_deg",
        "entry_axis_error_mm",
        "needle_skull_violation_mm",
        "arm_skull_violation_mm",
        "lambda",
        "entry_cone_angle_deg",
        "entry_cone_violation_deg",
    ]

    for col in numeric_cols:
        if col in out.columns:
            out[col] = pd.to_numeric(out[col], errors="coerce")

    out["mode"] = out["mode"].astype(str)
    out["phase"] = out["phase"].astype(str)

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
        m = mode.lower()

        if "entrytipcone" in m or "entry_tip_cone" in m:
            return "T4_entry_rcm_tip_cone_after_insertion"

        if "target" in m:
            return "T2_target_rcm_cone_after_insertion"

        if "double" in m or "insertion" in m:
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
    Add corrected safety semantics.

    raw_needle_skull_penetration_mm:
        Whatever Unity logged as needle/skull penetration.

    unsafe_needle_skull_violation_mm:
        Counts needle skull violation only when the needle is NOT supposed to be inside.

    In this current log format we do not have sampled needle points or exact corridor distance.
    Therefore we use a conservative stage/phase interpretation:
        - During T3 InsertToTarget or Done, needle penetration is intentional.
        - During T2/T4 after T3 Done, the needle is already inserted and the demo is post-insertion.
        - Before insertion starts, raw needle penetration is treated as unsafe.
    """
    out = df.copy()

    out["raw_needle_skull_penetration_mm"] = out["needle_skull_violation_mm"]

    intentional_insertion = (
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

    out["intentional_needle_inside_skull"] = intentional_insertion

    out["unsafe_needle_skull_violation_mm"] = out["raw_needle_skull_penetration_mm"].where(
        ~intentional_insertion,
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

    metrics["max_raw_needle_skull_penetration_mm"] = safe_max(df["raw_needle_skull_penetration_mm"])
    metrics["max_unsafe_needle_skull_violation_mm"] = safe_max(df["unsafe_needle_skull_violation_mm"])
    metrics["max_arm_skull_violation_mm"] = safe_max(df["arm_skull_violation_mm"])

    # T3
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

    # T2
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

    # T4
    t4 = stage_df(df, "T4_entry_rcm_tip_cone_after_insertion")
    t4_valid = t4[t4["valid_post_insertion_context"]].copy()

    if len(t4) > 0:
        metrics["T4_samples"] = int(len(t4))
        metrics["T4_valid_after_T3_done"] = bool(len(t4_valid) > 0)

        eval_t4 = t4_valid if len(t4_valid) > 0 else t4

        metrics["T4_mean_entry_rcm_error_mm"] = safe_mean(eval_t4["entry_error_mm"])
        metrics["T4_final_entry_rcm_error_mm"] = safe_last(eval_t4["entry_error_mm"])
        metrics["T4_max_entry_rcm_error_mm"] = safe_max(eval_t4["entry_error_mm"])

        metrics["T4_mean_tip_target_distance_mm"] = safe_mean(eval_t4["target_error_mm"])
        metrics["T4_final_tip_target_distance_mm"] = safe_last(eval_t4["target_error_mm"])

    metrics["PASS_skull_safety"] = (
        metrics.get("max_arm_skull_violation_mm", math.inf) <= SUCCESS_THRESHOLDS["max_arm_skull_violation_mm"]
        and metrics.get("max_unsafe_needle_skull_violation_mm", math.inf) <= SUCCESS_THRESHOLDS["max_unsafe_needle_skull_violation_mm"]
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
    plt.plot(df["time"], df["raw_needle_skull_penetration_mm"], label="Raw needle penetration")
    plt.plot(df["time"], df["unsafe_needle_skull_violation_mm"], label="Unsafe needle violation")
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
    plt.ylabel("Error / violation [mm]")
    plt.title("RCM, target, and skull metrics over time")
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
        "max_raw_needle_skull_penetration_mm",
        "max_unsafe_needle_skull_violation_mm",
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
    print("- Raw needle penetration is expected during insertion and post-insertion demos.")
    print("- Unsafe needle violation counts only needle penetration before valid insertion context.")
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
