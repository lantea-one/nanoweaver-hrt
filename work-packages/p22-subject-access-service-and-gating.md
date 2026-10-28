# Phase P22: Subject Access Control Service, Middleware Gating & Cryptographic Audit Ledger

## 1. Context & Architectural Decisions

- **Goal**: Implement high-throughput `ISubjectAccessService` evaluating tiered access rights for Researchers, integrate resolution into minimal-API middleware and route handlers, and log all authorization lifecycle events to `weaver_audit_ledger`.
- **Configurable Access Resolution**:
  - Direct grant access level: `SubjectGrant.AccessLevel`.
  - Group grant access level: `SubjectGroupGrant.AccessLevel` for all groups containing the subject.
  - Creator access: A Researcher who created a group has implicit access to member subjects (at least at their granted level).
  - Effective Access Level = `Max(DirectGrantLevel, Max(GroupGrantLevels))` across all applicable grants.
  - If effective access level >= Required Level: Access is `Allowed`.
  - Otherwise: Mask as `404 SubjectRecordNotFoundDetail` to prevent subject enumeration (Prime Directive 14).
- **Route Tiers**:
  - `SubjectAccessLevel.ReadAccess` (1):
    - `GET /api/users/{username}/profile`
  - `SubjectAccessLevel.GenomicAccess` (2):
    - `GET /api/patient/dna`
    - `GET /api/patients/{username}/dna` & `POST /api/patients/{username}/dna`
    - `GET /api/patients/{username}/imports` & `POST /api/patients/{username}/imports`
    - `GET /api/patients/{username}/results` & `POST /api/patients/{username}/results`
  - `SubjectAccessLevel.FullReadAccess` (3):
    - `GET /api/users/{username}/addresses`
    - `GET /api/users/{username}/contacts`
  - Modification of patient profile, addresses, or contacts remains restricted to `Administrator` or the patient (`isSelf`).

---

## 2. Step-by-Step Implementation Instructions

### Step 2.1: `ISubjectAccessService` & `SubjectAccessService`
Create `src/NanoWeaverHrt/Security/Access/ISubjectAccessService.cs` and `SubjectAccessService.cs`:
- Methods:
  - `Task<SubjectAccessResult> EvaluateAccessAsync(string callerUsername, UserRole callerRole, string targetSubjectUsername, SubjectAccessLevel requiredLevel, CancellationToken ct = default)`
  - `Task<IReadOnlyList<string>> GetAccessibleSubjectUsernamesAsync(string callerUsername, UserRole callerRole, SubjectAccessLevel minimumLevel = SubjectAccessLevel.ReadAccess, CancellationToken ct = default)`
  - `Task<bool> CanManageGroupAsync(string callerUsername, UserRole callerRole, string groupId, CancellationToken ct = default)`
  - `Task<bool> ValidateCanAddSubjectsToGroupAsync(string callerUsername, UserRole callerRole, IEnumerable<string> subjectUsernames, CancellationToken ct = default)`
- In-memory caching:
  - Cache effective access lookups using high-performance concurrent collections or eviction tokens on grant modifications.

### Step 2.2: Cryptographic Audit Ledger Persistence
Integrate all grant/group changes with `IComplianceAuditService.LogAction`:
- Action tags:
  - `SUBJECT_ACCESS_GRANTED`: logs granter, researcher, subject, access level.
  - `SUBJECT_ACCESS_REVOKED`: logs revoker, researcher, subject.
  - `SUBJECT_GROUP_CREATED`: logs creator, group id, name, initial member count.
  - `SUBJECT_GROUP_UPDATED`: logs updater, group id, name.
  - `SUBJECT_GROUP_DELETED`: logs deleter, group id.
  - `SUBJECT_GROUP_MEMBER_ADDED`: logs group id, subject added.
  - `SUBJECT_GROUP_MEMBER_REMOVED`: logs group id, subject removed.
  - `GROUP_ACCESS_GRANTED`: logs granter, researcher, group id, access level.
  - `GROUP_ACCESS_REVOKED`: logs revoker, researcher, group id.

### Step 2.3: Middleware & Route Handler Integration in `Program.cs`
Update `Program.cs`:
- Register `ISubjectAccessService` as a singleton.
- Update subject route gating middleware:
  - When caller is a Researcher, invoke `ISubjectAccessService.EvaluateAccessAsync` with the required level for the requested endpoint.
  - If access level is satisfied: proceed.
  - If denied: return `404 StatusCodes.Status404NotFound` with `SubjectRecordNotFoundDetail`.
- Update `/api/patient/dna` endpoint to permit Researchers who have `GenomicAccess` (Level 2) to the specified `patientId`.

### Step 2.4: Comprehensive Unit & Integration Tests
Create `tests/NanoWeaverHrt.Tests/SubjectAccessServiceTests.cs` and `tests/NanoWeaverHrt.Tests/SubjectAccessMiddlewareTests.cs`:
- Test access level hierarchy (Level 1 cannot access Level 2 endpoints; Level 2 can access Level 1 & 2; Level 3 can access all).
- Test group grant inheritance (adding a patient to a granted group immediately expands access).
- Test audit ledger emission for all access changes.
- Test RFC 7807 problem details (zero disclosure of non-existent vs unauthorized patients).
- 100.00% line & branch coverage.
