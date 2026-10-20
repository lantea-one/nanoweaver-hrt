#!/usr/bin/env bash
# ==============================================================================
# Simulation Node Entrypoint & Accelerator Telemetry
# ------------------------------------------------------------------------------
# WHAT: Detects available hardware accelerators (Intel iGPU, Arc A770, NVIDIA, 
#       AMD Radeon, or CPU fallback) and initializes the simulation workspace.
# WHY:  Enables sovereign nodes to automatically adapt to whatever GPU hardware 
#       is attached without manual reconfiguration.
# HOW:  Inspects OpenCL platforms, Level Zero devices, and DRI device nodes.
# ==============================================================================
set -euo pipefail

echo "=========================================================="
echo "  HRT-Lab: Sovereign Simulation Node Initialization"
echo "  Node Identity: ${HOSTNAME:-simulation-node}"
echo "  Target Backend: ${ACCELERATOR_BACKEND:-auto}"
echo "=========================================================="

echo "==> Inspecting Available Hardware Acceleration..."
if [ -d "/dev/dri" ]; then
    echo "Found /dev/dri devices:"
    ls -la /dev/dri || true
else
    echo "Notice: /dev/dri not mounted. Proceeding with CPU simulation mode."
fi

if command -v clinfo >/dev/null 2>&1; then
    echo "==> OpenCL Platform & Device Summary:"
    clinfo -l || echo "No OpenCL devices found or driver initialization failed."
fi

# Create shared data and config directories if they do not exist
mkdir -p /workspace/configs /workspace/data /workspace/exports

echo "==> Simulation Node ready. Listening for execution jobs..."
exec "$@"
