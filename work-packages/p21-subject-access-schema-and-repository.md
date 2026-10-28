# Phase P21: Database Schema & Repository Layer for Subject Access & Cohort Groups

## 1. Context & Architectural Decisions

- **Goal**: Implement deterministic database schema migration and repository access layer enabling Administrators to grant Researchers access to Patients/Subjects and Subject Groups (Cohorts), with configurable access levels.
- **Access Level Tiering**:
  - `SubjectAccessLevel.ReadAccess` (1): Demographic & clinical profile, pronouns, regimen (read-only).
  - `SubjectAccessLevel.GenomicAccess` (2): Level 1 + digitized DNA origami blueprints, sequence imports, simulation & analytical results (read and synthesis).
  - `SubjectAccessLevel.FullReadAccess` (3): Level 2 + patient delivery addresses and outreach emergency contacts (read-only).
- **Migration**: `Migration011_SubjectAccessAndGroups.cs` implementing `IDatabaseMigration` across both SQLite and PostgreSQL dialects.
- **Tables**:
  - `weaver_subject_grants`: Direct researcher-to-subject access grants with access level and granter audit trail.
  - `weaver_subject_groups`: Named subject cohorts created by Administrators or Researchers.
  - `weaver_subject_group_members`: Mapping of subjects to groups.
  - `weaver_subject_group_grants`: Group-level access grants to Researchers with access level.
- **Repository Interface**: `ISubjectAccessRepository` registered as a singleton with `IWeaverDbConnectionFactory`.

---

## 2. Step-by-Step Implementation Instructions

### Step 2.1: Domain Models (`src/NanoWeaverHrt/Security/Access/`)
Create the following domain types:
1. `SubjectAccessLevel`: Enum `ReadAccess = 1`, `GenomicAccess = 2`, `FullReadAccess = 3`.
2. `SubjectGrant`: Record with `Id`, `ResearcherUsername`, `SubjectUsername`, `AccessLevel`, `GrantedBy`, `GrantedAtUtc`, `Notes`.
3. `SubjectGroup`: Record with `Id`, `Name`, `Description`, `CreatedBy`, `CreatedAtUtc`, `UpdatedAtUtc`, `MemberCount`.
4. `SubjectGroupGrant`: Record with `Id`, `GroupId`, `ResearcherUsername`, `AccessLevel`, `GrantedBy`, `GrantedAtUtc`, `Notes`.

Every model and property must have complete XML doc comments (`/// <summary>`) in accordance with Prime Directive 2.

### Step 2.2: Migration 011 (`Migration011_SubjectAccessAndGroups.cs`)
Create `src/NanoWeaverHrt/Database/Migrations/Migration011_SubjectAccessAndGroups.cs` inheriting `DatabaseMigrationBase`:
- Version: `11`
- Description: `"Subject access grants and cohort group membership schema"`
- SQL DDL for SQLite:
  ```sql
  CREATE TABLE IF NOT EXISTS weaver_subject_grants (
      id TEXT PRIMARY KEY,
      researcher_username TEXT NOT NULL,
      subject_username TEXT NOT NULL,
      access_level INTEGER NOT NULL DEFAULT 2,
      granted_by TEXT NOT NULL,
      granted_at_utc TEXT NOT NULL,
      notes TEXT,
      CONSTRAINT uq_subject_grant UNIQUE (researcher_username, subject_username)
  );
  CREATE INDEX IF NOT EXISTS ix_weaver_subject_grants_researcher ON weaver_subject_grants(researcher_username);
  CREATE INDEX IF NOT EXISTS ix_weaver_subject_grants_subject ON weaver_subject_grants(subject_username);

  CREATE TABLE IF NOT EXISTS weaver_subject_groups (
      id TEXT PRIMARY KEY,
      name TEXT NOT NULL,
      description TEXT,
      created_by TEXT NOT NULL,
      created_at_utc TEXT NOT NULL,
      updated_at_utc TEXT NOT NULL
  );
  CREATE INDEX IF NOT EXISTS ix_weaver_subject_groups_created_by ON weaver_subject_groups(created_by);

  CREATE TABLE IF NOT EXISTS weaver_subject_group_members (
      group_id TEXT NOT NULL,
      subject_username TEXT NOT NULL,
      added_at_utc TEXT NOT NULL,
      PRIMARY KEY (group_id, subject_username),
      FOREIGN KEY (group_id) REFERENCES weaver_subject_groups(id) ON DELETE CASCADE
  );
  CREATE INDEX IF NOT EXISTS ix_weaver_subject_group_members_subject ON weaver_subject_group_members(subject_username);

  CREATE TABLE IF NOT EXISTS weaver_subject_group_grants (
      id TEXT PRIMARY KEY,
      group_id TEXT NOT NULL,
      researcher_username TEXT NOT NULL,
      access_level INTEGER NOT NULL DEFAULT 2,
      granted_by TEXT NOT NULL,
      granted_at_utc TEXT NOT NULL,
      notes TEXT,
      CONSTRAINT uq_group_grant UNIQUE (group_id, researcher_username),
      FOREIGN KEY (group_id) REFERENCES weaver_subject_groups(id) ON DELETE CASCADE
  );
  CREATE INDEX IF NOT EXISTS ix_weaver_subject_group_grants_researcher ON weaver_subject_group_grants(researcher_username);
  CREATE INDEX IF NOT EXISTS ix_weaver_subject_group_grants_group ON weaver_subject_group_grants(group_id);
  ```
- SQL DDL for PostgreSQL:
  Tailored with `VARCHAR(64)`, `VARCHAR(128)`, `TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')`, foreign key cascade constraints against `weaver_users(username)`.

### Step 2.3: Repository Interface & Implementation (`ISubjectAccessRepository.cs`, `SubjectAccessRepository.cs`)
1. Create `src/NanoWeaverHrt/Security/Access/ISubjectAccessRepository.cs`.
2. Create `src/NanoWeaverHrt/Security/Access/SubjectAccessRepository.cs`:
   - Inject `IWeaverDbConnectionFactory`.
   - Implement asynchronous database operations for grants, groups, members, and group grants.
   - Use `{ await using ... }` blocks to prevent async state-machine disposal leaks.
   - Parse timestamps using `WeaverDbValues.ReadTimestamp` and `CultureInfo.InvariantCulture`.
3. Register `ISubjectAccessRepository` as a singleton in `Program.cs`.

### Step 2.4: Comprehensive Unit & Integration Tests
Create `tests/NanoWeaverHrt.Tests/SubjectAccessRepositoryTests.cs`:
- Test all CRUD methods on `SubjectAccessRepository` against isolated SQLite test databases.
- Test unique constraint handling, cascade deletions, and member additions/removals.
- Test access level persistence and query filters.
- Achieve 100.00% line and branch coverage across new repository classes.
