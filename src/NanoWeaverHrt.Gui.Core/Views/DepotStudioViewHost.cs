using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using NanoWeaverHrt.Gui.Core.Theme;
using NanoWeaverHrt.Gui.Core.ViewModels;
using NanoWeaverHrt.Localization;

namespace NanoWeaverHrt.Gui.Core.Views;

/// <summary>
/// Responsive screen layout modes shared across Linux/macOS/Windows desktop, iPadOS split-view tablets,
/// and Android/iOS compact mobile heads.
/// </summary>
public enum WorkspaceLayoutMode
{
    /// <summary>
    /// Four-column desktop widescreen layout (&gt;= 1200 px).
    /// </summary>
    DesktopWide = 0,

    /// <summary>
    /// Two-column split layout for iPadOS tablets and narrow desktop windows.
    /// </summary>
    TabletSplit = 1,

    /// <summary>
    /// Single-column compact vertical card layout for Android and iOS handheld displays.
    /// </summary>
    MobileCompact = 2
}

/// <summary>
/// Primary interactive sections inside the native Depot &amp; Fabrication Lot Studio workspace.
/// </summary>
public enum DepotStudioSection
{
    /// <summary>
    /// Semi-annual 180-day depot formulation design and trajectory preview (Milestone 33).
    /// </summary>
    Formulations = 0,

    /// <summary>
    /// Longitudinal serum hormone assay history, Bayesian calibration, and clinical safety alerts (Milestone 34).
    /// </summary>
    LongitudinalCalibration = 1,

    /// <summary>
    /// Wet-lab batch fabrication, 5-gate pharmaceutical QC checks, and ML-DSA-65 lot release (Milestone 35).
    /// </summary>
    WetLabLots = 2,

    /// <summary>
    /// Portable Certificate of Analysis download, cross-node ML-DSA-65 trust verification, and lot recall (Milestone 36).
    /// </summary>
    CertificateVerification = 3
}

/// <summary>
/// Immutable presentation descriptor for a single high-contrast sovereign dark-mode KPI card
/// inside the native Depot &amp; Fabrication Lot Studio.
/// </summary>
/// <param name="CardId">Stable card identifier for deterministic automation and binding.</param>
/// <param name="Label">Localized human-readable KPI label.</param>
/// <param name="Value">Formatted primary KPI value.</param>
/// <param name="AccentHex">Sovereign palette hex accent color for the card border or badge.</param>
/// <param name="SurfaceHex">Sovereign dark-mode card background hex color.</param>
public sealed record DepotStudioCardDescriptor(
    string CardId,
    string Label,
    string Value,
    string AccentHex,
    string SurfaceHex);

/// <summary>
/// Immutable rendered screen state snapshot emitted by <see cref="DepotStudioViewHost"/> for native
/// desktop and mobile presentation layers.
/// </summary>
/// <param name="ScreenTitle">Localized screen title.</param>
/// <param name="StatusBanner">Localized single-line clinical status banner.</param>
/// <param name="ActiveSection">Currently active workspace section tab.</param>
/// <param name="ActiveSectionTitle">Localized heading for <paramref name="ActiveSection"/>.</param>
/// <param name="LayoutMode">Active responsive breakpoint mode.</param>
/// <param name="ColumnCount">Resolved responsive card grid column count (4, 2, or 1).</param>
/// <param name="IsRightToLeft">True when the active display language uses right-to-left layout (e.g. Arabic).</param>
/// <param name="BackgroundHex">Root canvas dark-mode background hex color.</param>
/// <param name="Cards">Ordered list of sovereign KPI cards to render.</param>
/// <param name="Summary">Underlying zero-LINQ <see cref="DepotWorkspaceSummary"/> metrics.</param>
public sealed record DepotStudioScreenSnapshot(
    string ScreenTitle,
    string StatusBanner,
    DepotStudioSection ActiveSection,
    string ActiveSectionTitle,
    WorkspaceLayoutMode LayoutMode,
    int ColumnCount,
    bool IsRightToLeft,
    string BackgroundHex,
    IReadOnlyList<DepotStudioCardDescriptor> Cards,
    DepotWorkspaceSummary Summary);

