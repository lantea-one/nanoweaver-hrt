namespace NanoWeaverHrt.Hardware.Benchmarking;

/// <summary>
/// Defines telemetry probe capabilities for physical accelerator hardware (temperature, power, and driver metadata).
/// </summary>
public interface IAcceleratorTelemetryProvider
{
    /// <summary>
    /// Gets the detected accelerator hardware device name.
    /// </summary>
    string DeviceName { get; }

    /// <summary>
    /// Gets the installed accelerator driver version string.
    /// </summary>
    string DriverVersion { get; }

    /// <summary>
    /// Gets the current operating temperature of the hardware accelerator in degrees Celsius.
    /// </summary>
    double TemperatureCelsius { get; }

    /// <summary>
    /// Gets the current power consumption of the hardware accelerator in watts.
    /// </summary>
    double PowerUsageWatts { get; }
}
