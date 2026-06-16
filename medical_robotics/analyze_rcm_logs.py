#!/usr/bin/env python3
"""
analyze_rcm_logs.py
Automatic evaluator for Unity Multi-RCM / ROSA CSV logs.

Usage examples:
  python analyze_rcm_logs.py RCM_logs/*.csv --out rcm_report
  python analyze_rcm_logs.py paper_rcm_log.csv --out rcm_report --entry-max-mm 5 --skull-max-mm 0

The script is deliberately defensive: if a column is missing it prints a warning,
continues, and computes every metric that can be reconstructed from the CSV.
"""

from __future__ import annotations

import argparse
import math
import sys
import warnings
import glob
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

import numpy as np
import pandas as pd

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


# ----------------------------- configuration -----------------------------

COLUMN_ALIASES: Dict[str, Sequence[str]] = {
    "time": ("time", "t", "time_s", "timestamp"),
    "task": ("task", "task_id", "task_name"),
    "phase": ("phase", "insertion_phase"),
    "mode": ("mode", "rcm_mode"),
    "stage": ("stage", "state"),
    "fps": ("fps", "frame_rate", "unity_fps"),
    "dt": ("dt", "delta_time", "deltaTime"),
    "tip_entry_error_mm": ("tip_entry_error_mm", "tipEntryErrorMm"),
    "entry_rcm_error_mm": ("entry_rcm_error_mm", "entry_error_mm", "entryRCMFormulaErrorMm", "entryErrorMm"),
    "entry_axis_error_deg": ("entry_axis_error_deg", "entryTargetAxisErrorDeg", "axis_error_deg"),
    "target_rcm_or_tip_error_mm": ("target_rcm_or_tip_error_mm", "target_error_mm", "targetTipErrorMm", "target_rcm_error_mm"),
    "final_target_tip_error_mm": ("final_target_tip_error_mm", "final_target_error_mm", "finalTargetTipErrorMm"),
    "insertion_progress": ("insertion_progress", "progress"),
    "insertion_intermediate_error_mm": ("insertion_intermediate_error_mm", "intermediate_target_error_mm"),
    "entry_cone_angle_deg": ("entry_cone_angle_deg", "cone_angle_deg", "entryConeAngleDeg"),
    "entry_cone_violation_deg": ("entry_cone_violation_deg", "entryConeViolationDeg"),
    "skull_violation_mm": ("skull_violation_mm", "needle_skull_violation_mm", "skullViolationMm"),
    "arm_skull_violation_mm": ("arm_skull_violation_mm", "armSkullViolationMm"),
    "entry_lambda": ("entry_lambda", "entryLambda"),
    "target_lambda": ("target_lambda", "targetLambda"),
    "joint_limits_ok": ("joint_limits_ok", "jointLimitsOk"),
    "tool_x": ("tool_x", "tool_pos_x"),
    "tool_y": ("tool_y", "tool_pos_y"),
    "tool_z": ("tool_z", "tool_pos_z"),
    "tip_x": ("tip_x", "tip_pos_x"),
    "tip_y": ("tip_y", "tip_pos_y"),
    "tip_z": ("tip_z", "tip_pos_z"),
    "entry_x": ("entry_x", "entry_pos_x"),
    "entry_y": ("entry_y", "entry_pos_y"),
    "entry_z": ("entry_z", "entry_pos_z"),
    "target_x": ("target_x", "target_pos_x"),
    "target_y": ("target_y", "target_pos_y"),
    "target_z": ("target_z", "target_pos_z"),
}

Q_COLS = [f"q{i}_deg" for i in range(6)]
POS_GROUPS = {
    "tool": ("tool_x", "tool_y", "tool_z"),
    "tip": ("tip_x", "tip_y", "tip_z"),
    "entry": ("entry_x", "entry_y", "entry_z"),
    "target": ("target_x", "target_y", "target_z"),
}

ERROR_COLS = [
    "tip_entry_error_mm",
    "entry_rcm_error_mm",
    "entry_axis_error_deg",
    "target_rcm_or_tip_error_mm",
    "final_target_tip_error_mm",
    "insertion_intermediate_error_mm",
    "entry_cone_angle_deg",
    "entry_cone_violation_deg",
    "skull_violation_mm",
    "arm_skull_violation_mm",
    "total_skull_violation_mm",
]


