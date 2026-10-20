# DIY & HomeLab Benchtop Fabrication Guide (How-To)

> **Accessible, Step-by-Step Physical DNA Origami Synthesis for Independent Scientists, Community Labs, and Bio-Hackers**

This guide provides complete, practical benchtop instructions for physically synthesizing patient-targeted DNA origami nanocarriers in non-institutional environments, including community biology labs (hackerspaces) and home laboratories.

Experts with professional biochemistry training already understand molecular stoichiometry and micro-pipetting protocols. This guide is written specifically for software engineers, bio-hackers, and DIY enthusiasts who are new to wet-lab molecular fabrication.

![DIY & HomeLab Benchtop Fabrication Instructions](images/07_diy_fabrication_guide.png)

---

## 1. Safety & Biological Fundamentals

### Is This Safe for a Home Lab?
**Yes.** Scaffolded DNA origami operates under **Biosafety Level 1 (BSL-1)**.
* **Non-Infectious**: While the long scaffold strand originates from bacteriophage M13mp18, it is **purely synthetic single-stranded DNA** devoid of viral coat proteins, capsid envelopes, or reproductive machinery. It cannot replicate or infect mammalian cells.
* **Non-Toxic**: The building materials are natural nucleic acids (A, T, G, C), identical to the DNA found in food, yeast, and living cells.
* **Safe Reagents**: Unlike traditional molecular biology labs that use hazardous mutagens (e.g., ethidium bromide) and damaging UV radiation, our protocol utilizes non-toxic stains (GelRed, SYBR Safe, or methylene blue) and safe visible/blue-LED transilluminators.

### Aseptic Technique & Nuclease Defense
In computer science, a corrupted byte crashes a program; in DNA nanotechnology, ubiquitous airborne **DNase enzymes** (found in human skin flakes, saliva, and ambient dust) will chew up DNA backbones if precautions are not taken.
* **Wipe Down**: Clean your bench surface and pipette shafts with **10% household bleach** (10-minute contact time), followed by **70% isopropanol** or ethanol to remove bleach residue.
* **Barrier Tips**: Always use sterile, aerosol-resistant filter pipette tips (10 µL and 200 µL) to prevent aerosol contamination.
* **Wear Gloves**: Always wear fresh nitrile gloves. Never touch tube interiors with bare fingers.

---

## 2. Reagents & Equipment Sourcing (Institutional vs. DiY)

You do not need a university lab or a $50,000 capital budget to fold DNA origami. The table below lists standard laboratory reagents alongside accessible DIY alternatives:

| Material | Professional Research Source | DIY / Community Lab Alternative | Reaction Quantity | Biophysical Function |
| :--- | :--- | :--- | :--- | :--- |
| **M13mp18 ssDNA Scaffold** | New England Biolabs (`#N4040S`), Guild BioSciences, or Tilibit | Shared community aliquot or purified non-lytic M13 phage prep | 5.0 µL (100 nM stock) | The 7,249-nucleotide master backbone that is folded into shape. |
| **Synthetic Staple Oligos** | IDT (96-well plate, standard desalted, 25 nmol scale), Twist, or Eurofins | Pooled custom synthesis orders via open-science community syndicates | 5.0 µL (1.0 µM pool) | 150–200 short oligos (32–48 nt) bridging scaffold crossovers. |
| **10x Folding Buffer** | Commercial 10x TAE with 125 mM MgCl2 | Prepared from 50x TAE stock and molecular-grade MgCl2, sterile filtered (0.22 µm) | 5.0 µL per 50 µL fold | Supplies Mg²⁺ counter-ions to screen phosphate charge repulsion. |
| **Nuclease-Free Water** | Ambion / Invitrogen Nuclease-Free Water | Sterile double-distilled or deionized water, autoclaved / boiled in clean glass | 35.0 µL per 50 µL fold | Reaction solvent free of active DNase enzymes. |
| **Thermal Cycler** | Bio-Rad T100, Eppendorf Mastercycler | Open-source Arduino thermocycler (OpenPCR, NinjaPCR) or sous-vide water bath | 1 apparatus | Executes the 6-stage linear thermal annealing ramp. |
| **Agarose Powder** | Low-EEO analytical grade (Bio-Rad) | Community lab or food-grade high-purity agar (molecular grade preferred) | 0.75 g per 50 mL gel | Gel matrix for verifying folding success via electrophoretic shift. |
| **Gel Stain** | Biotium GelRed or SYBR Safe | Methylene blue (visible light) or GelGreen with blue LED illuminator | 1x in gel | Fluorescent visualizer for DNA bands without toxic ethidium bromide. |
| **Purification Filters** | Millipore Amicon Ultra-0.5 (100 kDa MWCO) | Filterless PEG-8000 precipitation (15% PEG-8000 + 500 mM NaCl) | 1 filter or PEG solution | Removes unincorporated free staple strands from folded carriers. |

---

## 3. Standard 50 µL Reaction Pipetting Recipe

Pipette components into a sterile 0.2 mL thin-walled PCR tube in the exact order shown below:

```
[Pipetting Sequence for 50 µL Reaction]
  1. Water (35.0 µL) ---------> Sterile PCR tube
  2. 10x Buffer (5.0 µL) -----> Mix gently
  3. M13mp18 Scaffold (5.0 µL) -> Add on ice
  4. Staple Pool (5.0 µL) ----> 10x stoichiometric excess
                                    |
                                    v
                     Gently flick tube 5 times (DO NOT VORTEX!)
```

| Order | Component Name | Stock Concentration | Volume (µL) | Final Concentration | Handling Tip |
| :---: | :--- | :--- | :---: | :--- | :--- |
| **1** | Ultra-Pure Nuclease-Free ddH2O | Molecular Grade | **35.0 µL** | Balance to 50 µL | Pipette first to establish base liquid volume. |
| **2** | 10x Folding Buffer (10x TAE + 125 mM MgCl2) | 10x concentrate | **5.0 µL** | 1x TAE + 12.5 mM MgCl2 | Pipette up and down to ensure magnesium ions disperse evenly. |
| **3** | M13mp18 ssDNA Scaffold (7,249 nt) | 100 nM stock | **5.0 µL** | 10 nM final scaffold | Keep on ice. **NEVER vortex** the viral scaffold! |
| **4** | Synthetic Master Staple Pool | 1.0 µM per oligo | **5.0 µL** | 100 nM per staple (10x excess) | Excess staple concentration drives complete thermodynamic folding. |
| **TOTAL** | **Complete Reaction Mixture** | — | **50.0 µL** | Scalable (double all volumes for 100 µL) | Ready for thermal cycling. |

---

## 4. The 7-Step Physical Benchtop Protocol

### Step 1: Reconstitution of Lyophilized Oligo Staples
1. Before opening any oligo tubes, **pulse-spin** the tubes or 96-well plate at 4,000 RPM for 30 seconds in a benchtop centrifuge. This forces dried DNA pellets clinging to the tube walls down to the bottom.
2. Add nuclease-free water or 1x TE buffer (10 mM Tris, 0.1 mM EDTA, pH 8.0) according to the manufacturer's spec sheet to achieve an individual stock concentration of **100 µM** for each staple.
3. Allow the pellets to rehydrate at room temperature for 15 minutes. Pulse-vortex for 5 seconds and spin down briefly.
> [!TIP]
> **HomeLab Freezer Advice**: Store individual 100 µM stocks at -20°C in a standard, manual-defrost freezer. Avoid frost-free household freezers, which cycle through warming phases that degrade oligonucleotides.

### Step 2: Equimolar Staple Master Pool Assembly
1. Pipette equal volumes (e.g., 5.0 µL each) of every individual 100 µM staple stock into a single sterile 1.5 mL microcentrifuge tube.
2. Mix thoroughly by pipetting.
3. Dilute this pool with nuclease-free water to produce an **equimolar working staple pool** where each individual staple oligonucleotide is at a working concentration of **1.0 µM**.
4. Label the tube: `MASTER STAPLE POOL (1.0 µM)`. This master pool provides enough volume for over 50 folding reactions.

### Step 3: Reaction Assembly & Stoichiometric Loading
1. Label a clean 0.2 mL thin-walled PCR tube.
2. Using sterile barrier tips, add reagents in order:
   * 35.0 µL water
   * 5.0 µL 10x folding buffer
   * 5.0 µL M13mp18 scaffold
   * 5.0 µL Master Staple Pool
3. **Mix by gently flicking the tube bottom 5 times**. Pulse-spin in a micro-centrifuge for 3 seconds.
> [!CAUTION]
> **NEVER VORTEX THE ASSEMBLED REACTION MIXTURE!**
> High-speed vortexing generates intense hydrodynamic shear forces that can physically nick and break the 7.2 kb single-stranded viral scaffold.

### Step 4: Precision Thermal Annealing Ramp Execution
Place the reaction tube into your thermal cycler and execute the 6-stage program:

| Stage | Target Temperature | Hold Duration | Ramp Rate | Biophysical Molecular Action |
| :---: | :---: | :---: | :---: | :--- |
| **1** | 90.0°C | 5 min | 2.0°C/min | **Denaturation**: Melts all pre-existing secondary hairpins in the viral scaffold. |
| **2** | 65.0°C | 15 min | 0.5°C/min | **Scaffold Nucleation**: Longest staple crossovers begin recognizing complementary domains. |
| **3** | 55.0°C | 60 min | **0.1°C/min** | **Critical Core Folding**: Core lattice crossovers assemble cooperatively without kinetic traps. |
| **4** | 40.0°C | 30 min | 0.2°C/min | **Aptamer Latch Hybridization**: Terminal aptamer pockets and targeting loops zip into place. |
| **5** | 20.0°C | 10 min | 0.5°C/min | **Ambient Stabilization**: Reaches room-temperature equilibrium. |
| **6** | 4.0°C | ∞ | 1.0°C/min | **Quench & Storage**: Stable indefinitely in buffer. |

