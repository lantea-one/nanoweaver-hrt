using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Fabrication.Gel;
using NanoWeaverHrt.Fabrication.Opentrons;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Pipelines;
using NanoWeaverHrt.Storage;
using Moq;
using Xunit;

namespace NanoWeaverHrt.Tests.Components;

/// <summary>
/// Unit tests for the WorkflowWizard Stage 6 Agarose Gel Electrophoresis Densitometry inspection UI.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> bUnit component test suite verifying Stage 6 gel densitometry rendering, monomer yield metrics, staple smear percentages, and quality badges ("PASS (Acceptable)" vs "SUBOPTIMAL").
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enforces 100% test coverage and validates correct wet-lab quality control rendering for DNA origami nanocarrier synthesis.
/// </para>
/// <para>
/// <strong>How it works:</strong> Registers mock services in the bUnit test context, renders <see cref="WorkflowWizard"/>, navigates to Stage 6, and asserts DOM element values and CSS badges.
/// </para>
/// </remarks>
public class GelWizardComponentTests : TestContext
{
    /// <summary>
    /// Verifies that Stage 6 renders acceptable gel electrophoresis results and quality badges correctly.
    /// </summary>
    [Fact]
    public void Stage6_GelInspection_Acceptable_RendersPassingBadgeAndPlot()
    {
        // WHAT: Set up mock gel analysis service returning acceptable monomer yields and low smear.
        // WHY:  Validates successful wet-lab quality control UI rendering.
        // HOW:  Configures Moq to return a passing GelAnalysisResult and renders WorkflowWizard via bUnit.
        var mockFabrication = new Mock<IFabricationService>();
        mockFabrication.Setup(f => f.GetPresetSamples()).Returns([
            new DnaOrigamiSample
            {
                Id = "test-sample",
                Title = "Test Sample",
                Regimen = TherapeuticRegimen.MaleToFemale,
                VehicleGeometry = "Hexagonal 6-Helix Bundle",
                TargetTissue = "Anterior Pituitary",
                ActiveLigand = "17β-Estradiol",
                TargetKdNanomolar = 4.2,
                Description = "Test desc",
                BiomedicalRationale = "Test rationale",
                AptamerSequence = "ATCGATCGATCG",
                ScaffoldType = "M13mp18"
            }
        ]);
        mockFabrication.Setup(f => f.AnalyzeSequenceComposition(It.IsAny<string>()))
            .Returns((10, 10, 10, 10, 50.0, 12.5));

        var mockGelService = new Mock<IGelAnalysisService>();
        mockGelService.Setup(g => g.AnalyzeGel(It.IsAny<GelAnalysisRequest>()))
            .Returns(new GelAnalysisResult
            {
                OverallFoldingSuccessRate = 88.5,
                DetectedLaneCount = 3,
                QualityAssessmentNotes = "Optimal folding monomer yield with minimal staple smear.",
                Lanes =
                [
                    new() { LaneNumber = 1, Label = "1 kb Ladder", MonomerBandYieldPercentage = 0.0, UnannealedStapleSmearPercentage = 0.0, DimerMultimerPercentage = 0.0, FoldingQualityAcceptable = true },
                    new() { LaneNumber = 2, Label = "Monomer Sample A", MonomerBandYieldPercentage = 88.5, UnannealedStapleSmearPercentage = 12.0, DimerMultimerPercentage = 5.0, FoldingQualityAcceptable = true },
                    new() { LaneNumber = 3, Label = "Monomer Sample B", MonomerBandYieldPercentage = 85.0, UnannealedStapleSmearPercentage = 15.0, DimerMultimerPercentage = 6.0, FoldingQualityAcceptable = true }
                ]
            });

        Services.AddSingleton(mockFabrication.Object);
        Services.AddSingleton(mockGelService.Object);
        Services.AddSingleton<EndocrineStateEngine>();
        Services.AddSingleton<IPharmacogenomicsService, PharmacogenomicsService>();
        Services.AddSingleton<IConfigurationRepository>(new Mock<IConfigurationRepository>().Object);
        Services.AddSingleton<XyzPipeReader>();
        Services.AddSingleton<DcdPipeReader>();
        Services.AddSingleton<LammpsCustomPipeReader>();
        Services.AddSingleton<IParquetTrajectoryWriter>(new Mock<IParquetTrajectoryWriter>().Object);
        Services.AddSingleton<IOpentronsProtocolGenerator, OpentronsProtocolGenerator>();

        var cut = RenderComponent<WorkflowWizard>();
        cut.InvokeAsync(() => cut.Instance.SetStep(6));

        Assert.Contains("88.5%", cut.Markup);
        Assert.Contains("PASS", cut.Markup);
    }

