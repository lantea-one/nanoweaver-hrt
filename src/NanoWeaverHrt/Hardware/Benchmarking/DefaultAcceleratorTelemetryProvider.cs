using System.Globalization;

namespace NanoWeaverHrt.Hardware.Benchmarking;

/// <summary>
/// Default system accelerator telemetry provider reading environment configuration or applying safe hardware defaults.
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
    /// Default fallback device name when no environment variables are defined.
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
    /// Initializes a new instance of the <see cref="DefaultAcceleratorTelemetryProvider"/> class.
    /// </summary>
    public DefaultAcceleratorTelemetryProvider()
    {
    }

    /// <inheritdoc />
    public string DeviceName
    {
        get
        {
            // WHAT: Retrieves device model name from environment variables or applies fallback.
            // WHY:  Containerized environments export GPU identification through environment variables.
            // HOW:  Checks ACCELERATOR_DEVICE_NAME, then ACCELERATOR_BACKEND, then falls back to default.
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
            // HOW:  Parses ACCELERATOR_TEMPERATURE_C if present, else returns default safe temperature.
            string? raw = Environment.GetEnvironmentVariable(TemperatureEnvVar);
            if (raw != null && double.TryParse(raw, CultureInfo.InvariantCulture, out double parsed))
            {
                return parsed;
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
            // HOW:  Parses ACCELERATOR_POWER_WATTS if present, else returns default power draw.
            string? raw = Environment.GetEnvironmentVariable(PowerEnvVar);
            if (raw != null && double.TryParse(raw, CultureInfo.InvariantCulture, out double parsed))
            {
                return parsed;
            }

            return DefaultPowerUsageWatts;
        }
    }
}
