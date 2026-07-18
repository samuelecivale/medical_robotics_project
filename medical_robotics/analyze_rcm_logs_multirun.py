#!/usr/bin/env python3
"""
Clean analyzer for Project 4 Multi-RCM Unity logs, including multi-run
repeatability analysis.

Main difference from the previous analyzer:
- it DOES NOT put all tracking errors in the same figure;
- each task metric gets its own readable plot;
- each plot includes only the relevant metric, the validation threshold and the max value;
- outputs are easier to insert directly into slides.

Usage:
    python analyze_rcm_logs_clean.py RCM_logs --out RCM_analysis_clean
    python analyze_rcm_logs_clean.py RCM_logs/project4_multircm_*.csv --out RCM_analysis_clean
    python analyze_rcm_logs_clean.py combined_logs.csv --out RCM_analysis_clean

When two or more original logs are supplied, the analyzer also writes:
- per_run_pass_fail_checks.csv: the nine checks evaluated separately per run;
- repeatability_summary.csv: mean, sample SD, range and pass rate across runs;
- 98_repeatability_normalized.png: all runs compared with their thresholds;
- repeatability_summary_en.md: short report-ready interpretation.

Required packages:
    pip install pandas numpy matplotlib
"""

from __future__ import annotations

import argparse
import glob
import math
from pathlib import Path
from typing import Iterable, List, Optional, Sequence

import numpy as np
import pandas as pd
import matplotlib.pyplot as plt


TASK_ORDER = [
    "T3_safe_insertion",
    "T1_entry_rcm_tip_target",
    "T2_target_rcm_entry_cone",
    "T4_entry_rcm_tip_cone",
    "Hold",
]

TASK_LABELS = {
    "T1_entry_rcm_tip_target": "Task 1 — Entry-RCM + tip target",
    "T2_target_rcm_entry_cone": "Task 2 — Target-RCM + entry cone",
    "T3_safe_insertion": "Task 3 — Safe insertion",
    "T4_entry_rcm_tip_cone": "Task 4 — Entry-RCM + tip cone",
    "Hold": "Hold",
}

FALLBACK_RENAMES = {
    "tip_error_mm": "tip_target_error_mm",
    "cone_error_mm": "active_task_error_mm",
    "qdot_norm": "qdot_norm_rad_s",
}

ANALYSIS_OUTPUT_PREFIXES = (
    "summary_",
    "pass_fail_",
    "per_run_",
    "repeatability_",
    "combined_logs",
    "validation_",
)


def discover_csvs(inputs: Iterable[str]) -> List[Path]:
    files: List[Path] = []
    for raw in inputs:
        p = Path(raw)
        if p.is_dir():
            candidates = sorted(p.glob("*.csv"))
            # If a directory is passed, avoid accidentally re-reading analysis outputs.
            candidates = [
                f for f in candidates
                if not f.name.startswith(ANALYSIS_OUTPUT_PREFIXES)
            ]
            files.extend(candidates)
        else:
            matches = [Path(x) for x in sorted(glob.glob(raw))]
            if matches:
                files.extend(matches)
            elif p.exists():
                files.append(p)

    seen = set()
    unique: List[Path] = []
    for f in files:
        if f.is_file() and f.resolve() not in seen:
            seen.add(f.resolve())
            unique.append(f)
    return unique


def load_logs(files: List[Path]) -> pd.DataFrame:
    frames = []
    for path in files:
        # comment="#" skips the metadata line the Unity controller now writes before the
        # header (e.g. "# entry_rcm threshold = 2.00 mm, ..."). Without this, pandas would
        # read that line as the header and every column would come back empty/NaN.
        df = pd.read_csv(path, comment="#")
        df = df.rename(columns={k: v for k, v in FALLBACK_RENAMES.items() if k in df.columns and v not in df.columns})
        df["log_file"] = path.name

        if "task" not in df.columns:
            df["task"] = df.get("mode", "unknown").astype(str)
        if "phase" not in df.columns:
            df["phase"] = "unknown"
        if "mode" not in df.columns:
            df["mode"] = df["task"].astype(str)

        if "time_s" in df.columns:
            df["time_rel_s"] = pd.to_numeric(df["time_s"], errors="coerce") - pd.to_numeric(df["time_s"], errors="coerce").iloc[0]
        elif "time_rel_s" not in df.columns:
            df["time_rel_s"] = np.arange(len(df), dtype=float)

        frames.append(df)

    if not frames:
        raise FileNotFoundError("No CSV logs found. Pass a CSV file, a glob, or the RCM_logs folder.")

    all_df = pd.concat(frames, ignore_index=True, sort=False)

    # Convert likely numeric columns.
    for c in all_df.columns:
        if (
            c.endswith("_mm")
            or c.endswith("_s")
            or c.endswith("_deg")
            or c.endswith("_rad_s")
            or c in {"lambda", "fps", "lambdadot", "insertion_progress", "time_rel_s"}
        ):
            all_df[c] = pd.to_numeric(all_df[c], errors="coerce")

    return all_df.sort_values(["log_file", "time_rel_s"], kind="mergesort").reset_index(drop=True)


def available(df: pd.DataFrame, col: str) -> bool:
    return col in df.columns and pd.to_numeric(df[col], errors="coerce").notna().any()


