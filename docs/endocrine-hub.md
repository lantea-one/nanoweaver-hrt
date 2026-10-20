# Patient-Targeted Endocrine Regulatory Hub

> **Pharmacodynamics, Closed-Loop In-Vivo Kinetics, and Bistable Setpoint Stabilization**

The **Endocrine Regulatory Hub** provides computational modeling and real-time visualization of physiological hormone regulation. It bridges molecular nanocarrier design with biological systems pharmacology, modeling how targeted DNA origami nanocarriers maintain stable, permanent hormone levels in vivo.

![Patient-Targeted Endocrine Regulatory Hub](images/02_endocrine_hub.png)

---

## Biomedical Foundations: The HPG Axis & Feedback Loops

To understand how targeted DNA nanocarriers function as sovereign Hormone Replacement Therapy (HRT), it is essential to understand the biological control system governing human sex steroids: the **Hypothalamic-Pituitary-Gonadal (HPG) Axis**.

```
                           +------------------------+
                           |      Hypothalamus      |
                           |   GnRH Pulsing Pulse   |
                           +-----------+------------+
                                       |
                                       v (GnRH)
                           +------------------------+
                           |   Anterior Pituitary   |
                           |    Gonadotropes        |
                           +-----------+------------+
                                       |
                    +------------------+------------------+
                    |                                     |
                    v (LH)                                v (FSH)
         +---------------------+               +---------------------+
         |  Leydig Cells /     |               |  Granulosa Cells /  |
         |  Theca Cells        |               |  Sertoli Cells      |
         +----------+----------+               +----------+----------+
                    |                                     |
                    +------------------+------------------+
                                       |
                                       v
                    +-------------------------------------+
                    |     Steroid Hormones Produced:      |
                    |  - 17β-Estradiol (E2)               |
                    |  - Testosterone (T)                 |
                    |  - Progesterone (P4)                |
                    +------------------+------------------+
                                       |
             (Negative Feedback) - - - + - - - > Inhibit Hypothalamus & Pituitary
```

### The Endogenous Feedback Loop
1. **Hypothalamic Driving**: The hypothalamus secretes pulses of Gonadotropin-Releasing Hormone (GnRH).
2. **Pituitary Response**: GnRH binds to receptors on gonadotrope cells in the anterior pituitary, triggering the synthesis and pulsatile release of **Luteinizing Hormone (LH)** and **Follicle-Stimulating Hormone (FSH)**.
3. **Gonadal Steroidogenesis**: LH and FSH circulate via the bloodstream to gonadal tissues, where enzymes (such as Cytochrome P450 aromatase and 17β-hydroxysteroid dehydrogenase) convert cholesterol into testosterone, estrogens, and progesterone.
4. **Reciprocal Negative Feedback**: When circulating steroid hormone concentrations rise above physiological setpoints, they cross the blood-brain barrier and bind to estrogen/androgen receptors in the hypothalamus and pituitary. This **suppresses** further GnRH, LH, and FSH release, preventing runaway hormone production.

### The Failure of Conventional HRT
Conventional HRT relies on periodic exogenous depot injections (e.g., intramuscular estradiol valerate or testosterone cypionate weekly or biweekly), daily transdermal patches/gels, or oral pills.
* **Peak-to-Trough Volatility**: Injections produce massive supraphysiological spikes within 24–48 hours, followed by steep exponential decay into sub-therapeutic troughs before the next dose.
* **Symptomatic Distress**: Patients experience mood swings, fatigue, vasomotor hot flashes, and migraine episodes correlated directly with hormone peaks and troughs.
* **Cardiovascular & Thrombotic Risks**: Transiently high estrogen peaks trigger hepatic production of coagulation factors, increasing deep vein thrombosis (DVT) risk.

---

## How DNA Nanocarriers Achieve Permanent Homeostasis

Instead of dumping free steroids into the general circulation, our system utilizes **autonomous closed-loop DNA origami nanocarriers**:

1. **Cellular Targeting**: Nanocarriers home specifically to pituitary gonadotrope cells and hypothalamic microvasculature via surface-conjugated peptide ligands.
2. **Aptamer Gating**: The nanocarrier contains an engineered oligonucleotide aptamer latch responsive to LH/FSH concentration. When circulating hormone drops below the physiological target, LH/FSH levels rise; the elevated LH/FSH binds the nanocarrier's aptamer latch, inducing a conformational change that opens the nanocarrier cavity.
3. **On-Demand Micro-Dosing**: The opened cavity exposes its steroid reservoir, diffusing nanomolar quantities locally until feedback restores baseline gonadotropin levels, re-locking the carrier.

---

## The Mathematical Bistable State Machine

The Endocrine Regulatory Hub implements a mathematical state machine modeling the patient's hormonal state:

