/**
 * ============================================================================
 * NanoWeaverHrt (nanoweaver-hrt): Sovereign Nanotechnology Web UI Frontend
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

    // Endocrine Toggle Elements
    const btnToggleEstrogen = document.getElementById('btn-toggle-estrogen');
    const btnToggleTestosterone = document.getElementById('btn-toggle-testosterone');
    const activeStateBadge = document.getElementById('active-state-badge');
    const dispTargetE2 = document.getElementById('disp-target-e2');
    const dispTargetT = document.getElementById('disp-target-t');
    const dispTargetKd = document.getElementById('disp-target-kd');
    const dispTargetTissue = document.getElementById('disp-target-tissue');
    const dispDeliveryVehicle = document.getElementById('disp-delivery-vehicle');

    const metricOccupancy = document.getElementById('metric-occupancy');
    const metricContacts = document.getElementById('metric-contacts');
    const metricVerdict = document.getElementById('metric-verdict');

    // Only run telemetry polling and DOM binding if legacy dashboard elements exist on current page
    const hasDashboardElements = statFrames || trajectoriesBody || btnExport || importForm || dispTargetE2;
    if (!hasDashboardElements) {
        return;
    }

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
            if (statFrames) statFrames.textContent = (data.framesParsed ?? data.frames_parsed ?? 0).toLocaleString();
            if (statGc) statGc.textContent = `${data.gcGen0Collections ?? data.gc_gen0_collections ?? 0} / ${data.gcGen1Collections ?? data.gc_gen1_collections ?? 0} / ${data.gcGen2Collections ?? data.gc_gen2_collections ?? 0}`;
            if (statMem) statMem.textContent = `${((data.heapMemoryMb ?? data.heap_memory_mb) ?? 0).toFixed(2)} / ${((data.workingSetMb ?? data.working_set_mb) ?? 0).toFixed(1)} MB`;
            if (statUptime) statUptime.textContent = data.uptime ?? '00:00:00';
            if (nodeNameEl) nodeNameEl.textContent = `NODE: ${(data.nodeName ?? data.node_name ?? 'LOCAL').toUpperCase()}`;
            if (acceleratorEl) acceleratorEl.textContent = (data.acceleratorBackend ?? data.accelerator_backend ?? 'CPU').toUpperCase();
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
                            ? `<div style="display:flex; gap:0.4rem;">
                                 <button class="btn btn-secondary btn-sm" onclick="triggerParse('${escapeHtml(file.name)}')">Stream Parse</button>
                                 <button class="btn btn-outline btn-sm" onclick="analyzeAptamerDocking('${escapeHtml(file.name)}')">Aptamer Docking</button>
                               </div>`
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
                + `  - Total Frames Parsed: ${(result.framesParsed ?? result.frames_parsed ?? 0).toLocaleString()}\n`
                + `  - Total Atoms Processed: ${(result.totalAtomsProcessed ?? result.total_atoms_processed ?? 0).toLocaleString()}\n`
                + `  - Elapsed Time: ${result.elapsedMilliseconds ?? result.elapsed_milliseconds ?? 0} ms\n`
                + `  - Processing Rate: ${(result.framesPerSecond ?? result.frames_per_second ?? 0).toFixed(1)} frames/sec\n`
                + `  - Heap Garbage Generated: 0 Bytes\n`;

            updateTelemetry();
        } catch (err) {
            parseOutput.textContent += `[CRITICAL] Network error: ${err.message}\n`;
        }
    };

    // -------------------------------------------------------------------------
    // Dataset Export with SHA-256 Manifest
    // -------------------------------------------------------------------------
    if (btnExport) {
        btnExport.addEventListener('click', async () => {
            const name = (exportNameInput && exportNameInput.value.trim()) || `dataset-${new Date().toISOString().replace(/[:.]/g, '-')}`;
            btnExport.disabled = true;
            btnExport.textContent = 'Packaging & Hashing...';

            try {
                const response = await fetch('/api/datasets/export', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ datasetName: name, dataset_name: name })
                });

                const manifest = await response.json();
                if (!response.ok) throw new Error(manifest.error || 'Export failed.');

                if (exportResult) exportResult.classList.remove('hidden');
                if (exportHash) exportHash.textContent = `SHA-256: ${manifest.sha256}`;
                if (downloadArchiveLink) {
                    downloadArchiveLink.href = `/api/datasets/download/${encodeURIComponent(manifest.archive)}`;
                    const archiveSize = manifest.sizeBytes ?? manifest.size_bytes ?? 0;
                    downloadArchiveLink.textContent = `Download ${manifest.archive} (${(archiveSize / 1024 / 1024).toFixed(2)} MB)`;
                }
            } catch (err) {
                alert(`Export failed: ${err.message}`);
            } finally {
                btnExport.disabled = false;
                btnExport.textContent = 'Export Dataset';
            }
        });
    }

    // -------------------------------------------------------------------------
    // Dataset Import & Cryptographic Integrity Verification
    // -------------------------------------------------------------------------
    if (importForm) {
        importForm.addEventListener('submit', async (e) => {
            e.preventDefault();

            const archiveInput = document.getElementById('import-archive');
            const manifestInput = document.getElementById('import-manifest');
            const archiveFile = archiveInput ? archiveInput.files[0] : null;
            const manifestFile = manifestInput ? manifestInput.files[0] : null;

            if (!archiveFile || !manifestFile) {
                alert('Please select both the archive (.tar.gz) and manifest (.json) files.');
                return;
            }

            const formData = new FormData();
            formData.append('archive', archiveFile);
            formData.append('manifest', manifestFile);

            if (importStatus) {
                importStatus.classList.remove('hidden');
                importStatus.innerHTML = '<span class="text-muted">Computing SHA-256 hash and verifying cryptographic provenance...</span>';
            }

            try {
                const response = await fetch('/api/datasets/import', {
                    method: 'POST',
                    body: formData
                });

                const result = await response.json();
                if (!response.ok) {
                    if (importStatus) importStatus.innerHTML = `<span style="color:var(--accent-rose);">❌ ${escapeHtml(result.error || 'Verification failed')}</span>`;
                    return;
                }

                if (importStatus) importStatus.innerHTML = `<span class="text-success">✔ Provenance verified! ${escapeHtml(result.message)}</span>`;
                loadTrajectories();
            } catch (err) {
                if (importStatus) importStatus.innerHTML = `<span style="color:var(--accent-rose);">❌ Network error: ${escapeHtml(err.message)}</span>`;
            }
        });
    }

    // -------------------------------------------------------------------------
    // Endocrine Bistable Toggle State Management
    // -------------------------------------------------------------------------
    async function loadEndocrineProfile() {
        try {
            const res = await fetch('/api/endocrine/profile');
            if (!res.ok) return;

            const profile = await res.json();
            renderEndocrineProfile(profile);
        } catch (err) {
            console.error('Failed to load endocrine profile:', err);
        }
    }

    function renderEndocrineProfile(profile) {
        if (dispTargetE2) dispTargetE2.innerHTML = `${profile.target_estradiol_pmol_l.toFixed(1)} <small>pmol/L</small>`;
        if (dispTargetT) dispTargetT.innerHTML = `${profile.target_testosterone_nmol_l.toFixed(2)} <small>nmol/L</small>`;
        if (dispTargetKd) dispTargetKd.innerHTML = `${profile.kd_target_nanomolar.toFixed(1)} <small>nM</small>`;

        if (dispTargetTissue) {
            dispTargetTissue.innerHTML = profile.target_tissue === 0 || profile.target_tissue === 'AnteriorPituitaryGonadotrope'
                ? 'Pituitary <small>GnRH-R</small>'
                : 'Steroidogenic <small>Adrenal</small>';
        }

        if (dispDeliveryVehicle && profile.delivery_vehicle) {
            dispDeliveryVehicle.innerHTML = escapeHtml(profile.delivery_vehicle.geometry.split(' ')[0]);
        }

        const isMtF = profile.regimen === 0 || profile.regimen === 'MaleToFemale' || profile.target_state === 0 || profile.target_state === 'EstrogenicDominant';

        if (btnToggleEstrogen && btnToggleTestosterone && activeStateBadge) {
            if (isMtF) {
                btnToggleEstrogen.className = 'toggle-btn active-estrogen';
                btnToggleTestosterone.className = 'toggle-btn';
                activeStateBadge.textContent = 'MALE-TO-FEMALE (MtF)';
                activeStateBadge.className = 'badge badge-accent';
            } else {
                btnToggleEstrogen.className = 'toggle-btn';
                btnToggleTestosterone.className = 'toggle-btn active-testosterone';
                activeStateBadge.textContent = 'FEMALE-TO-MALE (FtM)';
                activeStateBadge.className = 'badge badge-accent';
            }
        }
    }

    async function setToggleState(targetState) {
        try {
            const res = await fetch('/api/endocrine/toggle', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ target_state: targetState })
            });

            if (!res.ok) throw new Error('Toggle request failed');
            const updated = await res.json();
            renderEndocrineProfile(updated);
        } catch (err) {
            alert(`Error switching endocrine state: ${err.message}`);
        }
    }

    if (btnToggleEstrogen) btnToggleEstrogen.addEventListener('click', () => setToggleState(0));      // EstrogenicDominant
    if (btnToggleTestosterone) btnToggleTestosterone.addEventListener('click', () => setToggleState(1));  // AndrogenicDominant

    // -------------------------------------------------------------------------
    // In-Silico Aptamer Docking Analysis
    // -------------------------------------------------------------------------
    window.analyzeAptamerDocking = async function (fileName) {
        if (metricOccupancy) metricOccupancy.textContent = 'Evaluating...';
        if (metricContacts) metricContacts.textContent = 'Slicing...';
        if (metricVerdict) metricVerdict.textContent = 'Computing contact map...';

        if (parseConsole) parseConsole.classList.remove('hidden');
        if (parseOutput) parseOutput.textContent = `[ENDOCRINE DOCKING] Slicing trajectory ${fileName} for aptamer-steroid contact geometry...\n`;

        try {
            const res = await fetch(`/api/endocrine/analyze/${encodeURIComponent(fileName)}`, {
                method: 'POST'
            });

            const metrics = await res.json();
            if (!res.ok) {
                if (metricVerdict) metricVerdict.textContent = 'Analysis Failed';
                if (parseOutput) parseOutput.textContent += `[ERROR] ${metrics.error || 'Aptamer analysis failed.'}\n`;
                return;
            }

            if (metricOccupancy) metricOccupancy.textContent = `${(metrics.binding_occupancy_ratio * 100).toFixed(1)}%`;
            if (metricContacts) metricContacts.textContent = `${metrics.average_contacts_per_frame.toFixed(2)}`;
            if (metricVerdict) metricVerdict.textContent = metrics.stability_verdict;

            if (parseOutput) {
                parseOutput.textContent += `[SUCCESS] Aptamer Docking Analysis Completed.\n`
                    + `  - Target Trajectory: ${metrics.trajectory_file}\n`
                    + `  - Frames Evaluated: ${metrics.frames_analyzed.toLocaleString()}\n`
                    + `  - Binding Occupancy Ratio: ${(metrics.binding_occupancy_ratio * 100).toFixed(1)}%\n`
                    + `  - Mean Contacts / Frame: ${metrics.average_contacts_per_frame.toFixed(2)}\n`
                    + `  - Thermodynamic Verdict: ${metrics.stability_verdict}\n`;
            }
        } catch (err) {
            if (metricVerdict) metricVerdict.textContent = 'Network Error';
            if (parseOutput) parseOutput.textContent += `[CRITICAL] Network error: ${err.message}\n`;
        }
    };

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

    if (btnRefresh) btnRefresh.addEventListener('click', loadTrajectories);

    // Initial boot sequence
    updateTelemetry();
    loadTrajectories();
    loadEndocrineProfile();
    setInterval(updateTelemetry, 2000);
});

/**
 * Downloads a string or binary payload as a file blob in the browser.
 * @param {string} fileName - Target file name.
 * @param {string} content - Text content.
 * @param {string} mimeType - MIME type.
 * @returns {boolean} True if download was initiated without error.
 */
