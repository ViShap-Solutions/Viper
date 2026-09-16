#!/usr/bin/env python3
"""Generate release-review artifacts from a BenchmarkDotNet CSV export."""
from pathlib import Path
import csv, html, math, shutil, sys
import matplotlib.pyplot as plt

src = Path(sys.argv[1])
out = Path(sys.argv[2] if len(sys.argv) > 2 else "BenchmarkReports")
out.mkdir(parents=True, exist_ok=True)
charts = out / "charts"
charts.mkdir(exist_ok=True)

with src.open(newline="", encoding="utf-8-sig") as f:
    rows = list(csv.DictReader(f))
if not rows:
    raise SystemExit("No benchmark rows")

shutil.copyfile(src, out / "results.csv")


def number(row, names):
    for name in names:
        value = row.get(name)
        if value not in (None, ""):
            try:
                return float(str(value).replace(",", ""))
            except ValueError:
                pass
    return None


def key(row):
    return (row.get("Method", ""), row.get("DataSet") or row.get("DataSetKind") or "")

normalized = []
for row in rows:
    candidates = [r for r in rows if key(r) == key(row)]
    viper = next((r for r in candidates if (r.get("Library") or "").startswith("Viper")), None)
    copy = dict(row)
    mean = number(row, ["Mean"])
    viper_mean = number(viper or {}, ["Mean"])
    copy["NormalizedMeanVsViper"] = "" if mean is None or not viper_mean else f"{mean / viper_mean:.4f}x"
    normalized.append(copy)

with (out / "normalized-results.csv").open("w", newline="", encoding="utf-8") as f:
    fields = list(normalized[0].keys())
    writer = csv.DictWriter(f, fieldnames=fields)
    writer.writeheader()
    writer.writerows(normalized)


def label(row):
    library = row.get("Library") or row.get("Method") or "unknown"
    dataset = row.get("DataSet") or row.get("DataSetKind") or ""
    return f"{library}\n{dataset}" if dataset else library


def plot_rows(filename, title, metric_names, ylabel, predicate=lambda r: True):
    points = [(label(r), number(r, metric_names)) for r in rows if predicate(r)]
    points = [p for p in points if p[1] is not None and math.isfinite(p[1])]
    if not points:
        return
    fig, ax = plt.subplots(figsize=(12, max(4, min(18, len(points) * 0.2))))
    ax.barh([p[0] for p in points], [p[1] for p in points])
    ax.set_xlabel(ylabel)
    ax.set_title(title)
    fig.tight_layout()
    fig.savefig(charts / filename, dpi=150)
    plt.close(fig)


plot_rows("serialize-throughput.png", "Benchmark time / operation", ["Mean"], "ns/op", lambda r: "Serialize" in r.get("Method", ""))
plot_rows("deserialize-throughput.png", "Deserialize time / operation", ["Mean"], "ns/op", lambda r: "Deserialize" in r.get("Method", ""))
plot_rows("roundtrip-time.png", "Round-trip time / operation", ["Mean"], "ns/op", lambda r: "RoundTrip" in r.get("Method", ""))
plot_rows("allocated-bytes.png", "Allocated bytes / operation", ["Allocated Memory", "Allocated"], "bytes/op")
plot_rows("gc-gen0.png", "Gen0 collections / operation", ["Gen0", "Gen 0"], "collections/op")
plot_rows("gc-gen1.png", "Gen1 collections / operation", ["Gen1", "Gen 1"], "collections/op")
plot_rows("gc-gen2.png", "Gen2 collections / operation", ["Gen2", "Gen 2"], "collections/op")
plot_rows("cold-start.png", "Cold-start time", ["Mean"], "ns/op", lambda r: "Cold" in r.get("Method", "") or "First" in r.get("Method", ""))
plot_rows("warm-start.png", "Warm steady-state time", ["Mean"], "ns/op", lambda r: "Warm" in r.get("Method", "") or "Serialize" in r.get("Method", "") or "Deserialize" in r.get("Method", ""))
plot_rows("compression-throughput.png", "Compression/decompression time", ["Mean"], "ns/op", lambda r: "Compress" in r.get("Method", "") or "Decompress" in r.get("Method", ""))
plot_rows("encryption-throughput.png", "Encryption/decryption time", ["Mean"], "ns/op", lambda r: "Encrypt" in r.get("Method", "") or "Decrypt" in r.get("Method", ""))

