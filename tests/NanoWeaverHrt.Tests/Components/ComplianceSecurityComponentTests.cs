using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Localization;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Components;

/// <summary>
/// Unit and component interaction tests verifying the post-quantum compliance and cryptographic
/// ledger dashboard in <see cref="ComplianceSecurity"/>.
/// </summary>
public sealed class ComplianceSecurityComponentTests : BunitContext
{
    /// <summary>
    /// Mock compliance audit service managing ledger status, verification, and HIPAA de-identification.
    /// </summary>
    private readonly Mock<IComplianceAuditService> _complianceMock = new();

    /// <summary>
    /// Mock key management service handling GDPR Article 17 crypto-shredding operations.
    /// </summary>
    private readonly Mock<IKeyManagementService> _keyManagementMock = new();

    /// <summary>
    /// Mock encrypted storage service injected into the page.
    /// </summary>
    private readonly Mock<IEncryptedStorageService> _storageMock = new();

    /// <summary>
    /// Mock regulatory dossier compiler and PQC audit bundle verification service.
    /// </summary>
    private readonly Mock<IRegulatoryDossierService> _dossierMock = new();

    /// <summary>
    /// Active user session state tracking authenticated operator credentials.
    /// </summary>
    private readonly UserSessionState _sessionState = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplianceSecurityComponentTests"/> class.
    /// Configures default mock behaviors and registers DI services.
    /// </summary>
    public ComplianceSecurityComponentTests()
    {
        // WHAT: Prepares mock responses for compliance status, audit records, and key management.
        // WHY:  Permits testing UI renders, ledger checks, de-identification, and crypto-shredding deterministically.
        // HOW:  Registers singleton mocks and UserSessionState in bUnit's BunitContext.Services.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _complianceMock
            .Setup(c => c.GetComplianceStatus())
            .Returns(new ComplianceStatusReport
            {
                OverallStatus = "Optimal",
                EncryptionAtRestAlgorithm = "NIST FIPS 203 (ML-KEM-768)",
                IsAuditLedgerIntact = true,
                AuditRecordCount = 12,
                TotalSubjectsRegistered = 5,
                CryptoShreddedSubjectCount = 1,
                AuditSignatureAlgorithm = "NIST FIPS 204 (ML-DSA-65)"
            });

        _complianceMock
            .Setup(c => c.GetAuditRecords(It.IsAny<int>()))
            .Returns(new List<AuditRecord>
            {
                new()
                {
                    SequenceNumber = 1,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    OperatorId = "admin_auditor",
                    Action = "GenesisInit",
                    Resource = "weaver_root",
                    PreviousHash = "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000",
                    CurrentHash = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90",
                    Signature = new PqcSignature
                    {
                        Algorithm = "ML-DSA-65",
                        SignerKeyId = "key_01",
                        SignatureBase64 = "MIIB1234567890"
                    },
                    DetailsJson = "{\"msg\":\"init\"}"
                }
            });

        _complianceMock
            .Setup(c => c.VerifyLedgerIntegrity())
            .Returns(new AuditLedgerVerificationResult
            {
                IsValid = true,
                TotalRecordsVerified = 12,
                Message = "Cryptographic ledger verified successfully: 12 records intact."
            });

        _complianceMock
            .Setup(c => c.PseudonymizeSubjectId(It.IsAny<string>()))
            .Returns<string>(id => $"pseudo_{id}");

        _complianceMock
            .Setup(c => c.DeIdentifySubject(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(new DeIdentificationResult
            {
                PseudonymSubjectId = "sub_98432_pseudo",
                StrippedIdentifiers = ["Jane Doe", "MRN-98432", "HRT-Lab"],
                SanitizedData = ">PSEUDONYM: sub_98432_pseudo\nATGCGATCGATCGATCGATCGATC",
                GdprConsentConfirmed = true,
                GinaSafeguardVerified = true
            });

        _keyManagementMock
            .Setup(k => k.CryptoShredSubjectKeyAsync(It.IsAny<string>(), default))
            .ReturnsAsync(SubjectErasureOutcome.Erased);

        Services.AddSingleton<IComplianceAuditService>(_complianceMock.Object);
        Services.AddSingleton<IKeyManagementService>(_keyManagementMock.Object);
        Services.AddSingleton<IEncryptedStorageService>(_storageMock.Object);
        Services.AddSingleton<IRegulatoryDossierService>(_dossierMock.Object);
        Services.AddSingleton<IWeaverLocalizer>(new WeaverLocalizer());
        Services.AddSingleton(_sessionState);
    }

    /// <summary>
    /// Verifies that <see cref="ComplianceSecurity"/> renders status cards, telemetry indicators, and audit table cleanly.
    /// </summary>
    [Fact]
    public void ComplianceSecurity_RendersInitialTelemetryAndAuditRecords()
    {
        // WHAT: Tests component initial render and telemetry dashboard display.
        // WHY:  Ensures operators see current encryption, audit integrity, and record counts.
        // HOW:  Renders component and asserts markup contains expected algorithm badges and audit entries.
        IRenderedComponent<ComplianceSecurity> cut = Render<ComplianceSecurity>();

        Assert.Contains("NIST FIPS 203 &amp; 204 Lattice Cryptography", cut.Markup);
        Assert.Contains("OPTIMAL", cut.Markup);
        Assert.Contains("NIST FIPS 203 (ML-KEM-768)", cut.Markup);
        Assert.Contains("INTACT", cut.Markup);
        Assert.Contains("admin_auditor", cut.Markup);
        Assert.Contains("GenesisInit", cut.Markup);
    }

    /// <summary>
    /// Verifies that clicking the ledger integrity button calls <see cref="IComplianceAuditService.VerifyLedgerIntegrity"/>
    /// and displays the verification alert.
    /// </summary>
    [Fact]
    public async Task VerifyLedger_InvokesAuditServiceAndDisplaysSuccessAlert()
    {
        // WHAT: Tests ledger integrity verification user interaction in ComplianceSecurity.
        // WHY:  Enforces FDA 21 CFR Part 11 audit verification visibility.
        // HOW:  Clicks Verify Ledger button and asserts alert message matches verification result.
        IRenderedComponent<ComplianceSecurity> cut = Render<ComplianceSecurity>();

        var verifyBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Verify"));
        await cut.InvokeAsync(() => verifyBtn.Click());

        _complianceMock.Verify(c => c.VerifyLedgerIntegrity(), Times.Once);
        var alert = cut.Find(".alert-success");
        Assert.Contains("Cryptographic ledger verified successfully", alert.TextContent);
    }

    /// <summary>
    /// Verifies that HIPAA Safe Harbor de-identification processes raw identifiers and fasta data,
    /// populates the target shred ID, and displays the issued pseudonym alert.
    /// </summary>
    [Fact]
    public async Task ExecuteDeIdentification_ProcessesInputsAndDisplaysIssuedPseudonym()
    {
        // WHAT: Tests de-identification workflow in ComplianceSecurity.
        // WHY:  Validates HIPAA § 164.514 direct identifier stripping before downstream processing.
        // HOW:  Clicks De-Identify button and verifies pseudonym alert display and stripped count.
        IRenderedComponent<ComplianceSecurity> cut = Render<ComplianceSecurity>();

        var deIdBtn = cut.FindAll("button").First(b => b.TextContent.Contains("De-Identify"));
        await cut.InvokeAsync(() => deIdBtn.Click());

        _complianceMock.Verify(c => c.DeIdentifySubject(
            It.IsAny<string>(),
            It.IsAny<string>(),
            true,
            true), Times.Once);

        var alert = cut.Find(".alert-success");
        Assert.Contains("De-Identification Success", alert.TextContent);
        Assert.Contains("sub_98432_pseudo", alert.TextContent);
        Assert.Contains("Stripped Direct Identifiers: 3", alert.TextContent);
    }

    /// <summary>
    /// Verifies that executing GDPR Article 17 crypto-shredding attributes the action to the authenticated session operator
    /// and logs the pseudonymized subject ID to the audit ledger.
    /// </summary>
    [Fact]
    public async Task ExecuteCryptoShred_RecordsAuthenticatedOperatorAndPseudonym()
    {
        // WHAT: Tests crypto-shredding key destruction and audit log attribution.
        // WHY:  Issue #155 (P152) requirement: records operator username from SessionState and pseudonymized subject ID.
        // HOW:  Sets authenticated operator in SessionState, clicks crypto-shred, and asserts service calls.
        _sessionState.SetUser(new UserAccount
        {
            Username = "compliance_officer_42",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.Web,
            PublicKeyBase64 = "MIIB..."
        });

        IRenderedComponent<ComplianceSecurity> cut = Render<ComplianceSecurity>();

        // Trigger de-identification first so _targetShredSubjectId is populated
        var deIdBtn = cut.FindAll("button").First(b => b.TextContent.Contains("De-Identify"));
        await cut.InvokeAsync(() => deIdBtn.Click());

        // Click crypto-shred button
        var shredBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Crypto-Shred"));
        await cut.InvokeAsync(() => shredBtn.Click());

        _keyManagementMock.Verify(k => k.CryptoShredSubjectKeyAsync("sub_98432_pseudo", default), Times.Once);

        _complianceMock.Verify(c => c.LogAction(
            "compliance_officer_42",
            "CryptoShredSubjectKey",
            "pseudo_sub_98432_pseudo",
            It.Is<string>(m => m.Contains("GDPR_Article_17"))), Times.Once);

        var alert = cut.FindAll(".alert-success").Last();
        Assert.Contains(Program.ShredOkMessage, alert.TextContent);
    }

    /// <summary>
    /// Verifies that submitting an empty subject identifier for crypto-shredding displays a validation error.
    /// </summary>
    [Fact]
    public async Task ExecuteCryptoShred_EmptySubjectId_DisplaysValidationError()
    {
        // WHAT: Tests validation when target subject ID is empty.
        // WHY:  Prevents issuing crypto-shred requests without a target identifier.
        // HOW:  Clicks crypto-shred without populating subject ID and verifies warning alert.
        IRenderedComponent<ComplianceSecurity> cut = Render<ComplianceSecurity>();

        var shredBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Crypto-Shred"));
        await cut.InvokeAsync(() => shredBtn.Click());

        var alerts = cut.FindAll(".alert-warning");
        Assert.Contains(alerts, a => a.TextContent.Contains("Please specify a valid subject identifier"));
        _keyManagementMock.Verify(k => k.CryptoShredSubjectKeyAsync(It.IsAny<string>(), default), Times.Never);
    }

    /// <summary>
    /// Verifies that crypto-shredding an already erased or unknown key displays the appropriate status message.
    /// </summary>
    [Fact]
    public async Task ExecuteCryptoShred_AlreadyErasedOrNotFound_DisplaysOutcomeMessage()
    {
        // WHAT: Tests AlreadyErased and NotFound outcomes in crypto-shredding.
        // WHY:  Ensures operators receive clear feedback when keys have already been shredded or do not exist.
        // HOW:  Sets mock to return AlreadyErased, invokes shred, and asserts outcome alert.
        _keyManagementMock
            .Setup(k => k.CryptoShredSubjectKeyAsync(It.IsAny<string>(), default))
            .ReturnsAsync(SubjectErasureOutcome.AlreadyErased);

        IRenderedComponent<ComplianceSecurity> cut = Render<ComplianceSecurity>();

        // Trigger de-identification to populate target ID
        var deIdBtn = cut.FindAll("button").First(b => b.TextContent.Contains("De-Identify"));
        await cut.InvokeAsync(() => deIdBtn.Click());

        var shredBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Crypto-Shred"));
        await cut.InvokeAsync(() => shredBtn.Click());

        var alerts = cut.FindAll(".alert-warning");
        Assert.Contains(alerts, a => a.TextContent.Contains(Program.ShredAlreadyMessage("sub_98432_pseudo")));
    }

    /// <summary>
    /// Verifies that submitting the dossier compiler with a blank patient ID fails closed with a validation alert
    /// and never dispatches a compilation request to <see cref="IRegulatoryDossierService"/>.
    /// </summary>
    [Fact]
    public async Task CompileRegulatoryDossier_EmptyPatientId_ShowsValidationError()
    {
        // WHAT: Renders ComplianceSecurity and clicks #btn-compile-dossier without filling #input-dossier-patient-id.
        // WHY:  Prevents empty subject queries from hitting the compilation service or audit ledger.
        // HOW:  Clicks compile button via InvokeAsync, queries fresh DOM #dossier-error, and verifies Times.Never on mock.
        IRenderedComponent<ComplianceSecurity> cut = Render<ComplianceSecurity>();

        var compileBtn = cut.Find("#btn-compile-dossier");
        await cut.InvokeAsync(() => compileBtn.Click());

        var errorBox = cut.Find("#dossier-error");
        Assert.Contains("Subject identifier is required", errorBox.TextContent);
        Assert.Empty(cut.FindAll("#dossier-compiled-card"));
        _dossierMock.Verify(
            s => s.CompileDossierAsync(It.IsAny<string>(), It.IsAny<CompileRegulatoryDossierRequest?>(), It.IsAny<string>(), default),
            Times.Never);
    }

    /// <summary>
    /// Verifies complete FDA IND compilation rendering 10 passing readiness gates, SUBMISSION READY badge,
    /// and signed bundle JSON browser export prefilling the cross-node verifier textarea.
    /// </summary>
    [Fact]
    public async Task CompileAndDownloadRegulatoryDossier_ReadyFdaInd_RendersTenGatesAndInvokesDownload()
    {
        // WHAT: Compiles a 100% ready FDA IND dossier via UI controls and triggers signed JSON download.
        // WHY:  Ensures wet-lab operators see the 10-gate readiness checklist, HIPAA pseudonym, and offline bundle export.
        // HOW:  Fills input fields, mocks CompileDossierAsync returning 10 passing gates, clicks download, and asserts JS interop.
        SignedRegulatoryDossierBundle readyBundle = CreateSampleDossierBundle(
            "ind_ui_ready",
            RegulatorySubmissionFramework.FdaInd,
            isReady: true);

        _dossierMock
            .Setup(s => s.CompileDossierAsync(
                "patient_ind_ui",
                It.Is<CompileRegulatoryDossierRequest>(r =>
                    r.SubmissionFramework == RegulatorySubmissionFramework.FdaInd
                    && r.LotId == "lot_ui_01"
                    && r.SponsorName == "Lantea Clinical"
                    && r.ClinicalTrialSiteCode == "SITE-09"),
                It.IsAny<string>(),
                default))
            .ReturnsAsync(readyBundle);

        IRenderedComponent<ComplianceSecurity> cut = Render<ComplianceSecurity>();

        cut.Find("#input-dossier-patient-id").Change("patient_ind_ui");
        cut.Find("#input-dossier-lot-id").Change("lot_ui_01");
        cut.Find("#input-dossier-sponsor").Change("Lantea Clinical");
        cut.Find("#input-dossier-site-code").Change("SITE-09");

        var compileBtn = cut.Find("#btn-compile-dossier");
        await cut.InvokeAsync(() => compileBtn.Click());

        var badge = cut.Find("#dossier-readiness-badge");
        Assert.Contains("dossier-badge-ready", badge.ClassName);
        Assert.Contains("SUBMISSION READY", badge.TextContent);

        var gateRows = cut.FindAll("#dossier-readiness-checklist tbody tr");
        Assert.Equal(10, gateRows.Count);
        Assert.Contains("100.0%", cut.Find("#dossier-compiled-card").TextContent);
        Assert.Contains("ind_ui_ready", cut.Find("#dossier-status").TextContent);
        Assert.Empty(cut.FindAll("#dossier-blocking-reasons"));

        var downloadBtn = cut.Find("#btn-download-dossier");
        await cut.InvokeAsync(() => downloadBtn.Click());

        JSInterop.VerifyInvoke("weaverDownloadBlob");
        var verifyTextarea = cut.Find("#textarea-verify-dossier-json");
        string textareaValue = verifyTextarea.GetAttribute("value") ?? verifyTextarea.TextContent;
        Assert.Contains("ind_ui_ready", textareaValue);
    }

    /// <summary>
    /// Verifies fail-closed EMA IMPD UI state when mandatory clinical or CMC readiness gates fail.
    /// </summary>
    [Fact]
    public async Task CompileRegulatoryDossier_IncompleteEmaImpd_RendersBlockedBadgeAndReasons()
    {
        // WHAT: Selects EMA IMPD and mocks a blocked compilation with unmet readiness gates and blocking reasons.
        // WHY:  Guarantees fail-closed visual feedback prevents premature regulatory filing when safety prerequisites fail.
        // HOW:  Changes #select-dossier-framework to EmaImpd, compiles, and asserts #dossier-readiness-badge & blockers list.
        SignedRegulatoryDossierBundle blockedBundle = CreateSampleDossierBundle(
            "impd_ui_blocked",
            RegulatorySubmissionFramework.EmaImpd,
            isReady: false);

        _dossierMock
            .Setup(s => s.CompileDossierAsync(
                "patient_blocked",
                It.Is<CompileRegulatoryDossierRequest>(r => r.SubmissionFramework == RegulatorySubmissionFramework.EmaImpd),
                It.IsAny<string>(),
                default))
            .ReturnsAsync(blockedBundle);

        IRenderedComponent<ComplianceSecurity> cut = Render<ComplianceSecurity>();

        cut.Find("#input-dossier-patient-id").Change("patient_blocked");
        cut.Find("#select-dossier-framework").Change(nameof(RegulatorySubmissionFramework.EmaImpd));

        var compileBtn = cut.Find("#btn-compile-dossier");
        await cut.InvokeAsync(() => compileBtn.Click());

        var badge = cut.Find("#dossier-readiness-badge");
        Assert.Contains("dossier-badge-blocked", badge.ClassName);
        Assert.Contains("BLOCKED / INCOMPLETE", badge.TextContent);

        var blockers = cut.Find("#dossier-blocking-reasons");
        Assert.Contains("Missing active paired reversal antidote.", blockers.TextContent);
    }

    /// <summary>
    /// Verifies client JSON validation guards plus trusted-peer and revoked-signer verification badges.
    /// </summary>
    [Fact]
    public async Task VerifyRegulatoryDossier_JsonValidationAndCrossNodeTrustStates_RenderExpectedBadges()
    {
        // WHAT: Exercises blank input, invalid JSON, missing manifest, trusted peer verification, and revoked signer verification.
        // WHY:  Ensures external audit bundles undergo strict structural parsing and fail-closed ML-DSA-65 trust display.
        // HOW:  Drives #textarea-verify-dossier-json and #btn-verify-dossier sequentially, querying fresh elements after each click.
        SignedRegulatoryDossierBundle sampleBundle = CreateSampleDossierBundle(
            "ind_peer_01",
            RegulatorySubmissionFramework.FdaInd,
            isReady: true);

        _dossierMock
            .SetupSequence(s => s.VerifyDossierBundleAsync(It.IsAny<SignedRegulatoryDossierBundle>(), It.IsAny<string>(), default))
            .ReturnsAsync(new RegulatoryDossierVerificationReport
            {
                DossierId = "ind_peer_01",
                HashValid = true,
                SignatureValid = true,
                IsTrusted = true,
                TrustSource = RegulatoryDossierTrustSource.TrustedPeer,
                TrustedPeerLabel = "Zurich Clinical Core"
            })
            .ReturnsAsync(new RegulatoryDossierVerificationReport
            {
                DossierId = "ind_peer_01",
                HashValid = true,
                SignatureValid = true,
                IsTrusted = false,
                TrustSource = RegulatoryDossierTrustSource.RevokedSigner,
                TrustedPeerLabel = "Revoked Facility"
            });

        IRenderedComponent<ComplianceSecurity> cut = Render<ComplianceSecurity>();

        // 1. Blank payload guard
        var verifyBtn = cut.Find("#btn-verify-dossier");
        await cut.InvokeAsync(() => verifyBtn.Click());
        Assert.Contains("Paste a signed dossier bundle JSON payload", cut.Find("#dossier-error").TextContent);

        // 2. Malformed JSON guard
        cut.Find("#textarea-verify-dossier-json").Change("{not-valid-json");
        verifyBtn = cut.Find("#btn-verify-dossier");
        await cut.InvokeAsync(() => verifyBtn.Click());
        Assert.NotEmpty(cut.Find("#dossier-error").TextContent);

        // 3. Valid JSON missing manifest
        cut.Find("#textarea-verify-dossier-json").Change("{\"bundleVersion\":\"nanoweaver-ind-dossier-v1\"}");
        verifyBtn = cut.Find("#btn-verify-dossier");
        await cut.InvokeAsync(() => verifyBtn.Click());
        Assert.Contains("missing a valid signed manifest", cut.Find("#dossier-error").TextContent);

        // 4. Valid signed bundle -> TrustedPeer badge
        string bundleJson = JsonSerializer.Serialize(sampleBundle, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        cut.Find("#textarea-verify-dossier-json").Change(bundleJson);
        verifyBtn = cut.Find("#btn-verify-dossier");
        await cut.InvokeAsync(() => verifyBtn.Click());

        var trustBadge = cut.Find("#dossier-verify-trust-badge");
        Assert.Contains("dossier-badge-trusted", trustBadge.ClassName);
        Assert.Contains("SIGNATURE TRUSTED", trustBadge.TextContent);
        Assert.Contains("Zurich Clinical Core", cut.Find("#dossier-verify-result-card").TextContent);

        // 5. Re-verify -> RevokedSigner fail-closed badge
        verifyBtn = cut.Find("#btn-verify-dossier");
        await cut.InvokeAsync(() => verifyBtn.Click());

        trustBadge = cut.Find("#dossier-verify-trust-badge");
        Assert.Contains("dossier-badge-untrusted", trustBadge.ClassName);
        Assert.Contains("SIGNATURE UNTRUSTED", trustBadge.TextContent);
    }

    /// <summary>
    /// Verifies that persisted browser locale selection translates the dossier compiler headings
    /// and that backend service exceptions render cleanly in <c>#dossier-error</c>.
    /// </summary>
    [Fact]
    public async Task DossierPanel_LocalizedBrowserPreference_RendersGermanLabelsAndHandlesServiceError()
    {
        // WHAT: Configures JSInterop to return de-DE on weaverGetLanguage and mocks CompileDossierAsync throwing an exception.
        // WHY:  Verifies multi-language reactivity in ComplianceSecurity alongside fail-safe UI error capture.
        // HOW:  Renders component, asserts translated German section heading, then triggers compilation error.
        JSInterop.Setup<string?>("weaverGetLanguage").SetResult("de-DE");
        _dossierMock
            .Setup(s => s.CompileDossierAsync(It.IsAny<string>(), It.IsAny<CompileRegulatoryDossierRequest?>(), It.IsAny<string>(), default))
            .ThrowsAsync(new InvalidOperationException("PQC signing vault locked"));

        IRenderedComponent<ComplianceSecurity> cut = Render<ComplianceSecurity>();

        var section = cut.Find("#ind-dossier-compiler-panel");
        Assert.Contains("Regulatorischer IND / IMPD Dossier-Compiler", section.TextContent);

        cut.Find("#input-dossier-patient-id").Change("patient_err");
        var compileBtn = cut.Find("#btn-compile-dossier");
        await cut.InvokeAsync(() => compileBtn.Click());

        Assert.Contains("PQC signing vault locked", cut.Find("#dossier-error").TextContent);
    }

    /// <summary>
    /// Constructs a deterministic <see cref="SignedRegulatoryDossierBundle"/> test fixture containing 10 readiness gates.
    /// </summary>
    /// <param name="dossierId">Synthetic dossier identifier.</param>
    /// <param name="framework">Target submission framework (FDA IND or EMA IMPD).</param>
    /// <param name="isReady">Whether all 10 readiness gates pass.</param>
    /// <returns>Constructed <see cref="SignedRegulatoryDossierBundle"/> instance.</returns>
    private static SignedRegulatoryDossierBundle CreateSampleDossierBundle(
        string dossierId,
        RegulatorySubmissionFramework framework,
        bool isReady)
    {
        // WHAT: Populates 10 DossierReadinessGate items matching the production gate inventory.
        // WHY:  Exercises both ready (100.0%) and fail-closed blocked UI states without duplicating fixture setup.
        // HOW:  Builds a 10-element gate array where CMC_REVERSAL_ANTIDOTE fails when isReady is false.
        string[] gateCodes =
        [
            "CMC_DNA_BLUEPRINT",
            "CMC_LOT_COA",
            "CMC_REVERSAL_ANTIDOTE",
            "CMC_COLD_CHAIN_CUSTODY",
            "QSP_PHARMACOGENOMICS",
            "QSP_DEPOT_180D",
            "QSP_EPIGENETIC_CIRCUIT",
            "QSP_MULTIYEAR_HOMEOSTASIS",
            "COMP_PHARMACOVIGILANCE",
            "COMP_AUDIT_LEDGER"
        ];

        List<DossierReadinessGate> gates = new(gateCodes.Length);
        for (int i = 0; i < gateCodes.Length; i++)
        {
            string code = gateCodes[i];
            bool passed = isReady || code != "CMC_REVERSAL_ANTIDOTE";
            gates.Add(new DossierReadinessGate
            {
                GateCode = code,
                Passed = passed,
                Summary = passed ? $"{code} verified." : "Missing active paired reversal antidote."
            });
        }

        SignedRegulatoryDossierBundle template = new SignedRegulatoryDossierBundle().GetExamples();
        return template with
        {
            PayloadSha256 = "abcdef0123456789abcdef0123456789",
            SignerKeyId = "node-mldsa65-test-key",
            Manifest = template.Manifest with
            {
                DossierId = dossierId,
                SubmissionFramework = framework,
                IsSubmissionReady = isReady,
                CompletenessScore = isReady ? 100.0 : 90.0,
                ReadinessGates = gates,
                BlockingReasons = isReady ? [] : ["Missing active paired reversal antidote."]
            }
        };
    }
}