def task_mask(df: pd.DataFrame, task: str) -> pd.Series:
    return df.get("task", pd.Series([""] * len(df))).astype(str).eq(task)


def local_time(g: pd.DataFrame) -> pd.Series:
    t = pd.to_numeric(g["time_rel_s"], errors="coerce")
    return t - t.min()


def metric_stats(series: pd.Series) -> dict:
    s = pd.to_numeric(series, errors="coerce").dropna()
    if s.empty:
        return {"mean": np.nan, "median": np.nan, "p95": np.nan, "max": np.nan, "final": np.nan}
    return {
        "mean": float(s.mean()),
        "median": float(s.median()),
        "p95": float(s.quantile(0.95)),
        "max": float(s.max()),
        "final": float(s.iloc[-1]),
    }


def fmt(x: Optional[float], unit: str = "mm") -> str:
    if x is None or (isinstance(x, float) and (math.isnan(x) or math.isinf(x))):
        return "n/a"
    return f"{x:.1f} {unit}"


def best_value(df: pd.DataFrame, mask: pd.Series, column: str, stat: str = "max") -> Optional[float]:
    if column not in df.columns:
        return None
    s = pd.to_numeric(df.loc[mask, column], errors="coerce").dropna()
    if s.empty:
        return None
    if stat == "final":
        return float(s.iloc[-1])
    if stat == "mean":
        return float(s.mean())
    if stat == "p95":
        return float(s.quantile(0.95))
    return float(s.max())


def save_fig(path: Path) -> None:
    plt.tight_layout()
    plt.savefig(path, dpi=190)
    plt.close()


def add_phase_markers(g: pd.DataFrame, y_top: float) -> None:
    if "phase" not in g.columns:
        return
    phases = g["phase"].astype(str).fillna("unknown")
    changes = phases.ne(phases.shift()).to_numpy().nonzero()[0]
    for idx in changes[1:]:
        x = float(local_time(g).iloc[idx])
        plt.axvline(x, linestyle="--", linewidth=0.9, alpha=0.35)
        label = str(phases.iloc[idx])
        if label and label != "unknown":
            plt.text(x, y_top, label, rotation=90, va="top", ha="right", fontsize=8, alpha=0.65)


def plot_task_metric(
    df: pd.DataFrame,
    out_dir: Path,
    *,
    task: str,
    metric: str,
    filename: str,
    title: str,
    ylabel: str = "Error [mm]",
    threshold: Optional[float] = None,
    phase_filter: Optional[Sequence[str]] = None,
    show_phase_markers: bool = False,
) -> None:
    if not available(df, metric):
        return
    g = df[task_mask(df, task)].copy()
    if phase_filter is not None and "phase" in g.columns:
        g = g[g["phase"].astype(str).isin(phase_filter)]
    g = g.dropna(subset=["time_rel_s", metric])
    if g.empty:
        return

    y = pd.to_numeric(g[metric], errors="coerce")
    max_val = float(y.max())
    y_limit = max(max_val, threshold or 0.0, 1e-6) * 1.18

    plt.figure(figsize=(10.5, 4.8))
    run_groups = list(g.groupby("log_file", sort=False)) if "log_file" in g.columns else [("run", g)]
    final_values = []
    for run_index, (log_file, run) in enumerate(run_groups, start=1):
        run_y = pd.to_numeric(run[metric], errors="coerce")
        if run_y.dropna().empty:
            continue
        final_values.append(float(run_y.dropna().iloc[-1]))
        label = metric.replace("_", " ") if len(run_groups) == 1 else f"Run {run_index}"
        plt.plot(local_time(run), run_y, linewidth=1.7, label=label)
    if threshold is not None:
        plt.axhline(threshold, linestyle="--", linewidth=1.2, label=f"threshold = {threshold:g} mm")
    if show_phase_markers and len(run_groups) == 1:
        add_phase_markers(g, y_limit)

    plt.title(title)
    plt.xlabel("Local task time [s]")
    plt.ylabel(ylabel)
    plt.ylim(bottom=min(0.0, float(y.min()) * 1.1), top=y_limit)
    plt.grid(True, alpha=0.28)
    plt.legend(loc="best", fontsize=9)
    if len(final_values) > 1:
        final_text = f"final mean = {np.mean(final_values):.1f} mm\nfinal SD = {np.std(final_values, ddof=1):.1f} mm"
    elif final_values:
        final_text = f"final = {final_values[0]:.1f} mm"
    else:
        final_text = "final = n/a"
    plt.text(
        0.99,
        0.95,
        f"worst max = {max_val:.1f} mm\n{final_text}",
        transform=plt.gca().transAxes,
        ha="right",
        va="top",
        fontsize=9,
        bbox=dict(boxstyle="round,pad=0.35", facecolor="white", alpha=0.85),
    )
    save_fig(out_dir / filename)