compression_file = out / "compression-metrics.csv"
if compression_file.exists():
    with compression_file.open(newline="", encoding="utf-8-sig") as f:
        compression_rows = list(csv.DictReader(f))
    points = []
    for row in compression_rows:
        try:
            points.append((row["Algorithm"], float(row["CompressionRatio"])))
        except (KeyError, ValueError):
            pass
    if points:
        fig, ax = plt.subplots(figsize=(8, 4))
        ax.bar([p[0] for p in points], [p[1] for p in points])
        ax.set_ylabel("compressed bytes / raw bytes")
        ax.set_title("Compression ratio")
        fig.tight_layout()
        fig.savefig(charts / "compression-ratio.png", dpi=150)
        plt.close(fig)

payload_file = out / "payload-sizes.csv"
if payload_file.exists():
    with payload_file.open(newline="", encoding="utf-8-sig") as f:
        payload_rows = list(csv.DictReader(f))
    points = []
    for row in payload_rows:
        try:
            points.append((f"{row['Library']}\n{row['DataSet']}", float(row['PayloadBytes'])))
        except (KeyError, ValueError):
            pass
    if points:
        fig, ax = plt.subplots(figsize=(12, max(4, min(18, len(points) * 0.2))))
        ax.barh([p[0] for p in points], [p[1] for p in points])
        ax.set_xlabel("payload bytes")
        ax.set_title("Serialized payload size")
        fig.tight_layout()
        fig.savefig(charts / "payload-size.png", dpi=150)
        plt.close(fig)


def esc(value):
    return html.escape("" if value is None else str(value))

headers = ["Library", "DataSet", "Method", "Mean", "Median", "P95", "StdDev", "Min", "Max", "Allocated Memory", "Gen0", "Gen1", "Gen2"]
body = []
for row in rows:
    body.append("<tr>" + "".join(f"<td>{esc(row.get(h, ''))}</td>" for h in headers) + "</tr>")

html_doc = """<!doctype html><html><head><meta charset='utf-8'><title>Viper Benchmark Summary</title><style>body{font-family:system-ui,sans-serif}table{border-collapse:collapse}td,th{border:1px solid #ccc;padding:4px 6px;font-size:12px}th{position:sticky;top:0;background:#fff}</style></head><body><h1>Viper Benchmark Summary</h1><p>Raw measurements are copied from BenchmarkDotNet. Missing metrics remain blank; no benchmark numbers are fabricated.</p><table><thead><tr>""" + "".join(f"<th>{esc(h)}</th>" for h in headers) + """</tr></thead><tbody>""" + "".join(body) + "</tbody></table></body></html>"
(out / "summary.html").write_text(html_doc, encoding="utf-8")

import json
(out / "results.json").write_text(json.dumps(rows, indent=2, ensure_ascii=False), encoding="utf-8")

with (out / "results.md").open("w", encoding="utf-8") as f:
    f.write("# Benchmark results\n\n")
    f.write("| " + " | ".join(headers) + " |\n")
    f.write("|" + "|".join("---" for _ in headers) + "|\n")
    for row in rows:
        f.write("| " + " | ".join(str(row.get(h, "")).replace("|", "\\|") for h in headers) + " |\n")

memory_headers = [h for h in rows[0].keys() if h in {"Library", "DataSet", "Method", "Allocated Memory", "Gen0", "Gen1", "Gen2"}]
with (out / "memory.csv").open("w", newline="", encoding="utf-8") as f:
    writer = csv.DictWriter(f, fieldnames=memory_headers)
    writer.writeheader()
    writer.writerows({h: row.get(h, "") for h in memory_headers} for row in rows)

print(f"Wrote {out / 'results.csv'}, {out / 'results.json'}, {out / 'results.md'}, {out / 'memory.csv'}, {out / 'normalized-results.csv'}, {out / 'summary.html'} and charts to {charts}")
