using NanoWeaverHrt.Hardware.Benchmarking;
using Xunit;

namespace NanoWeaverHrt.Tests.Hardware;

/// <summary>
/// Unit test suite verifying Linux sysfs DRM telemetry discovery and fallback heuristics in <see cref="DefaultAcceleratorTelemetryProvider"/>.
/// </summary>
[Collection("AcceleratorTelemetryEnvironmentTests")]
public sealed class AcceleratorBenchmarkTests : IDisposable
{
    /// <summary>
    /// Preserved original value of ACCELERATOR_DEVICE_NAME before test execution.
    /// </summary>
    private readonly string? _originalDeviceName;

    /// <summary>
    /// Preserved original value of ACCELERATOR_BACKEND before test execution.
    /// </summary>
    private readonly string? _originalBackend;

    /// <summary>
    /// Preserved original value of ACCELERATOR_DRIVER_VERSION before test execution.
    /// </summary>
    private readonly string? _originalDriverVersion;

    /// <summary>
    /// Preserved original value of ACCELERATOR_TEMPERATURE_C before test execution.
    /// </summary>
    private readonly string? _originalTemperature;

    /// <summary>
    /// Preserved original value of ACCELERATOR_POWER_WATTS before test execution.
    /// </summary>
    private readonly string? _originalPower;

    /// <summary>
    /// Root path of the temporary directory tree simulating Linux sysfs DRM nodes.
    /// </summary>
    private readonly string _testSysfsRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="AcceleratorBenchmarkTests"/> class, snapshotting environment variables and creating a sandbox sysfs tree.
    /// </summary>
    public AcceleratorBenchmarkTests()
    {
        _originalDeviceName = Environment.GetEnvironmentVariable("ACCELERATOR_DEVICE_NAME");
        _originalBackend = Environment.GetEnvironmentVariable("ACCELERATOR_BACKEND");
        _originalDriverVersion = Environment.GetEnvironmentVariable("ACCELERATOR_DRIVER_VERSION");
        _originalTemperature = Environment.GetEnvironmentVariable("ACCELERATOR_TEMPERATURE_C");
        _originalPower = Environment.GetEnvironmentVariable("ACCELERATOR_POWER_WATTS");

        _testSysfsRoot = Path.Combine(Path.GetTempPath(), "weaver_drm_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testSysfsRoot);
    }

    /// <summary>
    /// Restores environment variables and purges temporary simulated sysfs directories.
    /// </summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ACCELERATOR_DEVICE_NAME", _originalDeviceName);
        Environment.SetEnvironmentVariable("ACCELERATOR_BACKEND", _originalBackend);
        Environment.SetEnvironmentVariable("ACCELERATOR_DRIVER_VERSION", _originalDriverVersion);
        Environment.SetEnvironmentVariable("ACCELERATOR_TEMPERATURE_C", _originalTemperature);
        Environment.SetEnvironmentVariable("ACCELERATOR_POWER_WATTS", _originalPower);

        if (Directory.Exists(_testSysfsRoot))
        {
            try
            {
                Directory.Delete(_testSysfsRoot, recursive: true);
            }
            catch (Exception)
            {
                // Swallowed in disposal cleanup
            }
        }
    }

