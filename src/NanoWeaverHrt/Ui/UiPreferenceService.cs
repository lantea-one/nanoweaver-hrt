using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Security.Authentication;

namespace NanoWeaverHrt.Ui;

/// <summary>
/// Resolves per-user Web UI display preferences, audits mode changes, and supplies role-tailored Task Hub cards
/// and grouped navigation menus for <see cref="UiDisplayMode.Simple"/> vs. <see cref="UiDisplayMode.Advanced"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Production implementation of <see cref="IUiPreferenceService"/>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 47 (P246): Gives Patients, Researchers, and Administrators a focused,
/// non-overwhelming landing workspace by default while keeping full operator tools accessible via Advanced Display Mode.
/// </para>
/// <para>
/// <strong>How it works:</strong> Merges <c>weaver_user_preferences</c> with <c>Ui:DefaultDisplayMode</c>, logs changes via
/// <see cref="IComplianceAuditService"/>, and serves immutable static catalogs without per-request allocations.
/// </para>
/// </remarks>
public sealed class UiPreferenceService : IUiPreferenceService
{
    /// <summary>
    /// Configuration key controlling the node-wide default UI display mode for users without a personal override.
    /// </summary>
    public const string DefaultDisplayModeConfigKey = "Ui:DefaultDisplayMode";

    /// <summary>
    /// Persistence repository for per-user display preferences.
    /// </summary>
    private readonly IUserUiPreferenceRepository _repository;

    /// <summary>
    /// Configuration repository supplying the node default display mode override.
    /// </summary>
    private readonly IConfigurationRepository _configRepository;

    /// <summary>
    /// Compliance audit service recording user display mode mutations.
    /// </summary>
    private readonly IComplianceAuditService _auditService;

    /// <summary>
    /// Task Hub catalog tailored for accounts holding the Patient role.
    /// </summary>
    private static readonly UiTaskCardDescriptor[] PatientCards =
    [
        CreateCard("dna", "TaskHub.Card.PatientDna.Title", "TaskHub.Card.PatientDna.Desc", "TaskHub.Card.PatientDna.Action", "/dna", "🧬", "TaskHub.Category.MyCare", isPrimary: true, requiresAdvanced: false),
        CreateCard("profile", "TaskHub.Card.MyProfile.Title", "TaskHub.Card.MyProfile.Desc", "TaskHub.Card.MyProfile.Action", "/profile", "👤", "TaskHub.Category.Account", isPrimary: true, requiresAdvanced: false),
        CreateCard("endocrine", "TaskHub.Card.Endocrine.Title", "TaskHub.Card.Endocrine.Desc", "TaskHub.Card.Endocrine.Action", "/endocrine", "⚖️", "TaskHub.Category.Clinical", isPrimary: false, requiresAdvanced: true)
    ];

    /// <summary>
    /// Task Hub catalog tailored for accounts holding the Researcher role.
    /// </summary>
    private static readonly UiTaskCardDescriptor[] ResearcherCards =
    [
        CreateCard("wizard", "TaskHub.Card.Wizard.Title", "TaskHub.Card.Wizard.Desc", "TaskHub.Card.Wizard.Action", "/wizard", "🧬", "TaskHub.Category.Design", isPrimary: true, requiresAdvanced: false),
        CreateCard("depot-studio", "TaskHub.Card.DepotStudio.Title", "TaskHub.Card.DepotStudio.Desc", "TaskHub.Card.DepotStudio.Action", "/depot-studio", "⏳", "TaskHub.Category.Clinical", isPrimary: true, requiresAdvanced: false),
        CreateCard("simulations", "TaskHub.Card.Simulations.Title", "TaskHub.Card.Simulations.Desc", "TaskHub.Card.Simulations.Action", "/simulations", "🧪", "TaskHub.Category.Simulation", isPrimary: true, requiresAdvanced: false),
        CreateCard("compliance", "TaskHub.Card.Compliance.Title", "TaskHub.Card.Compliance.Desc", "TaskHub.Card.Compliance.Action", "/compliance", "🔒", "TaskHub.Category.Compliance", isPrimary: true, requiresAdvanced: false),
        CreateCard("endocrine", "TaskHub.Card.Endocrine.Title", "TaskHub.Card.Endocrine.Desc", "TaskHub.Card.Endocrine.Action", "/endocrine", "⚖️", "TaskHub.Category.Clinical", isPrimary: false, requiresAdvanced: true),
        CreateCard("trajectories", "TaskHub.Card.Trajectories.Title", "TaskHub.Card.Trajectories.Desc", "TaskHub.Card.Trajectories.Action", "/trajectories", "📁", "TaskHub.Category.Simulation", isPrimary: false, requiresAdvanced: true),
        CreateCard("dna", "TaskHub.Card.PatientDna.Title", "TaskHub.Card.PatientDna.Desc", "TaskHub.Card.PatientDna.Action", "/dna", "🧬", "TaskHub.Category.Clinical", isPrimary: false, requiresAdvanced: true)
    ];

