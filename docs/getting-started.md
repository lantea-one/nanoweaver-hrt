# Getting Started & Deployment Guide

> **Quickstart, Local Setup, and Hardware-Accelerated Container Deployment**

This guide walks you through setting up and running **NanoWeaverHrt** (`nanoweaver-hrt`) on your local workstation or high-performance compute node. The system is designed to run self-contained with zero external cloud dependencies.

---

## 1. System Requirements & Hardware Acceleration

### Minimum Workstation Requirements
* **CPU**: 64-bit x86_64 or ARM64 processor (SSE4.2 or ARM NEON support required).
* **RAM**: 8 GB minimum (16 GB+ recommended for multi-gigabyte trajectory parsing).
* **Storage**: 2 GB free disk space for the .NET service and container images.
* **Operating System**: Linux (Ubuntu 22.04+, Debian 12+, Fedora 39+, Arch Linux) or Windows 11 with WSL2.
* **Software Prerequisites**:
  * [.NET 10 SDK](https://dotnet.microsoft.com/)
  * [Docker](https://www.docker.com/) and [Docker Compose v2+](https://docs.docker.com/compose/)
  * [Node.js v20+](https://nodejs.org/) (for running UI unit tests)

### Hardware SIMD Vectorization
The trajectory parsing pipeline automatically detects and leverages SIMD vector instruction sets at runtime:
* **AVX-512**: Vectorizes 16 32-bit floats simultaneously (Xeon Scalable, AMD Zen 4/5).
* **AVX2**: Vectorizes 8 32-bit floats simultaneously (Intel Haswell+, AMD Zen 1+).
* **SSE4.2**: Vectorizes 4 32-bit floats simultaneously (legacy x86_64 fallback).

### GPU Acceleration Backends
The simulation containers support accelerated molecular dynamics (oxDNA and LAMMPS) across all major GPU hardware:
* **Intel Discrete GPUs** (Arc A770, A750, Flex, Max) via Intel oneAPI Level Zero / OpenCL.
* **NVIDIA GPUs** (RTX 30xx/40xx, A100, H100) via CUDA and NVIDIA Container Toolkit.
* **AMD Radeon GPUs** (RX 7000, Instinct MI200/MI300) via AMD ROCm.
* **Intel Integrated GPUs / CPU Fallback**: Directly via Linux Direct Rendering Infrastructure (`/dev/dri`).

---

## 2. Running Locally with .NET 10 SDK

For development, testing, and UI exploration, you can run the trajectory parser and sovereign Blazor dashboard directly on your host machine.

### Step 1: Clone the Repository
```bash
git clone https://git.lantea.one/Lantea.One/nanoweaver-hrt.git
cd nanoweaver-hrt
```

### Step 2: Build the Solution
Compile the .NET 10 microservice and test projects:
```bash
# Set XDG_RUNTIME_DIR if running in sandboxed or headless Linux environments
XDG_RUNTIME_DIR=/tmp dotnet build NanoWeaverHrt.sln
```

### Step 3: Run the Service
Launch the microservice and embedded web server:
```bash
XDG_RUNTIME_DIR=/tmp dotnet run --project src/WeaverLammpsHrt.TrajectoryParser/WeaverLammpsHrt.TrajectoryParser.csproj
```

Once running, navigate your web browser to:
```
http://localhost:5000
```

The web dashboard initializes automatically with embedded SQLite storage (or PostgreSQL if configured) and connects to local file streams.

### Step 4: First-Launch Master Administrator Setup Wizard

On initial boot, the sovereign node enforces a strict zero-backdoor policy: **no default passwords or insecure credentials exist**. All core platform operations and API endpoints are locked until the authoritative **Master Administrator** is initialized.

![First-Launch Administrator Setup Wizard](images/08_setup_wizard.png)

1. **Automatic Redirection**: Navigating to any page on a fresh node automatically redirects to `/setup`.
2. **Master Identity**: Choose your Administrator username and configure the access gate boundary (`ApiWeb` or `Web`).
3. **Cryptographic Keypair Generation**:
   - Generate a high-security keypair directly in the browser (Ed25519 recommended, ECDSA P-256, or RSA-4096).
   - Optionally encrypt the private key with a passphrase (AES-256-CBC with PBKDF2 HMAC-SHA256).
   - Alternatively, supply an existing public key in PEM or Base64 format.
4. **Mandatory Backup & Lock-In**:
   - Download the generated private key (`.pem`) file.
   - Confirm secure offline storage.
   - Once completed, the setup wizard is permanently locked to prevent administrative hijacking.

---

## 3. Running with Docker Compose

For production deployment and automated integration with oxDNA and LAMMPS simulation containers, use the provided Docker Compose profiles.

### Scenario A: Default Deployment (Intel iGPU or CPU Fallback)
This configuration passes through `/dev/dri` for Intel integrated graphics or falls back cleanly to multi-threaded CPU execution:
```bash
docker compose up -d
```

### Scenario B: Intel Arc Discrete GPUs (e.g., Arc A770)
Uses the Intel compute runtime with oneAPI Level Zero and OpenCL driver pass-through:
```bash
docker compose -f docker-compose.yml -f compose/docker-compose.arc.yml up -d
```

### Scenario C: NVIDIA CUDA GPUs
Requires the [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html) installed on the host:
```bash
docker compose -f docker-compose.yml -f compose/docker-compose.nvidia.yml up -d
```

### Scenario D: AMD Radeon GPUs (ROCm)
Passes through `/dev/kfd` and `/dev/dri` with ROCm user permissions:
```bash
docker compose -f docker-compose.yml -f compose/docker-compose.radeon.yml up -d
```

### Scenario E: Secure Public Access with Automated Let's Encrypt SSL
When deploying to a public VPS or homelab domain, use one of the hardened reverse proxy overlays (Caddy, Nginx, or Cloudflare Tunnel) to enforce HTTPS, eliminate cleartext HTTP, and auto-renew Let's Encrypt certificates:
```bash
# Option 1: Automatic Let's Encrypt TLS via Caddy (Recommended)
export DOMAIN="hrt.yourdomain.org"
export ACME_EMAIL="admin@yourdomain.org"
docker compose -f docker-compose.yml -f compose/docker-compose.public-caddy.yml up -d

# Option 2: Hardened Nginx with Certbot
docker compose -f docker-compose.yml -f compose/docker-compose.public-nginx.yml up -d

# Option 3: Cloudflare Zero Trust Tunnel (Bypasses CGNAT & ISP Port Blocks)
export TUNNEL_TOKEN="your-token"
docker compose -f docker-compose.yml -f compose/docker-compose.public-cloudflare.yml up -d
```
> For complete instructions on DNS setup, firewall rules, and reverse proxy architectures, consult the [**Secure Public Access & Reverse Proxies Guide**](secure-public-access.md).

### Verifying Container Health
Check the status of running containers:
```bash
docker compose ps
```
To view real-time log outputs:
```bash
docker compose logs -f trajectory-parser
```

---

## 4. Testing & Quality Verification

We maintain strict test-driven engineering standards. All pull requests must pass 100% of unit tests and achieve 100% line and branch code coverage.

### Running .NET Unit & Integration Tests
```bash
XDG_RUNTIME_DIR=/tmp dotnet test NanoWeaverHrt.sln \
  --collect:"XPlat Code Coverage" \
  --settings coverlet.runsettings
```

### Running Frontend JavaScript Visualization Tests
The custom sovereign HTML5 Canvas charting and 3D double helix visualization modules are thoroughly verified using Node's native test runner with zero external mock libraries:
```bash
cd src/WeaverLammpsHrt.TrajectoryParser/wwwroot
npm test
cd ../../..
```

---

## 5. Directory Structure Overview

```
nanoweaver-hrt/
├── compose/                              # Docker Compose overrides (Hardware & Public Access)
│   ├── caddy/                            # Hardened Caddy reverse proxy configuration
│   │   └── Caddyfile                     # Automatic Let's Encrypt TLS & WebSocket proxy
│   ├── nginx/                            # Hardened Nginx configuration template
│   │   └── nginx.conf                    # Zero-buffering reverse proxy template
│   ├── docker-compose.arc.yml            # Intel Arc A770 Level Zero override
│   ├── docker-compose.nvidia.yml         # NVIDIA CUDA override
│   ├── docker-compose.radeon.yml         # AMD ROCm override
│   ├── docker-compose.public-caddy.yml   # Hardened Caddy + Let's Encrypt SSL override
│   ├── docker-compose.public-nginx.yml   # Hardened Nginx + Certbot SSL override
│   └── docker-compose.public-cloudflare.yml # Cloudflare Zero Trust Tunnel override
├── docs/                                 # GitHub Pages wiki documentation & images
│   ├── images/                           # High-resolution UI screenshots
│   └── index.md                          # Portal home
├── simulations/                          # Simulation input, output, and shared data
│   ├── configs/                          # oxDNA and LAMMPS input parameter files
│   ├── exports/                          # SHA-256 signed dataset export bundles
│   └── shared-data/                      # Shared mount volume for trajectory streams
├── src/
│   └── WeaverLammpsHrt.TrajectoryParser/ # ASP.NET Core & Blazor trajectory service
│       ├── Services/                     # Pipeline parsing, SIMD, config, and endocrine math
│       ├── Components/Pages/             # Blazor Interactive Server UI pages
│       └── wwwroot/                      # Sovereign CSS, charting JS, and double helix 3D
└── tests/
    └── WeaverLammpsHrt.TrajectoryParser.Tests/ # Unit, integration, and coverage test suite
```

---

## Next Steps
* Configure [**Secure Public Access & Reverse Proxies**](secure-public-access.md) if deploying your node to the internet.
* Proceed to the [**Guided DNA Workflow Wizard**](workflow-wizard.md) to design your first targeted nanocarrier.
* Explore the [**Endocrine Hub & Pharmacodynamics**](endocrine-hub.md) to understand in-vivo setpoint stabilization.
