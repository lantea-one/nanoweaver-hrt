# Dynamic Closed-Loop Endocrine Simulation & LH/FSH Feedback Engine

> **Coupled 4th-Order Runge-Kutta (RK4) Mathematical Modeling of the Hypothalamic-Pituitary-Gonadal (HPG) Axis & Autonomous Aptamer Gate Modulation**

---

## 1. Overview & Biomedical Rationale

Conventional hormone replacement therapy (HRT) regimens rely on static exogenous dosing schedules (e.g., bi-weekly intramuscular injections or daily transdermal gel applications). This results in non-physiological peak-and-trough pharmacokinetic oscillations, chronic gonadotropin rebound during troughs, and elevated hepatic and thromboembolic risk during supra-physiological peaks.

NanoWeaverHrt models **autonomous closed-loop endocrine regulation**:
1. Synthetic DNA origami nanocarriers equipped with allosteric aptamer gates circulate within the capillary beds of the anterior pituitary gonadotropes.
2. In the presence of elevated Luteinizing Hormone (LH) or Follicle-Stimulating Hormone (FSH), aptamer affinity shifts, dynamically increasing steroid micro-release.
3. As circulating 17β-estradiol or testosterone levels reach clinical setpoints, negative feedback suppresses anterior pituitary gonadotropin secretion, naturally dampening nanocarrier micro-release to baseline homeostatic maintenance.

---

## 2. Mathematical Modeling: Coupled Non-Linear Differential Equations

The dynamic feedback engine implements a system of four coupled ordinary differential equations (ODEs) integrated via a deterministic Runge-Kutta 4th-order (RK4) numerical solver.

### 2.1 Gonadotropin Dynamics (LH and FSH Suppression)

Anterior pituitary secretion of LH and FSH is modeled via non-linear Hill-type cooperative inhibition driven by circulating 17β-estradiol (E2) and total testosterone (T):

$$
\frac{d[LH]}{dt} = \frac{V_{\max, LH}}{1 + \left(\frac{[E_2]}{K_{i, E2, LH}}\right)^{n_1} + \left(\frac{[T]}{K_{i, T, LH}}\right)^{n_2}} - k_{\text{clear}, LH} \cdot [LH]
$$

$$
\frac{d[FSH]}{dt} = \frac{V_{\max, FSH}}{1 + \left(\frac{[E_2]}{K_{i, E2, FSH}}\right)^{m_1} + \left(\frac{[T]}{K_{i, T, FSH}}\right)^{m_2}} - k_{\text{clear}, FSH} \cdot [FSH]
$$

Where:
- `Vmax_LH = 2.2 IU/(L·h)` is the basal maximum pituitary LH release rate.
- `Vmax_FSH = 1.6 IU/(L·h)` is the basal maximum pituitary FSH release rate.
- `Ki_E2_LH = 180.0 pmol/L` and `Ki_T_LH = 3.5 nmol/L` represent negative feedback half-inhibition constants.
- `k_clear_LH = 0.35 h⁻¹` and `k_clear_FSH = 0.28 h⁻¹` represent hepatic/renal clearance rate constants.

### 2.2 Steroid Kinetics & Dynamic Nanocarrier Micro-Release

Circulating 17β-estradiol and total testosterone concentrations evolve through the sum of endogenous gonadal production (stimulated by LH), autonomous nanocarrier micro-infusion, and metabolic elimination:

$$
\frac{d[E_2]}{dt} = k_{\text{endo}, E2} \cdot [LH] + R_{\text{carrier}, E2}(t, [LH], [E_2]) - k_{\text{clear}, E2} \cdot [E_2]
$$

$$
\frac{d[T]}{dt} = k_{\text{endo}, T} \cdot [LH] + R_{\text{carrier}, T}(t, [LH], [T]) - k_{\text{clear}, T} \cdot [T]
$$

Where:
- `R_carrier_E2` and `R_carrier_T` represent closed-loop nanocarrier micro-release rates.
- Under **Feminizing (MtF)** regimens, the carrier infuses 17β-estradiol proportional to the concentration deficit, dynamically modulated by circulating LH:

$$
R_{\text{carrier}, E2} = k_{\text{gain}, E2} \cdot \max(0, E_{2, \text{target}} - [E_2]) \cdot \left(1.0 + 0.3 \cdot \frac{[LH]}{6.5}\right)
$$

- Under **Masculinizing (FtM)** regimens, the carrier infuses testosterone proportional to the androgenic deficit:

$$
R_{\text{carrier}, T} = k_{\text{gain}, T} \cdot \max(0, T_{\text{target}} - [T]) \cdot \left(1.0 + 0.3 \cdot \frac{[LH]}{6.5}\right)
$$

- `k_clear_E2 = 0.08 × C_multiplier` and `k_clear_T = 0.09 × C_multiplier` are modulated by the patient's genetic clearance multiplier from pharmacogenomic screening.

---

## 3. REST API & Integration

### Minimal API Endpoint
- **Route**: `POST /api/endocrine/closed-loop/simulate`
- **Authentication**: Bearer Token (`Researcher` or `Administrator`)
- **Request Payload**:
  ```json
  {
    "initialEstradiolPmolL": 120.0,
    "initialTestosteroneNmolL": 18.0,
    "initialLhIuL": 6.5,
    "initialFshIuL": 5.0,
    "targetEstradiolPmolL": 550.0,
    "targetTestosteroneNmolL": 1.2,
    "targetState": "EstrogenicDominant",
    "totalHours": 72,
    "stepHours": 1.0,
    "clearanceMultiplier": 1.0
  }
  ```
- **Response**:
  ```json
  {
    "points": [
      {
        "hour": 0,
        "estradiolPmolL": 120.0,
        "testosteroneNmolL": 18.0,
        "lhIuL": 6.5,
        "fshIuL": 5.0,
        "carrierReleaseRate": 43.8
      },
      {
        "hour": 72,
        "estradiolPmolL": 548.9,
        "testosteroneNmolL": 0.85,
        "lhIuL": 0.42,
        "fshIuL": 0.61,
        "carrierReleaseRate": 1.2
      }
    ],
    "finalEstradiolPmolL": 548.9,
    "finalTestosteroneNmolL": 0.85,
    "finalLhIuL": 0.42,
    "finalFshIuL": 0.61,
    "steadyStateReached": true,
    "timeToSuppressionHours": 18.0
  }
  ```

---

## 5. Closed-Loop Feedforward Clearance Calibration & isMtF Gonadal Suppression
- **Feedforward Clearance Calibration**: The closed-loop ODE solver incorporates proportional feedback gain (`K_p = 14.0`) combined with steady-state metabolic clearance feedforward terms, ensuring rapid convergence to target endocrine setpoints without integral overshoot or oscillatory rebound.
- **`isMtF` Gonadal Suppression Kinetics**: The simulation engine evaluates the boolean flag `isMtF` to configure estrogenic dominant target matrices, applying targeted inhibitory Hill coefficients to suppress endogenous testicular testosterone production and luteinizing hormone secretion.
