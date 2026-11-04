using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NanoWeaverHrt.Gui.Core.ViewModels;

/// <summary>
/// ViewModel providing system diagnostics, application metadata, cryptographic capability manifests, and licensing info.
/// </summary>
/// <remarks>
/// // WHAT: Supplies immutable and environment-derived metadata for the sovereign About dialog.
/// // WHY:  Issue #70 (P67): Provides operator visibility into cluster runtime parameters and supported algorithms.
/// // HOW:  Inspects System.Runtime.InteropServices and exports structured strings and capability arrays.
/// </remarks>
public sealed class AboutViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// Occurs when a property value changes (About metadata is immutable).
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Gets the application display title.
    /// </summary>
    public string AppName => "NanoWeaver HRT";

    /// <summary>
    /// Gets the application version tag.
    /// </summary>
    public string Version => "2026.11.04.4-sovereign";

    /// <summary>
    /// Gets the project description string.
    /// </summary>
    public string Description => "Decentralized Synthetic Biology, DNA Origami & Hormone Replacement Therapy Research Platform";

    /// <summary>
    /// Gets the operating system description string.
    /// </summary>
    public string OSDescription => RuntimeInformation.OSDescription;

    /// <summary>
    /// Gets the target CPU process architecture string.
    /// </summary>
    public string Architecture => RuntimeInformation.ProcessArchitecture.ToString();

    /// <summary>
    /// Gets the .NET framework description.
    /// </summary>
    public string FrameworkDescription => RuntimeInformation.FrameworkDescription;

    /// <summary>
    /// Gets the software distribution license.
    /// </summary>
    public string License => "MIT";

    /// <summary>
    /// Gets the project author attribution.
    /// </summary>
    public string Author => "NanoWeaverHrt Contributors";

    /// <summary>
    /// Gets the list of supported asymmetric authentication algorithms.
    /// </summary>
    public IReadOnlyList<string> SupportedAlgorithms { get; } =
    [
        "Ed25519 (RFC 8032)",
        "ML-DSA-65 (NIST FIPS 204 Post-Quantum)"
    ];

    /// <summary>
    /// Gets the list of supported computational nanotechnology simulation engines.
    /// </summary>
    public IReadOnlyList<string> SupportedSimulations { get; } =
    [
        "serum-stability (oxDNA)",
        "aptamer-gating (LAMMPS)",
        "payload-leakage (LAMMPS)",
        "pbpk-endocrine (Closed-Loop Differential Solver)"
    ];

    /// <summary>
    /// Gets the authoritative sovereign source repository URI.
    /// </summary>
    public string RepositoryUri => "https://git.lantea.one/lantea-one/nanoweaver-hrt";
}
