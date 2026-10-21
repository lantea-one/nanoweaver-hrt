using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit test suite verifying model records, blittable structs, telemetry models, and enum representations.
/// </summary>
public sealed class ModelTests
{
    /// <summary>
    /// Verifies initialization and properties of <see cref="SimulationBox"/>.
    /// </summary>
    [Fact]
    public void SimulationBox_ShouldStoreDimensionsAccurately()
    {
        // WHAT: Tests SimulationBox struct initialization.
        // WHY:  Ensures simulation cell boundary vectors are exact.
        // HOW:  Instantiates struct and compares property values.
        SimulationBox box = new(0f, 100f, 0f, 200f, 0f, 300f);

        Assert.Equal(0f, box.XLo);
        Assert.Equal(100f, box.XHi);
        Assert.Equal(0f, box.YLo);
        Assert.Equal(200f, box.YHi);
        Assert.Equal(0f, box.ZLo);
        Assert.Equal(300f, box.ZHi);
    }

    /// <summary>
    /// Verifies initialization and properties of <see cref="TrajectoryFileItem"/>.
    /// </summary>
    [Fact]
    public void TrajectoryFileItem_ShouldInitializeRecordCorrectly()
    {
        DateTime modified = DateTime.UtcNow;
        TrajectoryFileItem item = new()
        {
            Name = "nanotube.xyz",
            Format = "XYZ",
            SizeBytes = 1048576,
            SizeFormatted = "1.00 MB",
            LastModifiedUtc = modified.ToString("o")
        };

        Assert.Equal("nanotube.xyz", item.Name);
        Assert.Equal("XYZ", item.Format);
        Assert.Equal(1048576, item.SizeBytes);
        Assert.Equal("1.00 MB", item.SizeFormatted);
        Assert.Equal(modified.ToString("o"), item.LastModifiedUtc);
    }

    /// <summary>
    /// Verifies properties and initialization of <see cref="SystemTelemetry"/>.
    /// </summary>
    [Fact]
    public void SystemTelemetry_ShouldStoreMetricsAccurately()
    {
        SystemTelemetry telemetry = new()
        {
            NodeName = "compute-node-1",
            AcceleratorBackend = "Intel Arc A770 (Level Zero)",
            FramesParsed = 15000,
            FramesPerSecond = 500.5,
            HeapMemoryMb = 128.5,
            WorkingSetMb = 256.0,
            GcGen0Collections = 10,
            GcGen1Collections = 2,
            GcGen2Collections = 0,
            Uptime = "1d 2h 3m 4s"
        };

        Assert.Equal("compute-node-1", telemetry.NodeName);
        Assert.Equal("Intel Arc A770 (Level Zero)", telemetry.AcceleratorBackend);
        Assert.Equal(15000, telemetry.FramesParsed);
        Assert.Equal(500.5, telemetry.FramesPerSecond);
        Assert.Equal(128.5, telemetry.HeapMemoryMb);
        Assert.Equal(256.0, telemetry.WorkingSetMb);
        Assert.Equal(10, telemetry.GcGen0Collections);
        Assert.Equal(2, telemetry.GcGen1Collections);
        Assert.Equal(0, telemetry.GcGen2Collections);
        Assert.Equal("1d 2h 3m 4s", telemetry.Uptime);
    }

    /// <summary>
    /// Verifies properties and initialization of <see cref="AptamerTrajectoryMetrics"/>.
    /// </summary>
    [Fact]
    public void AptamerTrajectoryMetrics_ShouldStoreMetricsAccurately()
    {
        AptamerTrajectoryMetrics metrics = new()
        {
            TrajectoryFileName = "simulation.xyz",
            FramesAnalyzed = 100,
            AverageContactsPerFrame = 3.5,
            BindingOccupancyRatio = 0.95,
            StabilityVerdict = "Stable High-Affinity Complex"
        };

        Assert.Equal("simulation.xyz", metrics.TrajectoryFileName);
        Assert.Equal(100, metrics.FramesAnalyzed);
        Assert.Equal(3.5, metrics.AverageContactsPerFrame);
        Assert.Equal(0.95, metrics.BindingOccupancyRatio);
        Assert.Equal("Stable High-Affinity Complex", metrics.StabilityVerdict);
    }

    /// <summary>
    /// Verifies properties and initialization of <see cref="ExportRequest"/>.
    /// </summary>
    [Fact]
    public void ExportRequest_ShouldInitializePropertiesCorrectly()
    {
        ExportRequest request = new()
        {
            DatasetName = "dataset-01",
            Files = ["file1.xyz", "file2.dcd"]
        };

        Assert.Equal("dataset-01", request.DatasetName);
        Assert.NotNull(request.Files);
        Assert.Equal(2, request.Files.Length);
    }

    /// <summary>
    /// Verifies properties and initialization of <see cref="DatasetManifest"/> and child file entries.
    /// </summary>
    [Fact]
    public void DatasetManifest_ShouldSerializeAndStoreEntries()
    {
        DatasetManifest manifest = new()
        {
            DatasetName = "TestDataset",
            TimestampUtc = DateTime.UtcNow.ToString("o"),
            ArchiveFileName = "bundle.tar.gz",
            Sha256Checksum = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef",
            SizeInBytes = 8192,
            IncludedFiles = ["simulations/sample.xyz"]
        };

        Assert.Equal("TestDataset", manifest.DatasetName);
        Assert.Equal("bundle.tar.gz", manifest.ArchiveFileName);
        Assert.Equal(8192, manifest.SizeInBytes);
        Assert.Single(manifest.IncludedFiles);
        Assert.Equal("simulations/sample.xyz", manifest.IncludedFiles[0]);
    }

    /// <summary>
    /// Verifies all enum values across the endocrine domain.
    /// </summary>
    [Fact]
    public void EndocrineEnums_ShouldContainExpectedEnumValues()
    {
        Assert.True(Enum.IsDefined(typeof(HormoneType), HormoneType.EstradiolE2));
        Assert.True(Enum.IsDefined(typeof(HormoneType), HormoneType.Testosterone));
        Assert.True(Enum.IsDefined(typeof(HormoneType), HormoneType.Dihydrotestosterone));
        Assert.True(Enum.IsDefined(typeof(HormoneType), HormoneType.Progesterone));

        Assert.True(Enum.IsDefined(typeof(ToggleState), ToggleState.EstrogenicDominant));
        Assert.True(Enum.IsDefined(typeof(ToggleState), ToggleState.AndrogenicDominant));
        Assert.True(Enum.IsDefined(typeof(ToggleState), ToggleState.Transitioning));

        Assert.True(Enum.IsDefined(typeof(TherapeuticRegimen), TherapeuticRegimen.MaleToFemale));
        Assert.True(Enum.IsDefined(typeof(TherapeuticRegimen), TherapeuticRegimen.FemaleToMale));
        Assert.True(Enum.IsDefined(typeof(TherapeuticRegimen), TherapeuticRegimen.Custom));

        Assert.True(Enum.IsDefined(typeof(TargetTissueType), TargetTissueType.AnteriorPituitaryGonadotrope));
        Assert.True(Enum.IsDefined(typeof(TargetTissueType), TargetTissueType.AdrenalSteroidogenic));
        Assert.True(Enum.IsDefined(typeof(TargetTissueType), TargetTissueType.GonadalTarget));
        Assert.True(Enum.IsDefined(typeof(TargetTissueType), TargetTissueType.PeripheralReceptor));
    }
}