@dataclass
class Thresholds:
    entry_max_mm: float = 10.0
    target_max_mm: float = 10.0
    final_target_max_mm: float = 8.0
    skull_max_mm: float = 0.0
    arm_skull_max_mm: float = 0.0
    min_fps: float = 30.0
    cone_min_angle_deg: float = 3.0
    cone_min_radius_mm: float = 8.0
    cone_min_angle_std_deg: float = 0.35
    cone_center_drift_max_mm: float = 8.0
    max_oscillations_per_s: float = 4.0
    steady_window_s: float = 5.0


# ----------------------------- helpers -----------------------------

def warn(msg: str) -> None:
    print(f"[WARN] {msg}", file=sys.stderr)


def info(msg: str) -> None:
    print(f"[INFO] {msg}")


def safe_name(s: str) -> str:
    out = "".join(c if c.isalnum() or c in "-_" else "_" for c in str(s))
    while "__" in out:
        out = out.replace("__", "_")
    return out.strip("_") or "unknown"


def find_col(df: pd.DataFrame, canonical: str) -> Optional[str]:
    aliases = COLUMN_ALIASES.get(canonical, (canonical,))
    lower = {c.lower(): c for c in df.columns}
    for a in aliases:
        if a in df.columns:
            return a
        if a.lower() in lower:
            return lower[a.lower()]
    return None


def canonicalize_columns(df: pd.DataFrame) -> pd.DataFrame:
    df = df.copy()
    df.columns = [str(c).strip() for c in df.columns]
    ren = {}
    for canonical in COLUMN_ALIASES:
        c = find_col(df, canonical)
        if c is not None and c != canonical:
            ren[c] = canonical
    df = df.rename(columns=ren)
    # Convert likely numeric columns to numeric. Keep mode/phase/stage as strings.
    for c in df.columns:
        if c in {"mode", "phase", "stage", "task", "source_file"}:
            df[c] = df[c].astype(str)
        else:
            df[c] = pd.to_numeric(df[c], errors="coerce")
    return df


def load_csv(path: Path) -> pd.DataFrame:
    try:
        df = pd.read_csv(path)
    except Exception as exc:
        raise RuntimeError(f"Cannot read {path}: {exc}") from exc
    df = canonicalize_columns(df)
    df["source_file"] = path.name
    if "time" not in df.columns:
        warn(f"{path.name}: missing 'time'. Using sample index as time.")
        df["time"] = np.arange(len(df), dtype=float)
    df = df.sort_values("time").reset_index(drop=True)
    return df


def has_cols(df: pd.DataFrame, cols: Sequence[str], label: str = "") -> bool:
    missing = [c for c in cols if c not in df.columns]
    if missing:
        warn(f"Missing columns for {label or 'metric'}: {missing}")
        return False
    return True


def vec(df: pd.DataFrame, prefix: str) -> Optional[np.ndarray]:
    cols = POS_GROUPS[prefix]
    if not has_cols(df, cols, prefix):
        return None
    return df.loc[:, cols].to_numpy(dtype=float)


def norm_rows(a: np.ndarray) -> np.ndarray:
    return np.linalg.norm(a, axis=1)


def unit_rows(a: np.ndarray) -> np.ndarray:
    n = norm_rows(a)
    out = np.zeros_like(a, dtype=float)
    ok = n > 1e-12
    out[ok] = a[ok] / n[ok, None]
    return out


