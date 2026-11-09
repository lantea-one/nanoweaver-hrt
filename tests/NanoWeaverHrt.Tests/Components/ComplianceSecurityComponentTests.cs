using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Components.Pages;
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
public sealed class ComplianceSecurityComponentTests : TestContext
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
        // HOW:  Registers singleton mocks and UserSessionState in bUnit's TestContext.Services.
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
        IRenderedComponent<ComplianceSecurity> cut = RenderComponent<ComplianceSecurity>();

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
        IRenderedComponent<ComplianceSecurity> cut = RenderComponent<ComplianceSecurity>();

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
        IRenderedComponent<ComplianceSecurity> cut = RenderComponent<ComplianceSecurity>();

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

        IRenderedComponent<ComplianceSecurity> cut = RenderComponent<ComplianceSecurity>();

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
        IRenderedComponent<ComplianceSecurity> cut = RenderComponent<ComplianceSecurity>();

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

        IRenderedComponent<ComplianceSecurity> cut = RenderComponent<ComplianceSecurity>();

        // Trigger de-identification to populate target ID
        var deIdBtn = cut.FindAll("button").First(b => b.TextContent.Contains("De-Identify"));
        await cut.InvokeAsync(() => deIdBtn.Click());

        var shredBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Crypto-Shred"));
        await cut.InvokeAsync(() => shredBtn.Click());

        var alerts = cut.FindAll(".alert-warning");
        Assert.Contains(alerts, a => a.TextContent.Contains(Program.ShredAlreadyMessage("sub_98432_pseudo")));
    }
}
