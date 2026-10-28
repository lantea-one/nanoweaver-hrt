using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Security.Authentication;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit tests verifying master administrator provisioning, asymmetric key generation,
/// public key upload parsing, and lock-in behavior in <see cref="SetupWizard"/>.
/// </summary>
public sealed class SetupWizardComponentTests : TestContext
{
    /// <summary>
    /// Mock authentication service managing identity provisioning and setup validation.
    /// </summary>
    private readonly Mock<IUserAuthenticationService> _authMock = new();

    /// <summary>
    /// User session state managing authentication status across pages.
    /// </summary>
    private readonly UserSessionState _sessionState = new();

    /// <summary>
    /// Master administrator account instance returned upon setup completion.
    /// </summary>
    private readonly UserAccount _masterAdmin = new()
    {
        Username = "administrator",
        Role = UserRole.Administrator,
        AccessGate = AccessGate.ApiWeb,
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "MCowBQYDK2VwAyEA9876543210abcdef9876543210abcdef9876543210==",
        CreatedAtUtc = DateTime.UtcNow
    };

    /// <summary>
    /// Helper creating a <see cref="GeneratedKeyBundle"/> instance for test assertions.
    /// </summary>
    /// <param name="privPem">Private key PEM text.</param>
    /// <param name="pubPem">Public key PEM text.</param>
    /// <param name="pubB64">Public key Base64 DER bytes.</param>
    /// <returns>A populated <see cref="GeneratedKeyBundle"/>.</returns>
    private static GeneratedKeyBundle CreateBundle(string privPem = "priv-pem", string pubPem = "pub-pem", string pubB64 = "pub-b64") => new()
    {
        Username = "administrator",
        Role = UserRole.Administrator,
        AccessGate = AccessGate.ApiWeb,
        Algorithm = KeyAlgorithm.Ed25519,
        PrivateKeyPem = privPem,
        PublicKeyPem = pubPem,
        PublicKeyBase64 = pubB64,
        IsPasswordProtected = false
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="SetupWizardComponentTests"/> class.
    /// </summary>
    public SetupWizardComponentTests()
    {
        // WHAT: Registers authentication and session services required by SetupWizard.
        // WHY:  SetupWizard injects IUserAuthenticationService and UserSessionState.
        // HOW:  Registers Mock<IUserAuthenticationService>.Object and UserSessionState instance.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_authMock.Object);
        Services.AddSingleton(_sessionState);
    }

    /// <summary>
    /// Verifies that when first-launch setup has already been completed, the component displays
    /// the system initialized warning banner and permanently hides the setup stepper.
    /// </summary>
    [Fact]
    public void AlreadyInitialized_RendersSystemInitializedBanner_AndHidesStepper()
    {
        // WHAT: Tests first-launch lockout when node is already initialized.
        // WHY:  Prevents administrative account hijacking on an initialized sovereign node.
        // HOW:  Sets IsSetupRequiredAsync to false, renders SetupWizard, and asserts banner and hidden stepper.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(false);

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();

        var alert = cut.Find(".alert-info");
        Assert.Contains("System Initialized:", alert.TextContent);
        Assert.Contains("First-launch setup is permanently locked", alert.TextContent);

        Assert.Empty(cut.FindAll(".setup-stepper"));
        var returnLink = cut.Find("a.btn-primary");
        Assert.Equal("/", returnLink.GetAttribute("href"));
    }

    /// <summary>
    /// Verifies that clicking Next on Step 1 with an empty or whitespace username displays a validation error.
    /// </summary>
    [Fact]
    public async Task Step1_ValidationError_WhenAdminUsernameEmpty()
    {
        // WHAT: Tests username validation on Step 1.
        // WHY:  An administrator handle cannot be empty or whitespace.
        // HOW:  Sets username to empty string, clicks Next, and asserts validation message.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        var usernameInput = cut.Find("#adminUsername");
        usernameInput.Change("   ");

        var nextBtn = cut.Find("button.btn-primary");
        await nextBtn.ClickAsync(new MouseEventArgs());

        var alert = cut.Find(".alert-danger");
        Assert.Equal("Administrator username must not be empty.", alert.TextContent.Trim());

        var activeStep = cut.Find(".step-indicator.active");
        Assert.Contains("1. Administrator Username", activeStep.TextContent);
    }

    /// <summary>
    /// Verifies that providing a valid administrator username and access gate advances to Step 2.
    /// </summary>
    [Fact]
    public async Task Step1_ProceedsToStep2_WithValidInputs()
    {
        // WHAT: Tests advancing from Step 1 to Step 2.
        // WHY:  Valid username and access gate permit configuring cryptographic credentials.
        // HOW:  Sets username and access gate, clicks Next, and verifies Step 2 is active.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        cut.Find("#adminUsername").Change("sovereign-lead");
        cut.Find("#accessGate").Change(AccessGate.Web.ToString());

        var nextBtn = cut.Find("button.btn-primary");
        await nextBtn.ClickAsync(new MouseEventArgs());

        var activeStep = cut.Find(".step-indicator.active");
        Assert.Contains("2. Cryptographic Keypair", activeStep.TextContent);
    }

    /// <summary>
    /// Verifies that attempting to advance to Step 3 before generating a keypair displays an error message.
    /// </summary>
    [Fact]
    public async Task Step2_Option0_ProceedToStep3_BeforeGenerating_ShowsError()
    {
        // WHAT: Tests key generation requirement before proceeding to Step 3.
        // WHY:  An administrator cannot be provisioned without an authoritative public key.
        // HOW:  Advances to Step 2, clicks Next without generating keys, and asserts error.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        var nextBtn = cut.FindAll("button.btn-primary")[0];
        await nextBtn.ClickAsync(new MouseEventArgs());

        var alert = cut.Find(".alert-danger");
        Assert.Equal("Please click 'Generate High-Security Keypair' first.", alert.TextContent.Trim());
    }

    /// <summary>
    /// Verifies that generating an Ed25519 keypair without a password creates a key bundle.
    /// </summary>
    [Fact]
    public async Task Step2_Option0_GenerateKeys_Ed25519_WithoutPassword_Succeeds()
    {
        // WHAT: Tests Ed25519 key generation without passphrase encryption.
        // WHY:  Standard default workflow for generating asymmetric node identity.
        // HOW:  Configures mock bundle, clicks Generate, and verifies key textarea renders.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);
        JSInterop.Setup<GeneratedKeyBundle>("weaverGenerateKeyBundle", _ => true)
            .SetResult(CreateBundle("-----BEGIN PRIVATE KEY-----\nED25519-PRIV\n-----END PRIVATE KEY-----", "-----BEGIN PUBLIC KEY-----\nED25519-PUB\n-----END PUBLIC KEY-----", "ED25519PUBKEYBASE64STRING1234567890=="));

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        var generateBtn = cut.Find("button.btn-secondary");
        await generateBtn.ClickAsync(new MouseEventArgs());

        var generatedBox = cut.Find(".generated-box");
        Assert.NotNull(generatedBox);
        Assert.Contains("ED25519-PRIV", generatedBox.TextContent);
    }

    /// <summary>
    /// Verifies that generating an ML-DSA-65 post-quantum keypair with a password creates an encrypted key bundle.
    /// </summary>
    [Fact]
    public async Task Step2_Option0_GenerateKeys_MlDsa65_WithPassword_Succeeds()
    {
        // WHAT: Tests ML-DSA-65 post-quantum key generation with passphrase encryption.
        // WHY:  Allows operators to select post-quantum algorithms and protect private keys with passwords.
        // HOW:  Selects ML-DSA-65 algorithm, enters passphrase, clicks Generate, and asserts bundle.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);
        JSInterop.Setup<GeneratedKeyBundle>("weaverGenerateKeyBundle", _ => true)
            .SetResult(CreateBundle("-----BEGIN ENCRYPTED PRIVATE KEY-----\nMLDSA-PRIV\n-----END ENCRYPTED PRIVATE KEY-----", "-----BEGIN PUBLIC KEY-----\nMLDSA-PUB\n-----END PUBLIC KEY-----", "MLDSA65PUBKEYBASE64STRING1234567890=="));

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        cut.Find("#keyAlgo").Change(KeyAlgorithm.MlDsa65.ToString());
        cut.Find("#keyPassword").Change("Passphrase!123");

        var generateBtn = cut.Find("button.btn-secondary");
        await generateBtn.ClickAsync(new MouseEventArgs());

        var generatedBox = cut.Find(".generated-box");
        Assert.NotNull(generatedBox);
        Assert.Contains("MLDSA-PRIV", generatedBox.TextContent);
    }

    /// <summary>
    /// Verifies that when key generation throws an exception, an error alert is displayed.
    /// </summary>
    [Fact]
    public async Task Step2_Option0_GenerateKeys_WhenAuthServiceThrows_ShowsError()
    {
        // WHAT: Tests error handling during keypair generation.
        // WHY:  Cryptographic hardware or entropy pool failures must be surfaced clearly.
        // HOW:  Configures JS interop for weaverGenerateKeyBundle to throw and asserts error alert.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);
        JSInterop.Setup<GeneratedKeyBundle>("weaverGenerateKeyBundle", _ => true)
            .SetException(new InvalidOperationException("Entropy pool exhausted."));

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        var generateBtn = cut.Find("button.btn-secondary");
        await generateBtn.ClickAsync(new MouseEventArgs());

        var alert = cut.Find(".alert-danger");
        Assert.Contains("Key generation error: Entropy pool exhausted.", alert.TextContent);
    }

    /// <summary>
    /// Verifies that attempting to advance to Step 3 before confirming private key backup displays an error.
    /// </summary>
    [Fact]
    public async Task Step2_Option0_ProceedToStep3_BeforeBackupConfirmation_ShowsError()
    {
        // WHAT: Tests mandatory confirmation of private key backup.
        // WHY:  Without backup confirmation, administrators risk losing access permanently.
        // HOW:  Generates key bundle, leaves checkbox unchecked, clicks Next, and asserts error.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);
        JSInterop.Setup<GeneratedKeyBundle>("weaverGenerateKeyBundle", _ => true)
            .SetResult(CreateBundle("priv", "pub", "pub-b64"));

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        await cut.Find("button.btn-secondary").ClickAsync(new MouseEventArgs());

        var nextBtn = cut.FindAll("button.btn-primary")[0];
        await nextBtn.ClickAsync(new MouseEventArgs());

        var alert = cut.Find(".alert-danger");
        Assert.Equal("You must confirm that you have downloaded, copied, or backed up your private key.", alert.TextContent.Trim());
    }

    /// <summary>
    /// Verifies that downloading the private key file triggers client-side blob download and marks backup confirmed.
    /// </summary>
    [Fact]
    public async Task Step2_Option0_DownloadPrivateKey_Success_MarksBackupConfirmed()
    {
        // WHAT: Tests successful private key file download trigger.
        // WHY:  Enables saving the private key to a local PEM file.
        // HOW:  Sets weaverDownloadBlob to return true, clicks Download, and verifies backup confirmed.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);
        JSInterop.Setup<GeneratedKeyBundle>("weaverGenerateKeyBundle", _ => true)
            .SetResult(CreateBundle("priv-pem-content", "pub", "pub-b64"));

        JSInterop.Setup<bool>("weaverDownloadBlob", "administrator_private_key.pem", "priv-pem-content", "application/x-pem-file")
            .SetResult(true);

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());
        await cut.Find("button.btn-secondary").ClickAsync(new MouseEventArgs());

        var downloadBtn = cut.Find("button.btn-outline");
        await downloadBtn.ClickAsync(new MouseEventArgs());

        var statusAlert = cut.Find(".alert-info");
        Assert.Contains("Private key (.pem) downloaded successfully!", statusAlert.TextContent);

        var checkbox = cut.Find("#backupConfirm");
        Assert.True(checkbox.HasAttribute("checked"));
    }

