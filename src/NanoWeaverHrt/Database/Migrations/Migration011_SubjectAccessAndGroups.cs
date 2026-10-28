namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 011: Creates the database tables for subject access grants and cohort group memberships.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The database schema tables (<c>weaver_subject_grants</c>, <c>weaver_subject_groups</c>,
/// <c>weaver_subject_group_members</c>, and <c>weaver_subject_group_grants</c>) storing fine-grained researcher access delegations
/// and subject cohort memberships.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enforces access boundaries allowing Administrators to grant Researchers configurable
/// access tiers (ReadAccess: 1, GenomicAccess: 2, FullReadAccess: 3) over individual subjects or entire cohorts.
/// </para>
/// <para>
/// <strong>How it works:</strong> Applies relational tables with unique constraints, cascade deletions on group removal,
/// and check constraints governing valid access levels across SQLite and PostgreSQL engines.
/// </para>
/// </remarks>
public sealed class Migration011_SubjectAccessAndGroups : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 11;

    /// <inheritdoc/>
    public override string Name => "011_SubjectAccessAndGroups";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL for subject grants, cohort groups, group members, and group grants.
        // WHY:  Provides deterministic schema migration across SQLite and PostgreSQL storage backends.
        // HOW:  Uses TIMESTAMPTZ and foreign key references on PostgreSQL; TEXT timestamps and ON DELETE CASCADE on SQLite.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_subject_grants (
    id                  VARCHAR(64)  PRIMARY KEY,
    researcher_username VARCHAR(128) NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
    subject_username    VARCHAR(128) NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
    access_level        INTEGER      NOT NULL DEFAULT 2,
    granted_by          VARCHAR(128) NOT NULL REFERENCES weaver_users(username),
    granted_at_utc      TIMESTAMPTZ  NOT NULL,
    notes               TEXT,
    CONSTRAINT uq_subject_grant UNIQUE (researcher_username, subject_username),
    CONSTRAINT ck_weaver_subject_grants_level CHECK (access_level IN (1, 2, 3))
);

CREATE INDEX IF NOT EXISTS ix_weaver_subject_grants_researcher ON weaver_subject_grants(researcher_username);
CREATE INDEX IF NOT EXISTS ix_weaver_subject_grants_subject ON weaver_subject_grants(subject_username);

CREATE TABLE IF NOT EXISTS weaver_subject_groups (
    id             VARCHAR(64)  PRIMARY KEY,
    name           VARCHAR(128) NOT NULL,
    description    TEXT,
    created_by     VARCHAR(128) NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
    created_at_utc TIMESTAMPTZ  NOT NULL,
    updated_at_utc TIMESTAMPTZ  NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_weaver_subject_groups_created_by ON weaver_subject_groups(created_by);

CREATE TABLE IF NOT EXISTS weaver_subject_group_members (
    group_id         VARCHAR(64)  NOT NULL REFERENCES weaver_subject_groups(id) ON DELETE CASCADE,
    subject_username VARCHAR(128) NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
    added_at_utc     TIMESTAMPTZ  NOT NULL,
    PRIMARY KEY (group_id, subject_username)
);

CREATE INDEX IF NOT EXISTS ix_weaver_subject_group_members_subject ON weaver_subject_group_members(subject_username);

CREATE TABLE IF NOT EXISTS weaver_subject_group_grants (
    id                  VARCHAR(64)  PRIMARY KEY,
    group_id            VARCHAR(64)  NOT NULL REFERENCES weaver_subject_groups(id) ON DELETE CASCADE,
    researcher_username VARCHAR(128) NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
    access_level        INTEGER      NOT NULL DEFAULT 2,
    granted_by          VARCHAR(128) NOT NULL REFERENCES weaver_users(username),
    granted_at_utc      TIMESTAMPTZ  NOT NULL,
    notes               TEXT,
    CONSTRAINT uq_group_grant UNIQUE (group_id, researcher_username),
    CONSTRAINT ck_weaver_subject_group_grants_level CHECK (access_level IN (1, 2, 3))
);

CREATE INDEX IF NOT EXISTS ix_weaver_subject_group_grants_researcher ON weaver_subject_group_grants(researcher_username);
CREATE INDEX IF NOT EXISTS ix_weaver_subject_group_grants_group ON weaver_subject_group_grants(group_id);
";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_subject_grants (
    id                  TEXT PRIMARY KEY,
    researcher_username TEXT NOT NULL,
    subject_username    TEXT NOT NULL,
    access_level        INTEGER NOT NULL DEFAULT 2 CHECK (access_level IN (1, 2, 3)),
    granted_by          TEXT NOT NULL,
    granted_at_utc      TEXT NOT NULL,
    notes               TEXT,
    CONSTRAINT uq_subject_grant UNIQUE (researcher_username, subject_username)
);

CREATE INDEX IF NOT EXISTS ix_weaver_subject_grants_researcher ON weaver_subject_grants(researcher_username);
CREATE INDEX IF NOT EXISTS ix_weaver_subject_grants_subject ON weaver_subject_grants(subject_username);

CREATE TABLE IF NOT EXISTS weaver_subject_groups (
    id             TEXT PRIMARY KEY,
    name           TEXT NOT NULL,
    description    TEXT,
    created_by     TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_weaver_subject_groups_created_by ON weaver_subject_groups(created_by);

CREATE TABLE IF NOT EXISTS weaver_subject_group_members (
    group_id         TEXT NOT NULL,
    subject_username TEXT NOT NULL,
    added_at_utc     TEXT NOT NULL,
    PRIMARY KEY (group_id, subject_username),
    FOREIGN KEY (group_id) REFERENCES weaver_subject_groups(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_weaver_subject_group_members_subject ON weaver_subject_group_members(subject_username);

CREATE TABLE IF NOT EXISTS weaver_subject_group_grants (
    id                  TEXT PRIMARY KEY,
    group_id            TEXT NOT NULL,
    researcher_username TEXT NOT NULL,
    access_level        INTEGER NOT NULL DEFAULT 2 CHECK (access_level IN (1, 2, 3)),
    granted_by          TEXT NOT NULL,
    granted_at_utc      TEXT NOT NULL,
    notes               TEXT,
    CONSTRAINT uq_group_grant UNIQUE (group_id, researcher_username),
    FOREIGN KEY (group_id) REFERENCES weaver_subject_groups(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_weaver_subject_group_grants_researcher ON weaver_subject_group_grants(researcher_username);
CREATE INDEX IF NOT EXISTS ix_weaver_subject_group_grants_group ON weaver_subject_group_grants(group_id);
";
    }
}