    /// <summary>
    /// Task Hub catalog tailored for accounts holding the Administrator role.
    /// </summary>
    private static readonly UiTaskCardDescriptor[] AdministratorCards =
    [
        CreateCard("wizard", "TaskHub.Card.Wizard.Title", "TaskHub.Card.Wizard.Desc", "TaskHub.Card.Wizard.Action", "/wizard", "🧬", "TaskHub.Category.Design", isPrimary: true, requiresAdvanced: false),
        CreateCard("depot-studio", "TaskHub.Card.DepotStudio.Title", "TaskHub.Card.DepotStudio.Desc", "TaskHub.Card.DepotStudio.Action", "/depot-studio", "⏳", "TaskHub.Category.Clinical", isPrimary: true, requiresAdvanced: false),
        CreateCard("users", "TaskHub.Card.Users.Title", "TaskHub.Card.Users.Desc", "TaskHub.Card.Users.Action", "/users", "👥", "TaskHub.Category.Admin", isPrimary: true, requiresAdvanced: false),
        CreateCard("compliance", "TaskHub.Card.Compliance.Title", "TaskHub.Card.Compliance.Desc", "TaskHub.Card.Compliance.Action", "/compliance", "🔒", "TaskHub.Category.Compliance", isPrimary: true, requiresAdvanced: false),
        CreateCard("services", "TaskHub.Card.Services.Title", "TaskHub.Card.Services.Desc", "TaskHub.Card.Services.Action", "/services", "⚙️", "TaskHub.Category.Admin", isPrimary: false, requiresAdvanced: true),
        CreateCard("configuration", "TaskHub.Card.Configuration.Title", "TaskHub.Card.Configuration.Desc", "TaskHub.Card.Configuration.Action", "/configuration", "🛡️", "TaskHub.Category.Admin", isPrimary: false, requiresAdvanced: true),
        CreateCard("simulations", "TaskHub.Card.Simulations.Title", "TaskHub.Card.Simulations.Desc", "TaskHub.Card.Simulations.Action", "/simulations", "🧪", "TaskHub.Category.Simulation", isPrimary: false, requiresAdvanced: true),
        CreateCard("trajectories", "TaskHub.Card.Trajectories.Title", "TaskHub.Card.Trajectories.Desc", "TaskHub.Card.Trajectories.Action", "/trajectories", "📁", "TaskHub.Category.Simulation", isPrimary: false, requiresAdvanced: true),
        CreateCard("endocrine", "TaskHub.Card.Endocrine.Title", "TaskHub.Card.Endocrine.Desc", "TaskHub.Card.Endocrine.Action", "/endocrine", "⚖️", "TaskHub.Category.Clinical", isPrimary: false, requiresAdvanced: true)
    ];

    /// <summary>
    /// Grouped navigation menus for the Patient role in Simple Display Mode.
    /// </summary>
    private static readonly UiNavGroupDescriptor[] PatientNavGroups =
    [
        new UiNavGroupDescriptor
        {
            GroupId = "my-care",
            LabelKey = "Nav.Group.MyCare",
            Icon = "🧬",
            Items =
            [
                new UiNavItemDescriptor { Route = "/dna", LabelKey = "Nav.DnaAndImports", Icon = "🧬" },
                new UiNavItemDescriptor { Route = "/profile", LabelKey = "Nav.MyProfile", Icon = "👤" }
            ]
        }
    ];

