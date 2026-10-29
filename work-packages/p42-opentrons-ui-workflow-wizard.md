# Work Package P42: Wet-Lab Liquid Handler UI Integration in Workflow Wizard

## 1. Context & Objective
- **Problem Statement**:
  Researchers using the sovereign Web UI's Guided DNA Origami Workflow Wizard (`/wizard` or `WorkflowWizard.razor`) can generate staple routing tables, stoichiometric ratios, and annealing schedules, but currently have no visual mechanism to configure robot hardware, preview the generated Opentrons script, or trigger a 1-click download of the `.py` automation file.
- **Goal & Scope**:
  1. Extend `src/NanoWeaverHrt/Components/Pages/WorkflowWizard.razor` (and accompanying modals/subcomponents).
  2. Add an "Automate with Opentrons" section in Stage 5 ("Thermal Annealing & Stoichiometry") and Stage 6 ("Bench Synthesis Manifest"):
     - Robot Model Selector: Opentrons OT-2 vs. Opentrons Flex.
     - Pipette Model Selector: Single-channel vs. Multi-channel.
     - Labware Plate Selector: 96-well vs. 384-well plates.
     - On-Deck Hardware Checkboxes: Temperature Module (4°C hold), Thermocycler Module (on-deck ramp).
  3. Provide an interactive Python Script Preview modal with 1-click "Download Protocol (.py)" and "Copy to Clipboard".
  4. Display deck layout visualization summary (Deck Slots, Tip Racks needed, Source Wells, Estimated Runtime).
  5. Synchronize `ExcludeByFile` rules or author bUnit component tests in `tests/NanoWeaverHrt.Tests/Components/WorkflowWizardTests.cs`.
- **Target Role / Model**: `coder` / `mechanic` (`flash`)
- **Transport Boundary**: Blazor Interactive Server UI (`/wizard`)

---

## 2. Technical Contracts & Invariants

### UI Contract
- Component: `src/NanoWeaverHrt/Components/Pages/WorkflowWizard.razor`
- Injected Dependencies:
  - `IOpentronsProtocolGenerator OpentronsGenerator`
  - `IJSRuntime JSRuntime`
- State Properties:
  - `OpentronsProtocolOptions ActiveOpentronsOptions`
  - `OpentronsProtocolResult? CurrentOpentronsResult`
  - `bool IsOpentronsModalOpen`

### Invariants
1. **High-Contrast Dark Theme Compliance**: Must utilize sovereign CSS classes (`card`, `btn-primary`, `sovereign-badge`, etc.), matching `docs/assets/sovereign-theme.css`. Zero arbitrary un-themed inline styles.
2. **Keyboard Accessibility**: Modal must support `Esc` to close and focus trapping.
3. **Download Interop**: Script download utilizes client-side blob download via zero-dependency helper in `weaver-session.js` / `weaver-visualizer.js`.
4. **Coverage Invariant**: If added to `ExcludeByFile` in `coverlet.runsettings`, must also be updated in `coverage-full.runsettings` to keep ratchet metrics synchronized.

---

## 3. Step-by-Step Implementation Tasks
1. Update `WorkflowWizard.razor` to add the Opentrons configuration card in Stage 5/6.
2. Add modal dialog markup for Python code preview with syntax-highlighted block and action buttons.
3. Wire event handlers for options changes and script generation.
4. Add client-side download helper if needed or leverage existing blob download mechanics.
5. Author bUnit tests asserting:
   - Selecting Opentrons options updates the displayed deck summary.
   - Clicking "Generate Opentrons Protocol" populates the script preview.
   - Modal opens and closes cleanly.
6. Verify with `dotnet test tests/NanoWeaverHrt.Tests`.

---

## 4. Verification Gate & Acceptance Criteria
- [ ] Builds with zero warnings (`/warnaserror`).
- [ ] Pre-flight gate step 4 and step 7 pass cleanly.
- [ ] UI is responsive and accessible in Chrome/Firefox dark mode.
- [ ] 100% XML docblocks on all component code-behind methods.
