/**
 * ============================================================================
 * Weaver-Visualizer Test Suite: 100% Line & Branch Coverage Verification
 * ----------------------------------------------------------------------------
 * WHAT: Unit test suite asserting mathematical rendering fidelity, edge cases,
 *       and full branch coverage for all canvas chart renderers.
 * WHY:  Ensures the sovereign visualization engine adheres to the strict build
 *       rules (100% line & branch coverage) with zero external dependencies.
 * HOW:  Node.js native test runner ('node:test', 'node:assert/strict') and mock
 *       high-DPI 2D canvas context.
 * ============================================================================
 */

import test from 'node:test';
import assert from 'node:assert/strict';

// WHAT: Creates a mock HTML5 Canvas and 2D Rendering Context.
// WHY:  Permits headless testing of vector math and canvas drawing routines in pure Node.js.
// HOW:  Provides stateful mock methods mimicking the W3C CanvasRenderingContext2D specification.
function createMockCanvas(width = 600, height = 300) {
    const calls = [];
    const ctx = {
        fillStyle: '',
        strokeStyle: '',
        lineWidth: 1,
        font: '',
        textAlign: '',
        textBaseline: '',
        shadowColor: '',
        shadowBlur: 0,
        globalAlpha: 1.0,
        scale: (x, y) => calls.push({ method: 'scale', x, y }),
        fillRect: (x, y, w, h) => calls.push({ method: 'fillRect', x, y, w, h }),
        beginPath: () => calls.push({ method: 'beginPath' }),
        moveTo: (x, y) => calls.push({ method: 'moveTo', x, y }),
        lineTo: (x, y) => calls.push({ method: 'lineTo', x, y }),
        stroke: () => calls.push({ method: 'stroke' }),
        fill: () => calls.push({ method: 'fill' }),
        arc: (x, y, r, sa, ea, ac) => calls.push({ method: 'arc', x, y, r, sa, ea, ac }),
        bezierCurveTo: (cp1x, cp1y, cp2x, cp2y, x, y) => calls.push({ method: 'bezierCurveTo', cp1x, cp1y, cp2x, cp2y, x, y }),
        fillText: (t, x, y) => calls.push({ method: 'fillText', t, x, y }),
        setLineDash: (d) => calls.push({ method: 'setLineDash', d }),
        closePath: () => calls.push({ method: 'closePath' }),
        roundRect: (x, y, w, h, r) => calls.push({ method: 'roundRect', x, y, w, h, r }),
        createLinearGradient: () => ({
            addColorStop: (pos, col) => calls.push({ method: 'addColorStop', pos, col })
        })
    };

    return {
        clientWidth: width,
        clientHeight: height,
        width: 0,
        height: 0,
        calls,
        ctx,
        getContext: (type) => (type === '2d' ? ctx : null)
    };
}

// Ensure mock DOM environment exists
const elementsMap = new Map();
globalThis.document = {
    readyState: 'complete',
    getElementById: (id) => elementsMap.get(id) || null,
    addEventListener: () => {}
};
globalThis.window = {
    devicePixelRatio: 2,
    addEventListener: () => {}
};

// Import WeaverVisualizer
import WeaverVisualizer from '../src/WeaverLammpsHrt.TrajectoryParser/wwwroot/js/weaver-visualizer.js';

test('renderKineticsChart: handles missing canvas or empty points gracefully', () => {
    // Missing canvas branch
    assert.doesNotThrow(() => WeaverVisualizer.renderKineticsChart('non-existent-canvas', [], 550, 1.2));

    const canvas = createMockCanvas();
    elementsMap.set('kinetics-test', canvas);

    // Null points branch
    assert.doesNotThrow(() => WeaverVisualizer.renderKineticsChart('kinetics-test', null, 550, 1.2));
    assert.ok(canvas.calls.some(c => c.method === 'fillRect')); // Drew background

    // Empty points branch
    canvas.calls.length = 0;
    assert.doesNotThrow(() => WeaverVisualizer.renderKineticsChart('kinetics-test', [], 550, 1.2));
    assert.ok(canvas.calls.some(c => c.method === 'fillRect'));
});