```
                  +--------------------------------+
                  |            STATE 0             |
                  |       Baseline / Unprimed      |
                  +---------------+----------------+
                                  |
              +-------------------+-------------------+
              |                                       |
   Select Feminizing (MtF)                 Select Masculinizing (FtM)
              |                                       |
              v                                       v
+-------------------------------+   +-------------------------------+
|            STATE 1            |   |            STATE 2            |
|    Feminizing Homeostasis     |   |    Masculinizing Homeostasis  |
| E2: 500-600 pmol/L (Target)   |   | T:  15-25 nmol/L (Target)     |
| T:  < 1.8 nmol/L (Suppressed) |   | E2: < 150 pmol/L (Suppressed) |
| LH: Basal suppression         |   | LH: Basal suppression         |
+-------------------------------+   +-------------------------------+
```

### State 1: Feminizing Hormone Therapy (MtF)
* **Estradiol (E2) Target Setpoint**: 500 to 600 pmol/L (136 to 163 pg/mL).
  * High enough to induce secondary female sex characteristics, breast tissue development, and feminine fat redistribution.
  * Sufficiently elevated to mediate pituitary negative feedback without requiring synthetic anti-androgens (spironolactone or cyproterone acetate).
* **Testosterone (T) Target Suppression**: < 1.8 nmol/L (< 50 ng/dL).
* **Progesterone (P4) Stabilization**: Optional third-trace co-factor for alveolar tissue maturation and neuroprotection.

### State 2: Masculinizing Hormone Therapy (FtM)
* **Testosterone (T) Target Setpoint**: 15.0 to 25.0 nmol/L (430 to 720 ng/dL).
  * Promotes virilization, vocal fold thickening, clitoral hypertrophy, lean muscle accretion, and cessation of menses.
* **Estradiol (E2) Target Suppression**: < 150 pmol/L (< 40 pg/mL).
  * Downregulated naturally via androgen-mediated negative feedback on hypothalamic GnRH pulsatility.

---

## Pharmacogenomics & Genetic Predisposition Screening

Standard endocrinology models assume "cis-typical" reference physiology. In real-world patient populations, however, single nucleotide polymorphisms (SNPs) and copy-number variations dramatically alter steroid clearance, receptor sensitivity, and thrombotic vulnerability.

The platform includes a dedicated **Pharmacogenomic Risk & Predisposition Engine** integrated into the runtime state engine. It screens patient genotypes or raw nucleotide sequences and automatically generates severity-tiered call-out alerts and mathematical kinetic modifiers.

### Severity-Tiered Clinical Call-Out Alerts

The platform categorizes genetic findings into four strict clinical alert tiers:

* **CRITICAL (Rose / Red Call-Out)**: Variants posing immediate physical risk (such as fatal venous thromboembolism) or acute nanocarrier rejection.
  * *Factor V Leiden (rs6025)*: Renders Factor Va resistant to Activated Protein C (APC), multiplying clotting risk 4.2x in estrogenic regimens. Automatically caps maximum safe 17β-estradiol setpoint to <= 500 pmol/L and prohibits burst release.
  * *Prothrombin G20210A (rs1799963)*: Increases plasma prothrombin levels by 30%. Caps peak estradiol to <= 480 pmol/L.
  * *TLR9 High-Sensitivity Promoter (rs187084)*: Triggers severe endosomal recognition of unmethylated CpG motifs in viral scaffold DNA (e.g. M13mp18). Emits mandatory requirement for pre-synthesis enzymatic CpG methylation using M.SssI methyltransferase.