    /// <summary>
    /// Grouped navigation menus for the Researcher role in Simple Display Mode.
    /// </summary>
    private static readonly UiNavGroupDescriptor[] ResearcherNavGroups =
    [
        new UiNavGroupDescriptor
        {
            GroupId = "design-sim",
            LabelKey = "Nav.Group.DesignSimulate",
            Icon = "🧪",
            Items =
            [
                new UiNavItemDescriptor { Route = "/wizard", LabelKey = "Nav.GuidedWizard", Icon = "🧬" },
                new UiNavItemDescriptor { Route = "/simulations", LabelKey = "Nav.Simulations", Icon = "🧪" },
                new UiNavItemDescriptor { Route = "/endocrine", LabelKey = "Nav.Endocrine", Icon = "⚖️" },
                new UiNavItemDescriptor { Route = "/trajectories", LabelKey = "Nav.TrajectoryBrowser", Icon = "📁" }
            ]
        },
        new UiNavGroupDescriptor
        {
            GroupId = "clinical-lab",
            LabelKey = "Nav.Group.ClinicalLab",
            Icon = "⏳",
            Items =
            [
                new UiNavItemDescriptor { Route = "/depot-studio", LabelKey = "Nav.DepotStudio", Icon = "⏳" },
                new UiNavItemDescriptor { Route = "/dna", LabelKey = "Nav.DnaAndImports", Icon = "🧬" },
                new UiNavItemDescriptor { Route = "/compliance", LabelKey = "Nav.Compliance", Icon = "🔒" }
            ]
        },
        new UiNavGroupDescriptor
        {
            GroupId = "account",
            LabelKey = "Nav.Group.Account",
            Icon = "👤",
            Items =
            [
                new UiNavItemDescriptor { Route = "/profile", LabelKey = "Nav.MyProfile", Icon = "👤" }
            ]
        }
    ];

    /// <summary>
    /// Grouped navigation menus for the Administrator role in Simple Display Mode.
    /// </summary>
    private static readonly UiNavGroupDescriptor[] AdministratorNavGroups =
    [
        ResearcherNavGroups[0],
        ResearcherNavGroups[1],
        new UiNavGroupDescriptor
        {
            GroupId = "administration",
            LabelKey = "Nav.Group.Administration",
            Icon = "🛡️",
            Items =
            [
                new UiNavItemDescriptor { Route = "/users", LabelKey = "Nav.UserManagement", Icon = "👥" },
                new UiNavItemDescriptor { Route = "/services", LabelKey = "Nav.StackServices", Icon = "⚙️" },
                new UiNavItemDescriptor { Route = "/configuration", LabelKey = "Nav.ConfigAndPeers", Icon = "🛡️" }
            ]
        },
        ResearcherNavGroups[2]
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="UiPreferenceService"/> class.
    /// </summary>
    /// <param name="repository">Repository storing per-user UI display overrides.</param>
    /// <param name="configRepository">Repository supplying the node-wide <c>Ui:DefaultDisplayMode</c> setting.</param>
    /// <param name="auditService">Compliance audit service logging display mode changes.</param>
    public UiPreferenceService(
        IUserUiPreferenceRepository repository,
        IConfigurationRepository configRepository,
        IComplianceAuditService auditService)
    {
        // WHAT: Validates and captures repository, configuration, and audit dependencies.
        // WHY:  Prevents null references when resolving UI display modes or logging audit events.
        // HOW:  Guards each parameter with ArgumentNullException.ThrowIfNull.
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(configRepository);
        ArgumentNullException.ThrowIfNull(auditService);
        _repository = repository;
        _configRepository = configRepository;
        _auditService = auditService;
    }

    /// <inheritdoc/>
    public async Task<UiDisplayMode> GetNodeDefaultDisplayModeAsync(CancellationToken ct = default)
    {
        // WHAT: Reads Ui:DefaultDisplayMode from node configuration and converts it to UiDisplayMode.
        // WHY:  Lets administrators choose the default complexity for newly provisioned users without overriding personal choices.
        // HOW:  Fetches configuration string and treats trimmed "Advanced" (case-insensitive) as Advanced, otherwise Simple.
        string? raw = await _configRepository.GetValueAsync(DefaultDisplayModeConfigKey, nameof(UiDisplayMode.Simple), ct);
        return string.Equals(raw?.Trim(), nameof(UiDisplayMode.Advanced), StringComparison.OrdinalIgnoreCase)
            ? UiDisplayMode.Advanced
            : UiDisplayMode.Simple;
    }

    /// <inheritdoc/>
    public async Task<EffectiveUiPreference> GetEffectivePreferenceAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Resolves the effective display mode for username by checking saved override against node default.
        // WHY:  Ensures a returning user always sees their chosen mode while first-time users inherit Ui:DefaultDisplayMode.
        // HOW:  Fetches node default and user preference record, returning user override when present.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        UiDisplayMode nodeDefault = await GetNodeDefaultDisplayModeAsync(ct);
        UserUiPreferenceRecord? saved = await _repository.GetByUsernameAsync(username, ct);

        if (saved != null)
        {
            return new EffectiveUiPreference
            {
                Username = saved.Username,
                DisplayMode = saved.DisplayMode,
                IsUserOverride = true,
                NodeDefaultDisplayMode = nodeDefault,
                UpdatedAtUtc = saved.UpdatedAtUtc
            };
        }

        return new EffectiveUiPreference
        {
            Username = username.Trim().ToLowerInvariant(),
            DisplayMode = nodeDefault,
            IsUserOverride = false,
            NodeDefaultDisplayMode = nodeDefault,
            UpdatedAtUtc = null
        };
    }

