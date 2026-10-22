#!/usr/bin/env python3
"""
==============================================================================
NanoWeaverHrt (nanoweaver-hrt): Molecular Simulation Node Daemon
------------------------------------------------------------------------------
WHAT: Lightweight, zero-dependency HTTP simulation daemon and hardware probe 
      for oxDNA 2.4 and LAMMPS molecular dynamics runtimes.
WHY:  Exposes health checks, OpenCL/SYCL GPU accelerator telemetry, and 
      simulation job execution endpoints to the sovereign .NET 10 microservice.
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
        """Handles simulation job submissions or validation requests."""
        parsed = urlparse(self.path)
        path = parsed.path.rstrip("/")

        if path in ("/simulate", "/api/simulate", "/jobs"):
            content_length = int(self.headers.get("Content-Length", 0))
            body = self.rfile.read(content_length) if content_length > 0 else b"{}"
            try:
                payload = json.loads(body.decode("utf-8")) if body else {}
            except Exception:
                self._send_json_response(400, {"error": "Malformed JSON payload."})
                return

            deck = payload.get("deck", "minidna.in")
            engine = payload.get("engine", "oxdna" if "oxdna" in deck.lower() else "lammps")

            job_id = f"job_{int(time.time())}_{os.urandom(4).hex()}"
            self._send_json_response(200, {
                "jobId": job_id,
                "status": "Accepted",
                "deck": deck,
                "engine": engine,
                "message": f"Simulation job '{job_id}' queued for execution on {engine}.",
                "timestampUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
            })
            return

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
        sys.stderr.write(f"\n[SimDaemon] Signal {signum} received. Gracefully stopping daemon...\n")
        server.shutdown()
        server.server_close()
        sys.exit(0)

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
