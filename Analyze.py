import os
import sys

import numpy as np
import pandas as pd
from scipy import stats
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

BUDGET_90 = 1000 / 90
BUDGET_72 = 1000 / 72

path = sys.argv[1]
out_dir = os.path.dirname(os.path.abspath(path))
df = pd.read_csv(path)

keys = ["run_id", "copies", "triangles", "aa", "repeat"]
runs = (df.groupby(keys)["frame_ms"]
          .agg(mean_ms="mean", median_ms="median",
               p95_ms=lambda s: np.percentile(s, 95),
               p99_ms=lambda s: np.percentile(s, 99),
               frames="count")
          .reset_index())
runs["fps_mean"] = 1000 / runs["mean_ms"]
over = (df.assign(over90=df.frame_ms > BUDGET_90, over72=df.frame_ms > BUDGET_72)
          .groupby("run_id")[["over90", "over72"]].mean().reset_index())
runs = runs.merge(over, on="run_id")

cond = (runs.groupby(["triangles", "aa"])
            .agg(n=("mean_ms", "size"), mean_ms=("mean_ms", "mean"),
                 sd_ms=("mean_ms", "std"), p99_ms=("p99_ms", "mean"),
                 fps_mean=("fps_mean", "mean"), over90=("over90", "mean"))
            .reset_index())
print("\n=== Condition summary (run-level means) ===")
print(cond.round(3).to_string(index=False))

print("\n=== Shapiro-Wilk on run mean frame time ===")
for (tri, aa), g in runs.groupby(["triangles", "aa"]):
    x = g["mean_ms"].values
    if len(x) >= 3:
        w, p = stats.shapiro(x)
        print(f"triangles={tri}, AA={aa}x: W={w:.3f}, p={p:.3f}")


def cohen_d(a, b):
    na, nb = len(a), len(b)
    sp = np.sqrt(((na - 1) * np.var(a, ddof=1) + (nb - 1) * np.var(b, ddof=1)) / (na + nb - 2))
    return (np.mean(a) - np.mean(b)) / sp if sp > 0 else float("nan")


print("\n=== AA off vs AA on (Mann-Whitney U, Cohen's d) ===")
aas = sorted(runs["aa"].unique())
if len(aas) >= 2:
    lo, hi = aas[0], aas[-1]
    for tri, g in runs.groupby("triangles"):
        a = g[g.aa == lo]["mean_ms"].values
        b = g[g.aa == hi]["mean_ms"].values
        if len(a) > 1 and len(b) > 1:
            u, p = stats.mannwhitneyu(a, b, alternative="two-sided")
            print(f"triangles={tri}: AA {lo}x mean={a.mean():.3f} ms vs AA {hi}x mean={b.mean():.3f} ms, "
                  f"U={u:.1f}, p={p:.4f}, d={cohen_d(a, b):.2f}")

print("\n=== Effect of triangle count (Kruskal-Wallis) ===")
for aa, g in runs.groupby("aa"):
    groups = [x["mean_ms"].values for _, x in g.groupby("triangles")]
    if len(groups) >= 2 and all(len(x) > 1 for x in groups):
        h, p = stats.kruskal(*groups)
        print(f"AA={aa}x: H={h:.2f}, p={p:.4f}")

runs.to_csv(os.path.join(out_dir, "runs_summary.csv"), index=False)
cond.to_csv(os.path.join(out_dir, "conditions_summary.csv"), index=False)

labels, data = [], []
for (tri, aa), g in runs.groupby(["triangles", "aa"]):
    labels.append(f"{tri:,} tri\nAA {aa}x")
    data.append(g["mean_ms"].values)
fig, ax = plt.subplots(figsize=(10, 5))
ax.boxplot(data)
ax.set_xticklabels(labels, fontsize=8)
ax.axhline(BUDGET_90, ls="--", color="tab:red", label="90 Hz budget (11.1 ms)")
ax.axhline(BUDGET_72, ls=":", color="tab:orange", label="72 Hz budget (13.9 ms)")
ax.set_ylabel("Mean frame time per run (ms)")
ax.set_title("Frame time by scene size and anti-aliasing")
ax.legend()
fig.tight_layout()
fig.savefig(os.path.join(out_dir, "frame_time_boxplot.png"), dpi=150)
print("\nSaved tables and plot in", out_dir)
