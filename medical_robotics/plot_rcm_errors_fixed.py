import pandas as pd
import matplotlib.pyplot as plt
from pathlib import Path

# ------------------------------------------------------------
# Locate latest Unity RCM log
# ------------------------------------------------------------

def find_log_dir() -> Path:
    candidates = [
        Path.cwd() / "RCM_logs",
        Path(__file__).resolve().parent / "RCM_logs",
        Path(__file__).resolve().parent.parent / "RCM_logs",
    ]

    for candidate in candidates:
        if candidate.exists() and candidate.is_dir():
            return candidate

    raise FileNotFoundError(
        "Cannot find RCM_logs folder. Run the Unity scene first, stop Play Mode, "
        "then run this script from the Unity project root."
    )


log_dir = find_log_dir()

csv_files = sorted(log_dir.glob("*.csv"), key=lambda p: p.stat().st_mtime, reverse=True)

if not csv_files:
    raise FileNotFoundError(
        f"No CSV files found in {log_dir}. Run the Unity scene first, then stop Play Mode."
    )

# The latest controller usually writes timestamped files such as double_rcm_log_YYYYMMDD_HHMMSS.csv.
csv_path = csv_files[0]
print(f"Using log file: {csv_path}")

df = pd.read_csv(csv_path)

if df.empty:
    raise RuntimeError("The CSV file is empty. Run the Unity scene for a few seconds.")

# ------------------------------------------------------------
# Normalize column names across old/new controller versions
# ------------------------------------------------------------

if "entry_error_mm" not in df.columns and "entry_rcm_error_mm" in df.columns:
    df["entry_error_mm"] = df["entry_rcm_error_mm"]

if "target_error_mm" not in df.columns:
    if "target_rcm_or_tip_error_mm" in df.columns:
        df["target_error_mm"] = df["target_rcm_or_tip_error_mm"]
    elif "final_target_tip_error_mm" in df.columns:
        df["target_error_mm"] = df["final_target_tip_error_mm"]

required_columns = ["time", "mode", "entry_error_mm", "target_error_mm"]
missing = [col for col in required_columns if col not in df.columns]

if missing:
    raise KeyError(
        "Missing required columns: " + ", ".join(missing) + "\n"
        "Available columns are: " + ", ".join(df.columns)
    )

# ------------------------------------------------------------
# Plot 1: RCM / target errors over time
# ------------------------------------------------------------

plt.figure(figsize=(10, 5))
plt.plot(df["time"], df["entry_error_mm"], label="Entry RCM error")
plt.plot(df["time"], df["target_error_mm"], label="Target / tip error")

if "skull_violation_mm" in df.columns:
    plt.plot(df["time"], df["skull_violation_mm"], label="Needle skull violation")

if "arm_skull_violation_mm" in df.columns:
    plt.plot(df["time"], df["arm_skull_violation_mm"], label="Arm skull violation")

plt.xlabel("Time [s]")
plt.ylabel("Error / violation [mm]")
plt.title("RCM, target, and skull errors over time")
plt.legend()
plt.grid(True)
plt.tight_layout()
plt.savefig(log_dir / "rcm_errors_over_time.png", dpi=250)
plt.close()

# ------------------------------------------------------------
# Plot 2: mean error by mode
# ------------------------------------------------------------

mode_stats = df.groupby("mode")[["entry_error_mm", "target_error_mm"]].mean()

ax = mode_stats.plot(kind="bar", figsize=(8, 5))
ax.set_ylabel("Mean error [mm]")
ax.set_title("Mean error by control mode")
ax.grid(axis="y")
plt.tight_layout()
plt.savefig(log_dir / "rcm_mean_error_by_mode.png", dpi=250)
plt.close()

# ------------------------------------------------------------
# Plot 3: final error by mode
# ------------------------------------------------------------

final_by_mode = df.groupby("mode")[["entry_error_mm", "target_error_mm"]].tail(1).copy()
final_by_mode["mode"] = df.loc[final_by_mode.index, "mode"].values
final_by_mode = final_by_mode.set_index("mode")

ax = final_by_mode[["entry_error_mm", "target_error_mm"]].plot(kind="bar", figsize=(8, 5))
ax.set_ylabel("Final error [mm]")
ax.set_title("Final error by control mode")
ax.grid(axis="y")
plt.tight_layout()
plt.savefig(log_dir / "rcm_final_error_by_mode.png", dpi=250)
plt.close()

# ------------------------------------------------------------
# Optional plot 4: phase-aware insertion plot
# ------------------------------------------------------------

if "phase" in df.columns:
    phase_stats = df.groupby("phase")[["entry_error_mm", "target_error_mm"]].mean()
    ax = phase_stats.plot(kind="bar", figsize=(8, 5))
    ax.set_ylabel("Mean error [mm]")
    ax.set_title("Mean error by insertion phase")
    ax.grid(axis="y")
    plt.tight_layout()
    plt.savefig(log_dir / "rcm_mean_error_by_phase.png", dpi=250)
    plt.close()

print("Saved plots in:", log_dir.resolve())
print("Generated:")
print("-", log_dir / "rcm_errors_over_time.png")
print("-", log_dir / "rcm_mean_error_by_mode.png")
print("-", log_dir / "rcm_final_error_by_mode.png")
if "phase" in df.columns:
    print("-", log_dir / "rcm_mean_error_by_phase.png")
