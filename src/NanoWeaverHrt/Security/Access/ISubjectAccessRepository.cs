namespace NanoWeaverHrt.Security.Access;

/// <summary>
/// Authoritative persistence interface for direct subject grants, cohort groups, group members, and group grants.
/// </summary>
public interface ISubjectAccessRepository
{
    /// <summary>
    /// Lists all direct subject access grants matching optional researcher or subject filters.
    /// </summary>
    /// <param name="researcherUsername">Optional researcher username filter.</param>
    /// <param name="subjectUsername">Optional subject username filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of matching subject grants.</returns>
    Task<IReadOnlyList<SubjectGrant>> ListSubjectGrantsAsync(string? researcherUsername = null, string? subjectUsername = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single direct subject grant by researcher and subject pair.
    /// </summary>
    /// <param name="researcherUsername">Researcher username.</param>
    /// <param name="subjectUsername">Subject/patient username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The grant record if found; otherwise null.</returns>
    Task<SubjectGrant?> GetSubjectGrantAsync(string researcherUsername, string subjectUsername, CancellationToken ct = default);

    /// <summary>
    /// Persists or updates a direct subject access grant.
    /// </summary>
    /// <param name="grant">The subject grant record to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the grant was saved; otherwise false.</returns>
    Task<bool> SaveSubjectGrantAsync(SubjectGrant grant, CancellationToken ct = default);

    /// <summary>
    /// Permanently revokes a direct subject grant.
    /// </summary>
    /// <param name="researcherUsername">Researcher username.</param>
    /// <param name="subjectUsername">Subject/patient username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if a grant was revoked; false if not found.</returns>
    Task<bool> RevokeSubjectGrantAsync(string researcherUsername, string subjectUsername, CancellationToken ct = default);

    /// <summary>
    /// Lists all cohort groups matching an optional creator filter.
    /// </summary>
    /// <param name="createdBy">Optional creator username filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of matching cohort groups.</returns>
    Task<IReadOnlyList<SubjectGroup>> ListGroupsAsync(string? createdBy = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a cohort group by unique identifier.
    /// </summary>
    /// <param name="groupId">Unique cohort group identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cohort group if found; otherwise null.</returns>
    Task<SubjectGroup?> GetGroupAsync(string groupId, CancellationToken ct = default);

    /// <summary>
    /// Persists or updates a cohort group header record.
    /// </summary>
    /// <param name="group">The cohort group to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the group was saved; otherwise false.</returns>
    Task<bool> SaveGroupAsync(SubjectGroup group, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes a cohort group and cascades member and grant deletions.
    /// </summary>
    /// <param name="groupId">Unique cohort group identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the group was deleted; false if not found.</returns>
    Task<bool> DeleteGroupAsync(string groupId, CancellationToken ct = default);

    /// <summary>
    /// Lists all enrolled subject usernames for a specific cohort group.
    /// </summary>
    /// <param name="groupId">Unique cohort group identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of subject usernames enrolled in the group.</returns>
    Task<IReadOnlyList<string>> ListGroupMemberUsernamesAsync(string groupId, CancellationToken ct = default);

    /// <summary>
    /// Enrolls a subject/patient into a cohort group.
    /// </summary>
    /// <param name="groupId">Unique cohort group identifier.</param>
    /// <param name="subjectUsername">Subject/patient username.</param>
    /// <param name="addedAtUtc">UTC timestamp when added.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if added or already member; otherwise false.</returns>
    Task<bool> AddGroupMemberAsync(string groupId, string subjectUsername, DateTimeOffset addedAtUtc, CancellationToken ct = default);

    /// <summary>
    /// Removes a subject/patient from a cohort group.
    /// </summary>
    /// <param name="groupId">Unique cohort group identifier.</param>
    /// <param name="subjectUsername">Subject/patient username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the member was removed; false if not enrolled.</returns>
    Task<bool> RemoveGroupMemberAsync(string groupId, string subjectUsername, CancellationToken ct = default);

    /// <summary>
    /// Lists all cohort group access grants matching optional researcher or group filters.
    /// </summary>
    /// <param name="researcherUsername">Optional researcher username filter.</param>
    /// <param name="groupId">Optional group identifier filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of group grants.</returns>
    Task<IReadOnlyList<SubjectGroupGrant>> ListGroupGrantsAsync(string? researcherUsername = null, string? groupId = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single group grant by group and researcher.
    /// </summary>
    /// <param name="groupId">Unique cohort group identifier.</param>
    /// <param name="researcherUsername">Researcher username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The group grant if found; otherwise null.</returns>
    Task<SubjectGroupGrant?> GetGroupGrantAsync(string groupId, string researcherUsername, CancellationToken ct = default);

    /// <summary>
    /// Persists or updates a group access grant.
    /// </summary>
    /// <param name="grant">The group grant record to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the grant was saved; otherwise false.</returns>
    Task<bool> SaveGroupGrantAsync(SubjectGroupGrant grant, CancellationToken ct = default);

    /// <summary>
    /// Permanently revokes a group access grant.
    /// </summary>
    /// <param name="groupId">Unique cohort group identifier.</param>
    /// <param name="researcherUsername">Researcher username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if a grant was revoked; false if not found.</returns>
    Task<bool> RevokeGroupGrantAsync(string groupId, string researcherUsername, CancellationToken ct = default);

    /// <summary>
    /// Lists all cohort group identifiers in which a specified subject is enrolled.
    /// </summary>
    /// <param name="subjectUsername">Subject/patient username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of cohort group identifiers containing the subject.</returns>
    Task<IReadOnlyList<string>> GetMemberGroupsForSubjectAsync(string subjectUsername, CancellationToken ct = default);
}