def angle_deg_between(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    au = unit_rows(a)
    bu = unit_rows(b)
    dot = np.sum(au * bu, axis=1)
    dot = np.clip(dot, -1.0, 1.0)
    return np.degrees(np.arccos(dot))


def remove_nan_pair(t: np.ndarray, y: np.ndarray) -> Tuple[np.ndarray, np.ndarray]:
    mask = np.isfinite(t) & np.isfinite(y)
    return t[mask], y[mask]


def robust_dt(t: np.ndarray) -> Optional[float]:
    t = np.asarray(t, dtype=float)
    dt = np.diff(t)
    dt = dt[np.isfinite(dt) & (dt > 1e-9)]
    if dt.size == 0:
        return None
    return float(np.median(dt))


def compute_fps(df: pd.DataFrame) -> pd.Series:
    if "fps" in df.columns:
        fps = pd.to_numeric(df["fps"], errors="coerce")
        return fps
    if "dt" in df.columns:
        dt = pd.to_numeric(df["dt"], errors="coerce")
        return 1.0 / dt.replace(0, np.nan)
    t = df["time"].to_numpy(dtype=float)
    d = np.diff(t, prepend=np.nan)
    fps = 1.0 / d
    fps[~np.isfinite(fps)] = np.nan
    # This is the CSV sample frequency, not necessarily Unity's true render FPS.
    return pd.Series(fps, index=df.index)


def infer_task(row: pd.Series) -> str:
    # Prefer explicit task/stage if present.
    if "task" in row and str(row.get("task", "")).strip() not in {"", "nan", "None"}:
        return str(row["task"])
    m = str(row.get("mode", "")).lower()
    p = str(row.get("phase", "")).lower()
    s = str(row.get("stage", "")).lower()
    text = f"{m} {p} {s}"
    if "task2" in text or "t2" in text:
        return "Task2_TargetRCM_EntryCone"
    if "task3" in text or "t3" in text:
        return "Task3_Insertion"
    if "task4" in text or "t4" in text:
        return "Task4_EntryRCM_TipCone"
    if "entrytipcone" in m:
        return "Task4_EntryRCM_TipCone"
    if m == "target" or "targetrcm" in m:
        return "Task2_TargetRCM_EntryCone"
    if m == "double" or p in {"approachentry", "alignatentry", "inserttotarget"}:
        return "Task3_Insertion"
    if m == "entry":
        return "Task1_EntryRCM_TargetTip"
    return "Unknown"


def add_derived_columns(df: pd.DataFrame) -> pd.DataFrame:
    df = df.copy()
    df["fps_est"] = compute_fps(df)
    if "mode" not in df.columns:
        df["mode"] = "Unknown"
    if "phase" not in df.columns:
        df["phase"] = "Unknown"
    if "stage" not in df.columns:
        df["stage"] = "Unknown"
    df["task_inferred"] = df.apply(infer_task, axis=1)

    skull_parts = []
    if "skull_violation_mm" in df.columns:
        skull_parts.append(pd.to_numeric(df["skull_violation_mm"], errors="coerce"))
    if "arm_skull_violation_mm" in df.columns:
        skull_parts.append(pd.to_numeric(df["arm_skull_violation_mm"], errors="coerce"))
    if skull_parts:
        df["total_skull_violation_mm"] = pd.concat(skull_parts, axis=1).max(axis=1)
    else:
        warn("No skull violation column found. Add skull_violation_mm and arm_skull_violation_mm to the Unity CSV.")

    # Reconstruct cone metrics from tool/tip/entry/target positions.
    tool = vec(df, "tool") if all(c in df.columns for c in POS_GROUPS["tool"]) else None
    tip = vec(df, "tip") if all(c in df.columns for c in POS_GROUPS["tip"]) else None
    entry = vec(df, "entry") if all(c in df.columns for c in POS_GROUPS["entry"]) else None
    target = vec(df, "target") if all(c in df.columns for c in POS_GROUPS["target"]) else None

    if tool is not None and tip is not None and entry is not None and target is not None:
        tool_axis = unit_rows(tip - tool)
        # Task 2: target is the cone vertex; actual axis points from target to entry side.
        nominal_target_to_entry = entry - target
        L = norm_rows(nominal_target_to_entry)
        nominal_t2_u = unit_rows(nominal_target_to_entry)
        sign = np.sign(np.sum(tool_axis * nominal_t2_u, axis=1))
        sign[sign == 0] = 1
        actual_t2 = tool_axis * sign[:, None]
        angle_t2 = angle_deg_between(actual_t2, nominal_t2_u)
        radius_t2 = L * np.sin(np.radians(angle_t2)) * 1000.0
        entry_side_point = target + actual_t2 * L[:, None]
        entry_side_dist = norm_rows(entry_side_point - entry) * 1000.0
        df["cone_t2_angle_deg_geom"] = angle_t2
        df["cone_t2_radius_mm_geom"] = radius_t2
        df["entry_side_distance_to_entry_mm_geom"] = entry_side_dist

        # Task 4: entry is the cone vertex; physical tip rotates around nominal entry->target axis.
        nominal_entry_to_target = target - entry
        nominal_t4_u = unit_rows(nominal_entry_to_target)
        actual_entry_to_tip = tip - entry
        angle_t4 = angle_deg_between(actual_entry_to_tip, nominal_entry_to_target)
        # radial distance of tip from entry-target line.
        v = actual_entry_to_tip
        axial = np.sum(v * nominal_t4_u, axis=1)
        radial_vec = v - axial[:, None] * nominal_t4_u
        radius_t4 = norm_rows(radial_vec) * 1000.0
        df["cone_t4_angle_deg_geom"] = angle_t4
        df["cone_t4_radius_mm_geom"] = radius_t4
        df["tip_distance_to_target_mm_geom"] = norm_rows(tip - target) * 1000.0

        # Motion smoothness from tip trajectory.
        t = df["time"].to_numpy(dtype=float)
        dt = robust_dt(t)
        if dt is not None and len(df) >= 5:
            # np.gradient accepts non-uniform times; use edge_order only with enough samples.
            try:
                vel = np.gradient(tip, t, axis=0, edge_order=1)
                acc = np.gradient(vel, t, axis=0, edge_order=1)
                jerk = np.gradient(acc, t, axis=0, edge_order=1)
                df["tip_speed_mm_s"] = norm_rows(vel) * 1000.0
                df["tip_acc_mm_s2"] = norm_rows(acc) * 1000.0
                df["tip_jerk_mm_s3"] = norm_rows(jerk) * 1000.0
            except Exception as exc:
                warn(f"Could not compute trajectory derivatives: {exc}")
    else:
        warn("Position columns incomplete: geometric cone metrics and tip smoothness may be unavailable.")

    # Joint smoothness if q columns exist.
    q_cols = [c for c in Q_COLS if c in df.columns]
    if len(q_cols) >= 1 and len(df) >= 5:
        t = df["time"].to_numpy(dtype=float)
        q = df[q_cols].to_numpy(dtype=float)
        try:
            qd = np.gradient(q, t, axis=0, edge_order=1)
            qdd = np.gradient(qd, t, axis=0, edge_order=1)
            df["joint_speed_rms_deg_s"] = np.sqrt(np.nanmean(qd * qd, axis=1))
            df["joint_acc_rms_deg_s2"] = np.sqrt(np.nanmean(qdd * qdd, axis=1))
        except Exception as exc:
            warn(f"Could not compute joint derivatives: {exc}")
    return df


def stats_for_series(s: pd.Series) -> Dict[str, float]:
    x = pd.to_numeric(s, errors="coerce").dropna().to_numpy(dtype=float)
    if x.size == 0:
        return {"n": 0, "mean": np.nan, "std": np.nan, "max": np.nan, "p95": np.nan, "final": np.nan}
    return {
        "n": int(x.size),
        "mean": float(np.mean(x)),
        "std": float(np.std(x, ddof=0)),
        "max": float(np.max(x)),
        "p95": float(np.percentile(x, 95)),
        "final": float(x[-1]),
    }


def oscillations_per_second(df: pd.DataFrame, col: str) -> Optional[float]:
    if col not in df.columns or "time" not in df.columns:
        return None
    t, y = remove_nan_pair(df["time"].to_numpy(dtype=float), df[col].to_numpy(dtype=float))
    if len(y) < 8:
        return None
    # Ignore tiny derivative signs to avoid counting numeric noise.
    dy = np.diff(y)
    scale = np.nanstd(y)
    eps = max(1e-6, 0.02 * scale)
    sign = np.sign(dy)
    sign[np.abs(dy) < eps] = 0
    # Remove zeros by forward filling nearest non-zero sign.
    nonzero = sign[sign != 0]
    if nonzero.size < 3:
        return 0.0
    changes = np.sum(nonzero[1:] * nonzero[:-1] < 0)
    duration = max(float(t[-1] - t[0]), 1e-9)
    return float(changes / duration)


def steady_std(df: pd.DataFrame, col: str, window_s: float) -> Optional[float]:
    if col not in df.columns:
        return None
    t = df["time"].to_numpy(dtype=float)
    if len(t) == 0:
        return None
    end = np.nanmax(t)
    mask = t >= end - window_s
    x = pd.to_numeric(df.loc[mask, col], errors="coerce").dropna().to_numpy(dtype=float)
    if x.size < 3:
        return None
    return float(np.std(x, ddof=0))


def pass_fail(value: Optional[float], op: str, threshold: float) -> str:
    if value is None or not np.isfinite(value):
        return "NA"
    if op == "<=":
        return "PASS" if value <= threshold else "FAIL"
    if op == ">=":
        return "PASS" if value >= threshold else "FAIL"
    raise ValueError(op)


# ----------------------------- analysis -----------------------------

def summarize_group(df: pd.DataFrame, name: str, th: Thresholds) -> List[str]:
    lines: List[str] = []
    if df.empty:
        return lines
    t0, t1 = float(df["time"].min()), float(df["time"].max())
    lines.append(f"\n## {name}")
    lines.append(f"samples={len(df)} | duration_s={t1 - t0:.3f} | modes={sorted(set(map(str, df['mode'].dropna().unique())))} | phases={sorted(set(map(str, df['phase'].dropna().unique())))}")

    for col in ERROR_COLS + ["fps_est", "tip_speed_mm_s", "tip_acc_mm_s2", "tip_jerk_mm_s3", "joint_speed_rms_deg_s", "joint_acc_rms_deg_s2"]:
        if col not in df.columns:
            continue
        st = stats_for_series(df[col])
        if st["n"] == 0:
            continue
        lines.append(
            f"{col}: mean={st['mean']:.3f}, std={st['std']:.3f}, p95={st['p95']:.3f}, max={st['max']:.3f}, final={st['final']:.3f}"
        )

    # Main acceptance checks.
    checks: List[Tuple[str, Optional[float], str, float]] = []
    if "entry_rcm_error_mm" in df.columns:
        checks.append(("entry_rcm_max_mm", stats_for_series(df["entry_rcm_error_mm"])["max"], "<=", th.entry_max_mm))
    if "target_rcm_or_tip_error_mm" in df.columns:
        checks.append(("target_rcm_or_tip_max_mm", stats_for_series(df["target_rcm_or_tip_error_mm"])["max"], "<=", th.target_max_mm))
    if "final_target_tip_error_mm" in df.columns:
        checks.append(("final_target_final_mm", stats_for_series(df["final_target_tip_error_mm"])["final"], "<=", th.final_target_max_mm))
    if "total_skull_violation_mm" in df.columns:
        checks.append(("total_skull_violation_max_mm", stats_for_series(df["total_skull_violation_mm"])["max"], "<=", th.skull_max_mm))
    elif "skull_violation_mm" in df.columns:
        checks.append(("skull_violation_max_mm", stats_for_series(df["skull_violation_mm"])["max"], "<=", th.skull_max_mm))
    if "fps_est" in df.columns:
        fps_p05 = float(np.nanpercentile(df["fps_est"].to_numpy(dtype=float), 5)) if df["fps_est"].notna().any() else np.nan
        checks.append(("fps_p05", fps_p05, ">=", th.min_fps))

    if checks:
        lines.append("Acceptance checks:")
        for label, val, op, thr in checks:
            val_txt = "NA" if val is None or not np.isfinite(val) else f"{val:.3f}"
            lines.append(f"  {pass_fail(val, op, thr):4s} {label}: {val_txt} {op} {thr:g}")

    # Oscillation checks on key errors.
    osc_cols = ["entry_rcm_error_mm", "target_rcm_or_tip_error_mm", "final_target_tip_error_mm", "entry_cone_angle_deg"]
    osc_lines = []
    for col in osc_cols:
        r = oscillations_per_second(df, col)
        if r is not None:
            osc_lines.append(f"{col}={r:.2f}/s ({pass_fail(r, '<=', th.max_oscillations_per_s)})")
    if osc_lines:
        lines.append("Oscillation estimate: " + "; ".join(osc_lines))

    steady_lines = []
    for col in ["entry_rcm_error_mm", "target_rcm_or_tip_error_mm", "final_target_tip_error_mm"]:
        s = steady_std(df, col, th.steady_window_s)
        if s is not None:
            steady_lines.append(f"{col}_last{th.steady_window_s:g}s_std={s:.3f}")
    if steady_lines:
        lines.append("Steady-state jitter: " + "; ".join(steady_lines))

    return lines


def cone_quality(df: pd.DataFrame, task_name: str, th: Thresholds) -> List[str]:
    lines: List[str] = []
    sub = df[df["task_inferred"] == task_name].copy()
    if sub.empty:
        return [f"\n## {task_name} cone quality", "No samples found for this task."]

    lines.append(f"\n## {task_name} cone quality")
    if task_name.startswith("Task2"):
        angle_col = "cone_t2_angle_deg_geom" if "cone_t2_angle_deg_geom" in sub.columns else "entry_cone_angle_deg"
        radius_col = "cone_t2_radius_mm_geom"
        drift_col = "entry_side_distance_to_entry_mm_geom"
        rcm_col = "target_rcm_or_tip_error_mm"
        rcm_label = "target_RCM_or_tip"
    else:
        angle_col = "cone_t4_angle_deg_geom" if "cone_t4_angle_deg_geom" in sub.columns else "entry_cone_angle_deg"
        radius_col = "cone_t4_radius_mm_geom"
        drift_col = "entry_rcm_error_mm"
        rcm_col = "entry_rcm_error_mm"
        rcm_label = "entry_RCM"

    if angle_col not in sub.columns:
        lines.append("Cone angle not available: add entry_cone_angle_deg or position columns tool/tip/entry/target.")
        return lines

    angle = pd.to_numeric(sub[angle_col], errors="coerce").dropna()
    if angle.empty:
        lines.append("Cone angle column is empty.")
        return lines
    angle_p95 = float(np.percentile(angle, 95))
    angle_std = float(np.std(angle, ddof=0))
    angle_range = float(np.max(angle) - np.min(angle))
    lines.append(f"angle_source={angle_col} | p95={angle_p95:.3f} deg | std={angle_std:.3f} deg | range={angle_range:.3f} deg")
    lines.append(f"  {pass_fail(angle_p95, '>=', th.cone_min_angle_deg):4s} cone_angle_p95 >= {th.cone_min_angle_deg:g} deg")
    lines.append(f"  {pass_fail(angle_std, '>=', th.cone_min_angle_std_deg):4s} cone_angle_std >= {th.cone_min_angle_std_deg:g} deg")

    if radius_col in sub.columns:
        radius = pd.to_numeric(sub[radius_col], errors="coerce").dropna()
        if not radius.empty:
            radius_p95 = float(np.percentile(radius, 95))
            radius_std = float(np.std(radius, ddof=0))
            lines.append(f"radius_source={radius_col} | p95={radius_p95:.3f} mm | std={radius_std:.3f} mm")
            lines.append(f"  {pass_fail(radius_p95, '>=', th.cone_min_radius_mm):4s} cone_radius_p95 >= {th.cone_min_radius_mm:g} mm")
    else:
        lines.append(f"Cone radius not available: missing {radius_col} / position columns.")

    if rcm_col in sub.columns:
        st = stats_for_series(sub[rcm_col])
        thr = th.target_max_mm if task_name.startswith("Task2") else th.entry_max_mm
        lines.append(f"{rcm_label}_constraint: max={st['max']:.3f} mm, mean={st['mean']:.3f} mm")
        lines.append(f"  {pass_fail(st['max'], '<=', thr):4s} {rcm_label}_max <= {thr:g} mm")

    if drift_col in sub.columns:
        st = stats_for_series(sub[drift_col])
        if task_name.startswith("Task2"):
            lines.append(f"entry-side point distance from nominal entry: mean={st['mean']:.3f} mm, max={st['max']:.3f} mm")
            lines.append("  Note: in Task 2 this distance is the visible entry-side cone radius, not necessarily an RCM violation.")
        else:
            lines.append(f"trocar/entry slip proxy ({drift_col}): mean={st['mean']:.3f} mm, max={st['max']:.3f} mm")
            lines.append(f"  {pass_fail(st['max'], '<=', th.entry_max_mm):4s} trocar_slip_max <= {th.entry_max_mm:g} mm")

    return lines


def make_plot(df: pd.DataFrame, cols: Sequence[str], title: str, ylabel: str, out: Path) -> None:
    available = [c for c in cols if c in df.columns and pd.to_numeric(df[c], errors="coerce").notna().any()]
    if not available:
        return
    fig = plt.figure(figsize=(11, 5.5))
    ax = fig.add_subplot(111)
    t = df["time"].to_numpy(dtype=float)
    for c in available:
        ax.plot(t, pd.to_numeric(df[c], errors="coerce"), label=c)
    ax.set_title(title)
    ax.set_xlabel("time [s]")
    ax.set_ylabel(ylabel)
    ax.grid(True, alpha=0.35)
    ax.legend(loc="best")
    fig.tight_layout()
    fig.savefig(out, dpi=160)
    plt.close(fig)


def make_cone_plane_plot(df: pd.DataFrame, task_name: str, out: Path) -> None:
    sub = df[df["task_inferred"] == task_name].copy()
    if sub.empty:
        return
    if not all(c in sub.columns for c in ["tip_x", "tip_y", "tip_z", "entry_x", "entry_y", "entry_z", "target_x", "target_y", "target_z"]):
        return
    tip = sub[["tip_x", "tip_y", "tip_z"]].to_numpy(float)
    entry = sub[["entry_x", "entry_y", "entry_z"]].to_numpy(float)
    target = sub[["target_x", "target_y", "target_z"]].to_numpy(float)
    axis = unit_rows(target - entry)
    # Build a stable local basis from the first valid axis.
    idx = np.where(norm_rows(axis) > 0.5)[0]
    if idx.size == 0:
        return
    a = axis[idx[0]]
    helper = np.array([0.0, 1.0, 0.0]) if abs(np.dot(a, [0, 1, 0])) < 0.95 else np.array([1.0, 0.0, 0.0])
    u = np.cross(a, helper); u = u / (np.linalg.norm(u) + 1e-12)
    v = np.cross(a, u); v = v / (np.linalg.norm(v) + 1e-12)
    rel = tip - target  # show tip around target; useful especially Task 4
    x = rel @ u * 1000.0
    y = rel @ v * 1000.0
    fig = plt.figure(figsize=(6, 6))
    ax = fig.add_subplot(111)
    ax.plot(x, y, marker=".", linewidth=0.8)
    ax.set_title(f"{task_name}: tip trajectory in cone plane")
    ax.set_xlabel("radial u [mm]")
    ax.set_ylabel("radial v [mm]")
    ax.axis("equal")
    ax.grid(True, alpha=0.35)
    fig.tight_layout()
    fig.savefig(out, dpi=160)
    plt.close(fig)


def generate_plots(df: pd.DataFrame, out_dir: Path, prefix: str) -> List[Path]:
    paths: List[Path] = []
    plot_specs = [
        (["entry_rcm_error_mm", "target_rcm_or_tip_error_mm", "final_target_tip_error_mm", "insertion_intermediate_error_mm"], "RCM / target errors", "error [mm]", "errors_time.png"),
        (["skull_violation_mm", "arm_skull_violation_mm", "total_skull_violation_mm"], "Skull violation", "violation [mm]", "skull_violation_time.png"),
        (["fps_est"], "FPS / CSV sample rate", "Hz", "fps_time.png"),
        (["entry_cone_angle_deg", "cone_t2_angle_deg_geom", "cone_t4_angle_deg_geom"], "Cone angle", "angle [deg]", "cone_angle_time.png"),
        (["cone_t2_radius_mm_geom", "cone_t4_radius_mm_geom", "entry_side_distance_to_entry_mm_geom"], "Cone radius / entry-side displacement", "mm", "cone_radius_time.png"),
        (["tip_speed_mm_s", "tip_acc_mm_s2", "joint_speed_rms_deg_s", "joint_acc_rms_deg_s2"], "Smoothness proxies", "mixed units", "smoothness_time.png"),
    ]
    for cols, title, ylabel, name in plot_specs:
        p = out_dir / f"{prefix}_{name}"
        before = p.exists()
        make_plot(df, cols, title, ylabel, p)
        if p.exists() and not before or p.exists():
            paths.append(p)
    for task in ["Task2_TargetRCM_EntryCone", "Task4_EntryRCM_TipCone"]:
        p = out_dir / f"{prefix}_{safe_name(task)}_cone_plane.png"
        make_cone_plane_plot(df, task, p)
        if p.exists():
            paths.append(p)
    return paths


def analyze(paths: Sequence[Path], out_dir: Path, th: Thresholds) -> Tuple[str, pd.DataFrame]:
    out_dir.mkdir(parents=True, exist_ok=True)
    all_frames: List[pd.DataFrame] = []
    report: List[str] = []
    report.append("RCM LOG ANALYSIS REPORT")
    report.append("=======================")
    report.append("Thresholds:")
    report.append(
        f"entry_max={th.entry_max_mm:g}mm | target_max={th.target_max_mm:g}mm | final_target_max={th.final_target_max_mm:g}mm | "
        f"skull_max={th.skull_max_mm:g}mm | min_fps={th.min_fps:g} | cone_min_angle={th.cone_min_angle_deg:g}deg | cone_min_radius={th.cone_min_radius_mm:g}mm"
    )

    for path in paths:
        info(f"Reading {path}")
        df = load_csv(path)
        df = add_derived_columns(df)
        all_frames.append(df)
        prefix = safe_name(path.stem)
        report.append(f"\n\n# FILE: {path.name}")
        report.extend(summarize_group(df, "whole file", th))
        for task_name, sub in df.groupby("task_inferred", dropna=False):
            report.extend(summarize_group(sub, f"task={task_name}", th))
        report.extend(cone_quality(df, "Task2_TargetRCM_EntryCone", th))
        report.extend(cone_quality(df, "Task4_EntryRCM_TipCone", th))
        plots = generate_plots(df, out_dir, prefix)
        if plots:
            report.append("\nPlots saved:")
            for p in plots:
                report.append(f"  {p.name}")
        derived_path = out_dir / f"{prefix}_derived.csv"
        df.to_csv(derived_path, index=False)
        report.append(f"Derived CSV saved: {derived_path.name}")

    if all_frames:
        merged = pd.concat(all_frames, ignore_index=True)
        report.append("\n\n# MERGED SUMMARY")
        report.extend(summarize_group(merged, "all files", th))
        for task_name, sub in merged.groupby("task_inferred", dropna=False):
            report.extend(summarize_group(sub, f"task={task_name}", th))
        report.extend(cone_quality(merged, "Task2_TargetRCM_EntryCone", th))
        report.extend(cone_quality(merged, "Task4_EntryRCM_TipCone", th))
        generate_plots(merged, out_dir, "ALL")
    else:
        merged = pd.DataFrame()

    text = "\n".join(report) + "\n"
    (out_dir / "rcm_analysis_report.txt").write_text(text, encoding="utf-8")
    if not merged.empty:
        # Useful compact table: stats by file/task/phase/mode for core columns.
        rows = []
        group_cols = ["source_file", "task_inferred", "mode", "phase"]
        for keys, sub in merged.groupby(group_cols, dropna=False):
            row = dict(zip(group_cols, keys))
            row["samples"] = len(sub)
            row["duration_s"] = float(sub["time"].max() - sub["time"].min()) if len(sub) else np.nan
            for col in ["entry_rcm_error_mm", "target_rcm_or_tip_error_mm", "final_target_tip_error_mm", "total_skull_violation_mm", "fps_est", "cone_t2_angle_deg_geom", "cone_t4_angle_deg_geom"]:
                if col in sub.columns:
                    st = stats_for_series(sub[col])
                    row[f"{col}_mean"] = st["mean"]
                    row[f"{col}_max"] = st["max"]
                    row[f"{col}_p95"] = st["p95"]
            rows.append(row)
        pd.DataFrame(rows).to_csv(out_dir / "rcm_summary_by_task_phase.csv", index=False)
    return text, merged


def expand_inputs(patterns: Sequence[str]) -> List[Path]:
    out: List[Path] = []
    for pat in patterns:
        p = Path(pat)
        # pathlib does not expand globs unless explicitly requested.
        matches = [Path(x) for x in glob.glob(pat)] if any(ch in pat for ch in "*?[") else [p]
        for m in matches:
            if m.is_file() and m.suffix.lower() == ".csv":
                out.append(m)
    # de-duplicate preserving order
    seen = set()
    unique = []
    for p in out:
        rp = p.resolve()
        if rp not in seen:
            seen.add(rp)
            unique.append(p)
    return unique


def build_argparser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(description="Analyze Unity ROSA Multi-RCM CSV logs.")
    ap.add_argument("csv", nargs="+", help="CSV file(s) or glob patterns, e.g. RCM_logs/*.csv")
    ap.add_argument("--out", default="rcm_analysis_out", help="Output folder for report, derived CSV and PNG plots")
    ap.add_argument("--entry-max-mm", type=float, default=10.0)
    ap.add_argument("--target-max-mm", type=float, default=10.0)
    ap.add_argument("--final-target-max-mm", type=float, default=8.0)
    ap.add_argument("--skull-max-mm", type=float, default=0.0)
    ap.add_argument("--arm-skull-max-mm", type=float, default=0.0)
    ap.add_argument("--min-fps", type=float, default=30.0)
    ap.add_argument("--cone-min-angle-deg", type=float, default=3.0)
    ap.add_argument("--cone-min-radius-mm", type=float, default=8.0)
    ap.add_argument("--cone-min-angle-std-deg", type=float, default=0.35)
    ap.add_argument("--cone-center-drift-max-mm", type=float, default=8.0)
    ap.add_argument("--max-oscillations-per-s", type=float, default=4.0)
    ap.add_argument("--steady-window-s", type=float, default=5.0)
    return ap


def main(argv: Optional[Sequence[str]] = None) -> int:
    ap = build_argparser()
    args = ap.parse_args(argv)
    paths = expand_inputs(args.csv)
    if not paths:
        print("No CSV files found.", file=sys.stderr)
        return 2
    th = Thresholds(
        entry_max_mm=args.entry_max_mm,
        target_max_mm=args.target_max_mm,
        final_target_max_mm=args.final_target_max_mm,
        skull_max_mm=args.skull_max_mm,
        arm_skull_max_mm=args.arm_skull_max_mm,
        min_fps=args.min_fps,
        cone_min_angle_deg=args.cone_min_angle_deg,
        cone_min_radius_mm=args.cone_min_radius_mm,
        cone_min_angle_std_deg=args.cone_min_angle_std_deg,
        cone_center_drift_max_mm=args.cone_center_drift_max_mm,
        max_oscillations_per_s=args.max_oscillations_per_s,
        steady_window_s=args.steady_window_s,
    )
    report, _ = analyze(paths, Path(args.out), th)
    print(report)
    print(f"\nSaved report to: {Path(args.out) / 'rcm_analysis_report.txt'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
