import pandas as pd
import matplotlib.pyplot as plt
from pathlib import Path

log_dir = Path("RCM_logs")
csv_path = log_dir / "rcm_log.csv"

if not csv_path.exists():
    raise FileNotFoundError(
        f"Cannot find {csv_path}. Run the Unity scene first, then stop Play Mode."
    )

df = pd.read_csv(csv_path)

if df.empty:
    raise RuntimeError("The CSV file is empty. Run the Unity scene for a few seconds.")

# ------------------------------------------------------------
# Plot 1: RCM errors over time
# ------------------------------------------------------------

plt.figure(figsize=(10, 5))
plt.plot(df["time"], df["entry_error_mm"], label="Entry RCM error")
plt.plot(df["time"], df["target_error_mm"], label="Target RCM error")
plt.xlabel("Time [s]")
plt.ylabel("Error [mm]")
plt.title("RCM errors over time")
plt.legend()
plt.grid(True)
plt.tight_layout()
plt.savefig(log_dir / "rcm_errors_over_time.png", dpi=250)
plt.show()

# ------------------------------------------------------------
# Plot 2: mean error by mode
# ------------------------------------------------------------

mode_stats = df.groupby("mode")[["entry_error_mm", "target_error_mm"]].mean()

plt.figure(figsize=(8, 5))
mode_stats.plot(kind="bar")
plt.ylabel("Mean error [mm]")
plt.title("Mean RCM error by control mode")
plt.grid(axis="y")
plt.tight_layout()
plt.savefig(log_dir / "rcm_mean_error_by_mode.png", dpi=250)
plt.show()

# ------------------------------------------------------------
# Plot 3: final error by mode
# ------------------------------------------------------------

final_by_mode = df.groupby("mode")[["entry_error_mm", "target_error_mm"]].tail(1)
final_by_mode = final_by_mode.copy()
final_by_mode["mode"] = df.loc[final_by_mode.index, "mode"].values
final_by_mode = final_by_mode.set_index("mode")

plt.figure(figsize=(8, 5))
final_by_mode[["entry_error_mm", "target_error_mm"]].plot(kind="bar")
plt.ylabel("Final error [mm]")
plt.title("Final RCM error by control mode")
plt.grid(axis="y")
plt.tight_layout()
plt.savefig(log_dir / "rcm_final_error_by_mode.png", dpi=250)
plt.show()

print("Saved plots in:", log_dir.resolve())
print("Generated:")
print("-", log_dir / "rcm_errors_over_time.png")
print("-", log_dir / "rcm_mean_error_by_mode.png")
print("-", log_dir / "rcm_final_error_by_mode.png")
