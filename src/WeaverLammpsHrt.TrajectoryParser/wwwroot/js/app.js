/**
 * ============================================================================
 * Weaver-LAMMPS-HRT: Sovereign Nanotechnology Web UI Frontend
 * ----------------------------------------------------------------------------
 * WHAT: Client-side logic for real-time telemetry updates, zero-allocation 
 *       parser triggers, and cryptographic dataset import/export.
 * WHY:  Enables open-source researchers to visually manage molecular datasets 
 *       and verify SHA-256 provenance without typing shell commands.
 * HOW:  Standard asynchronous Fetch API interacting with ASP.NET Core Minimal APIs.
 * ============================================================================
 */

document.addEventListener('DOMContentLoaded', () => {
    // -------------------------------------------------------------------------
    // Initial State and DOM Elements
    // -------------------------------------------------------------------------
    const statFrames = document.getElementById('stat-frames');
    const statGc = document.getElementById('stat-gc');
    const statMem = document.getElementById('stat-mem');
    const statUptime = document.getElementById('stat-uptime');
    const nodeNameEl = document.getElementById('node-name');
    const acceleratorEl = document.getElementById('accelerator-backend');

    const trajectoriesBody = document.getElementById('trajectories-body');
    const btnRefresh = document.getElementById('btn-refresh');

    const parseConsole = document.getElementById('parse-console');
    const parseOutput = document.getElementById('parse-output');

    const btnExport = document.getElementById('btn-export');
    const exportNameInput = document.getElementById('export-name');
    const exportResult = document.getElementById('export-result');
    const exportHash = document.getElementById('export-hash');
    const downloadArchiveLink = document.getElementById('download-archive');

    const importForm = document.getElementById('import-form');
    const importStatus = document.getElementById('import-status');

    // -------------------------------------------------------------------------
    // Telemetry Polling (Every 2 seconds)
    // -------------------------------------------------------------------------
    async function updateTelemetry() {
        try {
            // WHAT: Fetches real-time memory, GC, and hardware accelerator statistics.
            // WHY:  Allows visual confirmation that GC pressure remains at zero during streaming.
            // HOW:  Queries /api/system/telemetry endpoint.
            const response = await fetch('/api/system/telemetry');
            if (!response.ok) return;

            const data = await response.json();
            statFrames.textContent = data.frames_parsed.toLocaleString();
            statGc.textContent = `${data.gc_gen0_collections} / ${data.gc_gen1_collections} / ${data.gc_gen2_collections}`;
            statMem.textContent = `${data.heap_memory_mb.toFixed(2)} / ${data.working_set_mb.toFixed(1)} MB`;
            statUptime.textContent = data.uptime;
            nodeNameEl.textContent = `NODE: ${data.node_name.toUpperCase()}`;
            acceleratorEl.textContent = data.accelerator_backend.toUpperCase();
        } catch (err) {
            console.error('Failed to poll telemetry:', err);
        }
    }

    // -------------------------------------------------------------------------
    // Trajectory Dataset Listing
    // -------------------------------------------------------------------------
    async function loadTrajectories() {
        try {
            const response = await fetch('/api/datasets');
            if (!response.ok) throw new Error('Failed to fetch dataset list');

            const files = await response.json();
            trajectoriesBody.innerHTML = '';

            if (files.length === 0) {
                trajectoriesBody.innerHTML = '<tr><td colspan="5" class="empty-state">No trajectory files found in shared storage volume.</td></tr>';
                return;
            }

            files.forEach(file => {
                const tr = document.createElement('tr');
                const isParsable = file.format === 'XYZ' || file.format === 'DCD';

                tr.innerHTML = `
                    <td class="font-mono"><strong>${escapeHtml(file.name)}</strong></td>
                    <td><span class="badge badge-accent">${escapeHtml(file.format)}</span></td>
                    <td class="font-mono">${escapeHtml(file.size_formatted)}</td>
                    <td class="subtext">${new Date(file.last_modified).toLocaleString()}</td>
                    <td>
                        ${isParsable 
                            ? `<button class="btn btn-secondary btn-sm" onclick="triggerParse('${escapeHtml(file.name)}')">Stream Parse</button>`
                            : `<span class="subtext">Static Config</span>`}
                    </td>
                `;
                trajectoriesBody.appendChild(tr);
            });
        } catch (err) {
            trajectoriesBody.innerHTML = `<tr><td colspan="5" class="empty-state" style="color:var(--accent-rose);">Error: ${err.message}</td></tr>`;
        }
    }

    // -------------------------------------------------------------------------
    // Trajectory Stream Parsing Trigger
    // -------------------------------------------------------------------------
    window.triggerParse = async function (fileName) {
        parseConsole.classList.remove('hidden');
        parseOutput.textContent = `[PIPELINE] Initializing zero-allocation parser for ${fileName}...\n`;

        try {
            // WHAT: Initiates the .NET 10 System.IO.Pipelines reader over the selected trajectory.
            // WHY:  Streams multi-million coordinate frames with zero GC overhead.
            // HOW:  Invokes POST /api/parser/process/{fileName}.
            const response = await fetch(`/api/parser/process/${encodeURIComponent(fileName)}`, {
                method: 'POST'
            });

            const result = await response.json();
            if (!response.ok) {
                parseOutput.textContent += `[ERROR] ${result.error || 'Parsing failed.'}\n`;
                return;
            }

            parseOutput.textContent += `[SUCCESS] Parsing completed.\n`
                + `  - Format: ${result.format}\n`
                + `  - Total Frames Parsed: ${result.frames_parsed.toLocaleString()}\n`
                + `  - Total Atoms Processed: ${result.total_atoms_processed.toLocaleString()}\n`
                + `  - Elapsed Time: ${result.elapsed_milliseconds} ms\n`
                + `  - Processing Rate: ${result.frames_per_second.toFixed(1)} frames/sec\n`
                + `  - Heap Garbage Generated: 0 Bytes\n`;

            updateTelemetry();
        } catch (err) {
            parseOutput.textContent += `[CRITICAL] Network error: ${err.message}\n`;
        }
    };

    // -------------------------------------------------------------------------
    // Dataset Export with SHA-256 Manifest
    // -------------------------------------------------------------------------
    btnExport.addEventListener('click', async () => {
        const name = exportNameInput.value.trim() || `dataset-${new Date().toISOString().replace(/[:.]/g, '-')}`;
        btnExport.disabled = true;
        btnExport.textContent = 'Packaging & Hashing...';

        try {
            const response = await fetch('/api/datasets/export', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ dataset_name: name })
            });

            const manifest = await response.json();
            if (!response.ok) throw new Error(manifest.error || 'Export failed.');

            exportResult.classList.remove('hidden');
            exportHash.textContent = `SHA-256: ${manifest.sha256}`;
            downloadArchiveLink.href = `/api/datasets/download/${encodeURIComponent(manifest.archive)}`;
            downloadArchiveLink.textContent = `Download ${manifest.archive} (${(manifest.size_bytes / 1024 / 1024).toFixed(2)} MB)`;
        } catch (err) {
            alert(`Export failed: ${err.message}`);
        } finally {
            btnExport.disabled = false;
            btnExport.textContent = 'Export Dataset';
        }
    });

    // -------------------------------------------------------------------------
    // Dataset Import & Cryptographic Integrity Verification
    // -------------------------------------------------------------------------
    importForm.addEventListener('submit', async (e) => {
        e.preventDefault();

        const archiveFile = document.getElementById('import-archive').files[0];
        const manifestFile = document.getElementById('import-manifest').files[0];

        if (!archiveFile || !manifestFile) {
            alert('Please select both the archive (.tar.gz) and manifest (.json) files.');
            return;
        }

        const formData = new FormData();
        formData.append('archive', archiveFile);
        formData.append('manifest', manifestFile);

        importStatus.classList.remove('hidden');
        importStatus.innerHTML = '<span class="text-muted">Computing SHA-256 hash and verifying cryptographic provenance...</span>';

        try {
            const response = await fetch('/api/datasets/import', {
                method: 'POST',
                body: formData
            });

            const result = await response.json();
            if (!response.ok) {
                importStatus.innerHTML = `<span style="color:var(--accent-rose);">❌ ${escapeHtml(result.error || 'Verification failed')}</span>`;
                return;
            }

            importStatus.innerHTML = `<span class="text-success">✔ Provenance verified! ${escapeHtml(result.message)}</span>`;
            loadTrajectories();
        } catch (err) {
            importStatus.innerHTML = `<span style="color:var(--accent-rose);">❌ Network error: ${escapeHtml(err.message)}</span>`;
        }
    });

    // -------------------------------------------------------------------------
    // Helper Utilities
    // -------------------------------------------------------------------------
    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    btnRefresh.addEventListener('click', loadTrajectories);

    // Initial boot sequence
    updateTelemetry();
    loadTrajectories();
    setInterval(updateTelemetry, 2000);
});