test('renderKineticsChart: renders curves with property variations and fallback dimensions', () => {
    // Test with zero clientWidth/clientHeight to trigger fallback dimensions (600, 240)
    const fallbackCanvas = createMockCanvas(0, 0);
    elementsMap.set('kinetics-fallback', fallbackCanvas);

    // Also test without window.devicePixelRatio
    const prevDpr = globalThis.window.devicePixelRatio;
    delete globalThis.window.devicePixelRatio;

    const points = [
        { hour: 0, estradiol_pmol_l: 120.0, testosterone_nmol_l: 18.0 },
        { Hour: 24, estradiolPmolL: 450.0, testosteroneNmolL: 4.0 },
        { EstradiolPmolL: 550.0, TestosteroneNmolL: 1.2 } // Missing hour tests default 0
    ];

    WeaverVisualizer.renderKineticsChart('kinetics-fallback', points, 550.0, 1.2);
    assert.ok(fallbackCanvas.calls.some(c => c.method === 'bezierCurveTo'));
    assert.ok(fallbackCanvas.calls.some(c => c.method === 'fillText' && c.t === '0h'));

    // Test with completely empty point objects to trigger default 0 values in extractors
    const emptyPointsCanvas = createMockCanvas(600, 240);
    elementsMap.set('kinetics-empty-props', emptyPointsCanvas);
    WeaverVisualizer.renderKineticsChart('kinetics-empty-props', [{}, {}], 550.0, 1.2);
    assert.ok(emptyPointsCanvas.calls.some(c => c.method === 'stroke'));

    globalThis.window.devicePixelRatio = prevDpr;
});

test('renderCompositionDoughnut: handles missing canvas, zero total, and full doughnut', () => {
    // Missing canvas branch
    assert.doesNotThrow(() => WeaverVisualizer.renderCompositionDoughnut('missing-canvas', 1, 1, 1, 1, 50));

    const canvas = createMockCanvas(0, 0); // Tests default dimensions 220x220
    elementsMap.set('doughnut-test', canvas);

    // Total === 0 branch
    WeaverVisualizer.renderCompositionDoughnut('doughnut-test', 0, 0, 0, 0, 0);
    assert.equal(canvas.calls.filter(c => c.method === 'arc').length, 0);

    // Valid composition
    WeaverVisualizer.renderCompositionDoughnut('doughnut-test', 10, 15, 20, 25, 45.0);
    assert.ok(canvas.calls.some(c => c.method === 'fillText' && c.t === '45%'));
    assert.ok(canvas.calls.some(c => c.method === 'arc'));
});

test('renderAnnealingRampChart: handles missing canvas, empty steps, and step lines', () => {
    // Missing canvas
    assert.doesNotThrow(() => WeaverVisualizer.renderAnnealingRampChart('missing-ramp', []));

    const canvas = createMockCanvas(0, 0); // Tests default 500x180
    elementsMap.set('ramp-test', canvas);

    // Null steps branch
    WeaverVisualizer.renderAnnealingRampChart('ramp-test', null);
    assert.ok(canvas.calls.some(c => c.method === 'fillRect'));

    // Empty steps branch
    WeaverVisualizer.renderAnnealingRampChart('ramp-test', []);
    assert.ok(canvas.calls.some(c => c.method === 'fillRect'));

    // Single step branch (tests steps.length - 1 === 0 fallback to 1)
    canvas.calls.length = 0;
    WeaverVisualizer.renderAnnealingRampChart('ramp-test', [{ target_temperature_celsius: 85 }]);
    assert.ok(canvas.calls.some(c => c.method === 'fillText' && c.t === '85°C'));

    // Multi-step schedule
    canvas.calls.length = 0;
    const steps = [
        { target_temperature_celsius: 90 },
        { target_temperature_celsius: 65 },
        { target_temperature_celsius: 50 },
        { target_temperature_celsius: 20 },
        { target_temperature_celsius: 4 }
    ];
    WeaverVisualizer.renderAnnealingRampChart('ramp-test', steps);
    assert.ok(canvas.calls.some(c => c.method === 'lineTo'));
    assert.ok(canvas.calls.some(c => c.method === 'arc'));
});

