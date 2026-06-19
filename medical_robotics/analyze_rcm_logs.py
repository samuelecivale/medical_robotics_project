#!/usr/bin/env python3
"""
Analyze Project 4 Multi-RCM CSV logs produced by ROSADoubleRCMController.cs.

Usage examples:
    python analyze_rcm_logs.py RCM_logs
    python analyze_rcm_logs.py RCM_logs/project4_multircm_20260619_*.csv --out RCM_analysis

Outputs:
    - summary_overall.csv
    - summary_by_task_phase.csv
    - slide_summary_it.md
    - slide_summary_en.md
    - PNG figures ready to insert in slides
"""

from __future__ import annotations

import argparse
import glob
import math
from pathlib import Path
from typing import Iterable, List, Optional

import numpy as np
import pandas as pd
import matplotlib.pyplot as plt


ERROR_COLUMNS = [
    "tip_target_error_mm",
    "entry_rcm_error_mm",
    "target_rcm_error_mm",
    "task2_entry_cone_error_mm",
    "task4_tip_cone_error_mm",
    "active_task_error_mm",
    "tip_line_distance_mm",
    "shaft_line_max_distance_mm",
    "skull_violation_mm",
]

FALLBACK_RENAMES = {
    # Older logger compatibility.
    "tip_error_mm": "tip_target_error_mm",
    "cone_error_mm": "active_task_error_mm",
    "qdot_norm": "qdot_norm_rad_s",
}

TASK_ORDER = [
    "T1_entry_rcm_tip_target",
    "T2_target_rcm_entry_cone",
    "T3_safe_insertion",
    "T4_entry_rcm_tip_cone",
    "Hold",
]


def discover_csvs(inputs: Iterable[str]) -> List[Path]:
    files: List[Path] = []
    for raw in inputs:
        p = Path(raw)
        if p.is_dir():
            files.extend(sorted(p.glob("*.csv")))
        else:
            matches = [Path(x) for x in sorted(glob.glob(raw))]
            if matches:
                files.extend(matches)
            elif p.exists():
                files.append(p)
    # Keep only real files and deduplicate while preserving order.
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
        df = pd.read_csv(path)
        df = df.rename(columns={k: v for k, v in FALLBACK_RENAMES.items() if k in df.columns and v not in df.columns})
        df["log_file"] = path.name
        if "task" not in df.columns:
            df["task"] = df.get("mode", "unknown").astype(str)
        if "phase" not in df.columns:
            df["phase"] = "unknown"
        if "mode" not in df.columns:
            df["mode"] = df["task"].astype(str)
        if "time_s" in df.columns:
            df["time_rel_s"] = df["time_s"] - df["time_s"].iloc[0]
        else:
            df["time_rel_s"] = np.arange(len(df), dtype=float)
        frames.append(df)
    if not frames:
        raise FileNotFoundError("No CSV logs found. Pass a file, a glob, or the RCM_logs folder.")
    all_df = pd.concat(frames, ignore_index=True, sort=False)
    # Ensure numeric conversion for metrics.
    for c in all_df.columns:
        if c.endswith("_mm") or c.endswith("_s") or c.endswith("_deg") or c in {"lambda", "fps", "insertion_progress", "qdot_norm_rad_s", "lambdadot"}:
            all_df[c] = pd.to_numeric(all_df[c], errors="coerce")
    return all_df


def metric_stats(series: pd.Series) -> dict:
    s = pd.to_numeric(series, errors="coerce").dropna()
    if len(s) == 0:
        return {"mean": np.nan, "median": np.nan, "p95": np.nan, "max": np.nan, "final": np.nan}
    return {
        "mean": float(s.mean()),
        "median": float(s.median()),
        "p95": float(s.quantile(0.95)),
        "max": float(s.max()),
        "final": float(s.iloc[-1]),
    }


