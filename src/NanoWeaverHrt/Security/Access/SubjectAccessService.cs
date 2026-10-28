using System.Text.Json;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Security.Authentication;

namespace NanoWeaverHrt.Security.Access;

/// <summary>
/// Authoritative service implementing <see cref="ISubjectAccessService"/> with tiered permission evaluation
/// and cryptographically chained audit logging.
/// </summary>
public sealed class SubjectAccessService : ISubjectAccessService
{
    /// <summary>
    /// Underlying repository for direct grants, cohort groups, group members, and group grants.
    /// </summary>
    private readonly ISubjectAccessRepository _accessRepo;

    /// <summary>
    /// User repository for looking up patient accounts.
    /// </summary>
    private readonly IUserRepository _userRepo;

    /// <summary>
    /// Regulatory compliance audit service for recording authorization actions to the append-only ledger.
    /// </summary>
    private readonly IComplianceAuditService _auditService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubjectAccessService"/> class.
    /// </summary>
    /// <param name="accessRepo">The subject access repository.</param>
    /// <param name="userRepo">The user account repository.</param>
    /// <param name="auditService">The compliance audit service.</param>
    public SubjectAccessService(
        ISubjectAccessRepository accessRepo,
        IUserRepository userRepo,
        IComplianceAuditService auditService)
    {
        ArgumentNullException.ThrowIfNull(accessRepo);
        ArgumentNullException.ThrowIfNull(userRepo);
        ArgumentNullException.ThrowIfNull(auditService);

        _accessRepo = accessRepo;
        _userRepo = userRepo;
        _auditService = auditService;
    }

    /// <inheritdoc/>
    public async Task<SubjectAccessResult> EvaluateAccessAsync(
        string callerUsername,
        UserRole callerRole,
        string targetSubjectUsername,
        SubjectAccessLevel requiredLevel,
        CancellationToken ct = default)
    {
        // WHAT: Evaluates whether a caller possesses sufficient access level over a target subject.
        // WHY:  Enforces patient privacy and clinical boundaries across Administrator, Researcher, and Patient roles.
        // HOW:  Administrators and self-access callers bypass checks; Researchers are evaluated against direct and group grants.
        ArgumentNullException.ThrowIfNull(callerUsername);
        ArgumentNullException.ThrowIfNull(targetSubjectUsername);

        string cleanCaller = callerUsername.Trim().ToLowerInvariant();
        string cleanTarget = targetSubjectUsername.Trim().ToLowerInvariant();

        if (callerRole == UserRole.Administrator || string.Equals(cleanCaller, cleanTarget, StringComparison.OrdinalIgnoreCase))
        {
            return SubjectAccessResult.Allowed(SubjectAccessLevel.FullReadAccess);
        }

        if (callerRole != UserRole.Researcher)
        {
            return SubjectAccessResult.Denied();
        }

        SubjectAccessLevel? effectiveLevel = await GetEffectiveAccessLevelAsync(cleanCaller, cleanTarget, ct).ConfigureAwait(false);
        if (effectiveLevel.HasValue && effectiveLevel.Value >= requiredLevel)
        {
            return SubjectAccessResult.Allowed(effectiveLevel.Value);
        }

        return SubjectAccessResult.Denied();
    }