/// <summary>
/// Pure C# headless view host coordinating native Depot &amp; Fabrication Lot Studio section navigation,
/// responsive column layout, RTL directionality, and sovereign dark-mode KPI card generation.
/// </summary>
/// <remarks>
/// // WHAT: Cross-platform native view host wrapping <see cref="DepotAndLotWorkspaceViewModel"/> without XAML codegen overhead.
/// // WHY:  Milestone 37 (P196): Mirrors MainShellWindowHost's pure C# architecture for 100% deterministic headless testing
/// //       and zero Roslyn/Avalonia XAML state machine coverage regressions across desktop and mobile heads.
/// // HOW:  Tracks ActiveSection and LayoutMode, queries IWeaverLocalizer for all 18 community languages, and builds immutable screen snapshots.
/// </remarks>
public sealed class DepotStudioViewHost : INotifyPropertyChanged
{
    /// <summary>
    /// Root shell ViewModel providing active language, RTL flag, and localization catalog.
    /// </summary>
    private readonly MainShellViewModel _shellViewModel;

    /// <summary>
    /// Backing field for <see cref="ActiveSection"/>.
    /// </summary>
    private DepotStudioSection _activeSection = DepotStudioSection.Formulations;

    /// <summary>
    /// Backing field for <see cref="LayoutMode"/>.
    /// </summary>
    private WorkspaceLayoutMode _layoutMode = WorkspaceLayoutMode.DesktopWide;

    /// <summary>
    /// Occurs when an observable host property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="DepotStudioViewHost"/> class.
    /// </summary>
    /// <param name="shellViewModel">Parent shell ViewModel hosting the depot workspace and localization state.</param>
    public DepotStudioViewHost(MainShellViewModel shellViewModel)
    {
        // WHAT: Stores parent shell reference to coordinate ViewModel state and multilingual rendering.
        // WHY:  Ensures section titles, RTL layout, and clinical cards react automatically to language changes.
        // HOW:  Validates non-null shellViewModel and assigns readonly field.
        _shellViewModel = shellViewModel ?? throw new ArgumentNullException(nameof(shellViewModel));
    }

    /// <summary>
    /// Gets the underlying <see cref="DepotAndLotWorkspaceViewModel"/> bound to this view host.
    /// </summary>
    public DepotAndLotWorkspaceViewModel Workspace => _shellViewModel.DepotAndLotWorkspace;

