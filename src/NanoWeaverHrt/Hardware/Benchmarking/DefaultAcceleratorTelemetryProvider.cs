using System.Globalization;

namespace NanoWeaverHrt.Hardware.Benchmarking;

/// <summary>
/// Default system accelerator telemetry provider reading environment configuration, probing Linux sysfs DRM nodes, or applying safe hardware defaults.
/// </summary>
public sealed class DefaultAcceleratorTelemetryProvider : IAcceleratorTelemetryProvider
{
    /// <summary>
    /// Environment variable key for overriding the detected device name.
    /// </summary>
    private const string DeviceNameEnvVar = "ACCELERATOR_DEVICE_NAME";

    /// <summary>
    /// Environment variable key for standard accelerator backend name.
    /// </summary>
    private const string BackendEnvVar = "ACCELERATOR_BACKEND";

    /// <summary>
    /// Environment variable key for overriding the driver version.
    /// </summary>
    private const string DriverVersionEnvVar = "ACCELERATOR_DRIVER_VERSION";

    /// <summary>
    /// Environment variable key for overriding the operating temperature.
    /// </summary>
    private const string TemperatureEnvVar = "ACCELERATOR_TEMPERATURE_C";

    /// <summary>
    /// Environment variable key for overriding the power usage.
    /// </summary>
    private const string PowerEnvVar = "ACCELERATOR_POWER_WATTS";

    /// <summary>
    /// Default fallback device name when no environment variables or sysfs attributes are defined.
    /// </summary>
    private const string DefaultDeviceName = "Intel Arc A770 Discrete GPU (OpenCL / SYCL)";

    /// <summary>
    /// Default fallback driver version string.
    /// </summary>
    private const string DefaultDriverVersion = "31.0.101.4952";

    /// <summary>
    /// Default fallback operating temperature in degrees Celsius.
    /// </summary>
    private const double DefaultTemperatureCelsius = 48.5;

    /// <summary>
    /// Default fallback power consumption in watts.
    /// </summary>
    private const double DefaultPowerUsageWatts = 75.0;

    /// <summary>
    /// Path to the Linux sysfs DRM subsystem directory, or null if hardware probing is disabled.
    /// </summary>
    private readonly string? _sysfsDrmPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAcceleratorTelemetryProvider"/> class using the standard Linux DRM sysfs directory.
    /// </summary>
    public DefaultAcceleratorTelemetryProvider()
        : this("/sys/class/drm")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAcceleratorTelemetryProvider"/> class targeting a specific sysfs DRM path.
    /// </summary>
    /// <param name="sysfsDrmPath">The directory path of the sysfs DRM subsystem, or null to bypass hardware sysfs probing.</param>
    public DefaultAcceleratorTelemetryProvider(string? sysfsDrmPath)
    {
        _sysfsDrmPath = sysfsDrmPath;
    }

    /// <inheritdoc />
    public string DeviceName
    {
        get
        {
            // WHAT: Retrieves device model name from environment variables, sysfs discovery, or applies fallback.
            // WHY:  Containerized environments export GPU identification through environment variables or sysfs DRM nodes.
            // HOW:  Checks ACCELERATOR_DEVICE_NAME, then ACCELERATOR_BACKEND, then queries sysfs DRM, then falls back to default.
            string? deviceName = Environment.GetEnvironmentVariable(DeviceNameEnvVar);
            if (!string.IsNullOrEmpty(deviceName))
            {
                return deviceName;
            }

            string? backend = Environment.GetEnvironmentVariable(BackendEnvVar);
            if (!string.IsNullOrEmpty(backend))
            {
                return backend;
            }

            if (TryReadSysfsDeviceName(out string sysfsName))
            {
                return sysfsName;
            }

            return DefaultDeviceName;
        }
    }

    /// <inheritdoc />
    public string DriverVersion
    {
        get
        {
            // WHAT: Retrieves driver or runtime version string.
            // WHY:  Driver version is necessary to verify compute kernel capability.
            // HOW:  Checks ACCELERATOR_DRIVER_VERSION or falls back to default known version.
            string? driverVersion = Environment.GetEnvironmentVariable(DriverVersionEnvVar);
            if (!string.IsNullOrEmpty(driverVersion))
            {
                return driverVersion;
            }

            return DefaultDriverVersion;
        }
    }