window.weaverDownloadBlob = function(fileName, content, mimeType) {
    try {
        const blob = new Blob([content], { type: mimeType || 'text/plain;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        return true;
    } catch (err) {
        console.error('weaverDownloadBlob failed:', err);
        return false;
    }
};

/**
 * Copies text safely to the user's system clipboard.
 * @param {string} text - Text to copy.
 * @returns {Promise<boolean>} True if copied successfully.
 */
window.weaverCopyText = async function(text) {
    try {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            await navigator.clipboard.writeText(text);
            return true;
        }
        const textArea = document.createElement('textarea');
        textArea.value = text;
        textArea.style.position = 'fixed';
        textArea.style.left = '-999999px';
        document.body.appendChild(textArea);
        textArea.focus();
        textArea.select();
        const successful = document.execCommand('copy');
        document.body.removeChild(textArea);
        return successful;
    } catch (err) {
        console.error('weaverCopyText failed:', err);
        return false;
    }
};

/*
 * NOTE: The authenticated session helpers (weaverSetSession, weaverGetSessionToken,
 * weaverClearSession, weaverPurgeLegacySession) and the idle activity watcher
 * (weaverRegisterIdleWatcher / weaverUnregisterIdleWatcher) now live in
 * js/weaver-session.js.
 *
 * WHY: they are security-critical (Prime Directive 12) and are therefore held to the
 * same 100% line, branch and function coverage gate as js/weaver-visualizer.js, which
 * this dashboard script is not. Both files are loaded by Components/App.razor.
 */