def plot_global_metric(
    df: pd.DataFrame,
    out_dir: Path,
    *,
    metric: str,
    filename: str,
    title: str,
    ylabel: str,
    threshold: Optional[float] = None,
) -> None:
    if not available(df, metric):
        return
    g = df.dropna(subset=["time_rel_s", metric]).copy()
    if g.empty:
        return

    y = pd.to_numeric(g[metric], errors="coerce")
    max_val = float(y.max())
    y_limit = max(max_val, threshold or 0.0, 1e-6) * 1.18

    plt.figure(figsize=(10.5, 4.8))
    run_groups = list(g.groupby("log_file", sort=False)) if "log_file" in g.columns else [("run", g)]
    for run_index, (_, run) in enumerate(run_groups, start=1):
        label = metric.replace("_", " ") if len(run_groups) == 1 else f"Run {run_index}"
        plt.plot(local_time(run), pd.to_numeric(run[metric], errors="coerce"), linewidth=1.5, label=label)
    if threshold is not None:
        plt.axhline(threshold, linestyle="--", linewidth=1.2, label=f"threshold = {threshold:g}")
    plt.title(title)
    plt.xlabel("Time [s]")
    plt.ylabel(ylabel)
    plt.ylim(bottom=min(0.0, float(y.min()) * 1.1), top=y_limit)
    plt.grid(True, alpha=0.28)
    plt.legend(loc="best", fontsize=9)
    plt.text(
        0.99,
        0.95,
        f"worst max = {max_val:.2f}",
        transform=plt.gca().transAxes,
        ha="right",
        va="top",
        fontsize=9,
        bbox=dict(boxstyle="round,pad=0.35", facecolor="white", alpha=0.85),
    )
    save_fig(out_dir / filename)


def plot_joint_angles(df: pd.DataFrame, out_dir: Path) -> None:
    # Diagnostic plot, not part of the "for slides" set: shows all 6 joint angles over time
    # against their DH joint limits, to check whether a joint is pinned at its physical limit
    # during an error spike (as opposed to a numerical/damping issue, which wouldn't show this).
    # NOTE: these limits mirror the current SetDefaultDH() defaults in ROSADoubleRCMController.cs.
    # If you changed qMinDeg/qMaxDeg in the Inspector, update JOINT_LIMITS_DEG below to match.
    JOINT_LIMITS_DEG = [
        (-170.0, 170.0),
        (-120.0, 120.0),
        (-150.0, 150.0),
        (-170.0, 170.0),
        (-120.0, 120.0),
        (-360.0, 360.0),
    ]
    cols = [f"q{i}_deg" for i in range(1, 7)]
    if not all(available(df, c) for c in cols) or "time_rel_s" not in df.columns:
        return
    g = df.dropna(subset=["time_rel_s"] + cols).copy()
    if g.empty:
        return

    fig, axes = plt.subplots(2, 3, figsize=(15.5, 7.5), sharex=True)
    for i, ax in enumerate(axes.flat):
        col = cols[i]
        qmin, qmax = JOINT_LIMITS_DEG[i]
        y = pd.to_numeric(g[col], errors="coerce")
        ax.plot(g["time_rel_s"], y, linewidth=1.4)
        ax.axhline(qmin, linestyle="--", linewidth=1.0, color="crimson", alpha=0.7)
        ax.axhline(qmax, linestyle="--", linewidth=1.0, color="crimson", alpha=0.7)
        margin = max(1.0, 0.05 * (qmax - qmin))
        near_limit = ((y >= qmax - margin) | (y <= qmin + margin)).any()
        title = f"Joint {i + 1}" + ("  ⚠ near limit" if near_limit else "")
        ax.set_title(title, fontsize=10, color=("crimson" if near_limit else "black"))
        ax.set_ylabel("deg")
        ax.grid(True, alpha=0.25)
    for ax in axes[-1, :]:
        ax.set_xlabel("Time [s]")
    fig.suptitle("Joint angles vs. DH limits (diagnostic)", fontsize=13)
    save_fig(out_dir / "14_joint_angles_diagnostic.png")


def plot_nullspace_vs_task(df: pd.DataFrame, out_dir: Path) -> None:
    """Compare the two pre-saturation velocity components during Task 2 only."""
    task_col = "raw_task_qdot_norm_rad_s"
    null_col = "nullspace_joint_contrib_norm_rad_s"
    if not available(df, task_col) or not available(df, null_col):
        return
    g = df[task_mask(df, "T2_target_rcm_entry_cone")].copy()
    g = g.dropna(subset=["time_rel_s", task_col, null_col])
    if g.empty:
        return

    x = local_time(g)
    task_y = pd.to_numeric(g[task_col], errors="coerce")
    null_y = pd.to_numeric(g[null_col], errors="coerce")
    plt.figure(figsize=(10.5, 4.8))
    plt.plot(x, task_y, linewidth=1.8, label="raw primary-task qdot norm")
    plt.plot(x, null_y, linewidth=1.8, label="projected nullspace contribution norm")
    plt.title("Task 2 — primary task vs. nullspace joint velocity")
    plt.xlabel("Local task time [s]")
    plt.ylabel("Velocity norm [rad/s]")
    plt.grid(True, alpha=0.28)
    plt.legend(loc="best", fontsize=9)
    save_fig(out_dir / "15_nullspace_vs_task_diagnostic.png")


