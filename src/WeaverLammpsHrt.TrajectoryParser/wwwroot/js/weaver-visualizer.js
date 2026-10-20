/**
 * ============================================================================
 * Weaver-Visualizer: Open-Source High-Performance Biotechnology Canvas Charting
 * ----------------------------------------------------------------------------
 * WHAT: Zero-dependency, lightweight, high-DPI HTML5 Canvas visualization engine
 *       for molecular kinetics, DNA composition, 3D double helix projection,
 *       cryogenic annealing ramps, and endocrine anatomical tissue targeting.
 * WHY:  Wet-lab and sovereign simulation environments require 100% open-source,
 *       air-gapped visualization with zero external CDN dependencies.
 * HOW:  Vector math, trigonometric 3D-to-2D projection, cubic Bézier interpolation,
 *       and hardware-accelerated 2D canvas primitives.
 * ----------------------------------------------------------------------------
 * LICENSE: MIT License
 * Copyright (c) 2026 lantea-one / Weaver-LAMMPS-HRT Contributors
 * ============================================================================
 */

const WeaverVisualizer = {
    /**
     * Renders a dual-axis pharmacodynamic hormone stabilization trajectory.
     * 
     * @param {string} canvasId DOM element ID of the canvas target.
     * @param {Array<Object>} points Array of hormone curve points with hour and concentration metrics.
     * @param {number} targetE2 Target Estradiol plateau (pmol/L).
     * @param {number} targetT Target Testosterone plateau (nmol/L).
     */
    renderKineticsChart: function (canvasId, points, targetE2, targetT) {
        // WHAT: Dual-axis pharmacodynamic concentration curve visualization over 72 hours.
        // WHY:  Allows clinicians and researchers to observe aptamer steady-state hormone release.
        // HOW:  Maps time to X-axis and dual concentration scales to left/right Y-axes using cubic Bézier curves.
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        const dpr = (typeof window !== 'undefined' && window.devicePixelRatio) ? window.devicePixelRatio : 1;
        const width = canvas.clientWidth || 600;
        const height = canvas.clientHeight || 240;

        canvas.width = width * dpr;
        canvas.height = height * dpr;
        ctx.scale(dpr, dpr);

        const padLeft = 55;
        const padRight = 55;
        const padTop = 30;
        const padBottom = 35;
        const plotW = width - padLeft - padRight;
        const plotH = height - padTop - padBottom;

        // Background
        ctx.fillStyle = '#070a0f';
        ctx.fillRect(0, 0, width, height);

        // Gridlines
        ctx.strokeStyle = '#1e293b';
        ctx.lineWidth = 1;
        for (let i = 0; i <= 4; i++) {
            const y = padTop + (plotH / 4) * i;
            ctx.beginPath();
            ctx.moveTo(padLeft, y);
            ctx.lineTo(width - padRight, y);
            ctx.stroke();
        }

        if (!points || points.length === 0) return;

        const getH = (p) => (p.hour !== undefined ? p.hour : (p.Hour !== undefined ? p.Hour : 0));
        const getE2Val = (p) => (p.estradiol_pmol_l !== undefined ? p.estradiol_pmol_l : (p.estradiolPmolL !== undefined ? p.estradiolPmolL : (p.EstradiolPmolL !== undefined ? p.EstradiolPmolL : 0)));
        const getTVal = (p) => (p.testosterone_nmol_l !== undefined ? p.testosterone_nmol_l : (p.testosteroneNmolL !== undefined ? p.testosteroneNmolL : (p.TestosteroneNmolL !== undefined ? p.TestosteroneNmolL : 0)));

        const maxHours = getH(points[points.length - 1]) || 72;
        const maxE2 = Math.max(800, targetE2 * 1.25);
        const maxT = Math.max(30, targetT * 1.5);

        // Coordinate projection helpers
        const getX = (h) => padLeft + (h / maxHours) * plotW;
        const getYE2 = (val) => padTop + plotH - (val / maxE2) * plotH;
        const getYT = (val) => padTop + plotH - (val / maxT) * plotH;

        // 1. Draw Target E2 Guideline
        ctx.strokeStyle = 'rgba(56, 189, 248, 0.35)';
        ctx.setLineDash([4, 4]);
        ctx.beginPath();
        ctx.moveTo(padLeft, getYE2(targetE2));
        ctx.lineTo(width - padRight, getYE2(targetE2));
        ctx.stroke();
        ctx.setLineDash([]);

        // 2. Draw Target T Guideline
        ctx.strokeStyle = 'rgba(244, 63, 94, 0.35)';
        ctx.setLineDash([4, 4]);
        ctx.beginPath();
        ctx.moveTo(padLeft, getYT(targetT));
        ctx.lineTo(width - padRight, getYT(targetT));
        ctx.stroke();
        ctx.setLineDash([]);

        // 3. Draw Estradiol (E2) Curve & Gradient Fill
        const gradE2 = ctx.createLinearGradient(0, padTop, 0, height - padBottom);
        gradE2.addColorStop(0, 'rgba(56, 189, 248, 0.25)');
        gradE2.addColorStop(1, 'rgba(56, 189, 248, 0.0)');

        ctx.beginPath();
        ctx.moveTo(getX(getH(points[0])), getYE2(getE2Val(points[0])));
        for (let i = 1; i < points.length; i++) {
            const prev = points[i - 1];
            const curr = points[i];
            const cx = (getX(getH(prev)) + getX(getH(curr))) / 2;
            ctx.bezierCurveTo(cx, getYE2(getE2Val(prev)), cx, getYE2(getE2Val(curr)), getX(getH(curr)), getYE2(getE2Val(curr)));
        }
        ctx.lineTo(getX(getH(points[points.length - 1])), height - padBottom);
        ctx.lineTo(getX(getH(points[0])), height - padBottom);
        ctx.closePath();
        ctx.fillStyle = gradE2;
        ctx.fill();

        // Stroke E2 Line
        ctx.beginPath();
        ctx.moveTo(getX(getH(points[0])), getYE2(getE2Val(points[0])));
        for (let i = 1; i < points.length; i++) {
            const prev = points[i - 1];
            const curr = points[i];
            const cx = (getX(getH(prev)) + getX(getH(curr))) / 2;
            ctx.bezierCurveTo(cx, getYE2(getE2Val(prev)), cx, getYE2(getE2Val(curr)), getX(getH(curr)), getYE2(getE2Val(curr)));
        }
        ctx.strokeStyle = '#38bdf8';
        ctx.lineWidth = 2.5;
        ctx.shadowColor = '#00f2fe';
        ctx.shadowBlur = 6;
        ctx.stroke();
        ctx.shadowBlur = 0;

        // 4. Draw Testosterone (T) Curve
        ctx.beginPath();
        ctx.moveTo(getX(getH(points[0])), getYT(getTVal(points[0])));
        for (let i = 1; i < points.length; i++) {
            const prev = points[i - 1];
            const curr = points[i];
            const cx = (getX(getH(prev)) + getX(getH(curr))) / 2;
            ctx.bezierCurveTo(cx, getYT(getTVal(prev)), cx, getYT(getTVal(curr)), getX(getH(curr)), getYT(getTVal(curr)));
        }
        ctx.strokeStyle = '#f43f5e';
        ctx.lineWidth = 2.5;
        ctx.shadowColor = '#f43f5e';
        ctx.shadowBlur = 6;
        ctx.stroke();
        ctx.shadowBlur = 0;

        // Axes Text Labels
        ctx.fillStyle = '#94a3b8';
        ctx.font = '10px JetBrains Mono, monospace';

        // X Axis hours
        ctx.textAlign = 'center';
        for (let h = 0; h <= maxHours; h += 12) {
            ctx.fillText(`${h}h`, getX(h), height - 12);
        }

        // Left Y Axis (E2 pmol/L)
        ctx.textAlign = 'right';
        ctx.fillStyle = '#38bdf8';
        ctx.fillText(`${Math.round(maxE2)}`, padLeft - 6, padTop + 10);
        ctx.fillText(`${Math.round(targetE2)}`, padLeft - 6, getYE2(targetE2) + 3);
        ctx.fillText('0', padLeft - 6, height - padBottom);

        // Right Y Axis (Testosterone nmol/L)
        ctx.textAlign = 'left';
        ctx.fillStyle = '#f43f5e';
        ctx.fillText(`${maxT.toFixed(0)}`, width - padRight + 6, padTop + 10);
        ctx.fillText(`${targetT.toFixed(1)}`, width - padRight + 6, getYT(targetT) + 3);
        ctx.fillText('0', width - padRight + 6, height - padBottom);
    },

    /**
     * Renders an interactive nucleotide composition doughnut chart.
     * 
     * @param {string} canvasId DOM element ID of the canvas target.
     * @param {number} baseA Count of Adenine nucleotides.
     * @param {number} baseT Count of Thymine/Uracil nucleotides.
     * @param {number} baseG Count of Guanine nucleotides.
     * @param {number} baseC Count of Cytosine nucleotides.
     * @param {number} gcPercent Calculated GC content percentage.
     */
    renderCompositionDoughnut: function (canvasId, baseA, baseT, baseG, baseC, gcPercent) {
        // WHAT: Renders a segmented circular doughnut chart displaying nucleotide ratios.
        // WHY:  Higher GC content correlates directly with duplex thermal stability and melting temperature.
        // HOW:  Calculates proportional angular arcs for each nucleotide and draws an inner cutout.
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        const dpr = (typeof window !== 'undefined' && window.devicePixelRatio) ? window.devicePixelRatio : 1;
        const width = canvas.clientWidth || 220;
        const height = canvas.clientHeight || 220;

        canvas.width = width * dpr;
        canvas.height = height * dpr;
        ctx.scale(dpr, dpr);

        const cx = width / 2;
        const cy = height / 2;
        const radius = Math.min(cx, cy) - 15;
        const innerRadius = radius * 0.65;

        const total = baseA + baseT + baseG + baseC;
        if (total === 0) return;

        const slices = [
            { count: baseA, color: '#38bdf8', label: 'A' },
            { count: baseT, color: '#f43f5e', label: 'T' },
            { count: baseG, color: '#10b981', label: 'G' },
            { count: baseC, color: '#f59e0b', label: 'C' }
        ];

        let startAngle = -Math.PI / 2;

        slices.forEach(slice => {
            const sliceAngle = (slice.count / total) * 2 * Math.PI;
            ctx.beginPath();
            ctx.arc(cx, cy, radius, startAngle, startAngle + sliceAngle);
            ctx.arc(cx, cy, innerRadius, startAngle + sliceAngle, startAngle, true);
            ctx.closePath();
            ctx.fillStyle = slice.color;
            ctx.fill();
            startAngle += sliceAngle;
        });

        // Center cutout and text
        ctx.beginPath();
        ctx.arc(cx, cy, innerRadius - 2, 0, Math.PI * 2);
        ctx.fillStyle = '#0a0e14';
        ctx.fill();

        ctx.textAlign = 'center';
        ctx.fillStyle = '#00f2fe';
        ctx.font = 'bold 15px JetBrains Mono, monospace';
        ctx.fillText(`${gcPercent}%`, cx, cy + 2);

        ctx.fillStyle = '#94a3b8';
        ctx.font = '9px Inter, sans-serif';
        ctx.fillText('GC CONTENT', cx, cy + 16);
    },

    /**
     * Renders a 6-stage cryogenic thermal annealing schedule ramp chart.
     * 
     * @param {string} canvasId DOM element ID of the canvas target.
     * @param {Array<Object>} steps Collection of thermal annealing protocol stages.
     */
    renderAnnealingRampChart: function (canvasId, steps) {
        // WHAT: Step-line chart showing the multi-stage thermal annealing temperature gradient.
        // WHY:  Oligonucleotide origami folding requires controlled gradual cooling to prevent kinetically trapped misfolds.
        // HOW:  Maps stage indices across the X-axis and temperatures (0-95 C) across the Y-axis.
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        const dpr = (typeof window !== 'undefined' && window.devicePixelRatio) ? window.devicePixelRatio : 1;
        const width = canvas.clientWidth || 500;
        const height = canvas.clientHeight || 180;

        canvas.width = width * dpr;
        canvas.height = height * dpr;
        ctx.scale(dpr, dpr);

        ctx.fillStyle = '#070a0f';
        ctx.fillRect(0, 0, width, height);

        if (!steps || steps.length === 0) return;

        const padLeft = 45;
        const padRight = 30;
        const padTop = 20;
        const padBottom = 30;
        const plotW = width - padLeft - padRight;
        const plotH = height - padTop - padBottom;

        const maxTemp = 95;
        const minTemp = 0;

        const getY = (temp) => padTop + plotH - ((temp - minTemp) / (maxTemp - minTemp)) * plotH;
        const getX = (idx) => padLeft + (idx / (steps.length - 1 || 1)) * plotW;

        // Step Line
        ctx.beginPath();
        ctx.moveTo(getX(0), getY(steps[0].target_temperature_celsius));
        for (let i = 1; i < steps.length; i++) {
            ctx.lineTo(getX(i), getY(steps[i].target_temperature_celsius));
        }
        ctx.strokeStyle = '#00f2fe';
        ctx.lineWidth = 2.5;
        ctx.stroke();

        // Points
        for (let i = 0; i < steps.length; i++) {
            const x = getX(i);
            const y = getY(steps[i].target_temperature_celsius);
            ctx.beginPath();
            ctx.arc(x, y, 4, 0, Math.PI * 2);
            ctx.fillStyle = '#00f2fe';
            ctx.fill();
            ctx.strokeStyle = '#fff';
            ctx.lineWidth = 1.5;
            ctx.stroke();

            // Label
            ctx.fillStyle = '#e2e8f0';
            ctx.font = '9px JetBrains Mono, monospace';
            ctx.textAlign = 'center';
            ctx.fillText(`${steps[i].target_temperature_celsius}°C`, x, y - 8);
        }
    },

    /**
     * Renders a 3D-projected B-DNA double-helix structure with anti-parallel strands,
     * base pair rungs, hydrogen bonding, and major/minor groove geometry.
     * 
     * @param {string} canvasId DOM element ID of the canvas target.
     * @param {Object} [options] Helix rendering configuration options.
     * @param {string} [options.sequence] Nucleotide sequence string (e.g. 'ATCG...').
     * @param {number} [options.rotationAngle=0] Rotational phase angle around the vertical axis in radians.
     * @param {number} [options.numBasePairs=16] Number of base pair steps to project vertically.
     * @param {string} [options.strand1Color] Color of the 5' to 3' sugar-phosphate backbone.
     * @param {string} [options.strand2Color] Color of the 3' to 5' anti-parallel backbone.
     * @param {boolean} [options.showLabels=true] Whether to draw nucleotide letters and annotations.
     */
    renderDnaHelix: function (canvasId, options) {
        // WHAT: 3D-to-2D orthographic/perspective projection of double-stranded DNA.
        // WHY:  Provides intuitive visual feedback of the digitized oligonucleotide folding.
        // HOW:  Trigonometric helical coordinates with depth-sorted alpha attenuation.
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        const dpr = (typeof window !== 'undefined' && window.devicePixelRatio) ? window.devicePixelRatio : 1;
        const width = canvas.clientWidth || 320;
        const height = canvas.clientHeight || 420;

        canvas.width = width * dpr;
        canvas.height = height * dpr;
        ctx.scale(dpr, dpr);

        ctx.fillStyle = '#070a0f';
        ctx.fillRect(0, 0, width, height);

        const opts = options || {};
        const seq = (opts.sequence && opts.sequence.length > 0) ? opts.sequence.toUpperCase() : 'ATCGATCGATCG';
        const rotationAngle = opts.rotationAngle || 0;
        const numBp = opts.numBasePairs || 16;
        const showLabels = opts.showLabels !== false;

        const centerX = width / 2;
        const radius = Math.min(width * 0.35, 75);
        const topPadding = 40;
        const bottomPadding = 35;
        const availableHeight = height - topPadding - bottomPadding;
        const stepY = availableHeight / (numBp > 1 ? (numBp - 1) : 1);
        const radPerBp = (2 * Math.PI) / 10.5; // Natural B-DNA helical pitch: ~10.5 base pairs per 360 turn
        const minorGrooveOffset = 2.45; // ~140 degrees between strands creates natural major and minor grooves

        const baseColors = {
            'A': '#38bdf8',
            'T': '#f43f5e',
            'G': '#10b981',
            'C': '#f59e0b',
            'U': '#f43f5e'
        };
        const complements = {
            'A': 'T',
            'T': 'A',
            'G': 'C',
            'C': 'G',
            'U': 'A'
        };

        const strand1 = [];
        const strand2 = [];
        const basePairs = [];

        for (let i = 0; i < numBp; i++) {
            const theta1 = i * radPerBp + rotationAngle;
            const theta2 = theta1 + minorGrooveOffset;
            const y = topPadding + i * stepY;

            // Strand 1 (5' to 3')
            const x1 = centerX + Math.cos(theta1) * radius;
            const z1 = Math.sin(theta1) * radius;

            // Strand 2 (3' to 5')
            const x2 = centerX + Math.cos(theta2) * radius;
            const z2 = Math.sin(theta2) * radius;

            const base1 = seq[i % seq.length];
            const base2 = complements[base1] || 'T';

            strand1.push({ x: x1, y: y, z: z1, base: base1, color: baseColors[base1] || '#38bdf8' });
            strand2.push({ x: x2, y: y, z: z2, base: base2, color: baseColors[base2] });

            const avgZ = (z1 + z2) / 2;
            basePairs.push({
                index: i,
                p1: { x: x1, y: y, z: z1, base: base1, color: baseColors[base1] || '#38bdf8' },
                p2: { x: x2, y: y, z: z2, base: base2, color: baseColors[base2] },
                avgZ: avgZ,
                isGC: base1 === 'G' || base1 === 'C'
            });
        }

        // Draw backbone ribbon for Strand 1
        ctx.beginPath();
        ctx.moveTo(strand1[0].x, strand1[0].y);
        for (let i = 1; i < strand1.length; i++) {
            ctx.lineTo(strand1[i].x, strand1[i].y);
        }
        ctx.strokeStyle = opts.strand1Color || 'rgba(56, 189, 248, 0.4)';
        ctx.lineWidth = 3;
        ctx.stroke();

        // Draw backbone ribbon for Strand 2
        ctx.beginPath();
        ctx.moveTo(strand2[0].x, strand2[0].y);
        for (let i = 1; i < strand2.length; i++) {
            ctx.lineTo(strand2[i].x, strand2[i].y);
        }
        ctx.strokeStyle = opts.strand2Color || 'rgba(244, 63, 94, 0.4)';
        ctx.lineWidth = 3;
        ctx.stroke();

        // Depth-sort base pairs (back to front) for 3D realism
        const sortedBp = [...basePairs].sort((a, b) => a.avgZ - b.avgZ);

        sortedBp.forEach(bp => {
            const midX = (bp.p1.x + bp.p2.x) / 2;
            const midY = (bp.p1.y + bp.p2.y) / 2;
            const alpha = 0.35 + 0.65 * ((bp.avgZ + radius) / (2 * radius));

            // Strand 1 to Midpoint
            ctx.strokeStyle = bp.p1.color;
            ctx.globalAlpha = alpha;
            ctx.lineWidth = 2.5;
            ctx.beginPath();
            ctx.moveTo(bp.p1.x, bp.p1.y);
            ctx.lineTo(midX, midY);
            ctx.stroke();

            // Midpoint to Strand 2
            ctx.strokeStyle = bp.p2.color;
            ctx.beginPath();
            ctx.moveTo(midX, midY);
            ctx.lineTo(bp.p2.x, bp.p2.y);
            ctx.stroke();

            // Hydrogen bond markers (2 dots for A-T, 3 dots for G-C)
            ctx.fillStyle = '#ffffff';
            const numHbonds = bp.isGC ? 3 : 2;
            for (let h = 0; h < numHbonds; h++) {
                const offset = (h - (numHbonds - 1) / 2) * 3;
                ctx.beginPath();
                ctx.arc(midX, midY + offset, 1.2, 0, Math.PI * 2);
                ctx.fill();
            }

            // Strand 1 backbone bead
            const r1 = 3.5 + 2 * ((bp.p1.z + radius) / (2 * radius));
            ctx.beginPath();
            ctx.arc(bp.p1.x, bp.p1.y, r1, 0, Math.PI * 2);
            ctx.fillStyle = bp.p1.color;
            ctx.fill();
            ctx.strokeStyle = '#ffffff';
            ctx.lineWidth = 1;
            ctx.stroke();

            // Strand 2 backbone bead
            const r2 = 3.5 + 2 * ((bp.p2.z + radius) / (2 * radius));
            ctx.beginPath();
            ctx.arc(bp.p2.x, bp.p2.y, r2, 0, Math.PI * 2);
            ctx.fillStyle = bp.p2.color;
            ctx.fill();
            ctx.stroke();

            // Nucleotide base labels
            if (showLabels && alpha > 0.6) {
                ctx.font = 'bold 9px JetBrains Mono, monospace';
                ctx.textAlign = 'center';
                ctx.textBaseline = 'middle';
                ctx.fillStyle = '#ffffff';
                ctx.fillText(bp.p1.base, bp.p1.x < centerX ? bp.p1.x - 10 : bp.p1.x + 10, bp.p1.y);
                ctx.fillText(bp.p2.base, bp.p2.x < centerX ? bp.p2.x - 10 : bp.p2.x + 10, bp.p2.y);
            }
        });

        ctx.globalAlpha = 1.0;

        // Header and annotations
        ctx.font = 'bold 10px JetBrains Mono, monospace';
        ctx.textAlign = 'left';
        ctx.fillStyle = '#38bdf8';
        ctx.fillText("5' [Strand 1]", 15, 20);

        ctx.textAlign = 'right';
        ctx.fillStyle = '#f43f5e';
        ctx.fillText("3' [Strand 2]", width - 15, 20);

        ctx.textAlign = 'center';
        ctx.fillStyle = '#94a3b8';
        ctx.font = '9px Inter, sans-serif';
        ctx.fillText('B-DNA Double Helix • 10.5 bp/turn (3.4 nm Pitch)', centerX, height - 12);
    },

    /**
     * Renders a biomedical anatomical schematic of the Hypothalamus-Pituitary-Gonadal (HPG)
     * axis and patient-targeted nanocarrier biodistribution and tissue tropism.
     * 
     * @param {string} canvasId DOM element ID of the canvas target.
     * @param {Object} [options] Anatomical targeting visualization options.
     * @param {string} [options.regimen='MaleToFemale'] Regimen ('MaleToFemale' or 'FemaleToMale').
     * @param {string} [options.targetTissue='AnteriorPituitaryGonadotrope'] Primary targeted tissue name.
     * @param {number} [options.kdNanoMolar=4.2] Aptamer target dissociation constant (Kd in nM).
     * @param {number} [options.targetE2=550.0] Target circulating Estradiol (pmol/L).
     * @param {number} [options.targetT=1.2] Target circulating Testosterone (nmol/L).
     */
    renderAnatomicalTargeting: function (canvasId, options) {
        // WHAT: Biomedical anatomical schematics of the endocrine HPG feedback loop.
        // WHY:  Clarifies how aptamer nanocarriers deliver localized payload to pituitary gonadotropes.
        // HOW:  2D vector diagram depicting hypothalamus, pituitary reticle, vascular loop, and target organs.
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        const dpr = (typeof window !== 'undefined' && window.devicePixelRatio) ? window.devicePixelRatio : 1;
        const width = canvas.clientWidth || 540;
        const height = canvas.clientHeight || 340;

        canvas.width = width * dpr;
        canvas.height = height * dpr;
        ctx.scale(dpr, dpr);

        ctx.fillStyle = '#070a0f';
        ctx.fillRect(0, 0, width, height);

        const opts = options || {};
        const isMtF = opts.regimen !== 'FemaleToMale';
        const targetTissue = opts.targetTissue || 'AnteriorPituitaryGonadotrope';
        const kd = opts.kdNanoMolar !== undefined ? opts.kdNanoMolar : 4.2;
        const e2 = opts.targetE2 !== undefined ? opts.targetE2 : (isMtF ? 550.0 : 120.0);
        const t = opts.targetT !== undefined ? opts.targetT : (isMtF ? 1.2 : 22.0);

        // Grid background
        ctx.strokeStyle = '#121824';
        ctx.lineWidth = 1;
        for (let x = 0; x < width; x += 40) {
            ctx.beginPath();
            ctx.moveTo(x, 0);
            ctx.lineTo(x, height);
            ctx.stroke();
        }
        for (let y = 0; y < height; y += 40) {
            ctx.beginPath();
            ctx.moveTo(0, y);
            ctx.lineTo(width, y);
            ctx.stroke();
        }

        const centerX = width / 2;

        // Node 1: Hypothalamus (Top)
        const hypY = 55;
        ctx.fillStyle = 'rgba(56, 189, 248, 0.1)';
        ctx.strokeStyle = '#38bdf8';
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.roundRect(centerX - 95, hypY - 22, 190, 44, 8);
        ctx.fill();
        ctx.stroke();

        ctx.fillStyle = '#38bdf8';
        ctx.font = 'bold 11px JetBrains Mono, monospace';
        ctx.textAlign = 'center';
        ctx.fillText('HYPOTHALAMUS', centerX, hypY - 4);
        ctx.font = '9px Inter, sans-serif';
        ctx.fillStyle = '#94a3b8';
        ctx.fillText('Pulsatile GnRH Pulse Generator', centerX, hypY + 11);

        // Node 2: Anterior Pituitary (Center - Targeted Delivery Site)
        const pitY = 160;
        ctx.strokeStyle = 'rgba(0, 242, 254, 0.25)';
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.arc(centerX, pitY, 52, 0, Math.PI * 2);
        ctx.stroke();

        ctx.strokeStyle = 'rgba(0, 242, 254, 0.6)';
        ctx.setLineDash([4, 4]);
        ctx.beginPath();
        ctx.arc(centerX, pitY, 44, 0, Math.PI * 2);
        ctx.stroke();
        ctx.setLineDash([]);

        // Pituitary body
        ctx.fillStyle = 'rgba(0, 242, 254, 0.15)';
        ctx.strokeStyle = '#00f2fe';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.roundRect(centerX - 120, pitY - 26, 240, 52, 10);
        ctx.fill();
        ctx.stroke();

        // Target Reticle Crosshairs
        ctx.strokeStyle = '#00f2fe';
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(centerX - 130, pitY);
        ctx.lineTo(centerX - 110, pitY);
        ctx.moveTo(centerX + 110, pitY);
        ctx.lineTo(centerX + 130, pitY);
        ctx.moveTo(centerX, pitY - 36);
        ctx.lineTo(centerX, pitY - 20);
        ctx.moveTo(centerX, pitY + 20);
        ctx.lineTo(centerX, pitY + 36);
        ctx.stroke();

        ctx.fillStyle = '#00f2fe';
        ctx.font = 'bold 12px JetBrains Mono, monospace';
        ctx.fillText('🎯 ANTERIOR PITUITARY', centerX, pitY - 6);
        ctx.font = '9px Inter, sans-serif';
        ctx.fillStyle = '#e2e8f0';
        ctx.fillText(`Target Gonadotrope Tissue • Kd = ${kd.toFixed(1)} nM`, centerX, pitY + 10);
        ctx.font = '8px Inter, sans-serif';
        ctx.fillStyle = '#10b981';
        ctx.fillText('Aptamer-Mediated Nanocarrier Tropism (>98% Localization)', centerX, pitY + 21);

        // Flow connecting Hypothalamus to Pituitary (Hypophyseal Portal System)
        ctx.strokeStyle = '#38bdf8';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(centerX, hypY + 22);
        ctx.lineTo(centerX, pitY - 26);
        ctx.stroke();
        // Downward Arrow
        ctx.fillStyle = '#38bdf8';
        ctx.beginPath();
        ctx.moveTo(centerX, pitY - 26);
        ctx.lineTo(centerX - 4, pitY - 33);
        ctx.lineTo(centerX + 4, pitY - 33);
        ctx.closePath();
        ctx.fill();

        // Node 3: Systemic Endocrine & Peripheral Tissue Targets (Bottom)
        const sysY = 275;
        const boxW = 210;
        const boxH = 50;

        // Left box: Endocrine Receptors & Target Setpoints
        ctx.fillStyle = isMtF ? 'rgba(244, 63, 94, 0.1)' : 'rgba(0, 112, 243, 0.1)';
        ctx.strokeStyle = isMtF ? '#f43f5e' : '#0070f3';
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.roundRect(centerX - boxW - 15, sysY - boxH / 2, boxW, boxH, 8);
        ctx.fill();
        ctx.stroke();

        ctx.fillStyle = isMtF ? '#f43f5e' : '#38bdf8';
        ctx.font = 'bold 10px JetBrains Mono, monospace';
        ctx.fillText(isMtF ? 'ESTROGEN RECEPTORS (ERα/β)' : 'ANDROGEN RECEPTORS (AR)', centerX - boxW / 2 - 15, sysY - 8);
        ctx.font = '9px Inter, sans-serif';
        ctx.fillStyle = '#cbd5e1';
        ctx.fillText(isMtF ? `Target 17β-E2: ${e2.toFixed(0)} pmol/L` : `Target T: ${t.toFixed(1)} nmol/L`, centerX - boxW / 2 - 15, sysY + 6);
        ctx.fillStyle = '#94a3b8';
        ctx.font = '8px Inter, sans-serif';
        ctx.fillText('Tissue: Breast, Bone, Liver, Adipose', centerX - boxW / 2 - 15, sysY + 17);

        // Right box: Negative Feedback Arc
        ctx.fillStyle = 'rgba(16, 185, 129, 0.1)';
        ctx.strokeStyle = '#10b981';
        ctx.beginPath();
        ctx.roundRect(centerX + 15, sysY - boxH / 2, boxW, boxH, 8);
        ctx.fill();
        ctx.stroke();

        ctx.fillStyle = '#10b981';
        ctx.font = 'bold 10px JetBrains Mono, monospace';
        ctx.fillText('NEGATIVE FEEDBACK LOOP', centerX + boxW / 2 + 15, sysY - 8);
        ctx.font = '9px Inter, sans-serif';
        ctx.fillStyle = '#cbd5e1';
        ctx.fillText('LH & FSH Suppression', centerX + boxW / 2 + 15, sysY + 6);
        ctx.fillStyle = '#94a3b8';
        ctx.font = '8px Inter, sans-serif';
        ctx.fillText('Endogenous hormone suppression active', centerX + boxW / 2 + 15, sysY + 17);

        // Downward arrows from Pituitary to Targets
        ctx.strokeStyle = '#00f2fe';
        ctx.lineWidth = 1.5;

        // Path to left box
        ctx.beginPath();
        ctx.moveTo(centerX - 60, pitY + 26);
        ctx.lineTo(centerX - 60, sysY - boxH / 2);
        ctx.stroke();
        ctx.fillStyle = '#00f2fe';
        ctx.beginPath();
        ctx.moveTo(centerX - 60, sysY - boxH / 2);
        ctx.lineTo(centerX - 64, sysY - boxH / 2 - 7);
        ctx.lineTo(centerX - 56, sysY - boxH / 2 - 7);
        ctx.closePath();
        ctx.fill();

        // Path to right box
        ctx.beginPath();
        ctx.moveTo(centerX + 60, pitY + 26);
        ctx.lineTo(centerX + 60, sysY - boxH / 2);
        ctx.stroke();
        ctx.fillStyle = '#00f2fe';
        ctx.beginPath();
        ctx.moveTo(centerX + 60, sysY - boxH / 2);
        ctx.lineTo(centerX + 56, sysY - boxH / 2 - 7);
        ctx.lineTo(centerX + 64, sysY - boxH / 2 - 7);
        ctx.closePath();
        ctx.fill();

        // Negative feedback loop curving back to Hypothalamus
        ctx.strokeStyle = 'rgba(16, 185, 129, 0.4)';
        ctx.setLineDash([4, 4]);
        ctx.beginPath();
        ctx.moveTo(centerX + boxW + 15, sysY);
        ctx.bezierCurveTo(width - 25, sysY, width - 25, hypY, centerX + 95, hypY);
        ctx.stroke();
        ctx.setLineDash([]);

        ctx.fillStyle = '#10b981';
        ctx.font = '8px JetBrains Mono, monospace';
        ctx.textAlign = 'right';
        ctx.fillText('(-) Homeostatic Inh.', width - 30, (sysY + hypY) / 2);

        // Header Title
        ctx.font = 'bold 11px JetBrains Mono, monospace';
        ctx.textAlign = 'left';
        ctx.fillStyle = '#ffffff';
        ctx.fillText('HPG AXIS & PATIENT TISSUE TROPISM', 15, 20);

        ctx.textAlign = 'right';
        ctx.font = '9px JetBrains Mono, monospace';
        ctx.fillStyle = isMtF ? '#f43f5e' : '#38bdf8';
        ctx.fillText(isMtF ? '♀ MtF TARGETING' : '♂ FtM TARGETING', width - 15, 20);
    },

    /**
     * Inspects DOM on page load and automatically renders initial visual charts for prerendered canvases.
     */
    autoInit: function () {
        // Auto-render default kinetics chart if on Endocrine page
        const kineticsCanvas = document.getElementById('canvas-endocrine-kinetics');
        if (kineticsCanvas) {
            const defaultPoints = [
                { hour: 0, estradiolPmolL: 120.0, testosteroneNmolL: 18.0 },
                { hour: 4, estradiolPmolL: 237.8, testosteroneNmolL: 13.4 },
                { hour: 8, estradiolPmolL: 323.3, testosteroneNmolL: 10.06 },
                { hour: 12, estradiolPmolL: 385.4, testosteroneNmolL: 7.63 },
                { hour: 16, estradiolPmolL: 430.4, testosteroneNmolL: 5.87 },
                { hour: 20, estradiolPmolL: 463.2, testosteroneNmolL: 4.59 },
                { hour: 24, estradiolPmolL: 487.0, testosteroneNmolL: 3.66 },
                { hour: 28, estradiolPmolL: 504.2, testosteroneNmolL: 2.99 },
                { hour: 32, estradiolPmolL: 516.8, testosteroneNmolL: 2.50 },
                { hour: 36, estradiolPmolL: 525.9, testosteroneNmolL: 2.14 },
                { hour: 40, estradiolPmolL: 532.5, testosteroneNmolL: 1.88 },
                { hour: 44, estradiolPmolL: 537.3, testosteroneNmolL: 1.70 },
                { hour: 48, estradiolPmolL: 540.8, testosteroneNmolL: 1.56 },
                { hour: 52, estradiolPmolL: 543.3, testosteroneNmolL: 1.46 },
                { hour: 56, estradiolPmolL: 545.1, testosteroneNmolL: 1.39 },
                { hour: 60, estradiolPmolL: 546.5, testosteroneNmolL: 1.34 },
                { hour: 64, estradiolPmolL: 547.4, testosteroneNmolL: 1.30 },
                { hour: 68, estradiolPmolL: 548.1, testosteroneNmolL: 1.27 },
                { hour: 72, estradiolPmolL: 548.6, testosteroneNmolL: 1.25 }
            ];
            WeaverVisualizer.renderKineticsChart('canvas-endocrine-kinetics', defaultPoints, 550.0, 1.2);
        }

        // Auto-render default composition doughnut if on Workflow Wizard
        const compCanvas = document.getElementById('canvas-composition');
        if (compCanvas) {
            WeaverVisualizer.renderCompositionDoughnut('canvas-composition', 9, 22, 15, 5, 39.2);
        }

        // Auto-render DNA helix if on page
        const helixCanvas = document.getElementById('canvas-dna-helix');
        if (helixCanvas) {
            WeaverVisualizer.renderDnaHelix('canvas-dna-helix');
        }

        // Auto-render anatomical targeting if on page
        const anatCanvas = document.getElementById('canvas-anatomical-target');
        if (anatCanvas) {
            WeaverVisualizer.renderAnatomicalTargeting('canvas-anatomical-target');
        }
    },

    /**
     * Registers DOM and window event listeners for automatic chart initialization on page load.
     * 
     * @param {Object} [doc] Document object to attach listeners to.
     * @param {Object} [win] Window object to attach load listeners to.
     */
    registerAutoInitListeners: function (doc, win) {
        const targetDoc = doc !== undefined ? doc : (typeof document !== 'undefined' ? document : null);
        const targetWin = win !== undefined ? win : (typeof window !== 'undefined' ? window : null);

        if (targetDoc && typeof targetDoc.addEventListener === 'function') {
            if (targetDoc.readyState === 'loading') {
                targetDoc.addEventListener('DOMContentLoaded', () => setTimeout(WeaverVisualizer.autoInit, 50));
            } else {
                setTimeout(WeaverVisualizer.autoInit, 50);
            }
        }

        if (targetWin && typeof targetWin.addEventListener === 'function') {
            targetWin.addEventListener('load', () => setTimeout(WeaverVisualizer.autoInit, 100));
        }
    }
};

WeaverVisualizer.registerAutoInitListeners();

globalThis.WeaverVisualizer = WeaverVisualizer;

if (typeof module !== 'undefined') {
    module.exports = WeaverVisualizer;
}