def summarize_overall(df: pd.DataFrame) -> pd.DataFrame:
    rows = []
    group_cols = ["log_file"] if "log_file" in df.columns else []
    grouped = df.groupby(group_cols, dropna=False) if group_cols else [("all", df)]
    for key, g in grouped:
        row = {
            "log_file": key if isinstance(key, str) else key[0] if isinstance(key, tuple) else str(key),
            "samples": len(g),
            "duration_s": float(g["time_rel_s"].max() - g["time_rel_s"].min()) if "time_rel_s" in g else np.nan,
            "mean_fps": float(g["fps"].mean()) if "fps" in g else np.nan,
        }
        for c in ERROR_COLUMNS:
            if c in g.columns:
                stats = metric_stats(g[c])
                row[f"{c}_mean"] = stats["mean"]
                row[f"{c}_p95"] = stats["p95"]
                row[f"{c}_max"] = stats["max"]
                row[f"{c}_final"] = stats["final"]
        rows.append(row)
    return pd.DataFrame(rows)


def summarize_by_task_phase(df: pd.DataFrame) -> pd.DataFrame:
    rows = []
    group_cols = ["log_file", "task", "mode", "phase"]
    for col in group_cols:
        if col not in df.columns:
            df[col] = "unknown"
    for (log_file, task, mode, phase), g in df.groupby(group_cols, dropna=False):
        row = {
            "log_file": log_file,
            "task": task,
            "mode": mode,
            "phase": phase,
            "samples": len(g),
            "duration_s": float(g["time_rel_s"].max() - g["time_rel_s"].min()) if "time_rel_s" in g else np.nan,
        }
        for c in ERROR_COLUMNS:
            if c in g.columns:
                stats = metric_stats(g[c])
                for k, v in stats.items():
                    row[f"{c}_{k}"] = v
        rows.append(row)
    out = pd.DataFrame(rows)
    if not out.empty and "task" in out.columns:
        out["task_order"] = out["task"].apply(lambda x: TASK_ORDER.index(x) if x in TASK_ORDER else 999)
        out = out.sort_values(["log_file", "task_order", "phase"]).drop(columns=["task_order"])
    return out


def pass_fail_summary(df: pd.DataFrame, entry_thr: float, tip_thr: float, cone_thr: float, skull_thr: float) -> pd.DataFrame:
    checks = []

    def add_check(name: str, mask, column: str, threshold: float, relation: str = "<="):
        if column not in df.columns:
            return
        g = df[mask].copy()
        if g.empty:
            return
        s = pd.to_numeric(g[column], errors="coerce").dropna()
        if s.empty:
            return
        value = float(s.max())
        passed = value <= threshold if relation == "<=" else value >= threshold
        checks.append({
            "check": name,
            "samples": len(s),
            "metric": column,
            "value_max": value,
            "threshold": threshold,
            "passed": bool(passed),
        })

    task = df.get("task", pd.Series([""] * len(df))).astype(str)
    phase = df.get("phase", pd.Series([""] * len(df))).astype(str)

    add_check("T1 entry RCM stability", task.eq("T1_entry_rcm_tip_target"), "entry_rcm_error_mm", entry_thr)
    add_check("T1 tip reaches target", task.eq("T1_entry_rcm_tip_target"), "tip_target_error_mm", tip_thr)
    add_check("T2 cone tracking", task.eq("T2_target_rcm_entry_cone"), "task2_entry_cone_error_mm", cone_thr)
    add_check("T2 tip fixed at target", task.eq("T2_target_rcm_entry_cone"), "tip_target_error_mm", tip_thr)
    add_check("T3 insertion entry RCM", task.eq("T3_safe_insertion") & phase.isin(["InsertToTarget", "Done"]), "entry_rcm_error_mm", entry_thr)
    add_check("T3 final target", task.eq("T3_safe_insertion") & phase.eq("Done"), "tip_target_error_mm", tip_thr)
    add_check("T3 skull/corridor safety", task.eq("T3_safe_insertion"), "skull_violation_mm", skull_thr)
    add_check("T4 entry RCM stability", task.eq("T4_entry_rcm_tip_cone"), "entry_rcm_error_mm", entry_thr)
    add_check("T4 tip cone tracking", task.eq("T4_entry_rcm_tip_cone"), "task4_tip_cone_error_mm", cone_thr)
    return pd.DataFrame(checks)