    /// <summary>
    /// Verifies that Stage 6 renders suboptimal or failed gel inspection results when smear or aggregation exceeds thresholds.
    /// </summary>
    [Fact]
    public void Stage6_GelInspection_Suboptimal_RendersSmearWarningBadge()
    {
        // WHAT: Set up mock gel analysis service returning low yield and high staple smear.
        // WHY:  Ensures the UI correctly alerts operators to suboptimal folding batches.
        // HOW:  Configures Moq to return a failing GelAnalysisResult and asserts warning strings in markup.
        var mockFabrication = new Mock<IFabricationService>();
        mockFabrication.Setup(f => f.GetPresetSamples()).Returns([
            new DnaOrigamiSample
            {
                Id = "test-sample",
                Title = "Test Sample",
                Regimen = TherapeuticRegimen.MaleToFemale,
                VehicleGeometry = "Hexagonal 6-Helix Bundle",
                TargetTissue = "Anterior Pituitary",
                ActiveLigand = "17β-Estradiol",
                TargetKdNanomolar = 4.2,
                Description = "Test desc",
                BiomedicalRationale = "Test rationale",
                AptamerSequence = "ATCGATCGATCG",
                ScaffoldType = "M13mp18"
            }
        ]);
        mockFabrication.Setup(f => f.AnalyzeSequenceComposition(It.IsAny<string>()))
            .Returns((10, 10, 10, 10, 50.0, 12.5));

        var mockGelService = new Mock<IGelAnalysisService>();
        mockGelService.Setup(g => g.AnalyzeGel(It.IsAny<GelAnalysisRequest>()))
            .Returns(new GelAnalysisResult
            {
                OverallFoldingSuccessRate = 42.0,
                DetectedLaneCount = 2,
                QualityAssessmentNotes = "Excess unannealed staple smear and aggregate precipitation detected.",
                Lanes =
                [
                    new() { LaneNumber = 1, Label = "1 kb Ladder", MonomerBandYieldPercentage = 0.0, UnannealedStapleSmearPercentage = 0.0, DimerMultimerPercentage = 0.0, FoldingQualityAcceptable = true },
                    new() { LaneNumber = 2, Label = "Aggregated Sample", MonomerBandYieldPercentage = 42.0, UnannealedStapleSmearPercentage = 45.0, DimerMultimerPercentage = 25.0, FoldingQualityAcceptable = false }
                ]
            });

        Services.AddSingleton(mockFabrication.Object);
        Services.AddSingleton(mockGelService.Object);
        Services.AddSingleton<EndocrineStateEngine>();
        Services.AddSingleton<IPharmacogenomicsService, PharmacogenomicsService>();
        Services.AddSingleton<IConfigurationRepository>(new Mock<IConfigurationRepository>().Object);
        Services.AddSingleton<XyzPipeReader>();
        Services.AddSingleton<DcdPipeReader>();
        Services.AddSingleton<LammpsCustomPipeReader>();
        Services.AddSingleton<IParquetTrajectoryWriter>(new Mock<IParquetTrajectoryWriter>().Object);
        Services.AddSingleton<IOpentronsProtocolGenerator, OpentronsProtocolGenerator>();

        var cut = RenderComponent<WorkflowWizard>();
        cut.InvokeAsync(() => cut.Instance.SetStep(6));

        Assert.Contains("42.0%", cut.Markup);
        Assert.Contains("SUBOPTIMAL", cut.Markup);
    }
}
