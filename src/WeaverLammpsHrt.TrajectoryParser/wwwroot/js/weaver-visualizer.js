/**
 * ============================================================================
 * Weaver-Visualizer: Open-Source High-Performance Biotechnology Canvas Charting
 * ----------------------------------------------------------------------------
 * WHAT: Zero-dependency, lightweight, high-DPI HTML5 Canvas visualization engine
 *       for molecular kinetics, DNA composition, and cryogenic annealing ramps.
 * WHY:  Wet-lab and sovereign simulation environments require 100% open-source,
 *       air-gapped visualization with zero external CDN dependencies.
 * HOW:  Vector math, cubic Bézier interpolation, and hardware-accelerated canvas2D.
 * ----------------------------------------------------------------------------
 * LICENSE: MIT License
 * Copyright (c) 2026 lantea-one / Weaver-LAMMPS-HRT Contributors
 * ============================================================================
 */

window.WeaverVisualizer = {
    /**
     * Renders a dual-axis pharmacodynamic hormone stabilization trajectory.
     * @param {string} canvasId DOM element ID of the canvas
     * @param {Array} points Array of {hour, estradiol_pmol_l, testosterone_nmol_l}
     * @param {number} targetE2 Target Estradiol plateau (pmol/L)
     * @param {number} targetT Target Testosterone plateau (nmol/L)
     */
    renderKineticsChart: function (canvasId, points, targetE2, targetT) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        const dpr = window.devicePixelRatio || 1;
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

        const maxHours = points[points.length - 1].hour || 72;
        const maxE2 = Math.max(800, targetE2 * 1.25);
        const maxT = Math.max(30, targetT * 1.5);

        // Helper coordinate projections
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
        ctx.moveTo(getX(points[0].hour), getYE2(points[0].estradiol_pmol_l));
        for (let i = 1; i < points.length; i++) {
            const prev = points[i - 1];
            const curr = points[i];
            const cx = (getX(prev.hour) + getX(curr.hour)) / 2;
            ctx.bezierCurveTo(cx, getYE2(prev.estradiol_pmol_l), cx, getYE2(curr.estradiol_pmol_l), getX(curr.hour), getYE2(curr.estradiol_pmol_l));
        }
        ctx.lineTo(getX(points[points.length - 1].hour), height - padBottom);
        ctx.lineTo(getX(points[0].hour), height - padBottom);
        ctx.closePath();
        ctx.fillStyle = gradE2;
        ctx.fill();

        // Stroke E2 Line
        ctx.beginPath();
        ctx.moveTo(getX(points[0].hour), getYE2(points[0].estradiol_pmol_l));
        for (let i = 1; i < points.length; i++) {
            const prev = points[i - 1];
            const curr = points[i];
            const cx = (getX(prev.hour) + getX(curr.hour)) / 2;
            ctx.bezierCurveTo(cx, getYE2(prev.estradiol_pmol_l), cx, getYE2(curr.estradiol_pmol_l), getX(curr.hour), getYE2(curr.estradiol_pmol_l));
        }
        ctx.strokeStyle = '#38bdf8';
        ctx.lineWidth = 2.5;
        ctx.shadowColor = '#00f2fe';
        ctx.shadowBlur = 6;
        ctx.stroke();
        ctx.shadowBlur = 0;

        // 4. Draw Testosterone (T) Curve
        ctx.beginPath();
        ctx.moveTo(getX(points[0].hour), getYT(points[0].testosterone_nmol_l));
        for (let i = 1; i < points.length; i++) {
            const prev = points[i - 1];
            const curr = points[i];
            const cx = (getX(prev.hour) + getX(curr.hour)) / 2;
            ctx.bezierCurveTo(cx, getYT(prev.testosterone_nmol_l), cx, getYT(curr.testosterone_nmol_l), getX(curr.hour), getYT(curr.testosterone_nmol_l));
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
     */
    renderCompositionDoughnut: function (canvasId, baseA, baseT, baseG, baseC, gcPercent) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        const dpr = window.devicePixelRatio || 1;
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
     * Renders a thermal annealing ramp chart.
     */
    renderAnnealingRampChart: function (canvasId, steps) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        const dpr = window.devicePixelRatio || 1;
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
        const getX = (idx) => padLeft + (idx / (steps.length - 1)) * plotW;

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
    }
};