def plot_task2_conditioning(df: pd.DataFrame, out_dir: Path) -> None:
    condition_col = "jacobian_effective_condition_number"
    sigma_col = "jacobian_sigma_min_nonzero"
    if not available(df, condition_col) or not available(df, sigma_col):
        return
    g = df[task_mask(df, "T2_target_rcm_entry_cone")].copy()
    g = g.dropna(subset=["time_rel_s", condition_col, sigma_col])
    if g.empty:
        return
    x = local_time(g)
    fig, left = plt.subplots(figsize=(10.5, 4.8))
    right = left.twinx()
    line1 = left.plot(x, pd.to_numeric(g[condition_col]), linewidth=1.8,
                      label="effective condition number", color="tab:red")
    line2 = right.plot(x, pd.to_numeric(g[sigma_col]), linewidth=1.8,
                       label="smallest nonzero singular value", color="tab:blue")
    left.set_title("Task 2 — primary Jacobian conditioning")
    left.set_xlabel("Local task time [s]")
    left.set_ylabel("Condition number", color="tab:red")
    right.set_ylabel("Smallest singular value", color="tab:blue")
    left.grid(True, alpha=0.28)
    left.legend(line1 + line2, [line.get_label() for line in line1 + line2], loc="best", fontsize=9)
    save_fig(out_dir / "16_task2_jacobian_conditioning.png")


def plot_timeline(df: pd.DataFrame, out_dir: Path) -> None:
    if "task" not in df.columns or "time_rel_s" not in df.columns:
        return
    tasks = [t for t in TASK_ORDER if t in set(df["task"].astype(str))]
    tasks += [t for t in dict.fromkeys(df["task"].astype(str)) if t not in tasks]
    mapping = {name: i for i, name in enumerate(tasks)}
    y = df["task"].astype(str).map(mapping)

    plt.figure(figsize=(11.5, 4.0))
    plt.step(df["time_rel_s"], y, where="post", linewidth=2.0)
    plt.yticks(list(mapping.values()), [TASK_LABELS.get(t, t) for t in tasks], fontsize=8)
    plt.xlabel("Time [s]")
    plt.title("Executed task timeline")
    plt.grid(True, axis="x", alpha=0.3)
    save_fig(out_dir / "00_task_timeline.png")


def plot_active_error(df: pd.DataFrame, out_dir: Path, cone_thr: float) -> None:
    if not available(df, "active_task_error_mm"):
        return
    # This is not a combined-error plot: it shows only the controller-selected active task error.
    plot_global_metric(
        df,
        out_dir,
        metric="active_task_error_mm",
        filename="01_active_task_error_only.png",
        title="Active task tracking error only",
        ylabel="Active task error [mm]",
        threshold=cone_thr,
    )


def pass_fail_summary(
    df: pd.DataFrame,
    entry_thr: float,
    tip_thr: float,
    cone_thr: float,
    skull_thr: float,
    settle_time_s: float = 1.0,
) -> pd.DataFrame:
    rows = []

    # Exclude samples within `settle_time_s` of the start of each (log_file, task, phase) run
    # from the max/steady-state check. A "max" computed over the whole task window is dominated
    # by the transient right after switching mode/phase (the arm still converging from wherever
    # it was before), not by the controller's actual tracking accuracy — the same way a real
    # clinical accuracy measurement is taken after the instrument has settled, not mid-motion.
    # Set settle_time_s=0 to recover the old (transient-inclusive) behavior.
    df = df.copy()
    local_t = df.groupby(["log_file", "task", "phase"], dropna=False)["time_rel_s"].transform(
        lambda s: s - s.min()
    )
    settled = local_t >= settle_time_s

    def add(name: str, mask: pd.Series, metric: str, threshold: float, unit: str = "mm") -> None:
        if not available(df, metric):
            return
        s = pd.to_numeric(df.loc[mask & settled, metric], errors="coerce").dropna()
        if s.empty:
            return
        max_value = float(s.max())
        rows.append({
            "check": name,
            "metric": metric,
            "max_value": max_value,
            "threshold": threshold,
            "unit": unit,
            "passed": bool(max_value <= threshold),
            "samples": int(len(s)),
        })

    task = df.get("task", pd.Series([""] * len(df))).astype(str)
    phase = df.get("phase", pd.Series([""] * len(df))).astype(str)
    add("T1 entry RCM stability", task.eq("T1_entry_rcm_tip_target"), "entry_rcm_error_mm", entry_thr)
    add("T1 tip reaches target", task.eq("T1_entry_rcm_tip_target"), "tip_target_error_mm", tip_thr)
    add("T2 entry-cone tracking", task.eq("T2_target_rcm_entry_cone"), "task2_entry_cone_error_mm", cone_thr)
    add("T2 tip fixed at target", task.eq("T2_target_rcm_entry_cone"), "tip_target_error_mm", tip_thr)
    add("T3 insertion Entry-RCM", task.eq("T3_safe_insertion") & phase.isin(["InsertToTarget", "Done"]), "entry_rcm_error_mm", entry_thr)
    add("T3 final target", task.eq("T3_safe_insertion") & phase.eq("Done"), "tip_target_error_mm", tip_thr)
    add("T3 skull/corridor safety", task.eq("T3_safe_insertion"), "skull_violation_mm", skull_thr)
    add("T4 Entry-RCM stability", task.eq("T4_entry_rcm_tip_cone"), "entry_rcm_error_mm", entry_thr)
    add("T4 tip-cone tracking", task.eq("T4_entry_rcm_tip_cone"), "task4_tip_cone_error_mm", cone_thr)
    return pd.DataFrame(rows)


