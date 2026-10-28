# Phase P23: REST API Endpoints for Subject Grants & Cohort Group Management

## 1. Context & Architectural Decisions

- **Goal**: Expose authenticated, role-gated minimal API endpoints for managing direct subject grants, subject groups (cohorts), group memberships, and group grants.
- **Role Permissions**:
  - `Administrator`:
    - Full access to all endpoints.
    - Can grant/revoke subject access for any researcher.
    - Can grant/revoke group access for any researcher.
    - Can create, update, delete any group and add/remove any patient.
  - `Researcher`:
    - Can list their own granted subjects and groups (`GET /api/subject-access/my-subjects`, `GET /api/subject-groups`).
    - Can create groups containing **only** patients they currently have access to.
    - Can update/delete groups they created.
    - Can add/remove members in their own groups, but only adding patients they currently have access to.
    - Cannot grant subjects or groups to other researchers.
  - `Patient`:
    - Blocked from all `/api/subject-access` and `/api/subject-groups` endpoints (403 Forbidden).

---

## 2. Step-by-Step Implementation Instructions

### Step 2.1: Request & Response Contracts (`src/NanoWeaverHrt/Security/Access/SubjectAccessContracts.cs`)
Create DTO records:
- `GrantSubjectAccessRequest`: `ResearcherUsername`, `SubjectUsername`, `AccessLevel`, `Notes`.
- `CreateSubjectGroupRequest`: `Name`, `Description`, `SubjectUsernames`.
- `UpdateSubjectGroupRequest`: `Name`, `Description`.
- `AddGroupMemberRequest`: `SubjectUsername`.
- `GrantGroupAccessRequest`: `ResearcherUsername`, `AccessLevel`, `Notes`.
- OpenAPI example providers implementing `IExamplesProvider<T>`.

### Step 2.2: Endpoint Mapping in `Program.cs`
Append new endpoints at the **end** of `Program.Main` (right before `app.Run();`) to avoid renumbering existing state machines:
1. `GET /api/subject-access/grants` (Administrator only: list all grants; optional query params `researcher` and `subject`).
2. `POST /api/subject-access/grants` (Administrator only: grant access).
3. `DELETE /api/subject-access/grants/{researcherUsername}/{subjectUsername}` (Administrator only: revoke access).
4. `GET /api/subject-access/my-subjects` (Researcher or Administrator: returns list of accessible subject usernames with access levels).
5. `GET /api/subject-groups` (Administrator: all groups; Researcher: groups they created or have grant for).
6. `POST /api/subject-groups` (Administrator or Researcher: create group; validates member access).
7. `GET /api/subject-groups/{groupId}` (Administrator or authorized Researcher).
8. `PUT /api/subject-groups/{groupId}` (Administrator or group creator).
9. `DELETE /api/subject-groups/{groupId}` (Administrator or group creator).
10. `POST /api/subject-groups/{groupId}/members` (Administrator or group creator; validates subject access).
11. `DELETE /api/subject-groups/{groupId}/members/{subjectUsername}` (Administrator or group creator).
12. `GET /api/subject-groups/{groupId}/grants` (Administrator only).
13. `POST /api/subject-groups/{groupId}/grants` (Administrator only).
14. `DELETE /api/subject-groups/{groupId}/grants/{researcherUsername}` (Administrator only).

### Step 2.3: Route Role Declarations & OpenAPI Filters
- Declare route requirements in `RouteRoleRequirement` routing table.
- Provide descriptive summaries and response status codes (`200 OK`, `201 Created`, `400 BadRequest`, `403 Forbidden`, `404 NotFound`).

### Step 2.4: Comprehensive Integration Tests
Create `tests/NanoWeaverHrt.Tests/SubjectAccessEndpointsTests.cs`:
- Test every endpoint across Administrator, Researcher, and Patient callers.
- Verify that a Researcher cannot add a subject they don't have access to into a group.
- Verify that non-Administrators cannot call grant/revoke endpoints.
- Verify accurate problem details and status codes.
- 100.00% line & branch coverage.
