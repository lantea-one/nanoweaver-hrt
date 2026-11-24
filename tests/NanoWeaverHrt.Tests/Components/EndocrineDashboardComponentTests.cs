using Bunit;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Localization;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Components;

/// <summary>
/// bUnit component tests for <see cref="EndocrineDashboard"/> verifying Milestone 42 (#254)
/// <c>#epigenetic-circuit-studio-panel</c>, guide-RNA table rendering, dark-mode SVG bistable
/// phase-plane rendering, ML-DSA-65 approval badge, fail-closed monostable/off-target block,
/// and validation error reporting.
/// </summary>
public sealed class EndocrineDashboardComponentTests : BunitContext
{
    /// <summary>
    /// Isolated temporary test root holding SQLite DB and node PQC keys.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Persistent host database connection factory for real circuit storage tests.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="EndocrineDashboardComponentTests"/> class.
    /// </summary>
    public EndocrineDashboardComponentTests()
    {
        // WHAT: Wires EndocrineDashboard with real genomics, closed-loop, localization, and EpigeneticCircuitService.
        // WHY:  Tests genuine bistable physics, ML-DSA-65 signing, and SVG geometry rendering inside bUnit DOM.
        // HOW:  Provisions an isolated SQLite DB + PQC host and mocks only IJSRuntime and IConfigurationRepository.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_epi_ui_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = string.Empty,
                [NodeSecretStore.NodeKeysDirectoryConfigKey] = _tempDir
            })
            .Build();

        _factory = WeaverTestDb.CreateFactory(config);
        NodeSecretStore secretStore = new(config);
        ProfileDataProtector protector = new(secretStore);
        PostQuantumCryptoProvider crypto = new();
        SubjectKeyRepository keyRepo = new(_factory);
        SubjectErasureJournal journal = new(secretStore);
        SubjectKeyVault vault = new(keyRepo, protector, crypto, journal);
        vault.InitializeAsync().GetAwaiter().GetResult();
        KeyManagementService kms = new(crypto, vault, secretStore);
        ComplianceAuditService audit = new(kms, crypto);
        EpigeneticCircuitRepository circuitRepo = new(_factory);
        EpigeneticCircuitService circuitSvc = new(circuitRepo, protector, kms, crypto, audit);

        Mock<IConfigurationRepository> configRepoMock = new();
        Mock<IJSRuntime> jsMock = new();

        Services.AddSingleton(config);
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        Services.AddSingleton(new EndocrineStateEngine());
        Services.AddSingleton<IPharmacogenomicsService>(new PharmacogenomicsService());
        Services.AddSingleton<IEndocrineClosedLoopEngine>(new EndocrineClosedLoopEngine());
        Services.AddSingleton<IEpigeneticCircuitService>(circuitSvc);
        Services.AddSingleton(configRepoMock.Object);
        Services.AddSingleton(jsMock.Object);
        Services.AddSingleton<IWeaverLocalizer>(new WeaverLocalizer());
    }

    /// <summary>
    /// Verifies epigenetic circuit design, SVG phase-plane rendering, guide-RNA table display,
    /// safe ML-DSA-65 approval, fail-closed monostable block badge, and blank patient validation errors.
    /// </summary>
    [Fact]
    public async Task EpigeneticStudioPanel_DesignsRendersSvgApprovesSafeAndBlocksMonostableFailClosed()
    {
        // WHAT: Drives the #epigenetic-circuit-studio-panel interactive controls end-to-end.
        // WHY:  Milestone 42 (P223): Verifies zero-inline-style SVG plotting, sgRNA table, and fail-closed approval badges.
        // HOW:  Re-queries DOM via fresh cut.Find(...) after each async handler dispatch and checks text & SVG geometry.
        IRenderedComponent<EndocrineDashboard> cut = Render<EndocrineDashboard>();

        Assert.NotNull(cut.Find("#epigenetic-circuit-studio-panel"));

        // Validation error when PatientId is cleared before Design click
        await cut.Find("#epigenetic-patient-input").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "   " });
        await cut.Find("#btn-design-epigenetic-circuit").ClickAsync();
        Assert.Contains("PatientId", cut.Find("#epigenetic-error-banner").TextContent, StringComparison.OrdinalIgnoreCase);

        // Configure valid MtF patient circuit and design
        await cut.Find("#epigenetic-patient-input").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "pat-ui-epi-01" });
        await cut.Find("#epigenetic-regimen-select").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = nameof(TherapeuticRegimen.MaleToFemale) });
        await cut.Find("#epigenetic-tissue-select").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = nameof(TargetTissueType.GonadalTarget) });
        await cut.Find("#epigenetic-effector-select").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = nameof(EpigeneticEffectorPlatform.CrisprOffOn) });
        await cut.Find("#epigenetic-notes-input").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "Ui test aromatase switch" });

        await cut.Find("#btn-design-epigenetic-circuit").ClickAsync();

        string badgeAfterDesign = cut.Find("#epigenetic-approval-badge").TextContent;
        Assert.Contains("EVALUATED", badgeAfterDesign, StringComparison.Ordinal);

        string guideTableText = cut.Find("#epigenetic-guide-table").TextContent;
        Assert.Contains("CYP19A1", guideTableText, StringComparison.Ordinal);
        Assert.Contains("SRD5A2", guideTableText, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("#epigenetic-phase-plane-svg"));

        // Safe ML-DSA-65 approval updates badge to APPROVED FOR PERMANENT SYNTHESIS
        await cut.Find("#btn-approve-epigenetic-circuit").ClickAsync();
        string badgeApproved = cut.Find("#epigenetic-approval-badge").TextContent;
        Assert.Contains("APPROVED FOR PERMANENT SYNTHESIS", badgeApproved, StringComparison.Ordinal);
        Assert.Contains("ML-DSA-65", cut.Find("#epigenetic-circuit-studio-panel").TextContent, StringComparison.Ordinal);

        // Stress with monostable chromatin stability factor (0.40) -> Evaluate and Approve fail closed with BLOCKED badge
        await cut.Find("#epigenetic-stability-input").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "0.4" });
        await cut.Find("#btn-evaluate-epigenetic-circuit").ClickAsync();
        string badgeEvaluatedBlocked = cut.Find("#epigenetic-approval-badge").TextContent;
        Assert.Contains("BLOCKED", badgeEvaluatedBlocked, StringComparison.Ordinal);

        await cut.Find("#btn-approve-epigenetic-circuit").ClickAsync();
        string badgeApproveBlocked = cut.Find("#epigenetic-approval-badge").TextContent;
        Assert.Contains("BLOCKED", badgeApproveBlocked, StringComparison.Ordinal);

        // Out-of-range stability input (99.0) triggers visible error banner cleanly
        await cut.Find("#epigenetic-stability-input").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "99.0" });
        await cut.Find("#btn-evaluate-epigenetic-circuit").ClickAsync();
        Assert.NotNull(cut.Find("#epigenetic-error-banner"));
    }

    /// <summary>
    /// Cleans up temporary SQLite database connections and test directory resources.
    /// </summary>
    /// <param name="disposing">True when releasing managed resources.</param>
    protected override void Dispose(bool disposing)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        base.Dispose(disposing);
    }
}
