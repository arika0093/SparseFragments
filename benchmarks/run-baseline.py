#!/usr/bin/env python3
"""Capture a versioned performance baseline and compare compatible runs."""

import argparse
import datetime
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import statistics
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "benchmarks/SparseTypes.Benchmarks"
DLL = PROJECT / "bin/Release/net10.0/SparseTypes.Benchmarks.dll"
EXPECTED_CASES = 49
MODEL_COUNTS = {1, 4, 16}
PAYLOAD_SHAPES = {"ScalarDictionary", "ModelDictionary", "ModelKeyed"}


def command(*args):
    return subprocess.check_output(args, cwd=ROOT, text=True).strip()


def run(log, *args):
    print("Running:", " ".join(map(str, args)), flush=True)
    with log.open("w", encoding="utf-8") as output:
        subprocess.run(args, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT, check=True)


def digest(paths):
    result = hashlib.sha256()
    for path in sorted(paths):
        result.update(path.relative_to(ROOT).as_posix().encode())
        result.update(b"\0")
        result.update(path.read_bytes().replace(b"\r\n", b"\n"))
    return result.hexdigest()


def finite(value):
    if not isinstance(value, (int, float)) or not math.isfinite(value):
        raise ValueError(f"Missing/non-finite metric: {value!r}")
    return value


def collect(directory):
    cases = {}
    reports = {}
    environments = []
    quality = []
    for path in sorted((directory / "bdn/results").glob("*-report-full.json")):
        report = json.loads(path.read_text(encoding="utf-8-sig"))
        environments.append(report["HostEnvironmentInfo"])
        for case in report["Benchmarks"]:
            key = f'{case["Type"]}.{case["Method"]}({case["Parameters"]})'
            if key in cases:
                raise ValueError(f"Duplicate benchmark: {key}")
            stats = case.get("Statistics")
            memory = case.get("Memory")
            if not stats or not memory or not case.get("Measurements"):
                raise ValueError(f"Failed benchmark: {key}")
            operations = finite(memory["TotalOperations"])
            if operations <= 0:
                raise ValueError(f"No operations: {key}")
            cases[key] = {
                "mean_ns": finite(stats["Mean"]),
                "standard_error_ns": finite(stats["StandardError"]),
                "confidence_interval": stats["ConfidenceInterval"],
            }
            display = case["DisplayInfo"]
            if display in reports:
                raise ValueError(f"Duplicate benchmark display: {display}")
            reports[display] = case
            quality.extend(measurement_quality(key, case["Measurements"]))
    if len(cases) != EXPECTED_CASES:
        raise ValueError(f"Expected {EXPECTED_CASES} cases, received {len(cases)}; inspect benchmark.log")
    if any(environment != environments[0] for environment in environments[1:]):
        raise ValueError("Benchmark reports have different host environments")
    memory_quality(cases, quality, reports, directory / "benchmark.log")
    return cases, environments[0], quality


def read_process_memory(reports, path):
    records = {}
    current = None
    with path.open(encoding="utf-8-sig") as source:
        for line in source:
            if line.startswith("// Benchmark: "):
                current = line.removeprefix("// Benchmark: ").strip()
                if current not in reports or current in records:
                    raise ValueError(f"Unexpected/duplicate benchmark in log: {current}")
                records[current] = []
            elif line.startswith("// GC:"):
                values = [int(value) for value in line.split(":", 1)[1].split()]
                if current is None or len(values) != 5 or any(value < 0 for value in values) or values[4] <= 0:
                    raise ValueError(f"Invalid process GC record: {line.strip()}")
                records[current].append(values)
    if records.keys() != reports.keys():
        raise ValueError("Benchmark log and JSON reports contain different cases")
    return records


