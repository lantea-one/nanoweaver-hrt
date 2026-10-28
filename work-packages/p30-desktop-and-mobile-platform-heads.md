# Work Package P30: Desktop & Mobile Platform Heads (`NanoWeaverHrt.Desktop` & `NanoWeaverHrt.Mobile`)

## 0. Fix your base commit FIRST (before reading anything else)
```bash
git checkout feature/gui-application
```

## 1. Context & Objective
- **Problem Statement**: We need standalone cross-platform Desktop (`Linux amd64/arm64`, `macOS`, `Windows`) and Mobile (`Android`, `iOS`, `iPadOS`) executables that host `NanoWeaverHrt.Gui.Core`, wire up OS-native background notifications and system tray integration, and support single-instance IPC activation and headless CLI health verification (`--verify-self`).
- **Goal & Scope**:
  - **Included**:
    - Create `src/NanoWeaverHrt.Desktop/NanoWeaverHrt.Desktop.csproj` (`AssemblyName: NanoWeaverHrt`) with cross-platform native notification & tray adapters (`LinuxFreeDesktopAdapter`, `MacOsNotificationAdapter`, `WindowsToastNotificationAdapter`), single-instance lock (`SingleInstanceMutexGuard`), and background daemon loop.
    - Create `src/NanoWeaverHrt.Mobile/NanoWeaverHrt.Mobile.csproj` (`AssemblyName: NanoWeaverHrtMobile`) with Android (`AndroidManifest.xml`), iOS (`Info.iOS.plist`), and iPadOS (`Info.iPadOS.plist`) bundle metadata and mobile background notification adapter (`MobileNotificationAdapter`).
  - **Excluded**: Distribution-specific package builders (`.deb`, `.rpm`, `.pkg.tar.zst`, `.dmg`, `.exe`, `.msix`, `.apk`, `.ipa`) which are covered in P32 and P33.
- **Target Role / Boundary**: Standalone Desktop & Mobile Native Executables.

## 2. Technical Contracts & Invariants

### 2.1 Project Files & Solution Integration
- `src/NanoWeaverHrt.Desktop/NanoWeaverHrt.Desktop.csproj`:
  - `<OutputType>Exe</OutputType>` (with `<BuiltInComInteropSupport>true</BuiltInComInteropSupport>` and `<AssemblyName>NanoWeaverHrt</AssemblyName>` so macOS/Windows/Linux bundles produce the `NanoWeaverHrt` binary).
  - `<TargetFramework>net10.0</TargetFramework>`
  - `<Nullable>enable</Nullable>`
  - `<GenerateDocumentationFile>true</GenerateDocumentationFile>`
  - `<InternalsVisibleTo Include="NanoWeaverHrt.Tests" />`
- `src/NanoWeaverHrt.Mobile/NanoWeaverHrt.Mobile.csproj`:
  - `<TargetFramework>net10.0</TargetFramework>` (builds portable managed core + platform bundle manifests so Linux CI on `archlinux-multi-platform` and `ubuntu-latest` can compile, test, and package Android/iOS/iPadOS artifacts deterministically without proprietary Xcode hardware).
  - `<Nullable>enable</Nullable>`
  - `<GenerateDocumentationFile>true</GenerateDocumentationFile>`
  - `<InternalsVisibleTo Include="NanoWeaverHrt.Tests" />`
- Add both projects to `NanoWeaverHrt.slnx` and reference both from `tests/NanoWeaverHrt.Tests/NanoWeaverHrt.Tests.csproj`.

### 2.2 Platform Adapters (`namespace NanoWeaverHrt.Desktop.Platform` & `NanoWeaverHrt.Mobile.Platform`)

```csharp
namespace NanoWeaverHrt.Desktop.Platform;

/// <summary>
/// Abstracts OS command execution for dispatching native desktop notifications (notify-send / osascript / PowerShell Toast) so adapters are 100% testable without spawning real OS processes in unit tests.
/// </summary>
public interface IPlatformProcessRunner
{
    /// <summary>
    /// Executes an OS command with an argument list and returns true if exit code is 0.
    /// </summary>
    /// <param name="executable">Executable binary name or path.</param>
    /// <param name="arguments">Sanitized argument list (never interpolated into a shell string).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the process exited with status 0; otherwise false.</returns>
    ValueTask<bool> TryRunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken ct = default);
}

/// <summary>
/// Cross-platform desktop notification sink supporting Linux (notify-send / FreeDesktop), macOS (osascript display notification), and Windows (Toast notification).
/// </summary>
public sealed class DesktopNativeNotificationSink : INativeNotificationSink
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopNativeNotificationSink"/> class.
    /// </summary>
    /// <param name="processRunner">Process execution abstraction.</param>
    /// <param name="osPlatformOverride">Optional OS label ("linux", "macos", "windows") for deterministic cross-OS testing.</param>
    public DesktopNativeNotificationSink(IPlatformProcessRunner processRunner, string? osPlatformOverride = null);

    /// <inheritdoc/>
    public ValueTask<bool> SendNotificationAsync(InstanceAlertEvent alert, CancellationToken ct = default);
}
```

### 2.3 Security Invariant: Zero Shell Interpolation in Notifications
- When dispatching notifications containing remote instance names or alert messages, `DesktopNativeNotificationSink` MUST pass arguments as an explicit `IReadOnlyList<string>` (`ProcessStartInfo.ArgumentList`) and sanitize control characters—never concatenating untrusted text into `sh -c` or `cmd /c`.

## 3. Step-by-Step Implementation Tasks
1. **Task 1**: Create `src/NanoWeaverHrt.Desktop/NanoWeaverHrt.Desktop.csproj` and implement `IPlatformProcessRunner`, `DesktopNativeNotificationSink`, `DesktopTrayIconController`, and `DesktopProgram` (supporting `--verify-self` and `--headless-daemon` CLI flags).
2. **Task 2**: Create `src/NanoWeaverHrt.Mobile/NanoWeaverHrt.Mobile.csproj`, platform manifests (`Platforms/Android/AndroidManifest.xml`, `Platforms/iOS/Info.plist`, `Platforms/iPadOS/Info.plist`), `MobileNotificationSink`, and `MobileEntryPoint`.
3. **Task 3**: Add both projects to `NanoWeaverHrt.slnx` and `tests/NanoWeaverHrt.Tests/NanoWeaverHrt.Tests.csproj`.
4. **Task 4**: Write 100% line and branch unit tests in `tests/NanoWeaverHrt.Tests/Gui/DesktopAndMobilePlatformTests.cs` testing Linux, macOS, Windows, Android, iOS, and iPadOS code paths.
5. **Task 5**: Run `XDG_RUNTIME_DIR=/tmp dotnet format NanoWeaverHrt.slnx --no-restore && ./scripts/verify-all.sh`.

## 4. Verification Gate & Acceptance Criteria
- [ ] Zero build warnings (`/warnaserror`).
- [ ] 100.00% line & branch coverage on developer-authored code (Step 4).
- [ ] 100% downward ratchet compliance (Step 7).
- [ ] 100% XML docblock coverage on all members (Step 6).
- [ ] Zero unescaped single-dollar math tokens in documentation (Step 5).
