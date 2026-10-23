#!/usr/bin/env python3
"""
==============================================================================
NanoWeaverHrt (nanoweaver-hrt): Molecular Simulation Node Daemon
------------------------------------------------------------------------------
WHAT: Lightweight, zero-dependency HTTP simulation daemon and hardware probe 
      for oxDNA 2.4 and LAMMPS molecular dynamics runtimes.
WHY:  Exposes health checks and OpenCL/SYCL GPU accelerator telemetry to the
      sovereign .NET 10 microservice. It submits no jobs: the engine containers run
      the decks in configs/ directly, so every POST is refused.
HOW:  Utilizes Python 3's built-in http.server and socketserver to bind to 
      port 8080, inspecting /dev/dri, clinfo, and /workspace mounts.
==============================================================================
"""

import json
import os
import shutil
import signal
import socket
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse

START_TIME = time.time()
PORT = int(os.environ.get("SIMULATION_DAEMON_PORT", "8080"))
BIND_HOST = os.environ.get("SIMULATION_DAEMON_HOST", "0.0.0.0")
CONFIGS_DIR = os.environ.get("SIMULATION_CONFIGS_DIR", "/workspace/configs")
DATA_DIR = os.environ.get("TRAJECTORY_INCOMING_DIR", "/workspace/data")
EXPORTS_DIR = os.environ.get("TRAJECTORY_EXPORT_DIR", "/workspace/exports")


def get_available_executables():
    """Detects available scientific simulation binaries in PATH and standard locations."""
    return {
        "oxDNA": shutil.which("oxDNA") is not None or os.path.exists("/usr/local/bin/oxDNA"),
        "lammps": shutil.which("lammps") is not None or os.path.exists("/usr/bin/lammps"),
        "DNAnalysis": shutil.which("DNAnalysis") is not None or os.path.exists("/usr/local/bin/DNAnalysis"),
        "confGenerator": shutil.which("confGenerator") is not None or os.path.exists("/usr/local/bin/confGenerator"),
    }


def get_opencl_telemetry():
    """Extracts OpenCL platform and hardware device summary if clinfo is present."""
    devices = []
    if shutil.which("clinfo"):
        try:
            res = subprocess.run(["clinfo", "-l"], capture_output=True, text=True, timeout=3)
            if res.returncode == 0:
                devices = [line.strip() for line in res.stdout.splitlines() if line.strip()]
        except Exception:
            pass
    return devices


def get_dri_devices():
    """Inspects Linux direct rendering infrastructure (/dev/dri) character devices."""
    if os.path.exists("/dev/dri"):
        try:
            return sorted(os.listdir("/dev/dri"))
        except Exception:
            pass
    return []


def get_discovered_configs():
    """Enumerates discovered scientific input decks in the configuration volume."""
    if os.path.exists(CONFIGS_DIR):
        try:
            return sorted([f for f in os.listdir(CONFIGS_DIR) if os.path.isfile(os.path.join(CONFIGS_DIR, f))])
        except Exception:
            pass
    return []