def memory_quality(cases, quality, reports, path):
    records = read_process_memory(reports, path)
    indexed = {(item["case"], item["launch"]): item for item in quality}
    seen = set()
    for display, case in reports.items():
        key = f'{case["Type"]}.{case["Method"]}({case["Parameters"]})'
        count = re.search(r"\bLaunchCount=(\d+)\b", display)
        if count is None or int(count[1]) <= 0 or len(records[display]) != int(count[1]):
            raise ValueError(f"Missing/extra process GC records: {key}")
        processes = []
        for launch, values in enumerate(records[display], 1):
            identity = (key, launch)
            if identity not in indexed or indexed[identity]["measured_count"] <= 0:
                raise ValueError(f"Process GC record has no matching measurements: {identity}")
            process = {
                "launch": launch, "gc_operations": values[4], "total_allocated_bytes": values[3],
                "allocated_bytes": finite(values[3] / values[4]),
                **{f"gen{generation}_collections": values[generation] for generation in range(3)},
                **{f"gen{generation}_per_1000_ops": finite(values[generation] * 1000 / values[4])
                   for generation in range(3)},
            }
            indexed[identity].update(process)
            processes.append(process)
            seen.add(identity)
        last = records[display][-1]
        memory = case["Memory"]
        if (memory["TotalOperations"] != last[4]
                or any(memory[f"Gen{generation}Collections"] != last[generation] for generation in range(3))
                or abs(finite(memory["BytesAllocatedPerOperation"]) - processes[-1]["allocated_bytes"]) > 1):
            raise ValueError(f"Process GC log does not match the BDN memory result: {key}")
        names = ["allocated_bytes", *(f"gen{generation}_per_1000_ops" for generation in range(3))]
        cases[key].update({name: statistics.mean(item[name] for item in processes) for name in names})
        cases[key].update({
            "allocated_bytes_min": min(item["allocated_bytes"] for item in processes),
            "allocated_bytes_max": max(item["allocated_bytes"] for item in processes),
            "memory_launches": processes,
        })
    if seen != indexed.keys() or len(indexed) != len(quality):
        raise ValueError("Process GC records and measured launches differ")


def measurement_quality(key, measurements):
    launches = {}
    for item in measurements:
        stage = item["IterationStage"]
        if item["IterationMode"] != "Workload" or stage not in ("Warmup", "Actual"):
            continue
        operations = finite(item["Operations"])
        elapsed = finite(item["Nanoseconds"])
        if operations <= 0 or elapsed <= 0:
            raise ValueError(f"Invalid workload measurement: {key}")
        stages = launches.setdefault(item["LaunchIndex"], {"Warmup": [], "Actual": []})
        stages[stage].append((item["IterationIndex"], elapsed / operations))
    result = []
    for launch, stages in sorted(launches.items()):
        actual = [value for _, value in sorted(stages["Actual"])]
        enough = len(actual) >= 6
        first = statistics.median(actual[:3]) if enough else None
        last = statistics.median(actual[-3:]) if enough else None
        result.append({
            "case": key, "launch": launch, "warmup_count": len(stages["Warmup"]),
            "measured_count": len(actual), "first_three_median_ns": first,
            "last_three_median_ns": last,
            "first_to_last_ratio": first / last if enough else None,
        })
    return result


def quality_report(records):
    lines = ["# Measurement stability", "",
             "Each row compares the first three and last three measured workload iterations in one process.",
             "The ratio is early / late time. Drift can reflect runtime compilation, caches or machine load; inspect the raw log before attributing it to an implementation change.",
             "At least six iterations are required. Smoke runs cannot assess stability.", "",
             "Allocation and GC values come from each process's raw GC record. GC values are collections per 1,000 operations.", "",
             "| Case | Launch | Warmups | Measurements | Early / late | Allocated (B/op) | Gen0 | Gen1 | Gen2 |",
             "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"]
    for record in sorted(records, key=lambda item: (item["case"], item["launch"])):
        ratio = record["first_to_last_ratio"]
        display = f"{ratio:.3f}" if ratio is not None else "n/a"
        gc = " | ".join(f'{record[f"gen{generation}_per_1000_ops"]:.3f}' for generation in range(3))
        lines.append(f'| {record["case"]} | {record["launch"]} | {record["warmup_count"]} | {record["measured_count"]} | '
                     f'{display} | {record["allocated_bytes"]:.1f} | {gc} |')
    return "\n".join(lines) + "\n"


