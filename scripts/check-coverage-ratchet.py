#!/usr/bin/env python3
"""Gate step 7: compare an un-excluded Cobertura measurement against the committed ratchet baseline.

WHAT: Reads every coverage.cobertura.xml under one directory, totals lines and branches (summing
      classes that share a name), and compares the result with coverage-baseline.json.
WHY:  Gate step 4 excludes compiler-generated code (minimal-API endpoint lambdas, async state
      machines), so its 100.00% describes only part of the solution. This step measures the whole
      thing and refuses any regression, while forcing every improvement to be committed so the
      baseline can only ever move toward 100%.
HOW:  Exit 0 only when the measurement matches the baseline: the uncovered-line counts exactly, and
      the two derived rates within RATE_TOLERANCE, which absorbs a wobble no machine controls. Exit
      1 on any regression, on a stale baseline (a pure improvement), and when no report exists at
      all (fail closed).

Usage: python3 -I scripts/check-coverage-ratchet.py REPORT_DIR BASELINE_JSON [--update] [--allow-regression]
Pure standard library, like scripts/check-docblocks.py.
"""

import datetime
import glob
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

BRANCH_PATTERN = re.compile(r"\((\d+)/(\d+)\)")

# Largest difference in line_rate or branch_rate, in percentage points, treated as noise rather
# than as a regression or an improvement. Sized to absorb the ~0.01 wobble the ValkeyEventBridge
# race produces between machines and runs, and nothing larger. The integer uncovered-line counts
# are compared exactly and are what actually guards against a coverage loss.
RATE_TOLERANCE = 0.05


def measure(report_dir):
    """Totals coverage across every Cobertura report found under report_dir.

    Returns a dict with line_rate, branch_rate, uncovered_lines_total and classes (name -> uncovered
    line count, zero-count classes omitted), or None when no report exists.
    """
    reports = sorted(glob.glob(os.path.join(report_dir, "**", "coverage.cobertura.xml"), recursive=True))
    if not reports:
        return None

    lines_total = 0
    lines_covered = 0
    branches_total = 0
    branches_covered = 0
    uncovered_by_class = {}

    for report in reports:
        root = ET.parse(report).getroot()
        for cls in root.iter("class"):
            name = cls.attrib.get("name", "")
            uncovered_by_class.setdefault(name, 0)
            # Only the class-level <lines>: each line is repeated under its method as well.
            for line in cls.findall("lines/line"):
                lines_total += 1
                if int(line.attrib.get("hits", "0")) == 0:
                    uncovered_by_class[name] += 1
                else:
                    lines_covered += 1
                match = BRANCH_PATTERN.search(line.attrib.get("condition-coverage", ""))
                if match:
                    branches_covered += int(match.group(1))
                    branches_total += int(match.group(2))

    classes = {name: count for name, count in sorted(uncovered_by_class.items()) if count > 0}
    return {
        "line_rate": round(100.0 * lines_covered / lines_total, 2) if lines_total else 100.0,
        "branch_rate": round(100.0 * branches_covered / branches_total, 2) if branches_total else 100.0,
        "uncovered_lines_total": sum(classes.values()),
        "classes": classes,
    }


def compare(baseline, measured):
    """Classifies every difference. Returns (regressions, improvements) as lists of strings."""
    regressions = []
    improvements = []

    # WHAT: Compares the two derived percentages with a tolerance, not for exact equality.
    # WHY:  branch_rate is not reproducible across machines. The ValkeyEventBridge race makes
    #       ExecuteAsync run 169 or 170 times depending on timing, which moves the rate by about
    #       0.01. CI runs 13 and 14 both failed on exactly that: run 14 measured branch 89.56
    #       against a baseline of 89.57 while the line rate, the uncovered-line total and every
    #       per-class count matched to the digit. An exact float match on a value that wobbles
    #       cannot stay green, and because this gate fails closed in both directions - below is a
    #       regression, above is "stale" - the wobble alone was enough to block every push.
    # HOW:  A difference within RATE_TOLERANCE counts as neither a regression nor an improvement.
    #       The real detectors are untouched and stay exact: uncovered_lines_total and the
    #       per-class uncovered-line map below are integers, so they do not drift, and a genuine
    #       coverage loss shows up there as well as in the rate. This is a tolerance on a derived
    #       summary, not on the guarantee. The race itself is still an open defect worth fixing;
    #       see work-packages/ratchet-nondeterminism.md.
    for key in ("line_rate", "branch_rate"):
        old = baseline[key]
        new = measured[key]
        if abs(new - old) <= RATE_TOLERANCE:
            continue
        if new < old:
            regressions.append(f"{key}: baseline {old:.2f} -> measured {new:.2f}")
        else:
            improvements.append(f"{key}: baseline {old:.2f} -> measured {new:.2f}")

    old_total = baseline["uncovered_lines_total"]
    new_total = measured["uncovered_lines_total"]
    if new_total > old_total:
        regressions.append(f"uncovered_lines_total: baseline {old_total} -> measured {new_total}")
    elif new_total < old_total:
        improvements.append(f"uncovered_lines_total: baseline {old_total} -> measured {new_total}")

    for name in sorted(set(baseline["classes"]) | set(measured["classes"])):
        old = baseline["classes"].get(name, 0)
        new = measured["classes"].get(name, 0)
        if new > old:
            regressions.append(f"{name}: baseline {old} -> measured {new}")
        elif new < old:
            improvements.append(f"{name}: baseline {old} -> measured {new}")

    return regressions, improvements