> [!TIP]
> **Sous-Vide / OpenPCR Fallback**: If a commercial thermal cycler is unavailable, an insulated sous-vide water cooker or an open-source Arduino OpenPCR machine can execute this cooling schedule with high thermal stability.

### Step 5: Agarose Gel Electrophoresis Quality Control
To verify that your nanocarrier folded correctly rather than forming a clump of tangled aggregate:
1. Cast a **1.5% wt/vol agarose gel** in 0.5x TBE buffer supplemented with **11.0 mM MgCl2**. Add 1x GelRed or SYBR Safe directly into the molten gel before pouring.
2. Mix 5.0 µL of unheated folded origami sample with 1.0 µL of 6x glycerol loading dye. **Do NOT use dyes containing SDS or EDTA**, as EDTA will chelate the magnesium counter-ions and denature the origami!
3. Submerge the gel in 0.5x TBE running buffer containing **11.0 mM MgCl2**.
4. Run at 60–70 V for 2.5 hours placed on an ice bath.

> [!IMPORTANT]
> **The Magnesium Invariant Rule**: Both the agarose gel **AND** the running buffer **MUST contain 11 mM MgCl2**. Without magnesium, the negative charges of the DNA backbone will repel each other, causing the folded nanocarrier to immediately dissociate and disintegrate during electrophoresis.

### Step 6: Centrifugal Ultrafiltration Purification
To wash away excess unhybridized staple strands:
1. Transfer the 50 µL reaction into an **Amicon Ultra-0.5 mL (100 kDa MWCO)** spin column.
2. Add 400 µL of 1x TAE + 12.5 mM MgCl2 buffer.
3. Centrifuge at 6,000 × g for 5 minutes. Discard the flow-through (which contains the small, unincorporated ~12 kDa staples).
4. Repeat this washing step 2 additional times.
5. Invert the column into a clean collection tube and centrifuge at 1,000 × g for 2 minutes to recover purified, concentrated DNA origami nanocarriers.

#### Free Filterless Alternative: PEG Precipitation
If spin filters are unavailable, you can purify without columns:
1. Add an equal volume (50 µL) of PEG buffer (15% wt/vol PEG-8000, 500 mM NaCl, 1x TAE).
2. Incubate at room temperature for 30 minutes.
3. Centrifuge at 16,000 × g for 25 minutes. Free staples remain in the supernatant; the large DNA origami pellets as a translucent smear.
4. Remove supernatant and gently resuspend the pellet in 50 µL of 1x TAE + 12.5 mM MgCl2.

### Step 7: Safe Storage & Handling
* **Temperature**: Store in sterile polypropylene tubes at **4°C in the dark**. In the presence of 12.5 mM MgCl2, DNA origami remains structurally intact for over **12 months**.
* **Do NOT Freeze**: Never freeze folded DNA origami nanocarriers at -20°C without adding 10%–20% glycerol or trehalose cryoprotectant. Rapid water crystallization tears the delicate nanoscale crossover lattice apart.
* **In-Vitro Dilution**: When dosing cell cultures or assay buffers, ensure that the final Mg²⁺ concentration remains above 5 mM to maintain structural stability.

---

## 5. How to Read Your Agarose Quality Control Gel

```
       Loading Wells:   [ Lane 1 ]       [ Lane 2 ]       [ Lane 3 ]
                        Scaffold (ss)    Folded Origami   Failed / Aggregate
                             |                |                 |
     Top of Gel ----------   |                |            [Heavy smear in well]
                             |                |                 |
        M13mp18 (7.2 kb) ->  =================                  |
                             (Loose/Floppy)   |                 |
                                              |                 |
   Folded Origami (3D) --->                   ================= (Trailing smear)
                                              (Sharp Compact)
                                                                |
                                                                |
        Free Staples ----->                   .................
                                              (Fast Smear)
```

1. **Successful Fold (Lane 2)**:
   * Features a **sharp, mono-disperse band** that migrates significantly **faster** through the agarose than the unhybridized 7.2 kb single-stranded M13mp18 scaffold control (Lane 1).
   * *Why?* Although the folded origami has greater molecular mass (scaffold + staples ≈ 4.8 MDa), its dense, compact 3D geometry slips through agarose pores much faster than a floppy, random-coil single strand.
2. **Excess Staple Band**:
   * Appears as a faint, diffuse smear near the dye front (~30–50 bp). This confirms that staples were provided in excess to drive complete thermodynamic assembly.
3. **Failed Fold / Aggregation (Lane 3)**:
   * If you observe a broad smear trailing down the lane or bright fluorescence stuck inside the loading well, the nanocarrier aggregated. This is almost always caused by:
     * Insufficient Mg²⁺ in the folding buffer or gel running buffer (< 8 mM).
     * Cooling too quickly through the critical 65°C → 55°C folding window (> 0.5°C/min).

---

## Next Steps
* Walk through the end-to-end software pipeline in [**Guided DNA Workflow Wizard**](workflow-wizard.md).
* Review endocrine setpoints in [**Endocrine Hub & Pharmacodynamics**](endocrine-hub.md).
* Explore complete recipes in [**Example Biomedical Workflows**](example-workflows.md).
