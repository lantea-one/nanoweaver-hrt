# Work Package P41: Wet-Lab Liquid Handler REST API & Script Export Endpoints

## 1. Context & Objective
- **Problem Statement**:
  Following the implementation of `IOpentronsProtocolGenerator` in P40, researchers and external laboratory workflows require REST endpoints to programmatically generate Opentrons Python protocol scripts, query deck hardware requirements, and download standalone `.py` script files with proper MIME types for execution on physical OT-2 or Flex robots.
- **Goal & Scope**:
  1. Add Minimal API endpoints in `src/NanoWeaverHrt/Program.cs`:
     - `POST /api/fabrication/opentrons/generate` -> Generates protocol result with Python code, hashes, and deck summary.
     - `POST /api/fabrication/opentrons/download` -> Direct file download of `nanoweaver_origami_opentrons.py` (`text/x-python; charset=utf-8`).
  2. Map RFC 7807 problem details for invalid parameters (e.g. unknown sample ID, invalid volumes, negative concentrations).
  3. Ensure role-gated access (`Researcher` or `Administrator`).
  4. Write comprehensive integration tests in `tests/NanoWeaverHrt.Tests/Fabrication/OpentronsEndpointTests.cs` using `WebApplicationFactory`.
  5. Preserve downward coverage ratchet by appending endpoints to the very bottom of `Program.Main`.
- **Target Role / Model**: `coder` (`flash`)
- **Transport Boundary**: HTTP REST API (`/api/fabrication/opentrons/*`)

---

## 2. Technical Contracts & Invariants

### Request DTO
```csharp
namespace NanoWeaverHrt.Fabrication.Opentrons;

/// <summary>
/// Request payload for generating an Opentrons liquid handler protocol script.
/// </summary>
public sealed record GenerateOpentronsScriptRequest
{
    /// <summary>Raw DNA sequence or built-in sample identifier.</summary>
    public required string SequenceOrSampleId { get; init; }

    /// <summary>Target endocrine regimen.</summary>
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>Nanocarrier geometric lattice (e.g., Nanotube, Cuboid, Cage).</summary>
    public string Geometry { get; init; } = "Nanotube";

    /// <summary>Target aptamer dissociation constant in nanomolar.</summary>
    public double TargetKd { get; init; } = 4.8;

    /// <summary>Opentrons protocol generation options.</summary>
    public OpentronsProtocolOptions Options { get; init; } = new();
}
```

### Invariants
1. **Append Order Invariant**: Minimal API endpoint registrations MUST be added to the bottom of `Program.Main` immediately before `app.Run();` to prevent Roslyn compiler lambda ordinal divergence.
2. **No Outer Variable Captures**: Endpoint lambdas must accept dependencies via typed DI parameters (`IOpentronsProtocolGenerator generator`, `IFabricationService fabService`, etc.), never capturing outer local variables.
3. **MIME Type & Headers**: Download endpoint must return `text/x-python; charset=utf-8` with `Content-Disposition: attachment; filename="nanoweaver_origami_opentrons.py"`.
4. **RFC 7807 Compliance**: Returns `ValidationProblem` with RFC 7807 problem details when `SequenceOrSampleId` is empty or invalid.

---

## 3. Step-by-Step Implementation Tasks
1. Define `GenerateOpentronsScriptRequest` in `src/NanoWeaverHrt/Fabrication/Opentrons/OpentronsModels.cs`.
2. Append endpoints to the end of `Program.cs`:
   - `POST /api/fabrication/opentrons/generate`
   - `POST /api/fabrication/opentrons/download`
3. Implement `tests/NanoWeaverHrt.Tests/Fabrication/OpentronsEndpointTests.cs`:
   - Valid generation request returns 200 OK with valid Python code and SHA-256.
   - Missing sequence returns 400 Bad Request with RFC 7807 problem details.
   - Download request returns `nanoweaver_origami_opentrons.py` with `Content-Type: text/x-python`.
   - Role gating tests asserting 401/403 for unauthenticated or unauthorized roles.
4. Verify tests and coverage with `dotnet test tests/NanoWeaverHrt.Tests`.

---

## 4. Verification Gate & Acceptance Criteria
- [ ] Builds with zero warnings (`/warnaserror`).
- [ ] 100.00% Line & Branch coverage on all new endpoints and models.
- [ ] Un-excluded coverage ratchet matches baseline or improves.
- [ ] 100% XML docblock coverage on all new types and members.