    /// <inheritdoc />
    public double TemperatureCelsius
    {
        get
        {
            // WHAT: Reads thermal probe telemetry in degrees Celsius.
            // WHY:  Thermal throttling impairs simulation throughput and indicates cooling failure.
            // HOW:  Parses ACCELERATOR_TEMPERATURE_C if present, else queries Linux sysfs DRM hwmon, else returns default safe temperature.
            string? raw = Environment.GetEnvironmentVariable(TemperatureEnvVar);
            if (raw != null && double.TryParse(raw, CultureInfo.InvariantCulture, out double parsed))
            {
                return parsed;
            }

            if (TryReadSysfsTemperature(out double sysfsTemperature))
            {
                return sysfsTemperature;
            }

            return DefaultTemperatureCelsius;
        }
    }

    /// <inheritdoc />
    public double PowerUsageWatts
    {
        get
        {
            // WHAT: Reads electrical power consumption telemetry in watts.
            // WHY:  Power monitoring ensures accelerator operation stays within TDP bounds.
            // HOW:  Parses ACCELERATOR_POWER_WATTS if present, else queries Linux sysfs DRM hwmon, else returns default power draw.
            string? raw = Environment.GetEnvironmentVariable(PowerEnvVar);
            if (raw != null && double.TryParse(raw, CultureInfo.InvariantCulture, out double parsed))
            {
                return parsed;
            }

            if (TryReadSysfsPower(out double sysfsPower))
            {
                return sysfsPower;
            }

            return DefaultPowerUsageWatts;
        }
    }

    /// <summary>
    /// Locates the primary card device directory under the sysfs DRM subsystem.
    /// </summary>
    /// <returns>The path to the primary card device directory if it exists; otherwise, <c>null</c>.</returns>
    private string? GetCardDeviceDirectory()
    {
        // WHAT: Locates the device directory of the primary DRM card (card0).
        // WHY:  Standard Linux DRM sysfs exposes hardware attributes and hwmon under card0/device.
        // HOW:  Combines _sysfsDrmPath with "card0" and "device" and verifies existence.
        if (string.IsNullOrWhiteSpace(_sysfsDrmPath) || !Directory.Exists(_sysfsDrmPath))
        {
            return null;
        }

        string deviceDir = Path.Combine(_sysfsDrmPath, "card0", "device");
        return Directory.Exists(deviceDir) ? deviceDir : null;
    }

    /// <summary>
    /// Locates the first available hwmon directory under the card device directory.
    /// </summary>
    /// <param name="deviceDir">The card device directory.</param>
    /// <returns>The path to the first hwmon directory if found; otherwise, <c>null</c>.</returns>
    private static string? GetHwmonDirectory(string deviceDir)
    {
        // WHAT: Discovers the hwmon subsystem directory for thermal and electrical sensors.
        // WHY:  Linux hwmon drivers register under device/hwmon/hwmonN.
        // HOW:  Checks device/hwmon and returns the first hwmon* subdirectory if present.
        string hwmonBase = Path.Combine(deviceDir, "hwmon");
        if (!Directory.Exists(hwmonBase))
        {
            return null;
        }

        string[] hwmonDirs = Directory.GetDirectories(hwmonBase, "hwmon*");
        return hwmonDirs.Length > 0 ? hwmonDirs[0] : null;
    }

