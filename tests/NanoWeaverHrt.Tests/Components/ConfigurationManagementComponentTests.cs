using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Security.Authentication;
using Xunit;

namespace NanoWeaverHrt.Tests.Components;

/// <summary>
/// Unit and component interaction tests verifying administrative access gating, database precedence overrides,
/// empty share-key state verification, and single-use peer key creation and revocation in <see cref="ConfigurationManagement"/>.
/// </summary>
public sealed class ConfigurationManagementComponentTests : TestContext
{
    /// <summary>
    /// Mock configuration repository supplying settings and share-key operations.
    /// </summary>
    private readonly Mock<IConfigurationRepository> _configRepoMock = new();

    /// <summary>
    /// Active user session state controlling caller identity and role.
    /// </summary>
    private readonly UserSessionState _sessionState = new();

    /// <summary>
    /// In-memory list of configurations backing the mock repository.
    /// </summary>
    private readonly List<ConfigurationItem> _configurations;

    /// <summary>
    /// In-memory list of share keys backing the mock repository.
    /// </summary>
    private readonly List<ShareKeyRecord> _shareKeys = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationManagementComponentTests"/> class.
    /// Configures default mock behaviors and registers DI services.
    /// </summary>
    public ConfigurationManagementComponentTests()
    {
        // WHAT: Sets up mock repository state for configuration items and single-use share keys.
        // WHY:  Allows deterministic testing of Administrator access gating, override mutations, and key lifecycles.
        // HOW:  Registers singleton mocks and UserSessionState in bUnit TestContext.Services.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _configurations = new List<ConfigurationItem>
        {
            new()
            {
                Key = "Simulation:AcceleratorBackend",
                Value = "Intel iGPU / OpenCL (Default)",
                DefaultValue = "Intel iGPU / OpenCL (Default)",
                Category = "Simulation",
                Description = "Target OpenCL/SYCL hardware accelerator device.",
                Source = ConfigurationSource.DatabaseOverride,
                IsSecret = false
            },
            new()
            {
                Key = "Storage:PostgresConnectionString",
                Value = "Host=localhost;Database=weaver",
                DefaultValue = "Host=localhost;Database=weaver",
                Category = "Storage",
                Description = "Relational database connection string.",
                Source = ConfigurationSource.DefaultFallback,
                IsSecret = true
            }
        };

        _configRepoMock
            .Setup(c => c.ActiveBackendName)
            .Returns("SQLite (In-Memory)");

        _configRepoMock
            .Setup(c => c.GetAllConfigurationsAsync(default))
            .ReturnsAsync(() => _configurations);

        _configRepoMock
            .Setup(c => c.ListShareKeysAsync(default))
            .ReturnsAsync(() => _shareKeys);

        _configRepoMock
            .Setup(c => c.CreateShareKeyAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<string>(), default))
            .ReturnsAsync((string label, TimeSpan lifetime, string perms, CancellationToken _) =>
            {
                var record = new ShareKeyRecord
                {
                    TokenId = $"tok_{Guid.NewGuid():N}"[..12],
                    KeyToken = $"secret_token_{Guid.NewGuid():N}"[..20],
                    Label = label,
                    CreatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.Add(lifetime),
                    IsConsumed = false,
                    Permissions = perms
                };
                _shareKeys.Add(record);
                return record;
            });

        _configRepoMock
            .Setup(c => c.RevokeShareKeyAsync(It.IsAny<string>(), default))
            .ReturnsAsync((string tokenId, CancellationToken _) =>
            {
                int removed = _shareKeys.RemoveAll(k => k.TokenId == tokenId);
                return removed > 0;
            });