def pass_fail_by_run(
    df: pd.DataFrame,
    entry_thr: float,
    tip_thr: float,
    cone_thr: float,
    skull_thr: float,
    settle_time_s: float = 1.0,
) -> pd.DataFrame:
    """Evaluate the same nine validation checks independently for every log."""
    rows = []
    for run_index, (log_file, run_df) in enumerate(df.groupby("log_file", sort=False), start=1):
        checks = pass_fail_summary(
            run_df,
            entry_thr=entry_thr,
            tip_thr=tip_thr,
            cone_thr=cone_thr,
            skull_thr=skull_thr,
            settle_time_s=settle_time_s,
        )
        if checks.empty:
            continue
        checks.insert(0, "log_file", log_file)
        checks.insert(0, "run", run_index)
        rows.append(checks)
    return pd.concat(rows, ignore_index=True) if rows else pd.DataFrame()


def summarize_repeatability(per_run_checks: pd.DataFrame) -> pd.DataFrame:
    """Summarize between-run variation of the settled maximum for each check."""
    if per_run_checks.empty:
        return pd.DataFrame()

    order = {name: i for i, name in enumerate(dict.fromkeys(per_run_checks["check"]))}
    rows = []
    group_cols = ["check", "metric", "threshold", "unit"]
    for keys, g in per_run_checks.groupby(group_cols, sort=False, dropna=False):
        check, metric, threshold, unit = keys
        values = pd.to_numeric(g["max_value"], errors="coerce").dropna()
        if values.empty:
            continue
        mean = float(values.mean())
        sample_sd = float(values.std(ddof=1)) if len(values) > 1 else np.nan
        rows.append({
            "check": check,
            "metric": metric,
            "n_runs": int(len(values)),
            "mean_max": mean,
            "sd_max": sample_sd,
            "median_max": float(values.median()),
            "best_max": float(values.min()),
            "worst_max": float(values.max()),
            "range_max": float(values.max() - values.min()),
            "cv_percent": (100.0 * sample_sd / abs(mean)) if len(values) > 1 and abs(mean) > 1e-12 else np.nan,
            "threshold": float(threshold),
            "unit": unit,
            "pass_runs": int(g["passed"].astype(bool).sum()),
            "pass_rate_percent": float(100.0 * g["passed"].astype(bool).mean()),
            "all_pass": bool(g["passed"].astype(bool).all()),
        })

    out = pd.DataFrame(rows)
    if not out.empty:
        out["_order"] = out["check"].map(order)
        out = out.sort_values("_order").drop(columns="_order").reset_index(drop=True)
    return out


def plot_repeatability(per_run_checks: pd.DataFrame, out_dir: Path) -> None:
    """Plot every run and mean +/- SD, normalized by each positive threshold."""
    if per_run_checks.empty:
        return
    g = per_run_checks[per_run_checks["threshold"] > 0].copy()
    if g.empty:
        return

    check_order = list(dict.fromkeys(g["check"]))
    check_to_y = {name: i for i, name in enumerate(check_order)}
    runs = list(dict.fromkeys(g["run"]))
    offsets = np.linspace(-0.20, 0.20, len(runs)) if len(runs) > 1 else np.array([0.0])
    colors = plt.get_cmap("tab10")(np.arange(len(runs)) % 10)

    plt.figure(figsize=(11.8, max(5.4, 0.65 * len(check_order) + 1.8)))
    for offset, color, run_number in zip(offsets, colors, runs):
        run = g[g["run"] == run_number].copy()
        x = 100.0 * run["max_value"] / run["threshold"]
        y = run["check"].map(check_to_y).astype(float) + offset
        plt.scatter(x, y, s=42, color=color, alpha=0.90, label=f"Run {int(run_number)}", zorder=3)

    for check, check_df in g.groupby("check", sort=False):
        ratios = 100.0 * check_df["max_value"] / check_df["threshold"]
        mean = float(ratios.mean())
        sd = float(ratios.std(ddof=1)) if len(ratios) > 1 else 0.0
        y = check_to_y[check]
        plt.errorbar(mean, y, xerr=sd, fmt="D", color="black", markersize=5,
                     capsize=4, linewidth=1.4, zorder=4)

    short_labels = [
        name.replace(" stability", "").replace(" tracking", "")
        for name in check_order
    ]
    plt.axvline(100.0, linestyle="--", linewidth=1.4, color="crimson", label="Threshold")
    plt.yticks(np.arange(len(check_order)), short_labels, fontsize=9)
    plt.xlabel("Settled maximum / threshold [%]")
    plt.title("Repeatability across runs (black diamond = mean, bar = sample SD)")
    plt.grid(True, axis="x", alpha=0.25)
    plt.legend(loc="best", ncol=2, fontsize=8)
    plt.xlim(left=0)
    save_fig(out_dir / "98_repeatability_normalized.png")