    /// <summary>
    /// Verifies that when downloading the private key file returns false, an instruction alert is displayed.
    /// </summary>
    [Fact]
    public async Task Step2_Option0_DownloadPrivateKey_WhenReturnsFalse_ShowsFallbackNotice()
    {
        // WHAT: Tests download failure fallback notification.
        // WHY:  Browsers that block automatic downloads must instruct the user to copy manually.
        // HOW:  Sets weaverDownloadBlob to return false, clicks Download, and asserts fallback message.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);
        JSInterop.Setup<GeneratedKeyBundle>("weaverGenerateKeyBundle", _ => true)
            .SetResult(CreateBundle("priv-pem-content", "pub", "pub-b64"));

        JSInterop.Setup<bool>("weaverDownloadBlob", "administrator_private_key.pem", "priv-pem-content", "application/x-pem-file")
            .SetResult(false);

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());
        await cut.Find("button.btn-secondary").ClickAsync(new MouseEventArgs());

        var downloadBtn = cut.Find("button.btn-outline");
        await downloadBtn.ClickAsync(new MouseEventArgs());

        var statusAlert = cut.Find(".alert-info");
        Assert.Contains("Download could not be started automatically. Please use 'Copy to Clipboard' below.", statusAlert.TextContent);
    }

    /// <summary>
    /// Verifies that when downloading the private key file throws an exception, an error notice is displayed.
    /// </summary>
    [Fact]
    public async Task Step2_Option0_DownloadPrivateKey_WhenJsThrows_ShowsErrorNotice()
    {
        // WHAT: Tests exception handling when triggering download blob.
        // WHY:  Prevents unhandled JS interop exceptions from crashing the wizard.
        // HOW:  Configures weaverDownloadBlob to throw and asserts download issue notice.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);
        JSInterop.Setup<GeneratedKeyBundle>("weaverGenerateKeyBundle", _ => true)
            .SetResult(CreateBundle("priv-pem-content", "pub", "pub-b64"));

        JSInterop.Setup<bool>("weaverDownloadBlob", "administrator_private_key.pem", "priv-pem-content", "application/x-pem-file")
            .SetException(new JSException("DOM exception"));

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());
        await cut.Find("button.btn-secondary").ClickAsync(new MouseEventArgs());

        var downloadBtn = cut.Find("button.btn-outline");
        await downloadBtn.ClickAsync(new MouseEventArgs());

        var statusAlert = cut.Find(".alert-info");
        Assert.Contains("Download trigger encountered an issue (DOM exception)", statusAlert.TextContent);
    }

    /// <summary>
    /// Verifies that copying the private key to clipboard succeeds and marks backup confirmed.
    /// </summary>
    [Fact]
    public async Task Step2_Option0_CopyPrivateKey_Success_MarksBackupConfirmed()
    {
        // WHAT: Tests copying private key to clipboard.
        // WHY:  Allows operators to securely store their private key in a password manager.
        // HOW:  Sets weaverCopyText to succeed, clicks Copy, and asserts backup confirmed.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);
        JSInterop.Setup<GeneratedKeyBundle>("weaverGenerateKeyBundle", _ => true)
            .SetResult(CreateBundle("priv-pem-content", "pub", "pub-b64"));

        JSInterop.SetupVoid("weaverCopyText", "priv-pem-content").SetVoidResult();

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());
        await cut.Find("button.btn-secondary").ClickAsync(new MouseEventArgs());

        var copyBtn = cut.FindAll("button.btn-secondary")[0];
        await copyBtn.ClickAsync(new MouseEventArgs());

        var statusAlert = cut.Find(".alert-info");
        Assert.Contains("Private key copied to clipboard!", statusAlert.TextContent);

        var checkbox = cut.Find("#backupConfirm");
        Assert.True(checkbox.HasAttribute("checked"));
    }

    /// <summary>
    /// Verifies that when copying the private key to clipboard throws an exception, an error alert is displayed.
    /// </summary>
    [Fact]
    public async Task Step2_Option0_CopyPrivateKey_WhenJsThrows_ShowsErrorNotice()
    {
        // WHAT: Tests clipboard copy failure fallback.
        // WHY:  Surfaces clipboard permission denials cleanly with instructions to copy manually.
        // HOW:  Configures weaverCopyText to throw and asserts fallback message.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);
        JSInterop.Setup<GeneratedKeyBundle>("weaverGenerateKeyBundle", _ => true)
            .SetResult(CreateBundle("priv-pem-content", "pub", "pub-b64"));

        JSInterop.SetupVoid("weaverCopyText", "priv-pem-content")
            .SetException(new JSException("Clipboard permission denied"));

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());
        await cut.Find("button.btn-secondary").ClickAsync(new MouseEventArgs());

        var copyBtn = cut.FindAll("button.btn-secondary")[0];
        await copyBtn.ClickAsync(new MouseEventArgs());

        var statusAlert = cut.Find(".alert-info");
        Assert.Contains("Clipboard copy failed (Clipboard permission denied)", statusAlert.TextContent);
    }

    /// <summary>
    /// Verifies that switching to Option 1 and attempting to proceed with empty public key text displays an error.
    /// </summary>
    [Fact]
    public async Task Step2_Option1_EmptyCustomKey_ProceedToStep3_ShowsError()
    {
        // WHAT: Tests validation when importing a custom public key.
        // WHY:  An empty custom public key string cannot be registered as an authoritative identity.
        // HOW:  Switches to Option 1 tab, leaves textarea empty, clicks Next, and asserts error.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        var tabBtns = cut.FindAll("button.tab-btn");
        await tabBtns[1].ClickAsync(new MouseEventArgs());

        var nextBtn = cut.FindAll("button.btn-primary")[0];
        await nextBtn.ClickAsync(new MouseEventArgs());

        var alert = cut.Find(".alert-danger");
        Assert.Equal("Please provide your public key text.", alert.TextContent.Trim());
    }

    /// <summary>
    /// Verifies that uploading a public key file populates the custom public key textarea.
    /// </summary>
    [Fact]
    public async Task Step2_Option1_HandleCustomPubKeyUpload_Success_PopulatesTextarea()
    {
        // WHAT: Tests uploading a custom public key file.
        // WHY:  Allows administrators to import an existing public key from a file.
        // HOW:  Uploads text content to InputFile and verifies custom public key textarea content.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        var tabBtns = cut.FindAll("button.tab-btn");
        await tabBtns[1].ClickAsync(new MouseEventArgs());

        var inputFile = cut.FindComponent<InputFile>();
        string pubKeyPem = "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEAfakePublicKey1234567890\n-----END PUBLIC KEY-----";
        inputFile.UploadFiles(InputFileContent.CreateFromText(pubKeyPem, "custom_key.pub"));

        cut.WaitForAssertion(() =>
        {
            var textarea = cut.Find("#customPubKey");
            Assert.Contains("MCowBQYDK2VwAyEAfakePublicKey1234567890", textarea.OuterHtml);
        });
    }

    /// <summary>
    /// Verifies that uploading an oversized public key file handles the stream error gracefully.
    /// </summary>
    [Fact]
    public async Task Step2_Option1_HandleCustomPubKeyUpload_OversizedFile_ShowsError()
    {
        // WHAT: Tests upload size limit enforcement.
        // WHY:  Prevents memory exhaustion attacks via oversized key file uploads.
        // HOW:  Uploads a binary payload exceeding 65536 bytes and asserts error alert.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        var tabBtns = cut.FindAll("button.tab-btn");
        await tabBtns[1].ClickAsync(new MouseEventArgs());

        var inputFile = cut.FindComponent<InputFile>();
        var largeContent = new byte[70000];
        inputFile.UploadFiles(InputFileContent.CreateFromBinary(largeContent, "large.pub"));

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find(".alert-danger");
            Assert.Contains("Failed to read uploaded public key file", alert.TextContent);
        });
    }

    /// <summary>
    /// Verifies that clicking Back buttons navigates backwards through wizard steps.
    /// </summary>
    [Fact]
    public async Task Navigation_BackButtons_NavigateBackwardsThroughSteps()
    {
        // WHAT: Tests step navigation backwards.
        // WHY:  Operators must be able to revise earlier selections before committing setup.
        // HOW:  Advances to Step 2, clicks Back to Step 1, advances to Step 3, clicks Back to Step 2.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);
        JSInterop.Setup<GeneratedKeyBundle>("weaverGenerateKeyBundle", _ => true)
            .SetResult(CreateBundle("priv", "pub", "pub-b64-value"));

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        // Back from Step 2 to Step 1
        var backBtn = cut.Find("button.btn-outline");
        await backBtn.ClickAsync(new MouseEventArgs());
        var activeStep = cut.Find(".step-indicator.active");
        Assert.Contains("1. Administrator Username", activeStep.TextContent);

        // Advance to Step 2 again
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());
        await cut.Find("button.btn-secondary").ClickAsync(new MouseEventArgs());
        cut.Find("#backupConfirm").Change(true);

        // Advance to Step 3
        var nextBtn = cut.FindAll("button.btn-primary")[0];
        await nextBtn.ClickAsync(new MouseEventArgs());
        activeStep = cut.Find(".step-indicator.active");
        Assert.Contains("3. Confirmation & Lock-In", activeStep.TextContent);

        // Back from Step 3 to Step 2
        var backFrom3Btn = cut.Find("button.btn-outline");
        await backFrom3Btn.ClickAsync(new MouseEventArgs());
        activeStep = cut.Find(".step-indicator.active");
        Assert.Contains("2. Cryptographic Keypair", activeStep.TextContent);
    }

    /// <summary>
    /// Verifies that public key fingerprint calculation formats standard, short, and empty keys correctly.
    /// </summary>
    [Fact]
    public async Task Step3_FingerprintFormats_HandleDifferentKeyLengths()
    {
        // WHAT: Tests GetFingerprint formatting branches.
        // WHY:  Ensures readable display of cryptographic fingerprints on Step 3.
        // HOW:  Renders Step 3 with standard length key, short key, and tests empty string branch.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);

        // Test 1: Standard key > 24 chars
        JSInterop.Setup<GeneratedKeyBundle>("weaverGenerateKeyBundle", _ => true)
            .SetResult(CreateBundle("priv", "pub", "123456789012345678901234567890"));

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());
        await cut.Find("button.btn-secondary").ClickAsync(new MouseEventArgs());
        cut.Find("#backupConfirm").Change(true);
        await cut.FindAll("button.btn-primary")[0].ClickAsync(new MouseEventArgs());

        var fingerprintValue = cut.Find(".summary-row:last-child .value");
        Assert.Equal("123456789012...901234567890", fingerprintValue.TextContent.Trim());

        // Test 2: Custom key <= 24 chars
        IRenderedComponent<SetupWizard> cut2 = RenderComponent<SetupWizard>();
        await cut2.Find("button.btn-primary").ClickAsync(new MouseEventArgs());
        var tabBtns = cut2.FindAll("button.tab-btn");
        await tabBtns[1].ClickAsync(new MouseEventArgs());
        cut2.Find("#customPubKey").Change("short-pubkey-1234");
        await cut2.FindAll("button.btn-primary")[0].ClickAsync(new MouseEventArgs());

        var fingerprintValue2 = cut2.Find(".summary-row:last-child .value");
        Assert.Equal("short-pubkey-1234", fingerprintValue2.TextContent.Trim());

        // Test 3: Generated option before key generation produces "N/A"
        IRenderedComponent<SetupWizard> cut3 = RenderComponent<SetupWizard>();
        MethodInfo? getFingerprintMethod = typeof(SetupWizard).GetMethod("GetFingerprint", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(getFingerprintMethod);
        string emptyGeneratedFingerprint = (string)getFingerprintMethod.Invoke(cut3.Instance, null)!;
        Assert.Equal("N/A", emptyGeneratedFingerprint);

        // Test 4: Custom option with empty text produces "N/A"
        FieldInfo? keyOptionField = typeof(SetupWizard).GetField("_keyOption", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(keyOptionField);
        keyOptionField.SetValue(cut3.Instance, 1);
        string emptyCustomFingerprint = (string)getFingerprintMethod.Invoke(cut3.Instance, null)!;
        Assert.Equal("N/A", emptyCustomFingerprint);
    }

    /// <summary>
    /// Verifies that finalizing setup with Option 0 provisions the administrator, generates a bearer token,
    /// sets the browser session, and navigates to the sovereign dashboard.
    /// </summary>
    [Fact]
    public async Task Step3_FinalizeSetup_Option0_Success_SetsSessionAndNavigates()
    {
        // WHAT: Tests successful initial platform provisioning workflow via generated keypair.
        // WHY:  Completes first-launch wizard and enables immediate administrative access.
        // HOW:  Configures CompleteInitialSetupAsync and GenerateBearerToken, clicks Initialize, and verifies session.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);
        JSInterop.Setup<GeneratedKeyBundle>("weaverGenerateKeyBundle", _ => true)
            .SetResult(CreateBundle("priv", "pub", "pub-b64-ed25519-valid-key=="));

        _authMock.Setup(m => m.CompleteInitialSetupAsync("administrator", "pub-b64-ed25519-valid-key==", KeyAlgorithm.Ed25519, AccessGate.ApiWeb))
            .ReturnsAsync(_masterAdmin);
        _authMock.Setup(m => m.GenerateBearerToken(_masterAdmin)).Returns("master-admin-bearer-token");

        JSInterop.SetupVoid("weaverSetSession", "master-admin-bearer-token").SetVoidResult();

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());
        await cut.Find("button.btn-secondary").ClickAsync(new MouseEventArgs());
        cut.Find("#backupConfirm").Change(true);
        await cut.FindAll("button.btn-primary")[0].ClickAsync(new MouseEventArgs());

        var initBtn = cut.Find("button.btn-success");
        await initBtn.ClickAsync(new MouseEventArgs());

        _authMock.Verify(m => m.CompleteInitialSetupAsync("administrator", "pub-b64-ed25519-valid-key==", KeyAlgorithm.Ed25519, AccessGate.ApiWeb), Times.Once);
        Assert.True(_sessionState.IsAuthenticated);
        Assert.Equal("administrator", _sessionState.CurrentUser?.Username);

        JSInterop.VerifyInvoke("weaverSetSession");

        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        Assert.Equal("http://localhost/", nav.Uri);
    }

    /// <summary>
    /// Verifies that when weaverSetSession throws an exception during setup finalization,
    /// the component handles it gracefully and still sets UserSessionState and navigates to root.
    /// </summary>
    [Fact]
    public async Task Step3_FinalizeSetup_Option0_WhenJsThrows_StillSetsUserAndNavigates()
    {
        // WHAT: Tests resilience when browser session persistence throws an exception.
        // WHY:  Setup completion in the database must not be blocked by browser storage failure.
        // HOW:  Configures weaverSetSession to throw, clicks Initialize, and asserts navigation.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);
        JSInterop.Setup<GeneratedKeyBundle>("weaverGenerateKeyBundle", _ => true)
            .SetResult(CreateBundle("priv", "pub", "pub-b64-ed25519-valid-key=="));

        _authMock.Setup(m => m.CompleteInitialSetupAsync("administrator", "pub-b64-ed25519-valid-key==", KeyAlgorithm.Ed25519, AccessGate.ApiWeb))
            .ReturnsAsync(_masterAdmin);
        _authMock.Setup(m => m.GenerateBearerToken(_masterAdmin)).Returns("master-admin-bearer-token");

        JSInterop.SetupVoid("weaverSetSession", "master-admin-bearer-token")
            .SetException(new JSException("LocalStorage quota exceeded"));

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());
        await cut.Find("button.btn-secondary").ClickAsync(new MouseEventArgs());
        cut.Find("#backupConfirm").Change(true);
        await cut.FindAll("button.btn-primary")[0].ClickAsync(new MouseEventArgs());

        var initBtn = cut.Find("button.btn-success");
        await initBtn.ClickAsync(new MouseEventArgs());

        Assert.True(_sessionState.IsAuthenticated);
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        Assert.Equal("http://localhost/", nav.Uri);
    }

    /// <summary>
    /// Verifies that finalizing setup with Option 1 extracts clean Base64 public key bytes from PEM format.
    /// </summary>
    [Fact]
    public async Task Step3_FinalizeSetup_Option1_ExtractsCleanBase64AndCompletes()
    {
        // WHAT: Tests custom public key extraction from RFC 7468 PEM container.
        // WHY:  Ensures that header, footer, and newline tokens are stripped before database storage.
        // HOW:  Provides multi-line PEM public key, advances to Step 3, initializes, and verifies clean base64.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);

        string rawPem = "-----BEGIN PUBLIC KEY-----\r\nMCowBQYDK2VwAyEA9876543210abcdef\r\n9876543210abcdef9876543210==\r\n-----END PUBLIC KEY-----";
        string expectedBase64 = "MCowBQYDK2VwAyEA9876543210abcdef9876543210abcdef9876543210==";

        _authMock.Setup(m => m.CompleteInitialSetupAsync("administrator", expectedBase64, KeyAlgorithm.Ed25519, AccessGate.ApiWeb))
            .ReturnsAsync(_masterAdmin);
        _authMock.Setup(m => m.GenerateBearerToken(_masterAdmin)).Returns("token-opt1");

        JSInterop.SetupVoid("weaverSetSession", "token-opt1").SetVoidResult();

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        var tabBtns = cut.FindAll("button.tab-btn");
        await tabBtns[1].ClickAsync(new MouseEventArgs());

        cut.Find("#customPubKey").Change(rawPem);
        await cut.FindAll("button.btn-primary")[0].ClickAsync(new MouseEventArgs());

        var initBtn = cut.Find("button.btn-success");
        await initBtn.ClickAsync(new MouseEventArgs());

        _authMock.Verify(m => m.CompleteInitialSetupAsync("administrator", expectedBase64, KeyAlgorithm.Ed25519, AccessGate.ApiWeb), Times.Once);
        Assert.True(_sessionState.IsAuthenticated);
    }

    /// <summary>
    /// Verifies that when CompleteInitialSetupAsync throws an exception, an initialization failure message is displayed.
    /// </summary>
    [Fact]
    public async Task Step3_FinalizeSetup_WhenServiceThrows_ShowsErrorMessage()
    {
        // WHAT: Tests error handling when master administrator registration fails in the database.
        // WHY:  Database integrity violations or IO errors must be displayed to the operator.
        // HOW:  Configures CompleteInitialSetupAsync to throw and asserts alert message.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);
        JSInterop.Setup<GeneratedKeyBundle>("weaverGenerateKeyBundle", _ => true)
            .SetResult(CreateBundle("priv", "pub", "pub-b64"));

        _authMock.Setup(m => m.CompleteInitialSetupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<KeyAlgorithm>(), It.IsAny<AccessGate>()))
            .Throws(new InvalidOperationException("Master administrator conflict."));

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());
        await cut.Find("button.btn-secondary").ClickAsync(new MouseEventArgs());
        cut.Find("#backupConfirm").Change(true);
        await cut.FindAll("button.btn-primary")[0].ClickAsync(new MouseEventArgs());

        var initBtn = cut.Find("button.btn-success");
        await initBtn.ClickAsync(new MouseEventArgs());

        var alert = cut.Find(".alert-danger");
        Assert.Contains("Initialization failed: Master administrator conflict.", alert.TextContent);
        Assert.False(_sessionState.IsAuthenticated);
    }

    /// <summary>
    /// Verifies that calling DownloadPrivateKey or CopyPrivateKey before generating keys returns early without error.
    /// </summary>
    [Fact]
    public async Task DownloadAndCopy_WhenGeneratedBundleNull_ReturnsEarlyWithoutError()
    {
        // WHAT: Tests null bundle defensive checks in DownloadPrivateKey and CopyPrivateKey.
        // WHY:  Invoking download or copy handlers before key generation must be safe no-ops.
        // HOW:  Uses reflection to invoke DownloadPrivateKey and CopyPrivateKey when _generatedBundle is null.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);

        IRenderedComponent<SetupWizard> cut = RenderComponent<SetupWizard>();
        SetupWizard instance = cut.Instance;

        MethodInfo? downloadMethod = typeof(SetupWizard).GetMethod("DownloadPrivateKey", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(downloadMethod);
        Task downloadTask = (Task)downloadMethod.Invoke(instance, null)!;
        await downloadTask;

        MethodInfo? copyMethod = typeof(SetupWizard).GetMethod("CopyPrivateKey", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(copyMethod);
        Task copyTask = (Task)copyMethod.Invoke(instance, null)!;
        await copyTask;

        // Also test FinalizeSetup defensive branch when _keyOption is 0 and _generatedBundle is null
        MethodInfo? finalizeMethod = typeof(SetupWizard).GetMethod("FinalizeSetup", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(finalizeMethod);
        Task finalizeTask = (Task)finalizeMethod.Invoke(instance, null)!;
        await finalizeTask;

        cut.Render();
        var alert = cut.Find(".alert-danger");
        Assert.Contains("Please click 'Generate High-Security Keypair' first.", alert.TextContent);
    }
}
