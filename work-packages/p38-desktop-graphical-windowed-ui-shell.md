# Work Package P38: Desktop Graphical Windowed UI Shell & Default Execution Mode

## 1. Context & Objective
- **Problem Statement**:
  Currently, `DesktopProgram.cs` treats `args.Length == 0` identically to `--help`, printing CLI usage and immediately terminating with exit code 0. When an operator or patient double-clicks `NanoWeaverHrt.exe` on Windows or launches the app from an application menu on Linux or macOS, the process exits without launching the desktop UI shell or tray monitor.
- **Goal & Scope**:
  1. Update `DesktopProgram.cs` to introduce a default interactive execution path when `args.Length == 0` (or when passed `--gui`), launching the sovereign desktop application host.
  2. Implement `IDesktopUiHost` and `DefaultDesktopUiHost` to orchestrate `MainShellViewModel`, `InstanceOnboardingViewModel`, and `DesktopTrayIconController`.
  3. Ensure 100.00% line and branch test coverage across `DesktopProgram` and `DesktopUiHost` in `tests/NanoWeaverHrt.Tests`, utilizing headless test doubles.
- **Target Role / Model**: `architect` / `coder` (Recommended model: `flash`)
- **Transport Boundary**: Client GUI Layer (`Web` / Desktop)

---

## 2. Technical Contracts & Invariants
- **Interfaces & Types**:
  ```csharp
  namespace NanoWeaverHrt.Desktop.Platform;

  /// <summary>
  /// Abstraction for hosting the desktop graphical user interface lifecycle.
  /// </summary>
  public interface IDesktopUiHost
  {
      /// <summary>
      /// Launches and runs the desktop UI window shell and system tray loop.
      /// </summary>
      /// <param name="ct">Cancellation token signalling application termination.</param>
      /// <returns>Process exit code: 0 on clean exit, non-zero on failure.</returns>
      Task<int> RunUiAsync(CancellationToken ct = default);
  }
  ```
- **Execution Invariant in `DesktopProgram.RunAsync`**:
  ```csharp
  if (args.Length == 0 || args[0] is "--gui")
  {
      return await RunGuiAsync(outWriter, errWriter, uiHost, mutexName, ct).ConfigureAwait(false);
  }

  if (args[0] is "--help" or "-h" or "help")
  {
      PrintUsage(outWriter);
      return 0;
  }
  ```
- **Ratchet Invariants**:
  - All new methods must be appended to the end of `DesktopProgram.cs`.
  - Every member (public, internal, private) must have full XML docblocks (`/// <summary>`, `<param>`, `<returns>`).
  - 100.00% Line and Branch code coverage in `tests/NanoWeaverHrt.Tests/Platform/DesktopPlatformTests.cs`.

---

## 3. Step-by-Step Implementation Tasks
1. **Task 1 (`IDesktopUiHost.cs` & `DefaultDesktopUiHost.cs`)**:
   - Create `src/NanoWeaverHrt.Desktop/Platform/IDesktopUiHost.cs`.
   - Create `src/NanoWeaverHrt.Desktop/Platform/DefaultDesktopUiHost.cs` implementing `IDesktopUiHost`.
   - Wire up `MainShellViewModel`, `InstanceOnboardingViewModel`, and `DesktopTrayIconController`.
2. **Task 2 (`DesktopProgram.cs` GUI Mode)**:
   - Update `DesktopProgram.RunAsync` to accept optional `IDesktopUiHost? uiHost = null`.
   - Add `RunGuiAsync` private helper enforcing `SingleInstanceMutexGuard` and delegating to `uiHost.RunUiAsync(ct)`.
3. **Task 3 (Unit Tests & Coverage)**:
   - Add comprehensive tests in `tests/NanoWeaverHrt.Tests/Platform/DesktopPlatformTests.cs` exercising all branches of `RunGuiAsync`, `DefaultDesktopUiHost`, primary instance vs instance collision, and cancellation tokens.
4. **Task 4 (Verification Gate)**:
   - Run `./scripts/verify-all.sh` to ensure 100.00% line & branch coverage and 0 docblock errors.

---

## 4. Verification Gate & Acceptance Criteria
- [ ] Launching `DesktopProgram.RunAsync(Array.Empty<string>())` invokes GUI shell mode.
- [ ] 100.00% Line & Branch coverage on all new classes.
- [ ] 0 XML docblock errors.
- [ ] Ratchet baseline preserved or improved.