        Services.AddSingleton<IConfigurationRepository>(_configRepoMock.Object);
        Services.AddSingleton(_sessionState);
    }

    /// <summary>
    /// Verifies that rendering the configuration page while unauthenticated displays the authentication required alert.
    /// </summary>
    [Fact]
    public void Unauthenticated_DisplaysAuthenticationRequiredAlert()
    {
        // WHAT: Tests access gating for anonymous unauthenticated visitors.
        // WHY:  System configurations and cluster topology must never be accessible without authentication.
        // HOW:  Renders ConfigurationManagement with empty session state and asserts warning alert.
        IRenderedComponent<ConfigurationManagement> cut = RenderComponent<ConfigurationManagement>();

        var alert = cut.Find(".alert-warning");
        Assert.Contains("Authentication Required", alert.TextContent);
        Assert.DoesNotContain("Database Precedence", cut.Markup);
    }

    /// <summary>
    /// Verifies that an authenticated non-administrator user receives the access denied alert.
    /// </summary>
    [Fact]
    public void AuthenticatedNonAdmin_DisplaysAccessDeniedAlert()
    {
        // WHAT: Tests role-based access gating for researchers and patients.
        // WHY:  Only Administrator accounts may inspect or modify cluster settings or issue share keys.
        // HOW:  Sets SessionState user role to Researcher and asserts access denied alert.
        _sessionState.SetUser(new UserAccount
        {
            Username = "researcher_jane",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.Web,
            PublicKeyBase64 = "MIIB_res_key"
        });

        IRenderedComponent<ConfigurationManagement> cut = RenderComponent<ConfigurationManagement>();

        var alert = cut.Find(".alert-danger");
        Assert.Contains("Access Denied", alert.TextContent);
        Assert.Contains("Administrator", alert.TextContent);
        Assert.DoesNotContain("Database Precedence", cut.Markup);
    }

    /// <summary>
    /// Verifies that an administrator can view configuration parameters, categories, and database precedence instructions.
    /// </summary>
    [Fact]
    public void Administrator_RendersConfigurationsAndDatabasePrecedenceRules()
    {
        // WHAT: Tests complete configuration table rendering for Administrator sessions.
        // WHY:  Administrators must see all configurable parameters grouped by category.
        // HOW:  Sets Administrator session and verifies backend badge, categories, and parameter rows.
        _sessionState.SetUser(new UserAccount
        {
            Username = "cluster_admin",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.Web,
            PublicKeyBase64 = "MIIB_admin_key"
        });

        IRenderedComponent<ConfigurationManagement> cut = RenderComponent<ConfigurationManagement>();

        Assert.Contains("BACKEND: SQLite (In-Memory)", cut.Markup);
        Assert.Contains("Database Precedence", cut.Markup);
        Assert.Contains("Simulation:AcceleratorBackend", cut.Markup);
        Assert.Contains("Storage:PostgresConnectionString", cut.Markup);
    }

    /// <summary>
    /// Verifies that an empty share-key repository displays the empty state message and does not auto-seed demo keys.
    /// </summary>
    [Fact]
    public void EmptyShareKeys_DisplaysEmptyState_ZeroAutoSeededKeys()
    {
        // WHAT: Tests empty share key table behavior.
        // WHY:  Fulfills Issue #155 (P152) requirement: zero auto-seeded demo keys when key list is empty.
        // HOW:  Renders page with empty _shareKeys collection and verifies empty-state table text.
        _sessionState.SetUser(new UserAccount
        {
            Username = "cluster_admin",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.Web,
            PublicKeyBase64 = "MIIB_admin_key"
        });

        IRenderedComponent<ConfigurationManagement> cut = RenderComponent<ConfigurationManagement>();

        Assert.Contains("No share links have been generated yet", cut.Markup);
        Assert.DoesNotContain("Demo Collaborator", cut.Markup);
    }

    /// <summary>
    /// Verifies that an administrator can create a new single-use share key, generating an invitation link,
    /// and subsequently revoke it, returning the table to the empty state.
    /// </summary>
    [Fact]
    public async Task CreateAndRevokeShareKey_OperatesCleanlyWithoutResidualKeys()
    {
        // WHAT: Tests end-to-end share key generation and revocation lifecycle in ConfigurationManagement.
        // WHY:  Enables secure peer node federation bootstrapping via ephemeral share links.
        // HOW:  Clicks Generate Single-Use Share Link, asserts invitation link display, clicks Revoke, and asserts empty state.
        _sessionState.SetUser(new UserAccount
        {
            Username = "cluster_admin",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.Web,
            PublicKeyBase64 = "MIIB_admin_key"
        });

        IRenderedComponent<ConfigurationManagement> cut = RenderComponent<ConfigurationManagement>();

        // 1. Generate key
        var createBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Generate Single-Use Share Link"));
        await cut.InvokeAsync(() => createBtn.Click());

        _configRepoMock.Verify(c => c.CreateShareKeyAsync(
            "Remote Analysis Instance",
            TimeSpan.FromHours(24),
            "FullCollaborator",
            default), Times.Once);

        Assert.Contains("/connect?token=", cut.Markup);
        Assert.Single(_shareKeys);

        // 2. Revoke key
        var revokeBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Revoke"));
        await cut.InvokeAsync(() => revokeBtn.Click());

        _configRepoMock.Verify(c => c.RevokeShareKeyAsync(It.IsAny<string>(), default), Times.Once);
        Assert.Empty(_shareKeys);

        var emptyMsg = cut.FindAll(".data-table").Last();
        Assert.Contains("No share links have been generated yet", emptyMsg.TextContent);
    }

    /// <summary>
    /// Verifies that saving an override invokes <see cref="IConfigurationRepository.SetDatabaseOverrideAsync"/>
    /// and that clicking revert invokes <see cref="IConfigurationRepository.RevertToDefaultAsync"/>.
    /// </summary>
    [Fact]
    public async Task SaveOverrideAndRevertDefault_InvokesRepositoryMethods()
    {
        // WHAT: Tests saving runtime database overrides and reverting to defaults.
        // WHY:  Validates "Database Wins" runtime overrides without taking down containers.
        // HOW:  Clicks Save DB on a parameter, then clicks Revert, asserting repository method calls.
        _sessionState.SetUser(new UserAccount
        {
            Username = "cluster_admin",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.Web,
            PublicKeyBase64 = "MIIB_admin_key"
        });

        IRenderedComponent<ConfigurationManagement> cut = RenderComponent<ConfigurationManagement>();

        var saveBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Save DB"));
        await cut.InvokeAsync(() => saveBtn.Click());

        _configRepoMock.Verify(c => c.SetDatabaseOverrideAsync(
            "Simulation:AcceleratorBackend",
            "Intel iGPU / OpenCL (Default)",
            "",
            default), Times.Once);

        var revertBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Revert"));
        await cut.InvokeAsync(() => revertBtn.Click());

        _configRepoMock.Verify(c => c.RevertToDefaultAsync(
            "Simulation:AcceleratorBackend",
            default), Times.Once);
    }
}