def write_repeatability_summary(summary: pd.DataFrame, out_dir: Path) -> None:
    lines = [
        "# Multi-run repeatability summary",
        "",
        "The same nine validation checks were evaluated independently for each log after the configured settling exclusion.",
        "`pass_fail_checks.csv` remains the conservative pooled result: its maximum is the worst sample across all supplied runs.",
        "",
    ]
    if summary.empty:
        lines.append("Only one run (or no complete checks) was available; no between-run summary could be computed.")
    else:
        lines.extend([
            "| Check | Runs passed | Mean of run maxima | Sample SD | Best-worst range |",
            "|---|---:|---:|---:|---:|",
        ])
        for _, r in summary.iterrows():
            sd_text = "n/a" if pd.isna(r["sd_max"]) else f"{r['sd_max']:.3f}"
            lines.append(
                f"| {r['check']} | {int(r['pass_runs'])}/{int(r['n_runs'])} "
                f"({r['pass_rate_percent']:.0f}%) | {r['mean_max']:.3f} {r['unit']} | "
                f"{sd_text} {r['unit']} | {r['best_max']:.3f}-{r['worst_max']:.3f} {r['unit']} |"
            )
        lines.extend([
            "",
            "Interpretation: a 100% pass rate demonstrates repeatable satisfaction of the declared thresholds over the tested runs. "
            "The sample SD and range quantify numerical repeatability; with five runs they are descriptive, not population-level clinical statistics.",
        ])
    (out_dir / "repeatability_summary_en.md").write_text("\n".join(lines), encoding="utf-8")


def plot_validation_summary(checks: pd.DataFrame, out_dir: Path) -> None:
    if checks.empty:
        return
    # Normalize only metrics with a positive threshold. Skull has threshold 0 and is summarized in the table/markdown.
    g = checks[checks["threshold"] > 0].copy()
    if g.empty:
        return
    g["ratio_percent"] = 100.0 * g["max_value"] / g["threshold"]
    labels = [x.replace(" stability", "").replace(" tracking", "") for x in g["check"]]

    plt.figure(figsize=(10.5, 5.4))
    y_pos = np.arange(len(g))
    plt.barh(y_pos, g["ratio_percent"])
    plt.axvline(100.0, linestyle="--", linewidth=1.2, label="threshold")
    plt.yticks(y_pos, labels, fontsize=8)
    plt.xlabel("Max value / threshold [%]")
    plt.title("Validation summary normalized by threshold")
    plt.grid(True, axis="x", alpha=0.25)
    plt.legend(loc="lower right")
    for i, (_, r) in enumerate(g.iterrows()):
        plt.text(
            r["ratio_percent"] + 1.5,
            i,
            f"{r['max_value']:.1f}/{r['threshold']:.0f} mm",
            va="center",
            fontsize=8,
        )
    save_fig(out_dir / "99_validation_summary_normalized.png")


def summarize_by_task(df: pd.DataFrame) -> pd.DataFrame:
    metrics = [
        "tip_target_error_mm",
        "entry_rcm_error_mm",
        "target_rcm_error_mm",
        "task2_entry_cone_error_mm",
        "task4_tip_cone_error_mm",
        "active_task_error_mm",
        "skull_violation_mm",
        "qdot_norm_rad_s",
        "lambda",
        "fps",
    ]
    rows = []
    for (log_file, task, phase), g in df.groupby(["log_file", "task", "phase"], dropna=False):
        row = {
            "log_file": log_file,
            "task": task,
            "phase": phase,
            "samples": len(g),
            "duration_s": float(g["time_rel_s"].max() - g["time_rel_s"].min()) if "time_rel_s" in g else np.nan,
        }
        for m in metrics:
            if available(g, m):
                st = metric_stats(g[m])
                row[f"{m}_max"] = st["max"]
                row[f"{m}_mean"] = st["mean"]
                row[f"{m}_final"] = st["final"]
        rows.append(row)
    out = pd.DataFrame(rows)
    if not out.empty:
        out["task_order"] = out["task"].map({t: i for i, t in enumerate(TASK_ORDER)}).fillna(999)
        out = out.sort_values(["log_file", "task_order", "phase"]).drop(columns=["task_order"])
    return out