* **WARNING (Amber Call-Out)**: High-impact pharmacokinetic mutations requiring targeted setpoint and affinity offsets.
  * *Androgen Receptor Exon 1 Elongation (CAG > 26)*: Extended polyglutamine tract impairs AF-1 transactivation, causing androgen resistance. Increases minimum target testosterone to >= 22.0 nmol/L and offsets aptamer Kd by -1.5 nM (tighter affinity).
  * *COMT Val158Met (rs4680 A/A)*: 3- to 4-fold decrease in catechol-O-methyltransferase activity causes accumulation of reactive 2- and 4-hydroxyestrone metabolites. Reduces clearance multiplier to 0.75x baseline.
  * *UGT2B17 Null Deletion (*2/*2)*: Impaired hepatic glucuronidation prolongs circulating testosterone half-life by 60%. Reduces clearance multiplier to 0.60x baseline.
* **ADVISORY (Sky Blue Call-Out)**: Latent carrier status or minor pharmacodynamic variations.
  * *AR Polyglutamine Shortening (CAG < 20)*: Mild androgen hypersensitivity. Advises moderating target testosterone (14.0 to 18.0 nmol/L) to prevent excessive erythrocytosis.
  * *CYP1B1*3 Rapid Hydroxylase (rs1056836 G/G)*: Rapid 4-hydroxylation accelerates estrogen clearance (1.35x multiplier).
  * *Opposite-Regimen Carrier Status*: A carrier of Factor V Leiden who selects an FtM androgenic regimen receives a carrier notice without unnecessary estrogen capping.
* **OPTIMAL (Emerald Green Call-Out)**: Wild-type / homogeneous genome with no detected high-risk clotting, hyper-metabolic, or receptor insensitivity variants. Standard baseline parameters apply.

### Curated Clinical Genetic Marker Catalog

| Gene Symbol | Variant / dbSNP | Functional Category | Default Severity | Biophysical & Clinical Action |
| :--- | :--- | :--- | :--- | :--- |
| **F5** | Factor V Leiden (rs6025, c.1691G>A) | Thrombotic Risk | Critical | Cap MtF E2 <= 500 pmol/L; prohibit bolus burst release; 4.2x thrombosis index. |
| **F2** | Prothrombin G20210A (rs1799963) | Thrombotic Risk | Critical | Cap MtF E2 <= 480 pmol/L; continuous micro-release kinetics; 2.8x thrombosis index. |
| **TLR9** | Promoter Polymorphism (rs187084, -1486T>C) | Nanocarrier Immunogenicity | Critical | Mandatory enzymatic CpG methylation (M.SssI + SAM) on scaffold prior to thermal annealing. |
| **AR** | Polyglutamine Elongation (CAG > 26) | Receptor Sensitivity | Warning | Elevate FtM minimum T to >= 22.0 nmol/L; tune aptamer Kd by -1.5 nM (tighter binding). |
| **COMT** | Val158Met (rs4680, Low Activity Met/Met) | Steroid Metabolism | Warning | Reduce clearance rate to 0.75x; monitor hepatic enzymes and oxidative catechol metabolites. |
| **UGT2B17** | Null CNV Deletion (*2/*2) | Steroid Metabolism | Warning | Reduce clearance rate to 0.60x; extend half-life tracking to prevent testosterone stacking. |
| **AR** | Polyglutamine Shortening (CAG < 20) | Receptor Sensitivity | Advisory | Moderate FtM target T (14.0 to 18.0 nmol/L); offset aptamer Kd by +0.5 nM. |
| **CYP1B1** | CYP1B1*3 (rs1056836, Rapid Hydroxylase) | Steroid Metabolism | Advisory | Increase clearance rate to 1.35x; monitor gonadotropin negative feedback suppression. |

### Mathematical Kinetic Recalibration

When genetic markers are detected or selected, the engine recalibrates the closed-loop mathematical kinetics:

1. **Clearance Multiplier Scaling**:
   The baseline exponential rate constant ($$k = 0.08\text{ h}^{-1}$$) is dynamically scaled by the patient's combined metabolic multiplier:
   $$k_{\text{eff}} = 0.08 \times \prod_i M_{\text{clearance}, i}$$

2. **Thrombosis Surge Damping**:
   If the aggregate thrombosis risk index exceeds 1.5, the initial burst release rate is damped by 15% ($$k_{\text{eff}} \times 0.85$$) to model continuous micro-infusion delivery and eliminate transient hyper-coagulation peaks.

3. **Epigenetic Stability Extension**:
   When enzymatic CpG methylation is mandated for TLR9 tolerance, the methylated DNA origami nanocarrier exhibits 40% higher resistance to human serum endonucleases, extending estimated in-vivo half-life ($$t_{1/2} \times 1.40$$).

---

## 72-Hour Dynamic Pharmacodynamics Simulation

The Hub includes a high-resolution 72-hour kinetics engine implemented via interactive HTML5 Canvas charting:

### Multi-Trace Dynamic Curves
* **Cyan Trace (E2)**: Displays circulating 17β-estradiol in pmol/L.
* **Magenta Trace (T)**: Displays circulating testosterone in nmol/L.
* **Amber / Green Trace (LH / FSH)**: Displays anterior pituitary gonadotropin signaling in IU/L.

### Closed-Loop Perturbation Testing
Using the interactive UI controls, researchers can simulate acute physiological perturbations:
* **Exogenous Hormone Bolus**: Injects an immediate pulse to observe how rapidly the closed-loop nanocarrier shuts down release.
* **Metabolic Clearance Spike**: Increases enzymatic hepatic clearance rates to verify whether the nanocarrier accelerates release to protect the target setpoint.

---

## 3D Double Helix & Anatomical Visualizations

### Interactive 3D B-DNA Helix Projection
The hub renders a real-time 3D mathematical projection of the DNA double helix directly on the sovereign HTML5 Canvas:
* Rotates smoothly along its longitudinal axis (36° rotation per base pair).
* Visualizes major and minor grooves, phosphate-sugar backbones, and complementary base pairs (A-T in cyan, G-C in magenta).
* Allows researchers to visually inspect where targeting aptamer loops are inserted into the staple arrangement.

### Anatomical Targeting Map
Provides an anatomical schema linking the nanocarrier's physical route:
1. **Intravenous / Subcutaneous Depot** →
2. **Capillary Fenestrations** →
3. **Pituitary Gonadotrope Binding** →
4. **Circulatory Systemic Stabilization**.

---

## Next Steps
* Learn about database persistence in [**Configuration & Sovereign Peering**](configuration-peers.md).
* Monitor microservice vitals in [**Stack Topology & Hardware Telemetry**](stack-services.md).