    /// <inheritdoc/>
    public async Task<EffectiveUiPreference> SetUserDisplayModeAsync(
        string username,
        UiDisplayMode displayMode,
        CancellationToken ct = default)
    {
        // WHAT: Persists a personal UiDisplayMode preference for username and appends an audit record.
        // WHY:  Preserves user choice across restarts while maintaining 21 CFR Part 11 configuration traceability.
        // HOW:  Validates username and enum bounds, upserts repository row, logs audit event, and returns effective view.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        if (!Enum.IsDefined(displayMode))
        {
            throw new ArgumentOutOfRangeException(nameof(displayMode), displayMode, "Display mode must be Simple or Advanced.");
        }

        UiDisplayMode nodeDefault = await GetNodeDefaultDisplayModeAsync(ct);
        UserUiPreferenceRecord saved = await _repository.UpsertAsync(username, displayMode, ct);
        _ = _auditService.LogAction(
            saved.Username,
            "UI_DISPLAY_MODE_CHANGED",
            "ui/preferences",
            $"{{\"displayMode\":\"{displayMode}\"}}");

        return new EffectiveUiPreference
        {
            Username = saved.Username,
            DisplayMode = saved.DisplayMode,
            IsUserOverride = true,
            NodeDefaultDisplayMode = nodeDefault,
            UpdatedAtUtc = saved.UpdatedAtUtc
        };
    }

    /// <inheritdoc/>
    public async Task<EffectiveUiPreference> ResetUserDisplayModeAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Deletes the user's saved personal display preference so they inherit the node default again.
        // WHY:  Allows accounts to opt back into administrator-managed UI defaults at any time.
        // HOW:  Normalizes username, deletes row from weaver_user_preferences, logs reset action, and returns node default.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        string normalized = username.Trim().ToLowerInvariant();
        _ = await _repository.DeleteByUsernameAsync(normalized, ct);
        UiDisplayMode nodeDefault = await GetNodeDefaultDisplayModeAsync(ct);
        _ = _auditService.LogAction(
            normalized,
            "UI_DISPLAY_MODE_RESET",
            "ui/preferences",
            "{\"resetToNodeDefault\":true}");