def save_plot_timeseries(df: pd.DataFrame, out_dir: Path) -> None:
    cols = [c for c in [
        "tip_target_error_mm",
        "entry_rcm_error_mm",
        "target_rcm_error_mm",
        "task2_entry_cone_error_mm",
        "task4_tip_cone_error_mm",
        "skull_violation_mm",
    ] if c in df.columns]
    if not cols:
        return
    plt.figure(figsize=(11, 5.8))
    for c in cols:
        plt.plot(df["time_rel_s"], df[c], label=c.replace("_", " "))
    plt.xlabel("Time [s]")
    plt.ylabel("Error [mm]")
    plt.title("Multi-RCM tracking errors")
    plt.grid(True, alpha=0.3)
    plt.legend(fontsize=8)
    plt.tight_layout()
    plt.savefig(out_dir / "01_tracking_errors.png", dpi=180)
    plt.close()


def save_plot_task_zoom(df: pd.DataFrame, out_dir: Path, task_name: str, filename: str, cols: List[str], title: str) -> None:
    if "task" not in df.columns:
        return
    g = df[df["task"].astype(str).eq(task_name)]
    cols = [c for c in cols if c in g.columns]
    if g.empty or not cols:
        return
    plt.figure(figsize=(11, 5.8))
    for c in cols:
        plt.plot(g["time_rel_s"], g[c], label=c.replace("_", " "))
    plt.xlabel("Time [s]")
    plt.ylabel("Error / metric")
    plt.title(title)
    plt.grid(True, alpha=0.3)
    plt.legend(fontsize=8)
    plt.tight_layout()
    plt.savefig(out_dir / filename, dpi=180)
    plt.close()


def save_plot_velocity(df: pd.DataFrame, out_dir: Path) -> None:
    cols = [c for c in ["qdot_norm_rad_s", "lambda", "lambdadot", "fps"] if c in df.columns]
    if not cols:
        return
    for c in cols:
        plt.figure(figsize=(10.5, 4.8))
        plt.plot(df["time_rel_s"], df[c], label=c.replace("_", " "))
        plt.xlabel("Time [s]")
        plt.ylabel(c.replace("_", " "))
        plt.title(c.replace("_", " ").title())
        plt.grid(True, alpha=0.3)
        plt.tight_layout()
        plt.savefig(out_dir / f"velocity_{c}.png", dpi=180)
        plt.close()


def save_plot_mode_timeline(df: pd.DataFrame, out_dir: Path) -> None:
    if "task" not in df.columns:
        return
    categories = list(dict.fromkeys(df["task"].astype(str).tolist()))
    mapping = {name: i for i, name in enumerate(categories)}
    y = df["task"].astype(str).map(mapping)
    plt.figure(figsize=(11, 3.8))
    plt.step(df["time_rel_s"], y, where="post")
    plt.yticks(list(mapping.values()), list(mapping.keys()), fontsize=8)
    plt.xlabel("Time [s]")
    plt.title("Executed task timeline")
    plt.grid(True, axis="x", alpha=0.3)
    plt.tight_layout()
    plt.savefig(out_dir / "00_task_timeline.png", dpi=180)
    plt.close()


def best_value(df: pd.DataFrame, mask, column: str, stat: str = "max") -> Optional[float]:
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


def fmt(x: Optional[float], unit: str = "mm") -> str:
    if x is None or (isinstance(x, float) and (math.isnan(x) or math.isinf(x))):
        return "n/a"
    return f"{x:.1f} {unit}"