test('renderDnaHelix: handles missing canvas, options variations, and 3D projections', () => {
    // Missing canvas
    assert.doesNotThrow(() => WeaverVisualizer.renderDnaHelix('missing-helix'));

    // Default options and fallback dimensions (320x420)
    const canvasDefault = createMockCanvas(0, 0);
    elementsMap.set('helix-default', canvasDefault);
    WeaverVisualizer.renderDnaHelix('helix-default');
    assert.ok(canvasDefault.calls.some(c => c.method === 'fillText' && c.t.includes("5' [Strand 1]")));

    // Single base pair (tests numBp === 1 branch)
    const canvasSingle = createMockCanvas(400, 500);
    elementsMap.set('helix-single', canvasSingle);
    WeaverVisualizer.renderDnaHelix('helix-single', {
        sequence: 'A',
        numBasePairs: 1
    });
    assert.ok(canvasSingle.calls.some(c => c.method === 'arc'));

    // Full options with RNA 'U', unknown base 'X' (tests fallback color/complement), and showLabels = false
    const canvasCustom = createMockCanvas(400, 500);
    elementsMap.set('helix-custom', canvasCustom);
    WeaverVisualizer.renderDnaHelix('helix-custom', {
        sequence: 'aucgx',
        rotationAngle: 1.57,
        numBasePairs: 5, // Reaches 'x'
        strand1Color: '#00f2fe',
        strand2Color: '#ff0055',
        showLabels: false // Tests showLabels === false branch
    });
    assert.ok(canvasCustom.calls.some(c => c.method === 'arc'));

    // Multi base pairs with showLabels = true (tests both alpha > 0.6 and alpha <= 0.6)
    canvasCustom.calls.length = 0;
    WeaverVisualizer.renderDnaHelix('helix-custom', {
        sequence: 'ATGCATGC',
        rotationAngle: 0.5,
        numBasePairs: 18,
        showLabels: true
    });
    assert.ok(canvasCustom.calls.some(c => c.method === 'fillText' && c.t === 'A'));
    assert.ok(canvasCustom.calls.some(c => c.method === 'fillText' && c.t === 'G'));
});

test('renderAnatomicalTargeting: handles missing canvas, MtF and FtM regimens, and defaults', () => {
    // Missing canvas
    assert.doesNotThrow(() => WeaverVisualizer.renderAnatomicalTargeting('missing-anat'));

    // Default options and fallback dimensions (540x340) -> MtF default
    const canvasMtF = createMockCanvas(0, 0);
    elementsMap.set('anat-mtf', canvasMtF);
    WeaverVisualizer.renderAnatomicalTargeting('anat-mtf');
    assert.ok(canvasMtF.calls.some(c => c.method === 'fillText' && c.t.includes('HYPOTHALAMUS')));
    assert.ok(canvasMtF.calls.some(c => c.method === 'fillText' && c.t.includes('ESTROGEN RECEPTORS')));
    assert.ok(canvasMtF.calls.some(c => c.method === 'fillText' && c.t.includes('♀ MtF TARGETING')));

    // FtM regimen with custom targetTissue, Kd, E2, and T
    const canvasFtM = createMockCanvas(600, 400);
    elementsMap.set('anat-ftm', canvasFtM);
    WeaverVisualizer.renderAnatomicalTargeting('anat-ftm', {
        regimen: 'FemaleToMale',
        targetTissue: 'AnteriorPituitaryGonadotrope',
        kdNanoMolar: 8.5,
        targetE2: 90.0,
        targetT: 25.0
    });
    assert.ok(canvasFtM.calls.some(c => c.method === 'fillText' && c.t.includes('ANDROGEN RECEPTORS')));
    assert.ok(canvasFtM.calls.some(c => c.method === 'fillText' && c.t.includes('♂ FtM TARGETING')));

    // FtM with undefined e2 and t (tests FtM fallback targets 120.0 and 22.0)
    canvasFtM.calls.length = 0;
    WeaverVisualizer.renderAnatomicalTargeting('anat-ftm', {
        regimen: 'FemaleToMale'
    });
    assert.ok(canvasFtM.calls.some(c => c.method === 'fillText' && c.t.includes('Target T: 22.0 nmol/L')));
});

