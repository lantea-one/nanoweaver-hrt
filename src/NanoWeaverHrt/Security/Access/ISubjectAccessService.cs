using NanoWeaverHrt.Security.Authentication;

namespace NanoWeaverHrt.Security.Access;

/// <summary>
/// High-throughput access evaluation and lifecycle management service for subject grants and cohort groups.
/// </summary>
public interface ISubjectAccessService
{
    /// <summary>
    /// Evaluates whether a caller possesses the required access level over a target subject.
    /// </summary>
    /// <param name="callerUsername">Username of the calling identity.</param>
    /// <param name="callerRole">Authorization role held by the caller.</param>
    /// <param name="targetSubjectUsername">Username of the target subject or patient.</param>
    /// <param name="requiredLevel">Minimum tiered access level required for the operation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="SubjectAccessResult"/> indicating allowance and effective level.</returns>
    Task<SubjectAccessResult> EvaluateAccessAsync(
        string callerUsername,
        UserRole callerRole,
        string targetSubjectUsername,
        SubjectAccessLevel requiredLevel,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the effective access level a researcher holds over a target subject across direct and group grants.
    /// </summary>
    /// <param name="researcherUsername">Username of the researcher.</param>
    /// <param name="targetSubjectUsername">Username of the subject/patient.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The highest applicable access level, or null if no grant exists.</returns>
    Task<SubjectAccessLevel?> GetEffectiveAccessLevelAsync(
        string researcherUsername,
        string targetSubjectUsername,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves all subject usernames accessible to the caller matching or exceeding the minimum level.
    /// </summary>
    /// <param name="callerUsername">Username of the caller.</param>
    /// <param name="callerRole">Role held by the caller.</param>
    /// <param name="minimumLevel">Minimum required access level.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Distinct list of accessible subject usernames.</returns>
    Task<IReadOnlyList<string>> GetAccessibleSubjectUsernamesAsync(
        string callerUsername,
        UserRole callerRole,
        SubjectAccessLevel minimumLevel = SubjectAccessLevel.ReadAccess,
        CancellationToken ct = default);

    /// <summary>
    /// Determines whether a user is authorized to manage (update, delete, modify members) a cohort group.
    /// </summary>
    /// <param name="callerUsername">Username of the caller.</param>
    /// <param name="callerRole">Role held by the caller.</param>
    /// <param name="groupId">Unique cohort group identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the user can manage the group; otherwise false.</returns>
    Task<bool> CanManageGroupAsync(
        string callerUsername,
        UserRole callerRole,
        string groupId,
        CancellationToken ct = default);

    /// <summary>
    /// Validates whether a caller is authorized to add the specified collection of subjects to a cohort group.
    /// </summary>
    /// <param name="callerUsername">Username of the caller.</param>
    /// <param name="callerRole">Role held by the caller.</param>
    /// <param name="subjectUsernames">Collection of subject usernames to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if all subjects are accessible to the caller; otherwise false.</returns>
    Task<bool> ValidateCanAddSubjectsToGroupAsync(
        string callerUsername,
        UserRole callerRole,
        IEnumerable<string> subjectUsernames,
        CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a direct subject access grant with cryptographic audit ledger recording.
    /// </summary>
    /// <param name="granterUsername">Username of the administrator granting access.</param>
    /// <param name="researcherUsername">Username of the researcher receiving access.</param>
    /// <param name="subjectUsername">Username of the subject/patient.</param>
    /// <param name="accessLevel">Tiered access level granted.</param>
    /// <param name="notes">Optional clinical or oversight notes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="SubjectGrant"/> record.</returns>
    Task<SubjectGrant> GrantSubjectAccessAsync(
        string granterUsername,
        string researcherUsername,
        string subjectUsername,
        SubjectAccessLevel accessLevel,
        string? notes,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes a direct subject access grant with cryptographic audit ledger recording.
    /// </summary>
    /// <param name="revokerUsername">Username of the administrator revoking access.</param>
    /// <param name="researcherUsername">Username of the researcher.</param>
    /// <param name="subjectUsername">Username of the subject/patient.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if revoked; false if grant did not exist.</returns>
    Task<bool> RevokeSubjectAccessAsync(
        string revokerUsername,
        string researcherUsername,
        string subjectUsername,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new cohort group with initial member subjects and audit ledger recording.
    /// </summary>
    /// <param name="creatorUsername">Username of the creator.</param>
    /// <param name="creatorRole">Role held by the creator.</param>
    /// <param name="name">Name of the cohort group.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="initialSubjects">Initial subject usernames to enroll.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="SubjectGroup"/> record.</returns>
    Task<SubjectGroup> CreateGroupAsync(
        string creatorUsername,
        UserRole creatorRole,
        string name,
        string? description,
        IEnumerable<string> initialSubjects,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a cohort group name and description with audit ledger recording.
    /// </summary>
    /// <param name="updaterUsername">Username of the user updating the group.</param>
    /// <param name="updaterRole">Role held by the updater.</param>
    /// <param name="groupId">Unique cohort group identifier.</param>
    /// <param name="name">New name for the group.</param>
    /// <param name="description">New description for the group.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if updated; false if not found or unauthorized.</returns>
    Task<bool> UpdateGroupAsync(
        string updaterUsername,
        UserRole updaterRole,
        string groupId,
        string name,
        string? description,
        CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes a cohort group with audit ledger recording.
    /// </summary>
    /// <param name="deleterUsername">Username of the user deleting the group.</param>
    /// <param name="deleterRole">Role held by the deleter.</param>
    /// <param name="groupId">Unique cohort group identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if deleted; false if not found or unauthorized.</returns>
    Task<bool> DeleteGroupAsync(
        string deleterUsername,
        UserRole deleterRole,
        string groupId,
        CancellationToken ct = default);

    /// <summary>
    /// Enrolls a subject into a cohort group with authorization validation and audit ledger recording.
    /// </summary>
    /// <param name="callerUsername">Username of the caller.</param>
    /// <param name="callerRole">Role held by the caller.</param>
    /// <param name="groupId">Unique cohort group identifier.</param>
    /// <param name="subjectUsername">Subject/patient username to enroll.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if enrolled; false if already member or unauthorized.</returns>
    Task<bool> AddGroupMemberAsync(
        string callerUsername,
        UserRole callerRole,
        string groupId,
        string subjectUsername,
        CancellationToken ct = default);

    /// <summary>
    /// Removes a subject from a cohort group with authorization validation and audit ledger recording.
    /// </summary>
    /// <param name="callerUsername">Username of the caller.</param>
    /// <param name="callerRole">Role held by the caller.</param>
    /// <param name="groupId">Unique cohort group identifier.</param>
    /// <param name="subjectUsername">Subject/patient username to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if removed; false if not enrolled or unauthorized.</returns>
    Task<bool> RemoveGroupMemberAsync(
        string callerUsername,
        UserRole callerRole,
        string groupId,
        string subjectUsername,
        CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a cohort group access grant for a researcher with audit ledger recording.
    /// </summary>
    /// <param name="granterUsername">Username of the administrator granting group access.</param>
    /// <param name="groupId">Unique cohort group identifier.</param>
    /// <param name="researcherUsername">Username of the researcher receiving group access.</param>
    /// <param name="accessLevel">Tiered access level granted over group members.</param>
    /// <param name="notes">Optional clinical or oversight notes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="SubjectGroupGrant"/> record.</returns>
    Task<SubjectGroupGrant> GrantGroupAccessAsync(
        string granterUsername,
        string groupId,
        string researcherUsername,
        SubjectAccessLevel accessLevel,
        string? notes,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes a cohort group access grant with audit ledger recording.
    /// </summary>
    /// <param name="revokerUsername">Username of the administrator revoking group access.</param>
    /// <param name="groupId">Unique cohort group identifier.</param>
    /// <param name="researcherUsername">Username of the researcher.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if revoked; false if group grant did not exist.</returns>
    Task<bool> RevokeGroupAccessAsync(
        string revokerUsername,
        string groupId,
        string researcherUsername,
        CancellationToken ct = default);
}
