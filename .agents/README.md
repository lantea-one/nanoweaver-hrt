# NanoWeaverHrt Custom AI Agent Profiles

This directory contains tailored AI agent profiles configured specifically for **NanoWeaverHrt** (`NanoWeaverHrt` / `nanoweaver-hrt`), an open-source decentralized synthetic biology and DNA origami research platform for hormone replacement therapy delivery.

## Agent Directory & Specializations

Agent Name | Model | Target Scope | Responsibilities & Invariants
:--- | :--- | :--- | :---
[`architect`](agents/architect.md) / [`nanoweaver-hrt-architect`](agents/nanoweaver-hrt-architect.md) | `pro` | Architecture & Planning | Designs solutions and writes atomic 5-layer work packages in `work-packages/`. Enforces zero-allocation invariants, database migration contracts, PKI boundaries, and coverage ratchets. Never writes production code.
[`coder`](agents/coder.md) / [`nanoweaver-hrt-coder`](agents/nanoweaver-hrt-coder.md) | `flash` | Full Repository | Generalist senior software engineer implementing assigned work packages across the codebase. Enforces 100% docblocks, zero-allocation buffers, What/Why/How comments, and 100.00% line & branch coverage threshold.
[`nanoweaver-hrt-coder-web-service`](agents/nanoweaver-hrt-coder-web-service.md) | `flash` | `src/NanoWeaverHrt` | Specializes in ASP.NET Core minimal APIs, `System.IO.Pipelines` SIMD trajectory parsers, RFC 7807 problem details, multi-engine persistence (SQLite, PostgreSQL, DuckDB, MySQL), and bUnit Razor test suites.
[`nanoweaver-hrt-coder-desktop`](agents/nanoweaver-hrt-coder-desktop.md) | `flash` | `src/NanoWeaverHrt.Desktop`, `src/NanoWeaverHrt.Gui.Core` | Specializes in Avalonia UI 11 / .NET 10 desktop application, MVVM ViewModels, system tray integration, desktop localization (18 languages + RTL), and cross-platform native packaging (DEB, RPM, AppImage, DMG, MSIX).
[`nanoweaver-hrt-coder-android`](agents/nanoweaver-hrt-coder-android.md) | `flash` | `src/NanoWeaverHrt.Mobile` (Android) | Specializes in Android Avalonia lifecycle, `MainActivity`, hardware serial/BLE permissions, Android Keystore PKI keys, and APK/AAB packaging.
[`nanoweaver-hrt-coder-ios`](agents/nanoweaver-hrt-coder-ios.md) | `flash` | `src/NanoWeaverHrt.Mobile` (iOS) | Specializes in iOS/iPadOS Avalonia lifecycle, `AppDelegate`, Apple Silicon acceleration, Keychain/Secure Enclave PKI keys, and iOS packaging.
[`mechanic`](agents/mechanic.md) / [`nanoweaver-hrt-mechanic`](agents/nanoweaver-hrt-mechanic.md) | `flash_lite` | Full Repository | Executes fast, atomic mechanical updates: renames, formatting (`dotnet format`), XML docblock maintenance (`scripts/check-docblocks.py`), and compiler warnings.

## Configuration & Discovery

Custom agent definitions are declared in `.agents/agents.json`:
```json
{
  "entries": [
    {
      "path": ".agents/agents"
    }
  ]
}
```

## How to Invoke in Antigravity

When dispatching subtasks, AI assistants should invoke the specialized agent corresponding to the architectural layer or component:

```json
// Example: Invoking the Web Service Coder for an API endpoint
invoke_subagent({
  "TypeName": "nanoweaver-hrt-coder-web-service",
  "Role": "Trajectory Analytics REST Coder",
  "Prompt": "Implement POST /api/trajectories/analytics/query adhering to work-packages/p52-analytics-rest-and-ui.md."
})

// Example: Invoking the Desktop Coder for an Avalonia ViewModel
invoke_subagent({
  "TypeName": "nanoweaver-hrt-coder-desktop",
  "Role": "Tray Icon Notification Coder",
  "Prompt": "Implement desktop notification dispatch in DesktopTrayIconController.cs."
})
```