    /// <summary>
    /// Probes the Linux sysfs DRM directory hierarchy for accelerator thermal telemetry.
    /// </summary>
    /// <param name="temperature">The detected temperature in degrees Celsius if successful; otherwise, zero.</param>
    /// <returns><c>true</c> if valid thermal telemetry was discovered; otherwise, <c>false</c>.</returns>
    private bool TryReadSysfsTemperature(out double temperature)
    {
        // WHAT: Reads millidegree temperature telemetry from temp1_input under hwmon.
        // WHY:  sysfs DRM hwmon exposes core temperatures as integer millidegrees Celsius.
        // HOW:  Reads temp1_input, parses millidegrees, converts to Celsius, and validates positive value.
        temperature = 0.0;
        string? deviceDir = GetCardDeviceDirectory();
        if (deviceDir == null)
        {
            return false;
        }

        string? hwmonDir = GetHwmonDirectory(deviceDir);
        if (hwmonDir == null)
        {
            return false;
        }

        string tempFile = Path.Combine(hwmonDir, "temp1_input");
        if (!File.Exists(tempFile))
        {
            return false;
        }

        try
        {
            string content = File.ReadAllText(tempFile).Trim();
            if (double.TryParse(content, CultureInfo.InvariantCulture, out double milliCelsius) && milliCelsius > 0.0)
            {
                temperature = milliCelsius / 1000.0;
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Probes the Linux sysfs DRM directory hierarchy for accelerator electrical power telemetry.
    /// </summary>
    /// <param name="powerWatts">The detected electrical power in watts if successful; otherwise, zero.</param>
    /// <returns><c>true</c> if valid power telemetry was discovered; otherwise, <c>false</c>.</returns>
    private bool TryReadSysfsPower(out double powerWatts)
    {
        // WHAT: Reads electrical power consumption telemetry from power1_average or power1_input.
        // WHY:  sysfs DRM hwmon exposes average or current power draw in microwatts.
        // HOW:  Probes power1_average, falls back to power1_input, parses microwatts, and converts to watts.
        powerWatts = 0.0;
        string? deviceDir = GetCardDeviceDirectory();
        if (deviceDir == null)
        {
            return false;
        }

        string? hwmonDir = GetHwmonDirectory(deviceDir);
        if (hwmonDir == null)
        {
            return false;
        }

        string powerFile = Path.Combine(hwmonDir, "power1_average");
        if (!File.Exists(powerFile))
        {
            powerFile = Path.Combine(hwmonDir, "power1_input");
            if (!File.Exists(powerFile))
            {
                return false;
            }
        }

        try
        {
            string content = File.ReadAllText(powerFile).Trim();
            if (double.TryParse(content, CultureInfo.InvariantCulture, out double microWatts) && microWatts > 0.0)
            {
                powerWatts = microWatts / 1_000_000.0;
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Probes the Linux sysfs DRM directory hierarchy for accelerator device identification.
    /// </summary>
    /// <param name="deviceName">The discovered device or driver name if found; otherwise, empty string.</param>
    /// <returns><c>true</c> if a device identifier was located; otherwise, <c>false</c>.</returns>
    private bool TryReadSysfsDeviceName(out string deviceName)
    {
        // WHAT: Discovers accelerator device identity from sysfs label, uevent DRIVER, or hwmon name.
        // WHY:  Permits automatic accelerator identification without manual environment variable configuration.
        // HOW:  Probes card0/device/label, then uevent DRIVER=, then hwmon/name.
        deviceName = string.Empty;
        string? deviceDir = GetCardDeviceDirectory();
        if (deviceDir == null)
        {
            return false;
        }

        try
        {
            string labelFile = Path.Combine(deviceDir, "label");
            if (File.Exists(labelFile))
            {
                string label = File.ReadAllText(labelFile).Trim();
                if (!string.IsNullOrWhiteSpace(label))
                {
                    deviceName = label;
                    return true;
                }
            }

            string ueventFile = Path.Combine(deviceDir, "uevent");
            if (File.Exists(ueventFile))
            {
                string[] lines = File.ReadAllLines(ueventFile);
                foreach (string line in lines)
                {
                    if (line.StartsWith("DRIVER=", StringComparison.OrdinalIgnoreCase))
                    {
                        string driver = line["DRIVER=".Length..].Trim();
                        if (!string.IsNullOrWhiteSpace(driver))
                        {
                            deviceName = driver;
                            return true;
                        }
                    }
                }
            }

            string? hwmonDir = GetHwmonDirectory(deviceDir);
            if (hwmonDir != null)
            {
                string nameFile = Path.Combine(hwmonDir, "name");
                if (File.Exists(nameFile))
                {
                    string name = File.ReadAllText(nameFile).Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        deviceName = name;
                        return true;
                    }
                }
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }
}