    /// <inheritdoc/>
    public async Task<SubjectAccessLevel?> GetEffectiveAccessLevelAsync(
        string researcherUsername,
        string targetSubjectUsername,
        CancellationToken ct = default)
    {
        // WHAT: Computes the maximum access level a researcher holds over a target subject.
        // WHY:  Combines direct grants, cohort group grants, and researcher group ownership.
        // HOW:  Queries repository for direct grant, groups containing subject, and group grants.
        ArgumentNullException.ThrowIfNull(researcherUsername);
        ArgumentNullException.ThrowIfNull(targetSubjectUsername);

        string cleanRes = researcherUsername.Trim().ToLowerInvariant();
        string cleanSubj = targetSubjectUsername.Trim().ToLowerInvariant();

        SubjectAccessLevel? maxLevel = null;

        // 1. Direct grant check
        SubjectGrant? direct = await _accessRepo.GetSubjectGrantAsync(cleanRes, cleanSubj, ct).ConfigureAwait(false);
        if (direct != null)
        {
            maxLevel = direct.AccessLevel;
        }

        // 2. Cohort group grants and ownership check
        IReadOnlyList<string> memberGroups = await _accessRepo.GetMemberGroupsForSubjectAsync(cleanSubj, ct).ConfigureAwait(false);
        foreach (string groupId in memberGroups)
        {
            SubjectGroupGrant? groupGrant = await _accessRepo.GetGroupGrantAsync(groupId, cleanRes, ct).ConfigureAwait(false);
            if (groupGrant != null)
            {
                if (!maxLevel.HasValue || groupGrant.AccessLevel > maxLevel.Value)
                {
                    maxLevel = groupGrant.AccessLevel;
                }
            }

            // If the researcher created this group, grant at least GenomicAccess (level 2)
            SubjectGroup? group = await _accessRepo.GetGroupAsync(groupId, ct).ConfigureAwait(false);
            if (group != null && string.Equals(group.CreatedBy, cleanRes, StringComparison.OrdinalIgnoreCase))
            {
                SubjectAccessLevel ownerLevel = SubjectAccessLevel.GenomicAccess;
                if (!maxLevel.HasValue || ownerLevel > maxLevel.Value)
                {
                    maxLevel = ownerLevel;
                }
            }
        }

        return maxLevel;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetAccessibleSubjectUsernamesAsync(
        string callerUsername,
        UserRole callerRole,
        SubjectAccessLevel minimumLevel = SubjectAccessLevel.ReadAccess,
        CancellationToken ct = default)
    {
        // WHAT: Returns all subject usernames accessible to the caller at or above the minimum access tier.
        // WHY:  Powers dropdown selectors and cohort management listings in the Web UI and API.
        // HOW:  Administrators see all patients; Patients see themselves; Researchers aggregate direct and group memberships.
        ArgumentNullException.ThrowIfNull(callerUsername);

        string cleanCaller = callerUsername.Trim().ToLowerInvariant();

        if (callerRole == UserRole.Administrator)
        {
            var allUsers = await _userRepo.ListUsersAsync(ct).ConfigureAwait(false);
            return allUsers
                .Where(u => u.Role == UserRole.Patient)
                .Select(u => u.Username.ToLowerInvariant())
                .Distinct()
                .OrderBy(u => u, StringComparer.Ordinal)
                .ToList();
        }

        if (callerRole == UserRole.Patient)
        {
            return [cleanCaller];
        }

        if (callerRole != UserRole.Researcher)
        {
            return [];
        }

        HashSet<string> accessible = new(StringComparer.OrdinalIgnoreCase);

        // Direct grants
        var directGrants = await _accessRepo.ListSubjectGrantsAsync(researcherUsername: cleanCaller, ct: ct).ConfigureAwait(false);
        foreach (var grant in directGrants)
        {
            if (grant.AccessLevel >= minimumLevel)
            {
                accessible.Add(grant.SubjectUsername.ToLowerInvariant());
            }
        }

        // Group grants
        var groupGrants = await _accessRepo.ListGroupGrantsAsync(researcherUsername: cleanCaller, ct: ct).ConfigureAwait(false);
        foreach (var grant in groupGrants)
        {
            if (grant.AccessLevel >= minimumLevel)
            {
                var members = await _accessRepo.ListGroupMemberUsernamesAsync(grant.GroupId, ct).ConfigureAwait(false);
                foreach (string m in members)
                {
                    accessible.Add(m.ToLowerInvariant());
                }
            }
        }

        // Groups created by researcher
        var createdGroups = await _accessRepo.ListGroupsAsync(createdBy: cleanCaller, ct: ct).ConfigureAwait(false);
        foreach (var group in createdGroups)
        {
            var members = await _accessRepo.ListGroupMemberUsernamesAsync(group.Id, ct).ConfigureAwait(false);
            foreach (string m in members)
            {
                accessible.Add(m.ToLowerInvariant());
            }
        }

        return accessible.OrderBy(u => u, StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc/>
    public async Task<bool> CanManageGroupAsync(
        string callerUsername,
        UserRole callerRole,
        string groupId,
        CancellationToken ct = default)
    {
        // WHAT: Checks whether a caller is authorized to modify a cohort group.
        // WHY:  Administrators manage all groups; Researchers manage only cohorts they created.
        // HOW:  Queries group by ID and matches creator username.
        ArgumentNullException.ThrowIfNull(callerUsername);
        ArgumentNullException.ThrowIfNull(groupId);

        if (callerRole == UserRole.Administrator)
        {
            return true;
        }

        if (callerRole != UserRole.Researcher)
        {
            return false;
        }

        SubjectGroup? group = await _accessRepo.GetGroupAsync(groupId, ct).ConfigureAwait(false);
        return group != null && string.Equals(group.CreatedBy, callerUsername.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateCanAddSubjectsToGroupAsync(
        string callerUsername,
        UserRole callerRole,
        IEnumerable<string> subjectUsernames,
        CancellationToken ct = default)
    {
        // WHAT: Validates that all subjects to be added to a cohort group are accessible to the caller.
        // WHY:  Prevents researchers from widening their scope by enrolling unauthorized patients into a cohort.
        // HOW:  Fetches accessible subjects for the caller and checks containment of each candidate.
        ArgumentNullException.ThrowIfNull(callerUsername);
        ArgumentNullException.ThrowIfNull(subjectUsernames);

        if (callerRole == UserRole.Administrator)
        {
            return true;
        }

        if (callerRole != UserRole.Researcher)
        {
            return false;
        }

        var candidateList = subjectUsernames.Select(s => s.Trim().ToLowerInvariant()).ToList();
        if (candidateList.Count == 0)
        {
            return true;
        }

        var accessible = await GetAccessibleSubjectUsernamesAsync(callerUsername, callerRole, SubjectAccessLevel.ReadAccess, ct).ConfigureAwait(false);
        HashSet<string> accessibleSet = new(accessible, StringComparer.OrdinalIgnoreCase);

        return candidateList.All(accessibleSet.Contains);
    }

    /// <inheritdoc/>
    public async Task<SubjectGrant> GrantSubjectAccessAsync(
        string granterUsername,
        string researcherUsername,
        string subjectUsername,
        SubjectAccessLevel accessLevel,
        string? notes,
        CancellationToken ct = default)
    {
        // WHAT: Persists a direct subject grant and logs the action to the audit ledger.
        // WHY:  Administrators grant researchers tiered access to subjects with regulatory traceability.
        // HOW:  Upserts grant record via repository and calls IComplianceAuditService.LogAction.
        ArgumentNullException.ThrowIfNull(granterUsername);
        ArgumentNullException.ThrowIfNull(researcherUsername);
        ArgumentNullException.ThrowIfNull(subjectUsername);

        string cleanGranter = granterUsername.Trim().ToLowerInvariant();
        string cleanRes = researcherUsername.Trim().ToLowerInvariant();
        string cleanSubj = subjectUsername.Trim().ToLowerInvariant();

        SubjectGrant grant = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            ResearcherUsername = cleanRes,
            SubjectUsername = cleanSubj,
            AccessLevel = accessLevel,
            GrantedBy = cleanGranter,
            GrantedAtUtc = DateTimeOffset.UtcNow,
            Notes = notes
        };

        await _accessRepo.SaveSubjectGrantAsync(grant, ct).ConfigureAwait(false);

        _auditService.LogAction(
            cleanGranter,
            "SUBJECT_ACCESS_GRANTED",
            $"subject:{cleanSubj}",
            JsonSerializer.Serialize(new { researcher = cleanRes, accessLevel = accessLevel.ToString(), notes }));

        return grant;
    }

    /// <inheritdoc/>
    public async Task<bool> RevokeSubjectAccessAsync(
        string revokerUsername,
        string researcherUsername,
        string subjectUsername,
        CancellationToken ct = default)
    {
        // WHAT: Revokes a direct subject grant and logs the action to the audit ledger.
        // WHY:  Allows administrators to revoke researcher access with regulatory traceability.
        // HOW:  Deletes grant record via repository and calls IComplianceAuditService.LogAction.
        ArgumentNullException.ThrowIfNull(revokerUsername);
        ArgumentNullException.ThrowIfNull(researcherUsername);
        ArgumentNullException.ThrowIfNull(subjectUsername);

        string cleanRevoker = revokerUsername.Trim().ToLowerInvariant();
        string cleanRes = researcherUsername.Trim().ToLowerInvariant();
        string cleanSubj = subjectUsername.Trim().ToLowerInvariant();

        bool revoked = await _accessRepo.RevokeSubjectGrantAsync(cleanRes, cleanSubj, ct).ConfigureAwait(false);
        if (revoked)
        {
            _auditService.LogAction(
                cleanRevoker,
                "SUBJECT_ACCESS_REVOKED",
                $"subject:{cleanSubj}",
                JsonSerializer.Serialize(new { researcher = cleanRes }));
        }

        return revoked;
    }

    /// <inheritdoc/>
    public async Task<SubjectGroup> CreateGroupAsync(
        string creatorUsername,
        UserRole creatorRole,
        string name,
        string? description,
        IEnumerable<string> initialSubjects,
        CancellationToken ct = default)
    {
        // WHAT: Creates a new cohort group with initial enrolled subjects.
        // WHY:  Enables administrators and researchers to organize patient cohorts.
        // HOW:  Validates caller permissions and subject accessibility, saves group, enrolls members, and logs audit record.
        ArgumentNullException.ThrowIfNull(creatorUsername);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(initialSubjects);

        string cleanCreator = creatorUsername.Trim().ToLowerInvariant();
        var subjectsList = initialSubjects.Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList();

        if (creatorRole != UserRole.Administrator && creatorRole != UserRole.Researcher)
        {
            throw new UnauthorizedAccessException("Only Administrators and Researchers may create cohort groups.");
        }

        if (creatorRole == UserRole.Researcher)
        {
            bool canAdd = await ValidateCanAddSubjectsToGroupAsync(cleanCreator, creatorRole, subjectsList, ct).ConfigureAwait(false);
            if (!canAdd)
            {
                throw new UnauthorizedAccessException("Researchers may only create groups containing subjects they currently have access to.");
            }
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        SubjectGroup group = new()
        {
            Id = "grp_" + Guid.NewGuid().ToString("N")[..12],
            Name = name.Trim(),
            Description = description,
            CreatedBy = cleanCreator,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            MemberCount = subjectsList.Count
        };

        await _accessRepo.SaveGroupAsync(group, ct).ConfigureAwait(false);

        foreach (string subj in subjectsList)
        {
            await _accessRepo.AddGroupMemberAsync(group.Id, subj, now, ct).ConfigureAwait(false);
        }

        _auditService.LogAction(
            cleanCreator,
            "SUBJECT_GROUP_CREATED",
            $"group:{group.Id}",
            JsonSerializer.Serialize(new { name = group.Name, memberCount = subjectsList.Count }));

        return group;
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateGroupAsync(
        string updaterUsername,
        UserRole updaterRole,
        string groupId,
        string name,
        string? description,
        CancellationToken ct = default)
    {
        // WHAT: Updates a cohort group name and description.
        // WHY:  Allows group owners and administrators to maintain cohort metadata.
        // HOW:  Validates ownership, saves updated group, and logs audit record.
        ArgumentNullException.ThrowIfNull(updaterUsername);
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(name);

        string cleanUpdater = updaterUsername.Trim().ToLowerInvariant();
        bool canManage = await CanManageGroupAsync(cleanUpdater, updaterRole, groupId, ct).ConfigureAwait(false);
        if (!canManage)
        {
            throw new UnauthorizedAccessException("Unauthorized: You do not have permission to manage this cohort group.");
        }

        SubjectGroup? existing = await _accessRepo.GetGroupAsync(groupId, ct).ConfigureAwait(false);
        if (existing == null)
        {
            return false;
        }

        SubjectGroup updated = existing with
        {
            Name = name.Trim(),
            Description = description,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        bool saved = await _accessRepo.SaveGroupAsync(updated, ct).ConfigureAwait(false);
        if (saved)
        {
            _auditService.LogAction(
                cleanUpdater,
                "SUBJECT_GROUP_UPDATED",
                $"group:{groupId}",
                JsonSerializer.Serialize(new { name = updated.Name, description }));
        }

        return saved;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteGroupAsync(
        string deleterUsername,
        UserRole deleterRole,
        string groupId,
        CancellationToken ct = default)
    {
        // WHAT: Deletes a cohort group and logs the action.
        // WHY:  Permanently removes a cohort from the platform.
        // HOW:  Validates ownership, executes deletion via repository, and logs audit record.
        ArgumentNullException.ThrowIfNull(deleterUsername);
        ArgumentNullException.ThrowIfNull(groupId);

        string cleanDeleter = deleterUsername.Trim().ToLowerInvariant();
        bool canManage = await CanManageGroupAsync(cleanDeleter, deleterRole, groupId, ct).ConfigureAwait(false);
        if (!canManage)
        {
            throw new UnauthorizedAccessException("Unauthorized: You do not have permission to delete this cohort group.");
        }

        bool deleted = await _accessRepo.DeleteGroupAsync(groupId, ct).ConfigureAwait(false);
        if (deleted)
        {
            _auditService.LogAction(
                cleanDeleter,
                "SUBJECT_GROUP_DELETED",
                $"group:{groupId}",
                "{}");
        }

        return deleted;
    }

    /// <inheritdoc/>
    public async Task<bool> AddGroupMemberAsync(
        string callerUsername,
        UserRole callerRole,
        string groupId,
        string subjectUsername,
        CancellationToken ct = default)
    {
        // WHAT: Adds a subject to a cohort group.
        // WHY:  Enrolls patient into cohort with access validation.
        // HOW:  Validates caller group ownership and subject access, adds member via repository, and logs audit record.
        ArgumentNullException.ThrowIfNull(callerUsername);
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(subjectUsername);

        string cleanCaller = callerUsername.Trim().ToLowerInvariant();
        string cleanSubj = subjectUsername.Trim().ToLowerInvariant();

        bool canManage = await CanManageGroupAsync(cleanCaller, callerRole, groupId, ct).ConfigureAwait(false);
        if (!canManage)
        {
            throw new UnauthorizedAccessException("Unauthorized: You do not have permission to manage this cohort group.");
        }

        if (callerRole == UserRole.Researcher)
        {
            bool hasAccess = await ValidateCanAddSubjectsToGroupAsync(cleanCaller, callerRole, [cleanSubj], ct).ConfigureAwait(false);
            if (!hasAccess)
            {
                throw new UnauthorizedAccessException("Unauthorized: You cannot add a subject you do not have access to into this cohort.");
            }
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool added = await _accessRepo.AddGroupMemberAsync(groupId, cleanSubj, now, ct).ConfigureAwait(false);
        if (added)
        {
            _auditService.LogAction(
                cleanCaller,
                "SUBJECT_GROUP_MEMBER_ADDED",
                $"group:{groupId}",
                JsonSerializer.Serialize(new { subject = cleanSubj }));
        }

        return added;
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveGroupMemberAsync(
        string callerUsername,
        UserRole callerRole,
        string groupId,
        string subjectUsername,
        CancellationToken ct = default)
    {
        // WHAT: Removes a subject from a cohort group.
        // WHY:  Un-enrolls patient from cohort with authorization validation.
        // HOW:  Validates caller group ownership, removes member via repository, and logs audit record.
        ArgumentNullException.ThrowIfNull(callerUsername);
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(subjectUsername);

        string cleanCaller = callerUsername.Trim().ToLowerInvariant();
        string cleanSubj = subjectUsername.Trim().ToLowerInvariant();

        bool canManage = await CanManageGroupAsync(cleanCaller, callerRole, groupId, ct).ConfigureAwait(false);
        if (!canManage)
        {
            throw new UnauthorizedAccessException("Unauthorized: You do not have permission to manage this cohort group.");
        }

        bool removed = await _accessRepo.RemoveGroupMemberAsync(groupId, cleanSubj, ct).ConfigureAwait(false);
        if (removed)
        {
            _auditService.LogAction(
                cleanCaller,
                "SUBJECT_GROUP_MEMBER_REMOVED",
                $"group:{groupId}",
                JsonSerializer.Serialize(new { subject = cleanSubj }));
        }

        return removed;
    }

    /// <inheritdoc/>
    public async Task<SubjectGroupGrant> GrantGroupAccessAsync(
        string granterUsername,
        string groupId,
        string researcherUsername,
        SubjectAccessLevel accessLevel,
        string? notes,
        CancellationToken ct = default)
    {
        // WHAT: Grants a researcher access over an entire cohort group.
        // WHY:  Allows administrators to assign cohort access to researchers.
        // HOW:  Upserts group grant via repository and logs audit record.
        ArgumentNullException.ThrowIfNull(granterUsername);
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(researcherUsername);

        string cleanGranter = granterUsername.Trim().ToLowerInvariant();
        string cleanRes = researcherUsername.Trim().ToLowerInvariant();

        SubjectGroupGrant grant = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = groupId.Trim(),
            ResearcherUsername = cleanRes,
            AccessLevel = accessLevel,
            GrantedBy = cleanGranter,
            GrantedAtUtc = DateTimeOffset.UtcNow,
            Notes = notes
        };

        await _accessRepo.SaveGroupGrantAsync(grant, ct).ConfigureAwait(false);

        _auditService.LogAction(
            cleanGranter,
            "GROUP_ACCESS_GRANTED",
            $"group:{groupId}",
            JsonSerializer.Serialize(new { researcher = cleanRes, accessLevel = accessLevel.ToString(), notes }));

        return grant;
    }

    /// <inheritdoc/>
    public async Task<bool> RevokeGroupAccessAsync(
        string revokerUsername,
        string groupId,
        string researcherUsername,
        CancellationToken ct = default)
    {
        // WHAT: Revokes a researcher's access to a cohort group.
        // WHY:  Allows administrators to revoke cohort access.
        // HOW:  Deletes group grant via repository and logs audit record.
        ArgumentNullException.ThrowIfNull(revokerUsername);
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(researcherUsername);

        string cleanRevoker = revokerUsername.Trim().ToLowerInvariant();
        string cleanRes = researcherUsername.Trim().ToLowerInvariant();

        bool revoked = await _accessRepo.RevokeGroupGrantAsync(groupId, cleanRes, ct).ConfigureAwait(false);
        if (revoked)
        {
            _auditService.LogAction(
                cleanRevoker,
                "GROUP_ACCESS_REVOKED",
                $"group:{groupId}",
                JsonSerializer.Serialize(new { researcher = cleanRes }));
        }

        return revoked;
    }
}
