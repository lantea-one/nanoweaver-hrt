using System.Data.Common;
using NanoWeaverHrt.Database.Access;

namespace NanoWeaverHrt.Security.Access;

/// <summary>
/// Database repository implementing <see cref="ISubjectAccessRepository"/> on top of <see cref="IWeaverDbConnectionFactory"/>.
/// </summary>
public sealed class SubjectAccessRepository : ISubjectAccessRepository
{
    /// <summary>
    /// Database connection factory supplying open sessions and dialect primitives.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubjectAccessRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The database connection factory.</param>
    public SubjectAccessRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SubjectGrant>> ListSubjectGrantsAsync(string? researcherUsername = null, string? subjectUsername = null, CancellationToken ct = default)
    {
        // WHAT: Lists direct subject grants matching optional researcher and subject filters.
        // WHY:  Allows administrators and researchers to inspect active direct grants.
        // HOW:  Constructs parameterized query and maps rows to SubjectGrant instances.
        List<SubjectGrant> grants = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            string sql = "SELECT id, researcher_username, subject_username, access_level, granted_by, granted_at_utc, notes FROM weaver_subject_grants WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(researcherUsername))
            {
                sql += " AND researcher_username = @res";
            }
            if (!string.IsNullOrWhiteSpace(subjectUsername))
            {
                sql += " AND subject_username = @subj";
            }
            sql += " ORDER BY granted_at_utc DESC";

            await using DbCommand cmd = session.CreateCommand(sql);
            if (!string.IsNullOrWhiteSpace(researcherUsername))
            {
                WeaverDbParameters.Add(cmd, "@res", researcherUsername.Trim().ToLowerInvariant());
            }
            if (!string.IsNullOrWhiteSpace(subjectUsername))
            {
                WeaverDbParameters.Add(cmd, "@subj", subjectUsername.Trim().ToLowerInvariant());
            }

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                grants.Add(MapSubjectGrant(reader));
            }
        }
        return grants;
    }

    /// <inheritdoc/>
    public async Task<SubjectGrant?> GetSubjectGrantAsync(string researcherUsername, string subjectUsername, CancellationToken ct = default)
    {
        // WHAT: Retrieves a specific researcher-subject grant pair.
        // WHY:  Used during access evaluation and grant verification.
        // HOW:  Queries weaver_subject_grants by unique compound key.
        ArgumentNullException.ThrowIfNull(researcherUsername);
        ArgumentNullException.ThrowIfNull(subjectUsername);

        SubjectGrant? result = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                "SELECT id, researcher_username, subject_username, access_level, granted_by, granted_at_utc, notes FROM weaver_subject_grants WHERE researcher_username = @res AND subject_username = @subj");
            WeaverDbParameters.Add(cmd, "@res", researcherUsername.Trim().ToLowerInvariant());
            WeaverDbParameters.Add(cmd, "@subj", subjectUsername.Trim().ToLowerInvariant());

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                result = MapSubjectGrant(reader);
            }
        }
        return result;
    }

    /// <inheritdoc/>
    public async Task<bool> SaveSubjectGrantAsync(SubjectGrant grant, CancellationToken ct = default)
    {
        // WHAT: Upserts a direct subject access grant record.
        // WHY:  Creates or updates researcher permissions with idempotent ON CONFLICT DO UPDATE.
        // HOW:  Binds columns and executes non-query command.
        ArgumentNullException.ThrowIfNull(grant);

        int affected;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(@"
INSERT INTO weaver_subject_grants (id, researcher_username, subject_username, access_level, granted_by, granted_at_utc, notes)
VALUES (@id, @res, @subj, @level, @by, @at, @notes)
ON CONFLICT (researcher_username, subject_username) DO UPDATE SET
    access_level = excluded.access_level,
    granted_by = excluded.granted_by,
    granted_at_utc = excluded.granted_at_utc,
    notes = excluded.notes");

            WeaverDbParameters.Add(cmd, "@id", grant.Id);
            WeaverDbParameters.Add(cmd, "@res", grant.ResearcherUsername.Trim().ToLowerInvariant());
            WeaverDbParameters.Add(cmd, "@subj", grant.SubjectUsername.Trim().ToLowerInvariant());
            WeaverDbParameters.Add(cmd, "@level", (int)grant.AccessLevel);
            WeaverDbParameters.Add(cmd, "@by", grant.GrantedBy.Trim().ToLowerInvariant());
            WeaverDbParameters.Add(cmd, "@at", session.Dialect.TimestampValue(grant.GrantedAtUtc));
            WeaverDbParameters.Add(cmd, "@notes", (object?)grant.Notes ?? DBNull.Value);

            affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return affected >= 1;
    }

    /// <inheritdoc/>
    public async Task<bool> RevokeSubjectGrantAsync(string researcherUsername, string subjectUsername, CancellationToken ct = default)
    {
        // WHAT: Deletes a direct subject grant.
        // WHY:  Revokes researcher access to the specified subject.
        // HOW:  Executes DELETE against unique researcher-subject pair.
        ArgumentNullException.ThrowIfNull(researcherUsername);
        ArgumentNullException.ThrowIfNull(subjectUsername);

        int affected;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                "DELETE FROM weaver_subject_grants WHERE researcher_username = @res AND subject_username = @subj");
            WeaverDbParameters.Add(cmd, "@res", researcherUsername.Trim().ToLowerInvariant());
            WeaverDbParameters.Add(cmd, "@subj", subjectUsername.Trim().ToLowerInvariant());

            affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return affected >= 1;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SubjectGroup>> ListGroupsAsync(string? createdBy = null, CancellationToken ct = default)
    {
        // WHAT: Lists all cohort groups with member counts, filtered optionally by creator.
        // WHY:  Powers cohort views for administrators and researchers.
        // HOW:  Selects from weaver_subject_groups with correlated subquery for member_count.
        List<SubjectGroup> groups = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            string sql = @"
SELECT g.id, g.name, g.description, g.created_by, g.created_at_utc, g.updated_at_utc,
       (SELECT COUNT(*) FROM weaver_subject_group_members m WHERE m.group_id = g.id) AS member_count
FROM weaver_subject_groups g WHERE 1=1";

            if (!string.IsNullOrWhiteSpace(createdBy))
            {
                sql += " AND g.created_by = @by";
            }
            sql += " ORDER BY g.created_at_utc DESC";

            await using DbCommand cmd = session.CreateCommand(sql);
            if (!string.IsNullOrWhiteSpace(createdBy))
            {
                WeaverDbParameters.Add(cmd, "@by", createdBy.Trim().ToLowerInvariant());
            }

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                groups.Add(MapSubjectGroup(reader));
            }
        }
        return groups;
    }

    /// <inheritdoc/>
    public async Task<SubjectGroup?> GetGroupAsync(string groupId, CancellationToken ct = default)
    {
        // WHAT: Retrieves a single cohort group by ID.
        // WHY:  Used during group management, membership modifications, and access evaluation.
        // HOW:  Queries weaver_subject_groups by primary key.
        ArgumentNullException.ThrowIfNull(groupId);

        SubjectGroup? group = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(@"
SELECT g.id, g.name, g.description, g.created_by, g.created_at_utc, g.updated_at_utc,
       (SELECT COUNT(*) FROM weaver_subject_group_members m WHERE m.group_id = g.id) AS member_count
FROM weaver_subject_groups g WHERE g.id = @id");
            WeaverDbParameters.Add(cmd, "@id", groupId.Trim());

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                group = MapSubjectGroup(reader);
            }
        }
        return group;
    }

    /// <inheritdoc/>
    public async Task<bool> SaveGroupAsync(SubjectGroup group, CancellationToken ct = default)
    {
        // WHAT: Upserts a cohort group header record.
        // WHY:  Creates or updates cohort metadata in weaver_subject_groups.
        // HOW:  Executes INSERT ON CONFLICT (id) DO UPDATE.
        ArgumentNullException.ThrowIfNull(group);

        int affected;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(@"
INSERT INTO weaver_subject_groups (id, name, description, created_by, created_at_utc, updated_at_utc)
VALUES (@id, @name, @desc, @by, @created, @updated)
ON CONFLICT (id) DO UPDATE SET
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc");

            WeaverDbParameters.Add(cmd, "@id", group.Id.Trim());
            WeaverDbParameters.Add(cmd, "@name", group.Name.Trim());
            WeaverDbParameters.Add(cmd, "@desc", (object?)group.Description ?? DBNull.Value);
            WeaverDbParameters.Add(cmd, "@by", group.CreatedBy.Trim().ToLowerInvariant());
            WeaverDbParameters.Add(cmd, "@created", session.Dialect.TimestampValue(group.CreatedAtUtc));
            WeaverDbParameters.Add(cmd, "@updated", session.Dialect.TimestampValue(group.UpdatedAtUtc));

            affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return affected >= 1;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteGroupAsync(string groupId, CancellationToken ct = default)
    {
        // WHAT: Deletes a cohort group and cascades member and grant deletions.
        // WHY:  Permanently removes a cohort from the registry.
        // HOW:  Executes DELETE against weaver_subject_groups where id matches.
        ArgumentNullException.ThrowIfNull(groupId);

        int affected;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand("DELETE FROM weaver_subject_groups WHERE id = @id");
            WeaverDbParameters.Add(cmd, "@id", groupId.Trim());

            affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return affected >= 1;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListGroupMemberUsernamesAsync(string groupId, CancellationToken ct = default)
    {
        // WHAT: Lists all subject usernames enrolled in a cohort group.
        // WHY:  Determines cohort membership for access evaluation and UI display.
        // HOW:  Selects subject_username from weaver_subject_group_members.
        ArgumentNullException.ThrowIfNull(groupId);

        List<string> members = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                "SELECT subject_username FROM weaver_subject_group_members WHERE group_id = @gid ORDER BY added_at_utc ASC");
            WeaverDbParameters.Add(cmd, "@gid", groupId.Trim());

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                members.Add(reader.GetString(0));
            }
        }
        return members;
    }

    /// <inheritdoc/>
    public async Task<bool> AddGroupMemberAsync(string groupId, string subjectUsername, DateTimeOffset addedAtUtc, CancellationToken ct = default)
    {
        // WHAT: Adds a subject to a cohort group if not already present.
        // WHY:  Enrolls patient into the study cohort group.
        // HOW:  Executes INSERT ON CONFLICT DO NOTHING and updates group updated_at_utc.
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(subjectUsername);

        int affected;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                "INSERT INTO weaver_subject_group_members (group_id, subject_username, added_at_utc) VALUES (@gid, @subj, @added) ON CONFLICT (group_id, subject_username) DO NOTHING");
            WeaverDbParameters.Add(cmd, "@gid", groupId.Trim());
            WeaverDbParameters.Add(cmd, "@subj", subjectUsername.Trim().ToLowerInvariant());
            WeaverDbParameters.Add(cmd, "@added", session.Dialect.TimestampValue(addedAtUtc));

            affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            if (affected >= 1)
            {
                await using DbCommand updateCmd = session.CreateCommand(
                    "UPDATE weaver_subject_groups SET updated_at_utc = @updated WHERE id = @gid");
                WeaverDbParameters.Add(updateCmd, "@updated", session.Dialect.TimestampValue(addedAtUtc));
                WeaverDbParameters.Add(updateCmd, "@gid", groupId.Trim());
                await updateCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        return affected >= 1;
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveGroupMemberAsync(string groupId, string subjectUsername, CancellationToken ct = default)
    {
        // WHAT: Removes a subject from a cohort group.
        // WHY:  Un-enrolls patient from the study cohort group.
        // HOW:  Executes DELETE against weaver_subject_group_members.
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(subjectUsername);

        int affected;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                "DELETE FROM weaver_subject_group_members WHERE group_id = @gid AND subject_username = @subj");
            WeaverDbParameters.Add(cmd, "@gid", groupId.Trim());
            WeaverDbParameters.Add(cmd, "@subj", subjectUsername.Trim().ToLowerInvariant());

            affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return affected >= 1;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SubjectGroupGrant>> ListGroupGrantsAsync(string? researcherUsername = null, string? groupId = null, CancellationToken ct = default)
    {
        // WHAT: Lists all cohort group grants matching optional filters.
        // WHY:  Inspects group-level permissions for administrators and researchers.
        // HOW:  Selects from weaver_subject_group_grants with parameterized filters.
        List<SubjectGroupGrant> grants = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            string sql = "SELECT id, group_id, researcher_username, access_level, granted_by, granted_at_utc, notes FROM weaver_subject_group_grants WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(researcherUsername))
            {
                sql += " AND researcher_username = @res";
            }
            if (!string.IsNullOrWhiteSpace(groupId))
            {
                sql += " AND group_id = @gid";
            }
            sql += " ORDER BY granted_at_utc DESC";

            await using DbCommand cmd = session.CreateCommand(sql);
            if (!string.IsNullOrWhiteSpace(researcherUsername))
            {
                WeaverDbParameters.Add(cmd, "@res", researcherUsername.Trim().ToLowerInvariant());
            }
            if (!string.IsNullOrWhiteSpace(groupId))
            {
                WeaverDbParameters.Add(cmd, "@gid", groupId.Trim());
            }

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                grants.Add(MapGroupGrant(reader));
            }
        }
        return grants;
    }

    /// <inheritdoc/>
    public async Task<SubjectGroupGrant?> GetGroupGrantAsync(string groupId, string researcherUsername, CancellationToken ct = default)
    {
        // WHAT: Retrieves a specific group grant record.
        // WHY:  Checks if a researcher holds permissions over an entire cohort group.
        // HOW:  Queries weaver_subject_group_grants by compound key.
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(researcherUsername);

        SubjectGroupGrant? result = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                "SELECT id, group_id, researcher_username, access_level, granted_by, granted_at_utc, notes FROM weaver_subject_group_grants WHERE group_id = @gid AND researcher_username = @res");
            WeaverDbParameters.Add(cmd, "@gid", groupId.Trim());
            WeaverDbParameters.Add(cmd, "@res", researcherUsername.Trim().ToLowerInvariant());

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                result = MapGroupGrant(reader);
            }
        }
        return result;
    }

    /// <inheritdoc/>
    public async Task<bool> SaveGroupGrantAsync(SubjectGroupGrant grant, CancellationToken ct = default)
    {
        // WHAT: Upserts a cohort group access grant.
        // WHY:  Grants researcher access to all members of a cohort group.
        // HOW:  Executes INSERT ON CONFLICT DO UPDATE.
        ArgumentNullException.ThrowIfNull(grant);

        int affected;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(@"
INSERT INTO weaver_subject_group_grants (id, group_id, researcher_username, access_level, granted_by, granted_at_utc, notes)
VALUES (@id, @gid, @res, @level, @by, @at, @notes)
ON CONFLICT (group_id, researcher_username) DO UPDATE SET
    access_level = excluded.access_level,
    granted_by = excluded.granted_by,
    granted_at_utc = excluded.granted_at_utc,
    notes = excluded.notes");

            WeaverDbParameters.Add(cmd, "@id", grant.Id);
            WeaverDbParameters.Add(cmd, "@gid", grant.GroupId.Trim());
            WeaverDbParameters.Add(cmd, "@res", grant.ResearcherUsername.Trim().ToLowerInvariant());
            WeaverDbParameters.Add(cmd, "@level", (int)grant.AccessLevel);
            WeaverDbParameters.Add(cmd, "@by", grant.GrantedBy.Trim().ToLowerInvariant());
            WeaverDbParameters.Add(cmd, "@at", session.Dialect.TimestampValue(grant.GrantedAtUtc));
            WeaverDbParameters.Add(cmd, "@notes", (object?)grant.Notes ?? DBNull.Value);

            affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return affected >= 1;
    }

    /// <inheritdoc/>
    public async Task<bool> RevokeGroupGrantAsync(string groupId, string researcherUsername, CancellationToken ct = default)
    {
        // WHAT: Revokes a cohort group access grant.
        // WHY:  Removes researcher access from all members of a cohort group.
        // HOW:  Executes DELETE against weaver_subject_group_grants.
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(researcherUsername);

        int affected;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                "DELETE FROM weaver_subject_group_grants WHERE group_id = @gid AND researcher_username = @res");
            WeaverDbParameters.Add(cmd, "@gid", groupId.Trim());
            WeaverDbParameters.Add(cmd, "@res", researcherUsername.Trim().ToLowerInvariant());

            affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return affected >= 1;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetMemberGroupsForSubjectAsync(string subjectUsername, CancellationToken ct = default)
    {
        // WHAT: Lists all cohort group IDs in which a subject is currently enrolled.
        // WHY:  Allows determining group-based access grants that apply to a target subject.
        // HOW:  Selects group_id from weaver_subject_group_members where subject_username matches.
        ArgumentNullException.ThrowIfNull(subjectUsername);

        List<string> groups = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                "SELECT group_id FROM weaver_subject_group_members WHERE subject_username = @subj ORDER BY added_at_utc ASC");
            WeaverDbParameters.Add(cmd, "@subj", subjectUsername.Trim().ToLowerInvariant());

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                groups.Add(reader.GetString(0));
            }
        }
        return groups;
    }

    /// <summary>
    /// Maps a database reader row to a <see cref="SubjectGrant"/> record.
    /// </summary>
    /// <param name="reader">The active data reader.</param>
    /// <returns>Hydrated <see cref="SubjectGrant"/> record.</returns>
    private static SubjectGrant MapSubjectGrant(DbDataReader reader) => new()
    {
        Id = reader.GetString(0),
        ResearcherUsername = reader.GetString(1),
        SubjectUsername = reader.GetString(2),
        AccessLevel = (SubjectAccessLevel)reader.GetInt32(3),
        GrantedBy = reader.GetString(4),
        GrantedAtUtc = WeaverDbValues.ReadTimestamp(reader, 5),
        Notes = reader.IsDBNull(6) ? null : reader.GetString(6)
    };

    /// <summary>
    /// Maps a database reader row to a <see cref="SubjectGroup"/> record.
    /// </summary>
    /// <param name="reader">The active data reader.</param>
    /// <returns>Hydrated <see cref="SubjectGroup"/> record.</returns>
    private static SubjectGroup MapSubjectGroup(DbDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        CreatedBy = reader.GetString(3),
        CreatedAtUtc = WeaverDbValues.ReadTimestamp(reader, 4),
        UpdatedAtUtc = WeaverDbValues.ReadTimestamp(reader, 5),
        MemberCount = Convert.ToInt32(reader.GetValue(6))
    };

    /// <summary>
    /// Maps a database reader row to a <see cref="SubjectGroupGrant"/> record.
    /// </summary>
    /// <param name="reader">The active data reader.</param>
    /// <returns>Hydrated <see cref="SubjectGroupGrant"/> record.</returns>
    private static SubjectGroupGrant MapGroupGrant(DbDataReader reader) => new()
    {
        Id = reader.GetString(0),
        GroupId = reader.GetString(1),
        ResearcherUsername = reader.GetString(2),
        AccessLevel = (SubjectAccessLevel)reader.GetInt32(3),
        GrantedBy = reader.GetString(4),
        GrantedAtUtc = WeaverDbValues.ReadTimestamp(reader, 5),
        Notes = reader.IsDBNull(6) ? null : reader.GetString(6)
    };
}