        return new EffectiveUiPreference
        {
            Username = normalized,
            DisplayMode = nodeDefault,
            IsUserOverride = false,
            NodeDefaultDisplayMode = nodeDefault,
            UpdatedAtUtc = null
        };
    }

    /// <inheritdoc/>
    public IReadOnlyList<UiTaskCardDescriptor> GetTaskCardsForRole(UserRole role, UiDisplayMode displayMode)
    {
        // WHAT: Selects role-tailored Task Hub cards and filters advanced shortcuts unless Advanced Display Mode is active.
        // WHY:  Keeps the default landing experience calm and goal-oriented without overwhelming new users.
        // HOW:  Picks the static array matching UserRole and returns primary items or full items based on displayMode.
        UiTaskCardDescriptor[] source = role switch
        {
            UserRole.Administrator => AdministratorCards,
            UserRole.Researcher => ResearcherCards,
            _ => PatientCards
        };

        return FilterCardsByDisplayMode(source, displayMode);
    }

    /// <inheritdoc/>
    public IReadOnlyList<UiNavGroupDescriptor> GetNavigationGroupsForRole(UserRole role)
    {
        // WHAT: Returns role-filtered grouped navigation menus displayed in Simple Mode.
        // WHY:  Collapses 11 flat links into 1–4 task-oriented dropdown groups appropriate for the user's role.
        // HOW:  Matches UserRole against pre-built static descriptor arrays.
        return role switch
        {
            UserRole.Administrator => AdministratorNavGroups,
            UserRole.Researcher => ResearcherNavGroups,
            _ => PatientNavGroups
        };
    }

    /// <summary>
    /// Filters a static task card array down to primary cards in <see cref="UiDisplayMode.Simple"/>,
    /// or returns every card unmodified in <see cref="UiDisplayMode.Advanced"/>.
    /// </summary>
    /// <param name="source">Role-specific catalog containing primary and advanced cards.</param>
    /// <param name="displayMode">Active display mode controlling shortcut visibility.</param>
    /// <returns>Ordered read-only list of visible task cards.</returns>
    private static UiTaskCardDescriptor[] FilterCardsByDisplayMode(UiTaskCardDescriptor[] source, UiDisplayMode displayMode)
    {
        // WHAT: Filters out advanced-only shortcuts when Simple Display Mode is active.
        // WHY:  Avoids exposing secondary engineering pages on the calm initial Task Hub grid.
        // HOW:  Returns source directly for Advanced mode, or copies non-advanced cards via a single array pass without LINQ.
        if (displayMode == UiDisplayMode.Advanced)
        {
            return source;
        }

        int count = 0;
        for (int i = 0; i < source.Length; i++)
        {
            if (!source[i].RequiresAdvancedDisplay)
            {
                count++;
            }
        }

        UiTaskCardDescriptor[] filtered = new UiTaskCardDescriptor[count];
        int idx = 0;
        for (int i = 0; i < source.Length; i++)
        {
            if (!source[i].RequiresAdvancedDisplay)
            {
                filtered[idx++] = source[i];
            }
        }

        return filtered;
    }

    /// <summary>
    /// Factory helper constructing a <see cref="UiTaskCardDescriptor"/> with consistent defaults.
    /// </summary>
    /// <param name="taskId">Stable DOM task identifier.</param>
    /// <param name="titleKey">Localization key for card headline.</param>
    /// <param name="descriptionKey">Localization key for plain-English summary.</param>
    /// <param name="actionLabelKey">Localization key for CTA button label.</param>
    /// <param name="route">Target route navigated to on click.</param>
    /// <param name="icon">Emoji/symbol icon.</param>
    /// <param name="categoryKey">Localization key for category badge.</param>
    /// <param name="isPrimary">True when card belongs to primary workflow section.</param>
    /// <param name="requiresAdvanced">True when hidden unless Advanced Display Mode is active.</param>
    /// <returns>Constructed <see cref="UiTaskCardDescriptor"/> instance.</returns>
    private static UiTaskCardDescriptor CreateCard(
        string taskId,
        string titleKey,
        string descriptionKey,
        string actionLabelKey,
        string route,
        string icon,
        string categoryKey,
        bool isPrimary,
        bool requiresAdvanced) => new()
        {
            TaskId = taskId,
            TitleKey = titleKey,
            DescriptionKey = descriptionKey,
            ActionLabelKey = actionLabelKey,
            Route = route,
            Icon = icon,
            CategoryKey = categoryKey,
            IsPrimary = isPrimary,
            RequiresAdvancedDisplay = requiresAdvanced
        };
}