def head_commit():
    """Returns the current git HEAD, or 'unknown' when git is unavailable."""
    try:
        return subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    except (OSError, subprocess.CalledProcessError):
        return "unknown"


def main(argv):
    """Entry point; returns the process exit code."""
    args = [a for a in argv[1:] if not a.startswith("--")]
    flags = {a for a in argv[1:] if a.startswith("--")}
    if len(args) != 2 or not flags <= {"--update", "--allow-regression"}:
        print("usage: check-coverage-ratchet.py REPORT_DIR BASELINE_JSON [--update] [--allow-regression]")
        return 1
    report_dir, baseline_path = args

    measured = measure(report_dir)
    if measured is None:
        print(f"::error::No coverage report found (no coverage.cobertura.xml under {report_dir}).")
        return 1

    print(f"Measured: line {measured['line_rate']:.2f}%, branch {measured['branch_rate']:.2f}%, "
          f"{measured['uncovered_lines_total']} uncovered lines in {len(measured['classes'])} classes.")

    seeding = False
    if os.path.exists(baseline_path):
        with open(baseline_path, encoding="utf-8") as handle:
            baseline = json.load(handle)
    elif "--update" in flags:
        # A missing baseline being created for the first time has nothing to regress against.
        seeding = True
        baseline = {
            "schema_version": 1,
            "measurement": {"runsettings": "coverage-full.runsettings", "weaver_test_postgres": "unset"},
            "line_rate": 0.0,
            "branch_rate": 0.0,
            "uncovered_lines_total": 0,
            "classes": {},
        }
    else:
        print(f"::error::Baseline {baseline_path} not found.")
        return 1

    regressions, improvements = compare(baseline, measured)

    if "--update" in flags:
        if regressions and not seeding and "--allow-regression" not in flags:
            print("Refusing to update: the measurement regressed against the baseline.")
            for item in regressions:
                print(f"  REGRESSED {item}")
            return 1
        if not seeding:
            for item in regressions + improvements:
                print(f"  {item}")
        baseline["seeded_from_commit"] = head_commit()
        baseline["seeded_on_utc"] = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d")
        baseline["line_rate"] = measured["line_rate"]
        baseline["branch_rate"] = measured["branch_rate"]
        baseline["uncovered_lines_total"] = measured["uncovered_lines_total"]
        baseline["classes"] = measured["classes"]
        ordered = {k: baseline[k] for k in ("schema_version", "seeded_from_commit", "seeded_on_utc", "measurement",
                                            "line_rate", "branch_rate", "uncovered_lines_total", "classes")}
        with open(baseline_path, "w", encoding="utf-8") as handle:
            json.dump(ordered, handle, indent=2)
            handle.write("\n")
        print(f"Baseline updated: {baseline_path}")
        return 0

    if regressions:
        print("Coverage ratchet FAILED: the un-excluded measurement regressed.")
        for item in regressions:
            print(f"  REGRESSED {item}")
        for item in improvements:
            print(f"  IMPROVED  {item}")
        return 1

    if improvements:
        for item in improvements:
            print(f"  IMPROVED  {item}")
        print("Baseline is stale. Re-run with --update and commit the improved numbers in this same diff.")
        return 1

    print("Coverage ratchet verified: measurement matches the committed baseline exactly.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
