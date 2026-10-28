# Phase P24: Unified Cohorts & Subject Access Management in Sovereign Web UI

## 1. Context & Architectural Decisions

- **Goal**: Implement unified Cohort & Subject Access Management in the Blazor Web UI, accessible from both `DnaManagementPage.razor` and `UserManagement.razor`, with cohort filtering in the patient selection toolbar.
- **UI Architecture**:
  - **Shared Component / View (`CohortManagementView.razor`)**:
    - Manage Subject Groups (Cohorts): list cohorts, view members, create new cohort from accessible patients, edit name/description, add/remove member subjects.
    - View grants: Administrators can inspect and manage researcher access grants per cohort.
  - **`UserManagement.razor` Enhancements**:
    - Add "Subject Access Grants" management modal/section allowing Administrators to:
      - Grant a Researcher access to specific Patients (with access level dropdown: `ReadAccess: 1`, `GenomicAccess: 2`, `FullReadAccess: 3`).
      - Grant a Researcher access to a Cohort Group.
      - Revoke existing grants.
    - Quick link to the Cohorts management interface.
  - **`DnaManagementPage.razor` Enhancements**:
    - When a Researcher is logged in, the "Selected Patient" dropdown filters strictly to patients they have access to (`GenomicAccess` or higher).
    - Add "Cohort Filter" dropdown to filter the patient list by cohort group.
    - Provide a button to launch the Cohorts management interface.

---

## 2. Step-by-Step Implementation Instructions

### Step 2.1: Reusable Cohort Management Component (`CohortManagementModal.razor` or `CohortManagementView.razor`)
Create `src/NanoWeaverHrt/Components/Cohorts/CohortManagementView.razor`:
- Displays cohort cards with member count, creator, and updated date.
- Form to create a new cohort with name, description, and multi-select for accessible patients.
- Details modal showing member list with add/remove subject actions.
- If Administrator: shows group grant tab with ability to grant/revoke researcher access to the group.
- Complete XML doc comments and high-contrast dark theme styling conforming to `docs/assets/sovereign-theme.css`.

### Step 2.2: Update `UserManagement.razor`
- Add action buttons on user table rows (for Researcher rows): "🔑 Manage Subject Grants".
- Modal to view researcher's current subject and group grants, and grant new access with access level selection (`ReadAccess`, `GenomicAccess`, `FullReadAccess`).
- Embed link or tab to Cohort management.

### Step 2.3: Update `DnaManagementPage.razor`
- In `InitializeViewAsync()`:
  - Query accessible patients via `ISubjectAccessService` (or repository).
  - If Researcher: populate `_patients` strictly with accessible patients. If researcher has access to 0 patients, display a helpful informative alert explaining they need an Administrator grant.
  - If Administrator: retain access to all patients.
- Add Cohort selector dropdown next to the Patient selector dropdown:
  - Filters `_patients` list to members of the selected cohort.

### Step 2.4: bUnit Component & Verification Tests
Create `tests/NanoWeaverHrt.Tests/CohortManagementComponentTests.cs`:
- Test Administrator rendering of grant controls and cohort creation.
- Test Researcher rendering (restricted to accessible patients, cannot grant to other researchers).
- Test Patient selection and filtering by cohort in `DnaManagementPage`.
- 100.00% line & branch coverage across component code.