def validate_sizes(records, payload):
    if not isinstance(records, list):
        raise ValueError("Size records must be an array")
    expected = {(shape, count) for shape in PAYLOAD_SHAPES for count in MODEL_COUNTS} if payload else MODEL_COUNTS
    indexed = {}
    for item in records:
        if not isinstance(item, dict):
            raise ValueError("Size record must be an object")
        key = (item["Shape"], item["ModelCount"]) if payload else item["ModelCount"]
        if key not in expected or key in indexed:
            raise ValueError(f"Unexpected/duplicate size record: {key}")
        for name in ("ModelCount", "GeneratedFiles", "GeneratedSourceBytes", "AssemblyBytes"):
            value = item[name]
            if type(value) is not int or value <= 0:
                raise ValueError(f"Invalid {name} for size record {key}: {value!r}")
        fingerprint = item["GeneratedSourceSha256"]
        if not isinstance(fingerprint, str) or len(fingerprint) != 64 or any(
                character not in "0123456789abcdefABCDEF" for character in fingerprint):
            raise ValueError(f"Invalid source fingerprint for size record {key}")
        indexed[key] = item
    if indexed.keys() != expected:
        raise ValueError(f"Missing size records: {sorted(expected - indexed.keys())}")
    return indexed


def size_table(current, previous, payload):
    old_sizes = validate_sizes(previous, payload)
    now_sizes = validate_sizes(current, payload)
    label = "Shape / parent models" if payload else "Models"
    lines = ["", f"| {label} | Generated source Δ (UTF-8 B) | Assembly Δ (B) | Source fingerprint changed |",
             "| --- | ---: | ---: | --- |"]
    for key, now in sorted(now_sizes.items()):
        old = old_sizes[key]
        name = f"{key[0]} / {key[1]}" if payload else str(key)
        lines.append(f'| {name} | {now["GeneratedSourceBytes"] - old["GeneratedSourceBytes"]:+d} | '
                     f'{now["AssemblyBytes"] - old["AssemblyBytes"]:+d} | '
                     f'{"yes" if now["GeneratedSourceSha256"] != old["GeneratedSourceSha256"] else "no"} |')
    return lines