class SimulationDaemonHandler(BaseHTTPRequestHandler):
    """HTTP request handler providing health, telemetry, and simulation control."""

    server_version = "NanoWeaverHrt-SimDaemon/1.0"

    def _send_json_response(self, status_code, data):
        """Writes a JSON response body with no cross-origin sharing permission.

        WHAT: Serializes the payload and emits only the headers the response needs.
        WHY:  This daemon has exactly one legitimate caller, the .NET parser, which reaches
              it server-to-server over the internal compose network where CORS plays no
              part. The previous "Access-Control-Allow-Origin: *" header instead let any
              web page a researcher happened to visit read this node's hostname, GPU
              inventory, config filenames and data mount paths straight out of their
              browser. No host port is published for this daemon any more (Prime Directive
              #9), so the header had no remaining purpose and every remaining use of it
              was an attack.
        HOW:  Sends status, content type and length, and deliberately nothing else. Omitting
              the header makes the browser's same-origin policy refuse cross-origin reads.
        """
        payload = json.dumps(data, indent=2).encode("utf-8")
        self.send_response(status_code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def do_HEAD(self):
        """Responds to HEAD health probes with 200 OK."""
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.end_headers()

    def do_GET(self):
        """Handles incoming diagnostic and telemetry queries."""
        parsed = urlparse(self.path)
        path = parsed.path.rstrip("/")
        if not path:
            path = "/"

        if path in ("/", "/health", "/healthz", "/status"):
            configs = get_discovered_configs()
            data = {
                "status": "Healthy",
                "service": "nanoweaver-hrt-simulation",
                "version": "2024",
                "engine": "oxDNA / LAMMPS",
                "accelerator": os.environ.get("ACCELERATOR_BACKEND", "Intel iGPU / OpenCL (Default)"),
                "hostname": socket.gethostname(),
                "uptimeSeconds": round(time.time() - START_TIME, 2),
                "executables": get_available_executables(),
                "openclDevices": get_opencl_telemetry(),
                "driDevices": get_dri_devices(),
                "configsCount": len(configs),
                "configs": configs,
                "dataMount": DATA_DIR,
                "dataWritable": os.access(DATA_DIR, os.W_OK) if os.path.exists(DATA_DIR) else False,
            }
            self._send_json_response(200, data)
            return

        if path in ("/telemetry", "/api/system/telemetry"):
            data = {
                "status": "Healthy",
                "accelerator": os.environ.get("ACCELERATOR_BACKEND", "Intel iGPU / OpenCL (Default)"),
                "devices": get_opencl_telemetry(),
                "driNodes": get_dri_devices(),
                "shmSize": "4 GB Host Shared Memory",
                "timestampUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            }
            self._send_json_response(200, data)
            return

        if path in ("/configs", "/api/configs"):
            configs = get_discovered_configs()
            self._send_json_response(200, {"configs": configs, "count": len(configs), "directory": CONFIGS_DIR})
            return

        # Fallback 404
        self._send_json_response(404, {"error": f"Endpoint '{self.path}' not found on simulation daemon."})

    def do_POST(self):
        """Rejects every POST, because this daemon submits no jobs.

        WHAT: Answers any POST with a JSON 404, in the same shape do_GET uses.
        WHY:  There used to be a /simulate, /api/simulate and /jobs handler here that
              minted a job_id and answered {"status": "Accepted"} without ever starting
              a solver. Nothing in the platform called it, and a caller that believed it
              would wait forever for a trajectory no engine was ever asked to produce.
              Reporting success for work that will not happen is worse than refusing it,
              so the endpoint is gone rather than left as a stub.
        HOW:  Simulation runs are launched by the engine containers from the decks in
              configs/, not over HTTP. Should job submission ever be wanted, it needs a
              real queue and a real executor, not a revival of this handler.
        """
        self._send_json_response(404, {"error": f"POST endpoint '{self.path}' not supported."})

    def log_message(self, format_str, *args):
        """Formats request logging to standard error with ISO timestamp."""
        ts = time.strftime("%Y-%m-%d %H:%M:%S", time.localtime())
        sys.stderr.write(f"[{ts}] [SimDaemon] {format_str % args}\n")
        sys.stderr.flush()


def run_daemon():
    """Starts the multi-threaded simulation HTTP server and handles shutdown signals."""
    server = ThreadingHTTPServer((BIND_HOST, PORT), SimulationDaemonHandler)
    server.daemon_threads = True

    def _shutdown_handler(signum, frame):
        # WHAT: Requests shutdown from a helper thread instead of this one.
        # WHY:  A Python signal handler runs on the main thread, suspending whatever that
        #       thread was doing -- here, serve_forever(). shutdown() sets a flag and then
        #       BLOCKS until the serve_forever loop acknowledges it, but that loop cannot
        #       resume until this handler returns. Calling it directly therefore deadlocks:
        #       SIGTERM left the daemon listening and `docker stop` only ever killed it by
        #       waiting out its grace period and sending SIGKILL, which means no in-flight
        #       request was ever allowed to finish.
        # HOW:  A separate thread blocks on shutdown() while this handler returns at once,
        #       letting serve_forever observe the flag and exit. run_daemon's `finally`
        #       closes the socket, so neither server_close() nor sys.exit() belongs here.
        sys.stderr.write(f"\n[SimDaemon] Signal {signum} received. Gracefully stopping daemon...\n")
        sys.stderr.flush()
        threading.Thread(target=server.shutdown, name="shutdown", daemon=True).start()

    signal.signal(signal.SIGTERM, _shutdown_handler)
    signal.signal(signal.SIGINT, _shutdown_handler)

    sys.stderr.write(f"==========================================================\n")
    sys.stderr.write(f"  NanoWeaverHrt Simulation Daemon Active\n")
    sys.stderr.write(f"  Listening: http://{BIND_HOST}:{PORT}\n")
    sys.stderr.write(f"  Hostname:  {socket.gethostname()}\n")
    sys.stderr.write(f"  Configs:   {CONFIGS_DIR}\n")
    sys.stderr.write(f"==========================================================\n")
    sys.stderr.flush()

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    run_daemon()