def write_slide_summary(df: pd.DataFrame, checks: pd.DataFrame, out_dir: Path) -> None:
    task = df.get("task", pd.Series([""] * len(df))).astype(str)
    phase = df.get("phase", pd.Series([""] * len(df))).astype(str)
    t1 = task.eq("T1_entry_rcm_tip_target")
    t2 = task.eq("T2_target_rcm_entry_cone")
    t3 = task.eq("T3_safe_insertion")
    t3_insert = t3 & phase.isin(["InsertToTarget", "Done"])
    t4 = task.eq("T4_entry_rcm_tip_cone")

    lines = [
        "# Clean slide-ready results",
        "",
        "## Main message",
        "- The analyzer now avoids the confusing all-errors plot.",
        "- Each figure focuses on one task metric, with its own threshold and maximum value.",
        "- This makes the plots easier to explain during the presentation.",
        "",
        "## Key numbers",
        f"- Task 1 — max Entry-RCM error: {fmt(best_value(df, t1, 'entry_rcm_error_mm'))}; max tip-target error: {fmt(best_value(df, t1, 'tip_target_error_mm'))}.",
        f"- Task 2 — max entry-cone error: {fmt(best_value(df, t2, 'task2_entry_cone_error_mm'))}; max tip-target error: {fmt(best_value(df, t2, 'tip_target_error_mm'))}.",
        f"- Task 3 — max insertion Entry-RCM error: {fmt(best_value(df, t3_insert, 'entry_rcm_error_mm'))}; max skull violation: {fmt(best_value(df, t3, 'skull_violation_mm'))}; final target error: {fmt(best_value(df, t3 & phase.eq('Done'), 'tip_target_error_mm', 'final'))}.",
        f"- Task 4 — max Entry-RCM error: {fmt(best_value(df, t4, 'entry_rcm_error_mm'))}; max tip-cone error: {fmt(best_value(df, t4, 'task4_tip_cone_error_mm'))}.",
        "",
        "## Recommended figures",
        "- 00_task_timeline.png",
        "- 01_active_task_error_only.png",
        "- 02_T3_entry_rcm_error.png",
        "- 03_T3_skull_violation.png",
        "- 04_T2_entry_cone_error.png",
        "- 05_T2_tip_target_error.png",
        "- 06_T4_entry_rcm_error.png",
        "- 07_T4_tip_cone_error.png",
        "- 99_validation_summary_normalized.png",
        "",
        "## Automatic checks",
    ]
    if checks.empty:
        lines.append("- No checks available.")
    else:
        for _, r in checks.iterrows():
            status = "PASS" if bool(r["passed"]) else "FAIL"
            lines.append(f"- {status}: {r['check']} — max {r['metric']} = {r['max_value']:.1f} {r['unit']}, threshold {r['threshold']:.1f} {r['unit']}.")

    (out_dir / "slide_summary_clean_en.md").write_text("\n".join(lines), encoding="utf-8")