test('all renderers handle undefined window.devicePixelRatio cleanly', () => {
    const prevDpr = globalThis.window.devicePixelRatio;
    delete globalThis.window.devicePixelRatio;

    const c1 = createMockCanvas();
    const c2 = createMockCanvas();
    const c3 = createMockCanvas();
    const c4 = createMockCanvas();
    elementsMap.set('dpr-c1', c1);
    elementsMap.set('dpr-c2', c2);
    elementsMap.set('dpr-c3', c3);
    elementsMap.set('dpr-c4', c4);

    WeaverVisualizer.renderCompositionDoughnut('dpr-c1', 1, 1, 1, 1, 50);
    WeaverVisualizer.renderAnnealingRampChart('dpr-c2', [{ target_temperature_celsius: 70 }]);
    WeaverVisualizer.renderDnaHelix('dpr-c3');
    WeaverVisualizer.renderAnatomicalTargeting('dpr-c4');

    assert.ok(c1.calls.length > 0);
    assert.ok(c2.calls.length > 0);
    assert.ok(c3.calls.length > 0);
    assert.ok(c4.calls.length > 0);

    globalThis.window.devicePixelRatio = prevDpr;
});

test('autoInit: triggers when canvases exist and handles when canvases are absent', () => {
    // 1. Canvases do NOT exist
    elementsMap.clear();
    assert.doesNotThrow(() => WeaverVisualizer.autoInit());

    // 2. Canvases DO exist
    const kCanvas = createMockCanvas();
    const cCanvas = createMockCanvas();
    const hCanvas = createMockCanvas();
    const aCanvas = createMockCanvas();

    elementsMap.set('canvas-endocrine-kinetics', kCanvas);
    elementsMap.set('canvas-composition', cCanvas);
    elementsMap.set('canvas-dna-helix', hCanvas);
    elementsMap.set('canvas-anatomical-target', aCanvas);

    assert.doesNotThrow(() => WeaverVisualizer.autoInit());
    assert.ok(kCanvas.calls.some(c => c.method === 'bezierCurveTo'));
    assert.ok(cCanvas.calls.some(c => c.method === 'arc'));
    assert.ok(hCanvas.calls.some(c => c.method === 'arc'));
    assert.ok(aCanvas.calls.some(c => c.method === 'roundRect'));
});

test('registerAutoInitListeners: attaches listeners for both loading and complete ready states', () => {
    let domContentLoadedCallback = null;
    let loadCallback = null;

    const mockDocLoading = {
        readyState: 'loading',
        addEventListener: (event, cb) => {
            if (event === 'DOMContentLoaded') domContentLoadedCallback = cb;
        }
    };

    const mockDocComplete = {
        readyState: 'complete',
        addEventListener: () => {}
    };

    const mockWin = {
        addEventListener: (event, cb) => {
            if (event === 'load') loadCallback = cb;
        }
    };

    // Test with loading document and window
    WeaverVisualizer.registerAutoInitListeners(mockDocLoading, mockWin);
    assert.ok(typeof domContentLoadedCallback === 'function');
    assert.ok(typeof loadCallback === 'function');

    // Trigger the registered callbacks
    domContentLoadedCallback();
    loadCallback();

    // Test with complete document
    WeaverVisualizer.registerAutoInitListeners(mockDocComplete, null);

    // Test with null document and null window
    assert.doesNotThrow(() => WeaverVisualizer.registerAutoInitListeners(null, null));

    // Test with default undefined arguments (exercises globalThis.document and globalThis.window)
    assert.doesNotThrow(() => WeaverVisualizer.registerAutoInitListeners());
});