    /// <summary>
    /// Gets or sets the currently active section inside the native Depot &amp; Lot Studio.
    /// </summary>
    public DepotStudioSection ActiveSection
    {
        get => _activeSection;
        set
        {
            if (_activeSection != value)
            {
                _activeSection = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the active responsive layout mode for desktop, tablet, or mobile rendering.
    /// </summary>
    public WorkspaceLayoutMode LayoutMode
    {
        get => _layoutMode;
        set
        {
            if (_layoutMode != value)
            {
                _layoutMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColumnCount));
            }
        }
    }

    /// <summary>
    /// Gets the resolved responsive card grid column count for the active <see cref="LayoutMode"/>.
    /// </summary>
    public int ColumnCount => _layoutMode switch
    {
        WorkspaceLayoutMode.DesktopWide => 4,
        WorkspaceLayoutMode.TabletSplit => 2,
        _ => 1
    };

    /// <summary>
    /// Switches the active section tab inside the native Depot &amp; Lot Studio.
    /// </summary>
    /// <param name="section">Target section tab.</param>
    public void SelectSection(DepotStudioSection section)
    {
        // WHAT: Activates the requested workspace section tab.
        // WHY:  Provides an explicit command entry point for desktop sidebar and mobile bottom-bar buttons.
        // HOW:  Assigns ActiveSection property triggering PropertyChanged when modified.
        ActiveSection = section;
    }

    /// <summary>
    /// Selects the responsive layout mode automatically from a viewport width in device-independent pixels.
    /// </summary>
    /// <param name="viewportWidthPx">Current window or screen width in device-independent pixels.</param>
    public void ApplyViewportWidth(double viewportWidthPx)
    {
        // WHAT: Maps viewport width to DesktopWide (>= 1200), TabletSplit (>= 768), or MobileCompact (< 768).
        // WHY:  Keeps card grids readable without text overflow across Linux/macOS/Windows, iPadOS, and phones.
        // HOW:  Evaluates deterministic width thresholds and updates LayoutMode.
        if (viewportWidthPx >= 1200.0)
        {
            LayoutMode = WorkspaceLayoutMode.DesktopWide;
        }
        else if (viewportWidthPx >= 768.0)
        {
            LayoutMode = WorkspaceLayoutMode.TabletSplit;
        }
        else
        {
            LayoutMode = WorkspaceLayoutMode.MobileCompact;
        }
    }

    /// <summary>
    /// Builds an immutable <see cref="DepotStudioScreenSnapshot"/> reflecting the current clinical workspace state,
    /// localized headings, sovereign dark-mode cards, and RTL layout directionality.
    /// </summary>
    /// <returns>Complete <see cref="DepotStudioScreenSnapshot"/> ready for native rendering.</returns>
    public DepotStudioScreenSnapshot BuildScreenSnapshot()
    {
        // WHAT: Projects current workspace ViewModel data into localized sovereign dark-mode cards and layout metadata.
        // WHY:  Decouples presentation verification from OS windowing APIs while guaranteeing consistent desktop/mobile UI state.
        // HOW:  Computes DepotWorkspaceSummary, resolves localized titles/labels via MainShellViewModel, and assembles 4 KPI cards.
        DepotWorkspaceSummary summary = Workspace.BuildSummary();
        IWeaverLocalizer localizer = _shellViewModel.Localizer;
        WeaverLanguage lang = _shellViewModel.SelectedLanguage;

        string screenTitle = _shellViewModel.GetLocalizedText("Gui.DepotWorkspace.Title", "180-Day Depot & Fabrication Lot Studio");
        string statusBanner = Workspace.BuildLocalizedStatusSummary(localizer, lang);
        string activeSectionTitle = ResolveSectionTitle(_activeSection);

        string redoseText = summary.AdjustedRedoseDay.HasValue
            ? string.Format(CultureInfo.InvariantCulture, "Day {0}", summary.AdjustedRedoseDay.Value)
            : _shellViewModel.GetLocalizedText("Gui.DepotWorkspace.Uncalibrated", "Uncalibrated");

        string trustValue = ResolveTrustDisplayText();
        string trustAccent = DepotAndLotWorkspaceViewModel.ResolveVerificationAccentHex(Workspace.LastCertificateVerification);

        DepotStudioCardDescriptor[] cards =
        [
            new DepotStudioCardDescriptor(
                CardId: "depot-formulations-card",
                Label: _shellViewModel.GetLocalizedText("Gui.DepotWorkspace.CardFormulations", "Active Depots"),
                Value: summary.FormulationCount.ToString(CultureInfo.InvariantCulture),
                AccentHex: SovereignPalette.AccentCyanHex,
                SurfaceHex: SovereignPalette.SurfaceHex),
            new DepotStudioCardDescriptor(
                CardId: "depot-calibration-card",
                Label: _shellViewModel.GetLocalizedText("Gui.DepotWorkspace.CardRedoseDay", "Adjusted Re-Dose"),
                Value: redoseText,
                AccentHex: summary.SeverityAccentHex,
                SurfaceHex: SovereignPalette.SurfaceHex),
            new DepotStudioCardDescriptor(
                CardId: "fabrication-lots-card",
                Label: _shellViewModel.GetLocalizedText("Gui.DepotWorkspace.CardReleasedLots", "Released Lots"),
                Value: string.Format(CultureInfo.InvariantCulture, "{0} / {1}", summary.ReleasedLotCount, summary.TotalLotCount),
                AccentHex: summary.SeverityAccentHex,
                SurfaceHex: SovereignPalette.SurfaceHex),
            new DepotStudioCardDescriptor(
                CardId: "certificate-trust-card",
                Label: _shellViewModel.GetLocalizedText("Gui.DepotWorkspace.CardTrustStatus", "ML-DSA-65 Trust"),
                Value: trustValue,
                AccentHex: trustAccent,
                SurfaceHex: SovereignPalette.SurfaceHex)
        ];

        return new DepotStudioScreenSnapshot(
            ScreenTitle: screenTitle,
            StatusBanner: statusBanner,
            ActiveSection: _activeSection,
            ActiveSectionTitle: activeSectionTitle,
            LayoutMode: _layoutMode,
            ColumnCount: ColumnCount,
            IsRightToLeft: _shellViewModel.IsRightToLeft,
            BackgroundHex: SovereignPalette.BackgroundHex,
            Cards: cards,
            Summary: summary);
    }

    /// <summary>
    /// Resolves the localized heading for the specified <see cref="DepotStudioSection"/>.
    /// </summary>
    /// <param name="section">Section enum value.</param>
    /// <returns>Localized section heading.</returns>
    private string ResolveSectionTitle(DepotStudioSection section)
    {
        // WHAT: Translates the active section tab enum into a localized human-readable title.
        // WHY:  Displays consistent multilingual section headings across desktop and mobile navigation bars.
        // HOW:  Maps each DepotStudioSection to its corresponding localization resource key with English fallback.
        return section switch
        {
            DepotStudioSection.LongitudinalCalibration => _shellViewModel.GetLocalizedText("Gui.DepotWorkspace.SectionCalibration", "Longitudinal Calibration"),
            DepotStudioSection.WetLabLots => _shellViewModel.GetLocalizedText("Gui.DepotWorkspace.SectionLots", "Wet-Lab Fabrication Lots"),
            DepotStudioSection.CertificateVerification => _shellViewModel.GetLocalizedText("Gui.DepotWorkspace.SectionVerification", "Cross-Node CoA Verifier"),
            _ => _shellViewModel.GetLocalizedText("Gui.DepotWorkspace.SectionFormulations", "180-Day Formulations")
        };
    }

    /// <summary>
    /// Resolves the concise localized trust display label for the latest verified Certificate of Analysis.
    /// </summary>
    /// <returns>Localized trust verdict string.</returns>
    private string ResolveTrustDisplayText()
    {
        // WHAT: Formats the latest ML-DSA-65 verification report into a short localized KPI badge value.
        // WHY:  Lets clinicians see at a glance whether an imported batch certificate is trusted, revoked, or pending.
        // HOW:  Inspects LastCertificateVerification for null, IsRevoked, or IsTrusted and resolves localized labels.
        var report = Workspace.LastCertificateVerification;
        if (report == null)
        {
            return _shellViewModel.GetLocalizedText("Gui.DepotWorkspace.TrustPending", "Not Verified");
        }

        if (report.IsRevoked)
        {
            return _shellViewModel.GetLocalizedText("Gui.DepotWorkspace.TrustRevoked", "REVOKED");
        }

        return report.IsTrusted
            ? _shellViewModel.GetLocalizedText("Gui.DepotWorkspace.TrustVerified", "TRUSTED")
            : _shellViewModel.GetLocalizedText("Gui.DepotWorkspace.TrustUntrusted", "UNTRUSTED");
    }

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event for the specified property.
    /// </summary>
    /// <param name="propertyName">Name of the changed property.</param>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        // WHAT: Notifies data-bound views of section or layout changes.
        // WHY:  Fulfills INotifyPropertyChanged contract for reactive Avalonia bindings.
        // HOW:  Invokes PropertyChanged with PropertyChangedEventArgs.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
