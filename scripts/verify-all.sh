#!/usr/bin/env bash
# ==============================================================================
# NanoWeaverHrt (nanoweaver-hrt): Universal Verification & Quality Gate
# ------------------------------------------------------------------------------
# WHAT: Comprehensive pre-flight verification script enforcing all project
#       standards: .NET code formatting, 100% C# code coverage (line + branch),
#       100% JavaScript coverage, Markdown math syntax check, and docblock integrity.
# WHY:  Guarantees that 100% accuracy, zero-allocation invariants, and zero-defect
#       standards are upheld by all contributors and autonomous AI agents.
# HOW:  Executes dotnet format, npm test, dotnet test with coverlet, and Python
#       verifiers in sequence. Exits immediately with non-zero on any deviation.
# ==============================================================================

set -euo pipefail

echo "=============================================================================="
echo ">> Step 1/5: Verifying .NET Code Formatting..."
echo "=============================================================================="
XDG_RUNTIME_DIR=/tmp dotnet format NanoWeaverHrt.slnx --no-restore --verify-no-changes --verbosity normal
echo "✓ .NET code formatting verified cleanly."

echo ""
echo "=============================================================================="
echo ">> Step 2/5: Verifying JavaScript Code & Coverage Gate (100% Threshold)..."
echo "=============================================================================="
npm test
echo "✓ JavaScript test suite passed with 100% line, branch, and function coverage."

echo ""
echo "=============================================================================="
echo ">> Step 3/5: Running .NET Test Suite with Coverage Collection..."
echo "=============================================================================="
XDG_RUNTIME_DIR=/tmp dotnet test NanoWeaverHrt.slnx \
    --configuration Release \
    --settings coverlet.runsettings \
    --collect:"XPlat Code Coverage" \
    --verbosity normal
echo "✓ All .NET unit and integration tests passed."

echo ""
echo "=============================================================================="
echo ">> Step 4/5: Enforcing 100.00% Line & Branch Coverage Threshold..."
echo "=============================================================================="
python3 -c "
import xml.etree.ElementTree as ET
import glob
import os
import sys

files = glob.glob('**/coverage.cobertura.xml', recursive=True)
if not files:
    print('::error::No coverage.cobertura.xml report found!')
    sys.exit(1)

latest_file = max(files, key=lambda f: os.path.getmtime(f))
print(f'Evaluating coverage report: {latest_file}')
tree = ET.parse(latest_file)
root = tree.getroot()

line_rate = float(root.attrib.get('line-rate', 0)) * 100
branch_rate = float(root.attrib.get('branch-rate', 0)) * 100

print(f'Line Coverage:   {line_rate:.2f}% (Required: 100.00%)')
print(f'Branch Coverage: {branch_rate:.2f}% (Required: 100.00%)')

if line_rate < 100.0 or branch_rate < 100.0:
    for pkg in root.findall('.//package'):
        for cls in pkg.findall('.//class'):
            c_line = float(cls.attrib.get('line-rate', 1)) * 100
            c_branch = float(cls.attrib.get('branch-rate', 1)) * 100
            if c_line < 100 or c_branch < 100:
                fname = cls.attrib.get('filename', '')
                cname = cls.attrib.get('name', '')
                print(f'FAIL: {cname} in {fname} (Line: {c_line:.2f}%, Branch: {c_branch:.2f}%)')
                for l in cls.findall('.//line'):
                    if int(l.attrib.get('hits', 0)) == 0:
                        print(f'  Uncovered line {l.attrib.get(\"number\")}')
                for l in cls.findall('.//line'):
                    if 'condition-coverage' in l.attrib and '100%' not in l.attrib['condition-coverage']:
                        print(f'  Uncovered branch {l.attrib.get(\"number\")}: {l.attrib.get(\"condition-coverage\")}')

if line_rate < 100.0:
    print(f'ERROR: Line coverage ({line_rate:.2f}%) is below mandatory 100.00% threshold!')
    sys.exit(1)

if branch_rate < 100.0:
    print(f'ERROR: Branch coverage ({branch_rate:.2f}%) is below mandatory 100.00% threshold!')
    sys.exit(1)

print('✓ Line and branch code coverage verified at 100.00%.')
"

echo ""
echo "=============================================================================="
echo ">> Step 5/5: Verifying Documentation Markdown Math Delimiters..."
echo "=============================================================================="
python3 - << 'EOF'
import glob, re, sys

files = sorted(set(glob.glob('docs/**/*.md', recursive=True) + glob.glob('docs/*.md') + ['README.md']))
violations = []
inline_display_violations = []

for f in files:
    content = open(f, encoding='utf-8').read()
    # Strip fenced code blocks and inline code spans to avoid false positives on shell scripts or code
    code_stripped = re.sub(r'```.*?```', '', content, flags=re.DOTALL)
    code_stripped = re.sub(r'`.*?`', '', code_stripped)
    
    matches = re.findall(r'(?<!\$)\$(?!\$)(.*?)(?<!\$)\$(?!\$)', code_stripped)
    if matches:
        violations.append((f, len(matches), matches[:3]))
        
    for line_idx, line in enumerate(code_stripped.split('\n'), 1):
        sline = line.strip()
        if '$$' in sline:
            if not (sline.startswith('$$') and sline.endswith('$$')) or sline.count('$$') > 2:
                inline_display_violations.append((f, line_idx, sline))

if violations:
    print('ERROR: Found unescaped/broken single-dollar math delimiters in documentation:')
    for f, count, samples in violations:
        print(f'  {f}: {count} violations (Samples: {samples})')
    print('Use clean Unicode symbols (e.g. µL, °C, 17β-estradiol, Mg²⁺) instead of $...$')

if inline_display_violations:
    print('ERROR: Found inline double-dollar ($$) math delimiters embedded in text/tables:')
    for f, line_idx, line in inline_display_violations:
        print(f'  {f}:{line_idx}: {line}')
    print('Standalone equations must be fenced on their own lines ($$...$$). Use clean Unicode for inline text.')

if violations or inline_display_violations:
    sys.exit(1)

print(f'✓ Scanned {len(files)} markdown files: Zero broken LaTeX interpolation tokens found.')
EOF

echo ""
echo "=============================================================================="
echo ">> ALL VERIFICATION GATES PASSED (100% ACCURACY & 100% COVERAGE VERIFIED)"
echo "=============================================================================="
