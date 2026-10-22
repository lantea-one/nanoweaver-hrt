# Work Package: Non-Root Containers with Host-UID Matching

Status: Ready for implementation (single coder pass)
Change type: Container and deployment configuration plus documentation. No C# or JavaScript changes.

---

## Goal

Stop running the `simulation` and `trajectory-parser` containers as root, so that files they create in the host bind mounts (`./simulations/shared-data`, `./simulations/exports`, `./simulations/parquet`) are owned by the developer or operator account that launched the stack. Keep GPU access for the non-root simulation process, and give operators a documented way to recover hosts that already have root-owned `weaver_config.db`, `-wal`/`-shm`, export, and parquet files.

Background in plain English: a Docker bind mount maps a host folder straight into the container. Linux decides who may read or write a file using numeric user IDs (UIDs) and group IDs (GIDs). It does not use names. A process running as UID 0 (root) inside the container creates files owned by UID 0 on the host. If the container process runs with the same UID/GID as the host user, the files it creates belong to that host user.

### Decision: host-UID matching (chosen) vs. fixed in-image UID vs. named volume

| Option | How it works | Pros | Cons | Verdict |
| :--- | :--- | :--- | :--- | :--- |
| **A. Host-UID matching** | Compose sets `user: "${WEAVER_UID:-1000}:${WEAVER_GID:-1000}"`. Images also bake a non-root default `USER 1000:1000`. | Files on the host belong to the person who ran the stack. Host `dotnet run`/`dotnet test` and the containers can share `./simulations/*` both ways. No C# change. Works without any env var on the most common single-user Linux desktop (UID 1000). | Hosts whose UID is not 1000 must set two variables, via `.env`. If the variables are missing, the container silently uses UID 1000. | **Chosen** |
| **B. Fixed in-image UID** (e.g. aspnet `APP_UID` 1654) | Dockerfile `USER 1654`, no compose change. | No configuration needed. Matches upstream image convention. | Bind-mounted files end up owned by UID 1654, which no host user has. The host developer still cannot write `weaver_config.db`, so the reported defect returns under a different owner. Only fixes the root-in-container part. | Rejected |
| **C. Named volume for the database** | Move `weaver_config.db` into a Docker-managed volume. Trajectories stay on the bind mount. | The host never sees the DB file, so there is no ownership clash. | The DB lives in `TRAJECTORY_INCOMING_DIR` together with the trajectories. Splitting them needs a new configuration key in `SystemConfigurationStore` (C# change under the 100% coverage gate). Exports and parquet files written by the container still end up root-owned on the host. The container must still run non-root anyway (it would need A or B on top). Backups and inspection get harder (`docker volume` instead of the filesystem). | Rejected for this package (possible future hardening, see Non-goals) |

Why A: it is the only option that fixes both parts of the defect (host file ownership and root-in-container) without touching application code. The fixed in-image `USER` from option B is still added as a fallback, so bare `docker run` (no compose) is also non-root.

Why the variables are named `WEAVER_UID`/`WEAVER_GID` and not `UID`/`GID`: in bash and zsh, `UID` is a shell variable that is **not exported** to child processes by default. So `${UID:-1000}` in compose would quietly fall back to 1000 even on a host whose UID is 1001, and the defect would come back with nothing to show it. Using project-specific names that the operator sets in `.env` avoids this trap and avoids collisions with other tools.

### GPU access for the non-root simulation user

`/dev/dri/renderD*` and `/dev/dri/card*` (and `/dev/kfd` for ROCm) are character devices owned by `root:render` / `root:video` with mode `0660` on the host. When a bind-mounted device node shows up inside a container, it keeps its **host** numeric GID. A non-root process can open it only if one of its supplementary GIDs matches that host GID numerically. Compose's `group_add` adds supplementary GIDs to the process no matter which `user:` is set, so `user:` plus `group_add` together give GPU access without root.

Latent bug that this change will expose: the base compose hard-codes `44` (video) and `109` (render). Those are Debian/Ubuntu values. Arch, Fedora and others assign different GIDs (render is often 989 or similar). Root skips file permission checks, so the wrong values never mattered before. As soon as the process is non-root they matter. They must therefore be parameterised as `${WEAVER_VIDEO_GID:-44}` and `${WEAVER_RENDER_GID:-109}`.

Per overlay:
- **arc**: narrows `devices` to `card0` + `renderD128`. It inherits `user` and `group_add` from the base file. The render GID is required. Needs no edits.
- **radeon**: `/dev/kfd` is `root:render 0660` on current kernels/udev rules (older distros: `video`). It is covered by the inherited `group_add`. Keeps `seccomp=unconfined`. Needs no edits.
- **nvidia**: the NVIDIA Container Toolkit injects `/dev/nvidia*` nodes, which are world-accessible (`0666`), so no group is needed. It inherits `user`. Needs no edits.

---

## Files in scope

| File | Role / change |
| :--- | :--- |
| `docker/web/Dockerfile.web` | Runtime stage: create `/workspace/data`, `/workspace/exports`, `/workspace/parquet` owned by `1000:1000` **before** the `VOLUME` line (Docker throws away ownership changes made after `VOLUME`). Add `USER 1000:1000` (numeric) after the last root-requiring `RUN`/`COPY` and before `ENTRYPOINT`. Published binaries in `/app` stay root-owned and read-only to the app user (intentional: the service cannot overwrite its own code). WHAT/WHY/HOW comment block. |
| `docker/simulation/Dockerfile.simulation` | Runtime stage: `RUN mkdir -p /workspace/configs /workspace/data /workspace/exports && chown 1000:1000 /workspace /workspace/data /workspace/exports`. Add `USER 1000:1000` after the `chmod +x` `RUN` and before `ENTRYPOINT`. (UID 1000 is the stock `ubuntu` user in `ubuntu:24.04`.) WHAT/WHY/HOW comment block that explains the `group_add` GPU model. |
| `docker/simulation/entrypoint.sh` | Read-only check; edit only if needed. `mkdir -p` on directories that already exist succeeds for non-root users. Because the Dockerfile now pre-creates the directories, `set -e` cannot abort here. Add a one-line diagnostic `echo "Running as: $(id)"` next to the existing `/dev/dri` listing so operators can see the UID and groups. |
| `docker-compose.yml` | Both services: add `user: "${WEAVER_UID:-1000}:${WEAVER_GID:-1000}"` and `HOME=/tmp` in `environment`. `simulation`: replace the literal `group_add` values with `"${WEAVER_VIDEO_GID:-44}"` and `"${WEAVER_RENDER_GID:-109}"`, and update the comments. `trajectory-parser`: add `security_opt: ["no-new-privileges:true"]` and `cap_drop: [ALL]` (defense in depth for the publicly proxied service, Prime Directive #9). Update the header comment to mention `.env`. |
| `example.env` (new, repo root) | Template that documents `WEAVER_UID`, `WEAVER_GID`, `WEAVER_VIDEO_GID`, `WEAVER_RENDER_GID` with their defaults and the `id`/`getent` commands that discover them. The file name is already allow-listed by `.gitignore` (`!example.env`), and the real `.env` is already ignored (`*.env`). |
| `compose/docker-compose.arc.yml`, `compose/docker-compose.nvidia.yml`, `compose/docker-compose.radeon.yml`, `compose/docker-compose.valkey.yml`, `compose/docker-compose.public-caddy.yml`, `compose/docker-compose.public-nginx.yml`, `compose/docker-compose.public-cloudflare.yml` | **No functional change.** All of them inherit `user:`/`group_add` from the base file through compose merge. Only header comments change: add one line saying identity comes from `.env` (see `example.env`). The radeon file also gets a comment that `/dev/kfd` access is through the render GID. No overlay may redefine `user:`. |
| `docs/getting-started.md` | Section 3 "Running with Docker Compose": add a new first subsection "Container identity (.env)" before Scenario A, with the `.env` generation snippet. Add a subsection "Recovering from root-owned files (upgrading from earlier releases)" after "Verifying Container Health". Add a short note for rootless Docker/Podman users. |

No other files. In particular: no C# sources, no tests, no `.gitignore` change.

---

## Interfaces / signatures

### Environment contract (read by compose from `.env` in the project directory or from the shell)

| Variable | Default | Meaning | Discover with |
| :--- | :--- | :--- | :--- |
| `WEAVER_UID` | `1000` | Numeric UID that both services run as | `id -u` |
| `WEAVER_GID` | `1000` | Numeric primary GID that both services run as | `id -g` |
| `WEAVER_VIDEO_GID` | `44` | Host `video` group GID, added to `simulation` | `getent group video \| cut -d: -f3` |
| `WEAVER_RENDER_GID` | `109` | Host `render` group GID, added to `simulation` | `getent group render \| cut -d: -f3` |

Compose reads `.env` from the project directory, which is the directory of the first `-f` file (the repo root), so the file applies to every overlay combination in `docs/getting-started.md` without changing those commands.

### Exact compose fragments (load-bearing)

```yaml
  simulation:
    user: "${WEAVER_UID:-1000}:${WEAVER_GID:-1000}"
    group_add:
      - "${WEAVER_VIDEO_GID:-44}"
      - "${WEAVER_RENDER_GID:-109}"
    environment:
      - HOME=/tmp
      # ...existing entries unchanged

  trajectory-parser:
    user: "${WEAVER_UID:-1000}:${WEAVER_GID:-1000}"
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL
    environment:
      - HOME=/tmp
      # ...existing entries unchanged
```

`HOME=/tmp` is needed because a UID that has no `/etc/passwd` entry gets `HOME=/`, which is not writable. ASP.NET Core DataProtection (used by Blazor/antiforgery) and the Intel NEO shader cache write under `HOME`. Without a writable HOME, DataProtection logs a warning and falls back to in-memory keys. That is equivalent to today's behaviour, because keys in the container's writable layer are lost on recreate anyway, but the warning is noise and can be avoided.

### `.env` generation snippet for docs

```bash
cat > .env <<EOF
WEAVER_UID=$(id -u)
WEAVER_GID=$(id -g)
WEAVER_VIDEO_GID=$(getent group video | cut -d: -f3)
WEAVER_RENDER_GID=$(getent group render | cut -d: -f3)
EOF
```

### Operator recovery procedure (for docs)

Earlier releases ran as root, so the host may hold root-owned `weaver_config.db`, `weaver_config.db-wal`, `weaver_config.db-shm`, exports, and parquet files. SQLite needs write access to the **directory** (to create the `-wal`/`-shm` journals), not only to the file, so the ownership fix must be recursive over the directories:

```bash
docker compose down
# Option 1: with sudo
sudo chown -R "$(id -u):$(id -g)" simulations/shared-data simulations/exports simulations/parquet
# Option 2: without sudo (any member of the docker group)
docker run --rm -v "$PWD/simulations:/s" alpine chown -R "$(id -u):$(id -g)" /s/shared-data /s/exports /s/parquet
# Then create .env (snippet above) and restart
docker compose up -d
```

The docs must state that this is a one-time step and that no data is deleted. `simulations/configs` is mounted read-only and needs no change.

Rootless Docker / Podman note for docs: in rootless mode, container UID 0 maps to the invoking host user. In that mode operators should set `WEAVER_UID=0` and `WEAVER_GID=0` in `.env`; any other value maps to a sub-UID and produces files that the host user cannot write.

---

## Constraints

1. The `user:` field and the `group_add` GIDs are defined **only** in `docker-compose.yml`. Overlays must not redefine them, so all nine documented launch combinations behave the same.
2. Use numeric `USER 1000:1000` in both Dockerfiles, not names, so orchestrators that enforce "must run as non-root" can verify it and so it works when compose overrides it with a UID that has no passwd entry.
3. In `Dockerfile.web`, `mkdir`/`chown` for `/workspace/*` must come before `VOLUME`. `USER` must come after the `apt-get` `RUN` and the `COPY --from=build`.
4. Do not chown `/app` or the binaries in `/usr/local/bin` to the runtime user. Code stays root-owned and read-only.
5. Keep existing healthchecks working unchanged (`curl` runs as non-root, and ports 5000/8080 are above 1024, so no `NET_BIND_SERVICE` is needed).
6. Do not add `cap_drop`/`no-new-privileges` to `simulation`. GPU user-space drivers and the radeon `seccomp=unconfined` profile are not verified under those restrictions, so that is out of scope.
7. Every Dockerfile and compose change gets a `# WHAT: / # WHY: / # HOW:` comment, in line with the existing file headers (AGENTS.md Prime Directive #3).
8. Documentation: no single-dollar pairs outside code spans or fences. Shell variables such as `$(id -u)` must appear only inside fenced code blocks or inline code, or `scripts/verify-all.sh` step 5 fails (Prime Directive #8).
9. Do not commit a real `.env`. Only `example.env` is added.
10. `./scripts/verify-all.sh` must still pass. No C# or JS changes means coverage is unaffected.

---

## Acceptance criteria

Run all of these from the repo root. Each is pass/fail.

1. **Compose resolves identity for every combination.** For each of: base only, `+arc`, `+nvidia`, `+radeon`, `+valkey`, `+public-caddy`, `+public-nginx`, `+public-cloudflare`, the command `WEAVER_UID=1234 WEAVER_GID=2345 docker compose -f docker-compose.yml [-f compose/<overlay>.yml] config` shows `user: 1234:2345` under both `simulation` and `trajectory-parser`.
2. **Defaults apply without .env.** With no `.env` and the variables unset, `docker compose config` shows `user: 1000:1000` and `group_add` `"44"`, `"109"` for `simulation`.
3. **GPU GIDs are parameterised.** `WEAVER_VIDEO_GID=985 WEAVER_RENDER_GID=989 docker compose config` shows `group_add` `"985"`, `"989"`. `grep -nE '"(44|109)"' docker-compose.yml` returns no hard-coded literals except inside the `:-` defaults.
4. **Images default to non-root.** After `docker compose build`, both `docker image inspect -f '{{.Config.User}}' git.lantea.one/lantea-one/nanoweaver-hrt-web:latest` and the same command for `nanoweaver-hrt-simulation:latest` print `1000:1000`.
5. **Runtime is non-root.** After `docker compose up -d` with `.env` generated by the documented snippet, `docker compose exec trajectory-parser id -u` and `docker compose exec simulation id -u` both print the host's `id -u`. Neither prints `0`.
6. **Host ownership fixed (the reported defect).** Start from an empty `simulations/shared-data` (except tracked files), run `docker compose up -d`, wait for healthy, then `stat -c '%u:%g' simulations/shared-data/weaver_config.db` equals `$(id -u):$(id -g)`. After `docker compose down`, `./scripts/verify-all.sh` step 3 does not fail with `SQLite Error 8`.
7. **GPU access retained** (on a host with `/dev/dri`): `docker compose exec simulation sh -c 'test -r /dev/dri/renderD128 && test -w /dev/dri/renderD128 && echo ok'` prints `ok`, and `docker compose exec simulation clinfo -l` lists at least the same platforms as the pre-change root container on the same host.
8. **Healthchecks pass.** `docker compose ps` shows both services `healthy` within their `start_period` plus 3 intervals.
9. **Hardening applied to the parser.** `docker inspect weaver-parser -f '{{.HostConfig.CapDrop}} {{.HostConfig.SecurityOpt}}'` contains `ALL` and `no-new-privileges:true`.
10. **Bare docker run is non-root.** `docker run --rm --entrypoint id git.lantea.one/lantea-one/nanoweaver-hrt-web:latest -u` prints `1000`.
11. **Recovery procedure works.** On a tree where `sudo chown -R 0:0 simulations/shared-data` was applied first, follow the documented recovery steps exactly. Afterwards criterion 6 holds and the existing configuration data in `weaver_config.db` is preserved (row counts in the DB are the same before and after).
12. **Docs and gate.** `docs/getting-started.md` contains the `.env` subsection, the recovery subsection and the rootless note. `example.env` exists with all four variables. `./scripts/verify-all.sh` exits 0, including step 5 (markdown dollar scan).

---

## Non-goals

- Moving `weaver_config.db` out of `TRAJECTORY_INCOMING_DIR` or into a named volume (option C). Possible later hardening; it needs a C# configuration change with tests.
- Test isolation: the .NET test suite should not touch the real `simulations/shared-data/weaver_config.db` at all. That is tracked separately and is not fixed here.
- Running third-party containers (Caddy, Nginx, Certbot, cloudflared, Valkey) under different users. They already have their own hardening and do not write to the project bind mounts.
- Persisting ASP.NET DataProtection keys across container recreation.
- An automatic root "init/chown" sidecar container. It would bring back a root process that changes host file ownership. Recovery stays a documented one-time operator step.
- `cap_drop`/`no-new-privileges`/read-only rootfs for the `simulation` service.
- Kubernetes/Helm manifests and CI pipeline changes.