def write_slide_summaries(df: pd.DataFrame, checks: pd.DataFrame, out_dir: Path) -> None:
    task = df.get("task", pd.Series([""] * len(df))).astype(str)
    phase = df.get("phase", pd.Series([""] * len(df))).astype(str)

    t1 = task.eq("T1_entry_rcm_tip_target")
    t2 = task.eq("T2_target_rcm_entry_cone")
    t3 = task.eq("T3_safe_insertion")
    t3_insert = t3 & phase.isin(["InsertToTarget", "Done"])
    t4 = task.eq("T4_entry_rcm_tip_cone")

    lines_it = [
        "# Risultati pronti per le slide",
        "",
        "## Messaggio principale",
        "- Il controller implementa più task cinematiche: Entry-RCM, Target-RCM con cono, sequenza di inserzione e tip-cone con Entry-RCM.",
        "- I grafici generati mostrano tracking degli errori, stabilità del fulcro RCM e sicurezza/corridoio durante l'inserzione.",
        "",
        "## Numeri da riportare",
        f"- Task 1 — max errore RCM su entry: {fmt(best_value(df, t1, 'entry_rcm_error_mm'))}; max errore tip-target: {fmt(best_value(df, t1, 'tip_target_error_mm'))}.",
        f"- Task 2 — max errore entry-cone: {fmt(best_value(df, t2, 'task2_entry_cone_error_mm'))}; max errore tip-target: {fmt(best_value(df, t2, 'tip_target_error_mm'))}.",
        f"- Task 3 — max errore RCM durante inserzione: {fmt(best_value(df, t3_insert, 'entry_rcm_error_mm'))}; max violazione skull: {fmt(best_value(df, t3, 'skull_violation_mm'))}; errore finale sul target: {fmt(best_value(df, t3 & phase.eq('Done'), 'tip_target_error_mm', 'final'))}.",
        f"- Task 4 — max errore Entry-RCM: {fmt(best_value(df, t4, 'entry_rcm_error_mm'))}; max errore tip-cone: {fmt(best_value(df, t4, 'task4_tip_cone_error_mm'))}.",
        "",
        "## Figure consigliate",
        "- 00_task_timeline.png: sequenza dei task eseguiti.",
        "- 01_tracking_errors.png: confronto complessivo degli errori.",
        "- 02_insertion_detail.png: sicurezza e allineamento durante l'inserzione.",
        "- 03_task2_cone.png e 04_task4_cone.png: qualità del moto conico.",
        "",
        "## Check automatici",
    ]
    if checks.empty:
        lines_it.append("- Nessun check disponibile: probabilmente nel log mancano alcune colonne o alcuni task non sono stati eseguiti.")
    else:
        for _, r in checks.iterrows():
            status = "PASS" if bool(r["passed"]) else "FAIL"
            lines_it.append(f"- {status}: {r['check']} — max {r['metric']} = {r['value_max']:.1f} mm, soglia {r['threshold']:.1f} mm.")

    lines_en = [
        "# Slide-ready results",
        "",
        "## Main message",
        "- The controller evaluates four kinematic behaviours: Entry-RCM, Target-RCM with entry cone, safe insertion, and tip cone with Entry-RCM.",
        "- The generated figures support the presentation with tracking errors, RCM stability, insertion-line alignment and skull/corridor safety.",
        "",
        "## Key numbers",
        f"- Task 1 — max Entry-RCM error: {fmt(best_value(df, t1, 'entry_rcm_error_mm'))}; max tip-target error: {fmt(best_value(df, t1, 'tip_target_error_mm'))}.",
        f"- Task 2 — max entry-cone error: {fmt(best_value(df, t2, 'task2_entry_cone_error_mm'))}; max tip-target error: {fmt(best_value(df, t2, 'tip_target_error_mm'))}.",
        f"- Task 3 — max insertion RCM error: {fmt(best_value(df, t3_insert, 'entry_rcm_error_mm'))}; max skull violation: {fmt(best_value(df, t3, 'skull_violation_mm'))}; final target error: {fmt(best_value(df, t3 & phase.eq('Done'), 'tip_target_error_mm', 'final'))}.",
        f"- Task 4 — max Entry-RCM error: {fmt(best_value(df, t4, 'entry_rcm_error_mm'))}; max tip-cone error: {fmt(best_value(df, t4, 'task4_tip_cone_error_mm'))}.",
        "",
        "## Suggested figures",
        "- 00_task_timeline.png: executed task sequence.",
        "- 01_tracking_errors.png: global error comparison.",
        "- 02_insertion_detail.png: safety and alignment during insertion.",
        "- 03_task2_cone.png and 04_task4_cone.png: cone tracking quality.",
        "",
        "## Automatic checks",
    ]
    if checks.empty:
        lines_en.append("- No checks available: some columns or task executions may be missing from the log.")
    else:
        for _, r in checks.iterrows():
            status = "PASS" if bool(r["passed"]) else "FAIL"
            lines_en.append(f"- {status}: {r['check']} — max {r['metric']} = {r['value_max']:.1f} mm, threshold {r['threshold']:.1f} mm.")

    (out_dir / "slide_summary_it.md").write_text("\n".join(lines_it), encoding="utf-8")
    (out_dir / "slide_summary_en.md").write_text("\n".join(lines_en), encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description="Analyze Multi-RCM Unity logs and generate slide-ready outputs.")
    parser.add_argument("inputs", nargs="+", help="CSV file(s), glob(s), or RCM_logs folder")
    parser.add_argument("--out", default="RCM_analysis", help="Output folder")
    parser.add_argument("--entry-threshold-mm", type=float, default=10.0)
    parser.add_argument("--tip-threshold-mm", type=float, default=25.0)
    parser.add_argument("--cone-threshold-mm", type=float, default=50.0)
    parser.add_argument("--skull-threshold-mm", type=float, default=0.0)
    args = parser.parse_args()

    files = discover_csvs(args.inputs)
    if not files:
        raise SystemExit("No CSV files found. Example: python analyze_rcm_logs.py RCM_logs --out RCM_analysis")

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    df = load_logs(files)
    df.to_csv(out_dir / "combined_logs.csv", index=False)

    overall = summarize_overall(df)
    by_task = summarize_by_task_phase(df)
    checks = pass_fail_summary(
        df,
        entry_thr=args.entry_threshold_mm,
        tip_thr=args.tip_threshold_mm,
        cone_thr=args.cone_threshold_mm,
        skull_thr=args.skull_threshold_mm,
    )

    overall.to_csv(out_dir / "summary_overall.csv", index=False)
    by_task.to_csv(out_dir / "summary_by_task_phase.csv", index=False)
    checks.to_csv(out_dir / "pass_fail_checks.csv", index=False)

    save_plot_mode_timeline(df, out_dir)
    save_plot_timeseries(df, out_dir)
    save_plot_task_zoom(
        df,
        out_dir,
        "T3_safe_insertion",
        "02_insertion_detail.png",
        ["tip_target_error_mm", "entry_rcm_error_mm", "tip_line_distance_mm", "shaft_line_max_distance_mm", "skull_violation_mm", "tool_axis_angle_deg"],
        "Task 3 — insertion detail",
    )
    save_plot_task_zoom(
        df,
        out_dir,
        "T2_target_rcm_entry_cone",
        "03_task2_cone.png",
        ["tip_target_error_mm", "task2_entry_cone_error_mm", "target_rcm_error_mm", "qdot_norm_rad_s"],
        "Task 2 — target RCM with entry cone",
    )
    save_plot_task_zoom(
        df,
        out_dir,
        "T4_entry_rcm_tip_cone",
        "04_task4_cone.png",
        ["entry_rcm_error_mm", "task4_tip_cone_error_mm", "tip_target_error_mm", "qdot_norm_rad_s"],
        "Task 4 — entry RCM with tip cone",
    )
    save_plot_velocity(df, out_dir)
    write_slide_summaries(df, checks, out_dir)

    print(f"Loaded {len(files)} log file(s), {len(df)} samples.")
    print(f"Outputs saved in: {out_dir.resolve()}")
    print("Suggested slide files: 00_task_timeline.png, 01_tracking_errors.png, 02_insertion_detail.png, slide_summary_en.md")


if __name__ == "__main__":
    main()
