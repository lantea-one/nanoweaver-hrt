using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Fabrication.Opentrons;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Pipelines;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Components;

/// <summary>
/// Unit and component interaction tests verifying the Opentrons liquid handler automation UI,
/// robot and pipette option controls, deck layout summaries, and Python protocol preview modal in <see cref="WorkflowWizard"/>.
/// </summary>
public sealed class WorkflowWizardTests : TestContext
{
    /// <summary>
    /// Mock configuration repository supplying export directory paths.
    /// </summary>
    private readonly Mock<IConfigurationRepository> _configRepoMock = new();

    /// <summary>
    /// Mock parquet trajectory writer injected into the workflow wizard.
    /// </summary>
    private readonly Mock<IParquetTrajectoryWriter> _parquetWriterMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowWizardTests"/> class.
    /// Registers required singleton domain services and configures loose JSInterop.
    /// </summary>
    public WorkflowWizardTests()
    {
        // WHAT: Registers dependencies required by WorkflowWizard.razor.
        // WHY:  Ensures full DI graph resolution when rendering the interactive wizard component.
        // HOW:  Adds real domain services for fabrication, genomics, and Opentrons generation, alongside loose JS mocks.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _configRepoMock
            .Setup(c => c.GetValueAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("./simulations/exports");

        Services.AddSingleton<IFabricationService, FabricationService>();
        Services.AddSingleton<EndocrineStateEngine>();
        Services.AddSingleton<IPharmacogenomicsService, PharmacogenomicsService>();
        Services.AddSingleton<IConfigurationRepository>(_configRepoMock.Object);
        Services.AddSingleton<XyzPipeReader>();
        Services.AddSingleton<DcdPipeReader>();
        Services.AddSingleton<LammpsCustomPipeReader>();
        Services.AddSingleton<IParquetTrajectoryWriter>(_parquetWriterMock.Object);
        Services.AddSingleton<IOpentronsProtocolGenerator, OpentronsProtocolGenerator>();
    }

    /// <summary>
    /// Verifies that the "Automate with Opentrons" section renders in both Stage 5 and Stage 6.
    /// </summary>
    /// <returns>A task representing the asynchronous test execution.</returns>
    [Fact]
    public async Task OpentronsSection_RendersInStage5AndStage6()
    {
        // WHAT: Verifies Opentrons UI visibility across Stage 5 and Stage 6.
        // WHY:  Fulfills work package requirement to offer automation during stoichiometry (Stage 5) and manifest export (Stage 6).
        // HOW:  Transitions stepper to step 5 and step 6, checking presence of the .opentrons-section card in the DOM.
        IRenderedComponent<WorkflowWizard> cut = RenderComponent<WorkflowWizard>();

        await cut.InvokeAsync(() => cut.Instance.SetStep(5));
        Assert.NotEmpty(cut.FindAll(".opentrons-section"));
        Assert.NotNull(cut.Find("#opentrons-robot-select"));
        Assert.NotNull(cut.Find("#opentrons-pipette-select"));
        Assert.NotNull(cut.Find("#opentrons-plate-select"));
        Assert.NotNull(cut.Find("#opentrons-temp-module"));
        Assert.NotNull(cut.Find("#opentrons-thermocycler-module"));

        await cut.InvokeAsync(() => cut.Instance.SetStep(6));
        Assert.NotEmpty(cut.FindAll(".opentrons-section"));
        Assert.NotNull(cut.Find("#opentrons-robot-select"));
        Assert.NotNull(cut.Find("#opentrons-pipette-select"));
    }

    /// <summary>
    /// Verifies that selecting Opentrons hardware options updates the displayed deck layout summary.
    /// </summary>
    /// <returns>A task representing the asynchronous test execution.</returns>
    [Fact]
    public async Task SelectingOpentronsOptions_UpdatesDisplayedDeckSummary()
    {
        // WHAT: Tests reactive recalculation of deck slot and tip rack summaries upon option changes.
        // WHY:  Operators require live visual feedback on required labware, deck slots, and runtime.
        // HOW:  Renders stage 5, changes robot model from OT-2 to Flex, changes plate to 384-well, and asserts updated text.
        IRenderedComponent<WorkflowWizard> cut = RenderComponent<WorkflowWizard>();
        await cut.InvokeAsync(() => cut.Instance.SetStep(5));

        // Initial default is OT-2 (11 slots capacity)
        var deckSlotsInitial = cut.Find(".deck-slots-value");
        Assert.Contains("11 Slots", deckSlotsInitial.TextContent);

        // Switch to Flex platform
        var robotSelect = cut.Find("#opentrons-robot-select");
        robotSelect.Change(OpentronsRobotModel.Flex.ToString());

        var deckSlotsFlex = cut.Find(".deck-slots-value");
        Assert.Contains("12 Slots", deckSlotsFlex.TextContent);
        Assert.Equal(OpentronsRobotModel.Flex, cut.Instance.ActiveOpentronsOptions.RobotModel);

        // Switch source plate to 384-well format
        var plateSelect = cut.Find("#opentrons-plate-select");
        plateSelect.Change(OpentronsPlateType.Nest384WellPcr.ToString());
        Assert.Equal(OpentronsPlateType.Nest384WellPcr, cut.Instance.ActiveOpentronsOptions.SourcePlateType);

        // Toggle on-deck thermocycler module
        var tcCheckbox = cut.Find("#opentrons-thermocycler-module");
        tcCheckbox.Change(true);
        Assert.True(cut.Instance.ActiveOpentronsOptions.UseThermocyclerModule);

        // Estimated runtime should increase due to thermal ramp steps
        var runtimeValue = cut.Find(".estimated-runtime-value");
        Assert.True(cut.Instance.CurrentOpentronsResult?.EstimatedRunDurationMinutes > 0);
        Assert.Contains("min", runtimeValue.TextContent);
    }

    /// <summary>
    /// Verifies that clicking "Generate Opentrons Protocol" populates the Python script preview,
    /// and that the preview modal dialog opens and closes cleanly.
    /// </summary>
    /// <returns>A task representing the asynchronous test execution.</returns>
    [Fact]
    public async Task ClickingGenerateOpentronsProtocol_PopulatesScriptPreview_AndModalOpensAndClosesCleanly()
    {
        // WHAT: Tests modal lifecycle and Python protocol script rendering.
        // WHY:  Guarantees operators can preview, inspect, and dismiss the generated automation protocol.
        // HOW:  Asserts modal is hidden initially, clicks generate button, asserts modal visibility and script content, then clicks close.
        IRenderedComponent<WorkflowWizard> cut = RenderComponent<WorkflowWizard>();
        await cut.InvokeAsync(() => cut.Instance.SetStep(6));

        // Modal should initially be closed
        Assert.False(cut.Instance.IsOpentronsModalOpen);
        Assert.Empty(cut.FindAll(".modal-overlay"));

        // Click generate button
        var generateBtn = cut.Find("#btn-generate-opentrons");
        generateBtn.Click();

        // Modal should now be open with populated Python script
        Assert.True(cut.Instance.IsOpentronsModalOpen);
        Assert.NotEmpty(cut.FindAll(".modal-overlay"));

        var codeBlock = cut.Find(".protocol-preview-block code");
        Assert.Contains("from opentrons import protocol_api", codeBlock.TextContent);
        Assert.Contains("metadata = {", codeBlock.TextContent);
        Assert.Contains("def run(protocol: protocol_api.ProtocolContext):", codeBlock.TextContent);

        // Close modal via close button
        var closeBtn = cut.Find("#btn-close-opentrons-modal");
        closeBtn.Click();

        Assert.False(cut.Instance.IsOpentronsModalOpen);
        Assert.Empty(cut.FindAll(".modal-overlay"));
    }

    /// <summary>
    /// Verifies that pressing the Escape key dismisses the Opentrons script preview modal.
    /// </summary>
    /// <returns>A task representing the asynchronous test execution.</returns>
    [Fact]
    public async Task ModalDialog_ClosesOnEscapeKeyPress()
    {
        // WHAT: Tests keyboard accessibility invariant (Escape key closes modal).
        // WHY:  WCAG 2.1 compliance requires accessible keyboard dismiss triggers for modal dialogs.
        // HOW:  Opens modal, dispatches onkeydown event with Key = "Escape", and asserts modal closure.
        IRenderedComponent<WorkflowWizard> cut = RenderComponent<WorkflowWizard>();
        await cut.InvokeAsync(() => cut.Instance.SetStep(5));

        cut.Find("#btn-generate-opentrons").Click();
        Assert.True(cut.Instance.IsOpentronsModalOpen);

        var modalOverlay = cut.Find(".modal-overlay");
        modalOverlay.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.False(cut.Instance.IsOpentronsModalOpen);
        Assert.Empty(cut.FindAll(".modal-overlay"));
    }

    /// <summary>
    /// Verifies that clicking "Download Protocol (.py)" and "Copy to Clipboard" invokes corresponding JS interop functions.
    /// </summary>
    /// <returns>A task representing the asynchronous test execution.</returns>
    [Fact]
    public async Task DownloadAndCopyActions_InvokeExpectedJsInterop()
    {
        // WHAT: Tests client-side interop dispatch for protocol download and clipboard copy.
        // WHY:  Validates that operator download and copy actions bridge smoothly to browser helpers.
        // HOW:  Opens modal, clicks download button to trigger weaverDownloadBlob, clicks copy button to trigger weaverCopyText.
        IRenderedComponent<WorkflowWizard> cut = RenderComponent<WorkflowWizard>();
        await cut.InvokeAsync(() => cut.Instance.SetStep(6));

        cut.Find("#btn-generate-opentrons").Click();

        // Click download button
        var downloadBtn = cut.Find("#btn-download-opentrons-script");
        downloadBtn.Click();

        JSInterop.VerifyInvoke("weaverDownloadBlob");

        // Click copy to clipboard button
        var copyBtn = cut.Find("#btn-copy-opentrons-script");
        copyBtn.Click();

        JSInterop.VerifyInvoke("weaverCopyText");

        var copyBadge = cut.Find(".opentrons-copy-badge");
        Assert.Equal("Copied to clipboard!", copyBadge.TextContent.Trim());
    }

    /// <summary>
    /// Verifies that switching the robot platform automatically reconciles incompatible pipette models.
    /// </summary>
    /// <returns>A task representing the asynchronous test execution.</returns>
    [Fact]
    public async Task RobotModelSwitch_ReconcilesIncompatiblePipettes()
    {
        // WHAT: Validates automatic pipette model reconciliation when switching between OT-2 and Flex.
        // WHY:  OT-2 pipettes cannot be used on Flex and vice versa.
        // HOW:  Invokes SetOpentronsRobot and verifies that PipetteModel adjusts to compatible defaults.
        IRenderedComponent<WorkflowWizard> cut = RenderComponent<WorkflowWizard>();

        // Default: OT-2 with P20 Single
        Assert.Equal(OpentronsRobotModel.OT2, cut.Instance.ActiveOpentronsOptions.RobotModel);
        Assert.Equal(OpentronsPipetteModel.P20_Single_Gen2, cut.Instance.ActiveOpentronsOptions.PipetteModel);

        // Switch to Flex -> pipette becomes Flex_1Channel_50
        await cut.InvokeAsync(() => cut.Instance.SetOpentronsRobot(OpentronsRobotModel.Flex));
        Assert.Equal(OpentronsRobotModel.Flex, cut.Instance.ActiveOpentronsOptions.RobotModel);
        Assert.Equal(OpentronsPipetteModel.Flex_1Channel_50, cut.Instance.ActiveOpentronsOptions.PipetteModel);

        // Switch back to OT-2 -> pipette becomes P20_Single_Gen2
        await cut.InvokeAsync(() => cut.Instance.SetOpentronsRobot(OpentronsRobotModel.OT2));
        Assert.Equal(OpentronsRobotModel.OT2, cut.Instance.ActiveOpentronsOptions.RobotModel);
        Assert.Equal(OpentronsPipetteModel.P20_Single_Gen2, cut.Instance.ActiveOpentronsOptions.PipetteModel);
    }

    /// <summary>
    /// Verifies deep-linking into specific wizard stages via the StepQuery query parameter.
    /// </summary>
    [Fact]
    public void StepQueryParameter_DeepLinksToStage5Directly()
    {
        // WHAT: Tests query string deep-linking to stage 5 (?step=5).
        // WHY:  Enables researchers to link directly into stoichiometric liquid handling planning.
        // HOW:  Navigates to /wizard?step=5 prior to mounting WorkflowWizard and asserts active step is 5.
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/wizard?step=5");

        IRenderedComponent<WorkflowWizard> cut = RenderComponent<WorkflowWizard>();

        Assert.Equal(5, cut.Instance.CurrentStep);
        Assert.NotEmpty(cut.FindAll(".opentrons-section"));
    }
}
