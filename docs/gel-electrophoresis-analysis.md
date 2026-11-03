# Agarose Gel Electrophoresis Image Processing & Nanocarrier Band Analysis

> **Automated Densitometry, Lane Segmentation, and Molecular Weight Calibration for DNA Origami Quality Control**

---

## 1. Overview & Wet-Lab Context

Following thermal annealing of DNA origami nanocarriers (e.g. 90°C to 4°C slow ramp in 11 mM MgCl2 buffer), laboratory researchers perform agarose gel electrophoresis (typically 1.5% to 2.0% agarose in 0.5× TBE buffer supplemented with 11 mM MgCl2) to evaluate assembly yield and structural integrity.

Key visual indicators on an agarose gel:
- **Sharp Monomer Band**: High-yield properly folded nanocarriers migrate as a distinct, dense, clean band.
- **High-Mobility Staple Smear**: Excess un-annealed single-stranded oligonucleotide staples migrate rapidly near the bottom of the lane (high relative mobility, Rf > 0.8).
- **Low-Mobility Multimers / Aggregates**: Mis-folded concatemers and aggregates migrate slowly near the well (low relative mobility, Rf < 0.2).

`IGelAnalysisService` automates image pre-processing, lane boundary detection, background subtraction, peak densitometry, relative migration (Rf) computation, and log-linear ladder interpolation directly within NanoWeaverHrt.

---

## 2. Image Processing Pipeline

```
Raw Grayscale Gel Image (Buffer / Stream)
                   │
                   ▼
  1. Horizontal Intensity Profile Projection
                   │
                   ▼
  2. Lane Boundary Segmentation (Valley Detection)
                   │
                   ▼
  3. Vertical Column Extraction & Local Background Subtraction
                   │
                   ▼
  4. Intensity Peak Detection (Full-Width Half-Max Band Calling)
                   │
                   ▼
  5. Relative Mobility (Rf = d_band / d_front) Calculation
                   │
                   ▼
  6. DNA Ladder Log-Linear Calibration (log10 bp vs. Rf)
                   │
                   ▼
  7. Densitometric Yield & Folding Purity Assessment
```

### Algorithmic Highlights

1. **Zero-Allocation Memory Policy**:
   - The analysis service rents intensity and coordinate arrays from `System.Buffers.ArrayPool<double>.Shared` and `ArrayPool<byte>.Shared`, returning them in structured `finally` blocks to guarantee zero Garbage Collection pressure during high-resolution gel scans.

2. **Relative Mobility (Rf)**:
   - For every detected band in a designated lane:

$$
R_f = \frac{d_{\text{migration}}}{h_{\text{lane}}}
$$

   - Where `d_migration` is the vertical pixel distance from the well bottom to the intensity peak, and `h_lane` is the total accessible migration distance to the dye front.

3. **Log-Linear Ladder Calibration**:
   - Using standardized ladder standards (e.g., 1 kb DNA Ladder with reference bands from 500 bp to 10,000 bp), the service fits a log-linear regression:

$$
\log_{10}(\text{size}_{\text{bp}}) = a \cdot R_f + b
$$

   - Estimated molecular weights of experimental DNA origami nanocarrier bands are computed from this calibrated curve.

4. **Folding Quality Acceptance Criteria**:
   - **Monomer Yield Percentage**: ≥ 65.0% of total lane integrated optical density.
   - **Un-annealed Staple Smear**: ≤ 25.0% of total lane integrated optical density.
   - When both thresholds are met, the sample is classified as **`FoldingQualityAcceptable = true`**.

---

## 3. REST API Reference

### Minimal API Endpoint
- **Route**: `POST /api/fabrication/gel/analyze`
- **Authentication**: Bearer Token (`Researcher` or `Administrator`)
- **Request Payload**:
  ```json
  {
    "imageBytes": "...Base64EncodedGrayscaleBytes...",
    "width": 640,
    "height": 480,
    "expectedLaneCount": 8,
    "ladderType": "OneKbDnaLadder",
    "ladderLaneNumber": 1,
    "backgroundThreshold": 0.15
  }
  ```
- **Response**:
  ```json
  {
    "width": 640,
    "height": 480,
    "detectedLaneCount": 8,
    "lanes": [
      {
        "laneNumber": 2,
        "label": "Annealed M13 Hexagonal Nanotube",
        "bands": [
          {
            "bandIndex": 0,
            "migrationDistancePixels": 182.4,
            "relativeMigrationRf": 0.38,
            "estimatedBasePairs": 7249.0,
            "intensityIntegratedDensity": 84210.0,
            "relativeYieldPercentage": 78.4
          }
        ],
        "monomerBandYieldPercentage": 78.4,
        "unannealedStapleSmearPercentage": 14.2,
        "dimerMultimerPercentage": 7.4,
        "totalIntensity": 107410.0,
        "foldingQualityAcceptable": true
      }
    ],
    "overallFoldingSuccessRate": 78.4,
    "qualityAssessmentNotes": "High-purity monomer formation detected across 7 experimental lanes. Staple smear within acceptable tolerances.",
    "analyzedAtUtc": "2026-11-03T19:00:00Z"
  }
  ```

---

## 4. UI Integration in Guided Workflow Wizard

Stage 6 of `WorkflowWizard.razor` includes an interactive **Gel Electrophoresis Quality Inspection** panel:
- Visual lane intensity plots rendered via HTML5 canvas.
- Instant feedback badges: **QUALITY ACCEPTABLE** (green) vs. **EXCESS SMEAR / AGGREGATION** (amber/red).
- Direct linkage to Opentrons thermal annealing protocols, validating synthesis runs prior to downstream endocrine administration.