def write_readme(out_dir: Path) -> None:
    text = """# Clean Multi-RCM log analysis

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
- `98_repeatability_normalized.png` — multi-run comparison, with every run plus mean and sample SD.

## Tables

- `pass_fail_checks.csv` — conservative pooled checks (worst settled value across all runs).
- `per_run_pass_fail_checks.csv` — the same nine checks evaluated independently for every run.
- `repeatability_summary.csv` — mean, sample SD, range and pass rate across runs.
- `summary_by_task_phase.csv` — detailed task/phase statistics.
- `combined_logs.csv` — merged input logs.
- `slide_summary_clean_en.md` — text ready to paste into slides or notes.
- `repeatability_summary_en.md` — report-ready multi-run summary.
"""
    (out_dir / "README_CLEAN_ANALYZER.md").write_text(text, encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate clean, one-metric-per-plot analysis for Multi-RCM Unity logs.")
    parser.add_argument("inputs", nargs="+", help="CSV file(s), glob(s), or RCM_logs folder")
    parser.add_argument("--out", default="RCM_analysis_clean", help="Output folder")
    parser.add_argument("--entry-threshold-mm", type=float, default=2.0)
    parser.add_argument("--tip-threshold-mm", type=float, default=3.0)
    parser.add_argument("--cone-threshold-mm", type=float, default=50.0)
    parser.add_argument("--skull-threshold-mm", type=float, default=0.0)
    parser.add_argument(
        "--settle-time-s",
        type=float,
        default=1.0,
        help="Ignore this many seconds after each task/phase switch when computing the "
             "pass/fail max (excludes the mode-change transient). Use 0 to disable.",
    )
    args = parser.parse_args()

    files = discover_csvs(args.inputs)
    if not files:
        raise SystemExit("No CSV files found. Example: python analyze_rcm_logs_clean.py RCM_logs --out RCM_analysis_clean")

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    df = load_logs(files)
    df.to_csv(out_dir / "combined_logs.csv", index=False)

    checks = pass_fail_summary(
        df,
        entry_thr=args.entry_threshold_mm,
        tip_thr=args.tip_threshold_mm,
        cone_thr=args.cone_threshold_mm,
        skull_thr=args.skull_threshold_mm,
        settle_time_s=args.settle_time_s,
    )
    checks.to_csv(out_dir / "pass_fail_checks.csv", index=False)
    summarize_by_task(df).to_csv(out_dir / "summary_by_task_phase.csv", index=False)

    per_run_checks = pass_fail_by_run(
        df,
        entry_thr=args.entry_threshold_mm,
        tip_thr=args.tip_threshold_mm,
        cone_thr=args.cone_threshold_mm,
        skull_thr=args.skull_threshold_mm,
        settle_time_s=args.settle_time_s,
    )
    per_run_checks.to_csv(out_dir / "per_run_pass_fail_checks.csv", index=False)
    repeatability = summarize_repeatability(per_run_checks)
    repeatability.to_csv(out_dir / "repeatability_summary.csv", index=False)

    plot_timeline(df, out_dir)
    plot_active_error(df, out_dir, cone_thr=args.cone_threshold_mm)
    plot_joint_angles(df, out_dir)
    plot_nullspace_vs_task(df, out_dir)
    plot_task2_conditioning(df, out_dir)

    # Task 1: each relevant metric gets its own plot.
    plot_task_metric(df, out_dir, task="T1_entry_rcm_tip_target", metric="entry_rcm_error_mm", filename="08_T1_entry_rcm_error.png", title="Task 1 — Entry-RCM stability", threshold=args.entry_threshold_mm)
    plot_task_metric(df, out_dir, task="T1_entry_rcm_tip_target", metric="tip_target_error_mm", filename="09_T1_tip_target_error.png", title="Task 1 — tip-target tracking", threshold=args.tip_threshold_mm)

    # Task 2.
    plot_task_metric(df, out_dir, task="T2_target_rcm_entry_cone", metric="task2_entry_cone_error_mm", filename="04_T2_entry_cone_error.png", title="Task 2 — entry-cone tracking", threshold=args.cone_threshold_mm)
    plot_task_metric(df, out_dir, task="T2_target_rcm_entry_cone", metric="tip_target_error_mm", filename="05_T2_tip_target_error.png", title="Task 2 — tip fixed at target", threshold=args.tip_threshold_mm)
    plot_task_metric(df, out_dir, task="T2_target_rcm_entry_cone", metric="target_rcm_error_mm", filename="05b_T2_target_rcm_error.png", title="Task 2 — Target-RCM stability", threshold=args.entry_threshold_mm)

    # Task 3.
    plot_task_metric(df, out_dir, task="T3_safe_insertion", metric="entry_rcm_error_mm", filename="02_T3_entry_rcm_error.png", title="Task 3 — Entry-RCM error during insertion", threshold=args.entry_threshold_mm, phase_filter=["InsertToTarget", "Done"], show_phase_markers=True)
    plot_task_metric(df, out_dir, task="T3_safe_insertion", metric="tip_target_error_mm", filename="02b_T3_tip_target_error.png", title="Task 3 — final tip-target error", threshold=args.tip_threshold_mm, phase_filter=["Done"], show_phase_markers=True)
    plot_task_metric(df, out_dir, task="T3_safe_insertion", metric="skull_violation_mm", filename="03_T3_skull_violation.png", title="Task 3 — skull/corridor violation", threshold=args.skull_threshold_mm, show_phase_markers=True)
    plot_task_metric(df, out_dir, task="T3_safe_insertion", metric="tip_line_distance_mm", filename="03b_T3_tip_line_distance.png", title="Task 3 — tip distance from entry-target line", ylabel="Distance [mm]", show_phase_markers=True)
    plot_task_metric(df, out_dir, task="T3_safe_insertion", metric="shaft_line_max_distance_mm", filename="03c_T3_shaft_line_distance.png", title="Task 3 — max shaft distance from entry-target line", ylabel="Distance [mm]", show_phase_markers=True)
    plot_task_metric(df, out_dir, task="T3_safe_insertion", metric="tool_axis_angle_deg", filename="03d_T3_tool_axis_angle.png", title="Task 3 — tool-axis angular error", ylabel="Angle [deg]", show_phase_markers=True)

    # Task 4.
    plot_task_metric(df, out_dir, task="T4_entry_rcm_tip_cone", metric="entry_rcm_error_mm", filename="06_T4_entry_rcm_error.png", title="Task 4 — Entry-RCM stability", threshold=args.entry_threshold_mm)
    plot_task_metric(df, out_dir, task="T4_entry_rcm_tip_cone", metric="task4_tip_cone_error_mm", filename="07_T4_tip_cone_error.png", title="Task 4 — tip-cone tracking", threshold=args.cone_threshold_mm)
    plot_task_metric(df, out_dir, task="T4_entry_rcm_tip_cone", metric="tip_target_error_mm", filename="07b_T4_tip_target_radius.png", title="Task 4 — tip distance from target center", threshold=args.tip_threshold_mm)

    # Kinematic / runtime plots are also separated.
    plot_global_metric(df, out_dir, metric="lambda", filename="10_lambda.png", title="Penetration coordinate lambda", ylabel="lambda")
    plot_global_metric(df, out_dir, metric="lambdadot", filename="11_lambdadot.png", title="Lambda rate", ylabel="lambda dot")
    plot_global_metric(df, out_dir, metric="qdot_norm_rad_s", filename="12_qdot_norm_rad_s.png", title="Joint velocity norm", ylabel="||qdot|| [rad/s]")
    plot_global_metric(df, out_dir, metric="fps", filename="13_fps.png", title="Runtime frame rate", ylabel="FPS")

    plot_validation_summary(checks, out_dir)
    plot_repeatability(per_run_checks, out_dir)
    write_slide_summary(df, checks, out_dir)
    write_repeatability_summary(repeatability, out_dir)
    write_readme(out_dir)

    print(f"Loaded {len(files)} CSV file(s), {len(df)} samples.")
    print(f"Clean analysis saved to: {out_dir.resolve()}")
    print("Main images: 00_task_timeline.png, 01_active_task_error_only.png, 02_T3_entry_rcm_error.png, 03_T3_skull_violation.png, 04_T2_entry_cone_error.png, 06_T4_entry_rcm_error.png")
    if len(files) > 1:
        print("Multi-run outputs: per_run_pass_fail_checks.csv, repeatability_summary.csv, 98_repeatability_normalized.png")


if __name__ == "__main__":
    main()