def compare(current, previous):
    lines = ["# Performance comparison", "", f'Previous: `{previous["commit"]}`',
             f'Current: `{current["commit"]}`', ""]
    reasons = [name for name in ("schema", "suite", "profile", "environment", "benchmark_sha256")
               if current[name] != previous.get(name)]
    if current["cases"].keys() != previous.get("cases", {}).keys():
        reasons.append("case set")
    if reasons:
        lines += ["Comparison unavailable: " + ", ".join(reasons) + " changed.",
                  "Keep this run as a new baseline; numeric ratios would mix measurement conditions."]
        return "\n".join(lines) + "\n", False
    lines += ["Allocation and GC changes use equally weighted process means. GC columns are collections per 1,000 operations. Time intervals are the BDN 99.9% intervals.", "",
              "| Case | Time change | Allocated change (B) | Allocation ranges (B, previous / current) | Gen0 Δ | Gen1 Δ | Gen2 Δ | Time intervals overlap |",
              "| --- | ---: | ---: | --- | ---: | ---: | ---: | --- |"]
    for key, now in sorted(current["cases"].items()):
        old = previous["cases"][key]
        ratio = f'{(now["mean_ns"] / old["mean_ns"] - 1) * 100:+.2f}%' if old["mean_ns"] > 0 else "n/a"
        before, after = old["confidence_interval"], now["confidence_interval"]
        intervals = [before["Lower"], before["Upper"], after["Lower"], after["Upper"]]
        overlap = ("yes" if before["Lower"] <= after["Upper"] and after["Lower"] <= before["Upper"] else "no") if all(isinstance(item, (int, float)) for item in intervals) else "n/a"
        changes = [now[f"gen{gen}_per_1000_ops"] - old[f"gen{gen}_per_1000_ops"] for gen in range(3)]
        ranges = (f'{old["allocated_bytes_min"]:.1f}..{old["allocated_bytes_max"]:.1f} / '
                  f'{now["allocated_bytes_min"]:.1f}..{now["allocated_bytes_max"]:.1f}')
        lines.append(f'| {key} | {ratio} | {now["allocated_bytes"] - old["allocated_bytes"]:+.1f} | '
                     + ranges + " | " + " | ".join(f"{change:+.3f}" for change in changes) + f' | {overlap} |')
    lines += size_table(current["sizes"], previous["sizes"], False)
    lines += ["", "## Payload generator output"]
    lines += size_table(current["payload_sizes"], previous["payload_sizes"], True)
    return "\n".join(lines) + "\n", True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--previous", type=Path)
    parser.add_argument("--smoke", action="store_true", help="Validate the harness; never compare with a full baseline")
    parser.add_argument("--collect-only", action="store_true", help="Normalize an already completed run")
    args = parser.parse_args()
    output = args.output.resolve()
    benchmark_paths = [*PROJECT.glob("*.cs"), *PROJECT.glob("*.csproj"), Path(__file__).resolve()]
    if not args.collect_only:
        output.mkdir(parents=True, exist_ok=False)
        provenance = {
            "commit": command("git", "rev-parse", "HEAD"),
            "dirty": bool(command("git", "status", "--porcelain", "--untracked-files=normal")),
            "benchmark_sha256": digest(benchmark_paths),
            "profile": "smoke" if args.smoke else "baseline",
            "sdk": command("dotnet", "--version"),
            "runtime_variables": {key: value for key, value in sorted(os.environ.items()) if key.startswith(("DOTNET_GC", "DOTNET_Tiered", "DOTNET_ReadyToRun", "DOTNET_Enable", "COMPlus_"))},
        }
        (output / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")
        run(output / "build.log", "dotnet", "build", "-c", "Release", str(PROJECT / "SparseTypes.Benchmarks.csproj"),
            "-m:1", "--disable-build-servers", "/p:UseSharedCompilation=false", "-v", "quiet")
        run(output / "inputs.log", "dotnet", str(DLL), "--baseline-validate-inputs")
        run(output / "sizes.log", "dotnet", str(DLL), "--baseline-sizes", str(output / "sizes.json"))
        run(output / "payload-sizes.log", "dotnet", str(DLL), "--payload-sizes", str(output / "payload-sizes.json"))
        run(output / "benchmark.log", "dotnet", str(DLL), "--baseline", *( ["--smoke"] if args.smoke else [] ),
            "--filter", "*", "--artifacts", str(output / "bdn"))
    provenance = json.loads((output / "provenance.json").read_text(encoding="utf-8"))
    if digest(benchmark_paths) != provenance["benchmark_sha256"]:
        raise ValueError("Benchmark source changed during the run; start a new run")
    cases, host, quality = collect(output)
    (output / "measurement-quality.json").write_text(json.dumps(quality, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    (output / "measurement-quality.md").write_text(quality_report(quality), encoding="utf-8")
    sizes = json.loads((output / "sizes.json").read_text(encoding="utf-8-sig"))
    payload_sizes = json.loads((output / "payload-sizes.json").read_text(encoding="utf-8-sig"))
    validate_sizes(sizes, False)
    validate_sizes(payload_sizes, True)
    current = {
        "schema": 3,
        "suite": "sparsetypes-v1",
        "profile": provenance["profile"],
        "captured_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "commit": provenance["commit"],
        "dirty": provenance["dirty"],
        "benchmark_sha256": provenance["benchmark_sha256"],
        "environment": {"sdk": provenance["sdk"], "host": host, "runtime_variables": provenance["runtime_variables"],
                        "logical_cpu_count": os.cpu_count(), "machine": platform.machine()},
        "sizes": sizes,
        "payload_sizes": payload_sizes,
        "cases": cases,
    }
    (output / "baseline.json").write_text(json.dumps(current, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    if args.previous:
        previous = json.loads(args.previous.read_text(encoding="utf-8-sig"))
        report, comparable = compare(current, previous)
        (output / "comparison.md").write_text(report, encoding="utf-8")
        print("Comparison written." if comparable else "Environment/suite changed; recorded a new baseline.")
    print(f"Recorded {len(cases)} cases: {output / 'baseline.json'}")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, OSError, subprocess.CalledProcessError) as error:
        print(f"Baseline failed: {error}", file=sys.stderr)
        sys.exit(1)