    /// <summary>
    /// Clears all accelerator-related environment variables for isolated sysfs probing tests.
    /// </summary>
    private static void ClearAllEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("ACCELERATOR_DEVICE_NAME", null);
        Environment.SetEnvironmentVariable("ACCELERATOR_BACKEND", null);
        Environment.SetEnvironmentVariable("ACCELERATOR_DRIVER_VERSION", null);
        Environment.SetEnvironmentVariable("ACCELERATOR_TEMPERATURE_C", null);
        Environment.SetEnvironmentVariable("ACCELERATOR_POWER_WATTS", null);
    }

    /// <summary>
    /// Verifies that the default parameterless constructor initializes without throwing.
    /// </summary>
    [Fact]
    public void Constructor_DefaultParameterless_InitializesSuccessfully()
    {
        DefaultAcceleratorTelemetryProvider provider = new();
        Assert.NotNull(provider);
        Assert.False(string.IsNullOrWhiteSpace(provider.DeviceName));
        Assert.False(string.IsNullOrWhiteSpace(provider.DriverVersion));
        Assert.True(provider.TemperatureCelsius > 0.0);
        Assert.True(provider.PowerUsageWatts > 0.0);
    }

    /// <summary>
    /// Verifies that passing null or whitespace for sysfs path falls back to default values.
    /// </summary>
    [Fact]
    public void Constructor_NullOrWhitespaceSysfsPath_FallsBackToDefaults()
    {
        ClearAllEnvironmentVariables();

        DefaultAcceleratorTelemetryProvider nullProvider = new(null);
        Assert.Equal("Intel Arc A770 Discrete GPU (OpenCL / SYCL)", nullProvider.DeviceName);
        Assert.Equal(48.5, nullProvider.TemperatureCelsius);
        Assert.Equal(75.0, nullProvider.PowerUsageWatts);

        DefaultAcceleratorTelemetryProvider whitespaceProvider = new("   ");
        Assert.Equal("Intel Arc A770 Discrete GPU (OpenCL / SYCL)", whitespaceProvider.DeviceName);
        Assert.Equal(48.5, whitespaceProvider.TemperatureCelsius);
        Assert.Equal(75.0, whitespaceProvider.PowerUsageWatts);
    }

    /// <summary>
    /// Verifies that a non-existent sysfs directory path falls back to default values.
    /// </summary>
    [Fact]
    public void Sysfs_NonExistentDirectory_FallsBackToDefaults()
    {
        ClearAllEnvironmentVariables();
        string nonExistent = Path.Combine(_testSysfsRoot, "missing_drm_dir");

        DefaultAcceleratorTelemetryProvider provider = new(nonExistent);

        Assert.Equal("Intel Arc A770 Discrete GPU (OpenCL / SYCL)", provider.DeviceName);
        Assert.Equal(48.5, provider.TemperatureCelsius);
        Assert.Equal(75.0, provider.PowerUsageWatts);
    }

    /// <summary>
    /// Verifies that <see cref="DefaultAcceleratorTelemetryProvider.TemperatureCelsius"/> discovers and parses millidegree telemetry from hwmon temp1_input.
    /// </summary>
    [Fact]
    public void TemperatureCelsius_SysfsDiscovery_ParsesMillidegreesToCelsius()
    {
        ClearAllEnvironmentVariables();

        string hwmon0Dir = Path.Combine(_testSysfsRoot, "card0", "device", "hwmon", "hwmon0");
        Directory.CreateDirectory(hwmon0Dir);
        File.WriteAllText(Path.Combine(hwmon0Dir, "temp1_input"), "62500\n");

        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);

        Assert.Equal(62.5, provider.TemperatureCelsius);
    }

    /// <summary>
    /// Verifies that environment variable overrides sysfs discovery for temperature telemetry.
    /// </summary>
    [Fact]
    public void TemperatureCelsius_EnvironmentVariable_TakesPrecedenceOverSysfs()
    {
        string hwmon0Dir = Path.Combine(_testSysfsRoot, "card0", "device", "hwmon", "hwmon0");
        Directory.CreateDirectory(hwmon0Dir);
        File.WriteAllText(Path.Combine(hwmon0Dir, "temp1_input"), "62500\n");

        Environment.SetEnvironmentVariable("ACCELERATOR_TEMPERATURE_C", "53.2");

        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);

        Assert.Equal(53.2, provider.TemperatureCelsius);
    }

    /// <summary>
    /// Verifies that invalid or non-positive temperature readings in sysfs fall back to default temperature.
    /// </summary>
    [Fact]
    public void TemperatureCelsius_InvalidOrNonPositiveSysfs_FallsBackToDefault()
    {
        ClearAllEnvironmentVariables();

        string hwmon0Dir = Path.Combine(_testSysfsRoot, "card0", "device", "hwmon", "hwmon0");
        Directory.CreateDirectory(hwmon0Dir);
        string tempFile = Path.Combine(hwmon0Dir, "temp1_input");

        // Non-numeric content
        File.WriteAllText(tempFile, "corrupted_temp");
        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);
        Assert.Equal(48.5, provider.TemperatureCelsius);

        // Negative value
        File.WriteAllText(tempFile, "-1000");
        Assert.Equal(48.5, provider.TemperatureCelsius);

        // Zero value
        File.WriteAllText(tempFile, "0");
        Assert.Equal(48.5, provider.TemperatureCelsius);
    }

    /// <summary>
    /// Verifies that missing temp1_input or missing hwmon directories fall back to default temperature.
    /// </summary>
    [Fact]
    public void TemperatureCelsius_MissingSysfsFilesOrDirectories_FallsBackToDefault()
    {
        ClearAllEnvironmentVariables();

        // Card0 exists, but no device directory
        string card0 = Path.Combine(_testSysfsRoot, "card0");
        Directory.CreateDirectory(card0);
        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);
        Assert.Equal(48.5, provider.TemperatureCelsius);

        // Device exists, but no hwmon directory
        string deviceDir = Path.Combine(card0, "device");
        Directory.CreateDirectory(deviceDir);
        Assert.Equal(48.5, provider.TemperatureCelsius);

        // hwmon base exists, but no hwmon* subdirectories
        string hwmonBase = Path.Combine(deviceDir, "hwmon");
        Directory.CreateDirectory(hwmonBase);
        Assert.Equal(48.5, provider.TemperatureCelsius);

        // hwmon0 exists, but temp1_input is absent
        string hwmon0 = Path.Combine(hwmonBase, "hwmon0");
        Directory.CreateDirectory(hwmon0);
        Assert.Equal(48.5, provider.TemperatureCelsius);
    }

    /// <summary>
    /// Verifies that <see cref="DefaultAcceleratorTelemetryProvider.PowerUsageWatts"/> discovers and parses microwatt telemetry from power1_average.
    /// </summary>
    [Fact]
    public void PowerUsageWatts_SysfsDiscovery_ParsesPower1AverageMicrowatts()
    {
        ClearAllEnvironmentVariables();

        string hwmon0Dir = Path.Combine(_testSysfsRoot, "card0", "device", "hwmon", "hwmon0");
        Directory.CreateDirectory(hwmon0Dir);
        File.WriteAllText(Path.Combine(hwmon0Dir, "power1_average"), "125000000\n");

        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);

        Assert.Equal(125.0, provider.PowerUsageWatts);
    }

    /// <summary>
    /// Verifies that <see cref="DefaultAcceleratorTelemetryProvider.PowerUsageWatts"/> falls back to power1_input when power1_average is absent.
    /// </summary>
    [Fact]
    public void PowerUsageWatts_SysfsDiscovery_FallsBackToPower1Input()
    {
        ClearAllEnvironmentVariables();

        string hwmon0Dir = Path.Combine(_testSysfsRoot, "card0", "device", "hwmon", "hwmon0");
        Directory.CreateDirectory(hwmon0Dir);
        File.WriteAllText(Path.Combine(hwmon0Dir, "power1_input"), "95000000\n");

        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);

        Assert.Equal(95.0, provider.PowerUsageWatts);
    }

    /// <summary>
    /// Verifies that environment variable overrides sysfs discovery for power telemetry.
    /// </summary>
    [Fact]
    public void PowerUsageWatts_EnvironmentVariable_TakesPrecedenceOverSysfs()
    {
        string hwmon0Dir = Path.Combine(_testSysfsRoot, "card0", "device", "hwmon", "hwmon0");
        Directory.CreateDirectory(hwmon0Dir);
        File.WriteAllText(Path.Combine(hwmon0Dir, "power1_average"), "125000000\n");

        Environment.SetEnvironmentVariable("ACCELERATOR_POWER_WATTS", "210.5");

        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);

        Assert.Equal(210.5, provider.PowerUsageWatts);
    }

    /// <summary>
    /// Verifies that invalid or non-positive power readings in sysfs fall back to default power.
    /// </summary>
    [Fact]
    public void PowerUsageWatts_InvalidOrNonPositiveSysfs_FallsBackToDefault()
    {
        ClearAllEnvironmentVariables();

        string hwmon0Dir = Path.Combine(_testSysfsRoot, "card0", "device", "hwmon", "hwmon0");
        Directory.CreateDirectory(hwmon0Dir);
        string powerFile = Path.Combine(hwmon0Dir, "power1_average");

        // Non-numeric content
        File.WriteAllText(powerFile, "corrupted_power");
        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);
        Assert.Equal(75.0, provider.PowerUsageWatts);

        // Negative value
        File.WriteAllText(powerFile, "-500000");
        Assert.Equal(75.0, provider.PowerUsageWatts);

        // Zero value
        File.WriteAllText(powerFile, "0");
        Assert.Equal(75.0, provider.PowerUsageWatts);
    }

    /// <summary>
    /// Verifies that missing power files or directories fall back to default power.
    /// </summary>
    [Fact]
    public void PowerUsageWatts_MissingSysfsFilesOrDirectories_FallsBackToDefault()
    {
        ClearAllEnvironmentVariables();

        string card0 = Path.Combine(_testSysfsRoot, "card0");
        Directory.CreateDirectory(card0);
        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);
        Assert.Equal(75.0, provider.PowerUsageWatts);

        string deviceDir = Path.Combine(card0, "device");
        Directory.CreateDirectory(deviceDir);
        Assert.Equal(75.0, provider.PowerUsageWatts);

        string hwmonBase = Path.Combine(deviceDir, "hwmon");
        Directory.CreateDirectory(hwmonBase);
        Assert.Equal(75.0, provider.PowerUsageWatts);

        string hwmon0 = Path.Combine(hwmonBase, "hwmon0");
        Directory.CreateDirectory(hwmon0);
        Assert.Equal(75.0, provider.PowerUsageWatts);
    }

    /// <summary>
    /// Verifies that <see cref="DefaultAcceleratorTelemetryProvider.DeviceName"/> discovers hardware label from device/label.
    /// </summary>
    [Fact]
    public void DeviceName_SysfsDiscovery_ReadsLabelFile()
    {
        ClearAllEnvironmentVariables();

        string deviceDir = Path.Combine(_testSysfsRoot, "card0", "device");
        Directory.CreateDirectory(deviceDir);
        File.WriteAllText(Path.Combine(deviceDir, "label"), "Intel Data Center GPU Flex 170\n");

        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);

        Assert.Equal("Intel Data Center GPU Flex 170", provider.DeviceName);
    }

    /// <summary>
    /// Verifies that <see cref="DefaultAcceleratorTelemetryProvider.DeviceName"/> falls back to uevent DRIVER when label is absent.
    /// </summary>
    [Fact]
    public void DeviceName_SysfsDiscovery_FallsBackToUeventDriver()
    {
        ClearAllEnvironmentVariables();

        string deviceDir = Path.Combine(_testSysfsRoot, "card0", "device");
        Directory.CreateDirectory(deviceDir);
        File.WriteAllText(
            Path.Combine(deviceDir, "uevent"),
            "PCI_CLASS=30000\nDRIVER=i915\nPCI_ID=8086:56A0\n");

        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);

        Assert.Equal("i915", provider.DeviceName);
    }

    /// <summary>
    /// Verifies that <see cref="DefaultAcceleratorTelemetryProvider.DeviceName"/> falls back to hwmon name when label and uevent DRIVER are absent.
    /// </summary>
    [Fact]
    public void DeviceName_SysfsDiscovery_FallsBackToHwmonName()
    {
        ClearAllEnvironmentVariables();

        string deviceDir = Path.Combine(_testSysfsRoot, "card0", "device");
        string hwmon0Dir = Path.Combine(deviceDir, "hwmon", "hwmon0");
        Directory.CreateDirectory(hwmon0Dir);

        // Write uevent without DRIVER
        File.WriteAllText(Path.Combine(deviceDir, "uevent"), "PCI_CLASS=30000\nPCI_ID=8086:56A0\n");
        // Write hwmon name
        File.WriteAllText(Path.Combine(hwmon0Dir, "name"), "xe\n");

        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);

        Assert.Equal("xe", provider.DeviceName);
    }

    /// <summary>
    /// Verifies that <see cref="DefaultAcceleratorTelemetryProvider.DeviceName"/> falls back to default name when all sysfs files are empty or missing.
    /// </summary>
    [Fact]
    public void DeviceName_SysfsDiscovery_EmptyOrMissing_FallsBackToDefault()
    {
        ClearAllEnvironmentVariables();

        string deviceDir = Path.Combine(_testSysfsRoot, "card0", "device");
        string hwmon0Dir = Path.Combine(deviceDir, "hwmon", "hwmon0");
        Directory.CreateDirectory(hwmon0Dir);

        // Write empty/whitespace label, uevent with empty DRIVER, and empty hwmon name
        File.WriteAllText(Path.Combine(deviceDir, "label"), "   \n");
        File.WriteAllText(Path.Combine(deviceDir, "uevent"), "DRIVER=   \n");
        File.WriteAllText(Path.Combine(hwmon0Dir, "name"), "   \n");

        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);

        Assert.Equal("Intel Arc A770 Discrete GPU (OpenCL / SYCL)", provider.DeviceName);
    }

    /// <summary>
    /// Verifies that environment variable ACCELERATOR_DEVICE_NAME overrides sysfs discovery.
    /// </summary>
    [Fact]
    public void DeviceName_EnvironmentVariable_TakesPrecedenceOverSysfs()
    {
        string deviceDir = Path.Combine(_testSysfsRoot, "card0", "device");
        Directory.CreateDirectory(deviceDir);
        File.WriteAllText(Path.Combine(deviceDir, "label"), "Sysfs Hardware Label");

        Environment.SetEnvironmentVariable("ACCELERATOR_DEVICE_NAME", "NVIDIA H100 PCIe");

        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);

        Assert.Equal("NVIDIA H100 PCIe", provider.DeviceName);
    }

    /// <summary>
    /// Verifies that ACCELERATOR_BACKEND takes precedence over sysfs discovery when ACCELERATOR_DEVICE_NAME is unset.
    /// </summary>
    [Fact]
    public void DeviceName_BackendEnvironmentVariable_TakesPrecedenceOverSysfs()
    {
        string deviceDir = Path.Combine(_testSysfsRoot, "card0", "device");
        Directory.CreateDirectory(deviceDir);
        File.WriteAllText(Path.Combine(deviceDir, "label"), "Sysfs Hardware Label");

        Environment.SetEnvironmentVariable("ACCELERATOR_DEVICE_NAME", null);
        Environment.SetEnvironmentVariable("ACCELERATOR_BACKEND", "AMD Radeon RX 7900 XTX (ROCm)");

        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);

        Assert.Equal("AMD Radeon RX 7900 XTX (ROCm)", provider.DeviceName);
    }

    /// <summary>
    /// Verifies that exceptions during sysfs file access (such as permissions denial) are handled safely.
    /// </summary>
    [Fact]
    public void Sysfs_UnreadableFiles_CatchesExceptionAndFallsBackToDefaults()
    {
        ClearAllEnvironmentVariables();

        string deviceDir = Path.Combine(_testSysfsRoot, "card0", "device");
        string hwmon0Dir = Path.Combine(deviceDir, "hwmon", "hwmon0");
        Directory.CreateDirectory(hwmon0Dir);

        string labelFile = Path.Combine(deviceDir, "label");
        File.WriteAllText(labelFile, "Test Label");

        string tempFile = Path.Combine(hwmon0Dir, "temp1_input");
        File.WriteAllText(tempFile, "60000");

        string powerFile = Path.Combine(hwmon0Dir, "power1_average");
        File.WriteAllText(powerFile, "80000000");

        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(labelFile, UnixFileMode.None);
            File.SetUnixFileMode(tempFile, UnixFileMode.None);
            File.SetUnixFileMode(powerFile, UnixFileMode.None);
        }

        DefaultAcceleratorTelemetryProvider provider = new(_testSysfsRoot);

        if (OperatingSystem.IsLinux())
        {
            Assert.Equal("Intel Arc A770 Discrete GPU (OpenCL / SYCL)", provider.DeviceName);
            Assert.Equal(48.5, provider.TemperatureCelsius);
            Assert.Equal(75.0, provider.PowerUsageWatts);
        }
    }
}
