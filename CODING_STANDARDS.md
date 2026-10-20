# CODING & DOCUMENTATION STANDARDS: THE OPEN-BIO PROTOCOL

## 1. Philosophical Mandate: "Zero Ignorance, Sovereign Understanding"

This project powers a decentralized, sovereign laboratory for synthetic biology and DNA nanotechnology. Our code and data pipelines will be read, audited, modified, and extended by a diverse community:
- Molecular biologists with minimal systems programming experience.
- Software engineers with zero biochemistry or physics background.
- Autonomous AI coding agents operating across multiple platforms (Claude, Cursor, Copilot, Gemini, ChatGPT).

**Core Rule:** *Anyone must be able to read this codebase and confidently make changes without prior biomedical or high-performance systems engineering expertise.*

---

## 2. Universal Documentation Rules

### A. 100% Docblock Coverage (All Access Modifiers)
Every class, struct, record, interface, enum, property, field, method, and delegate—**regardless of whether it is `public`, `internal`, `protected`, or `private`**—must have complete XML documentation comments (`///`).

#### Required Tags:
- `<summary>`: What this entity is and what problem it solves in plain English. Avoid unexplained acronyms.
- `<param name="...">`: Purpose of every parameter, its units (e.g., Angstroms, picoseconds), and acceptable ranges.
- `<returns>`: Exactly what is returned under success and failure states.
- `<remarks>`: Domain context (the biological or algorithmic purpose) and performance/memory constraints (e.g., zero-allocation warnings).
- `<exception cref="...">`: Any exception that could be thrown and under what conditions.

#### Example:
```csharp
/// <summary>
/// Represents a single atom or DNA nucleotide bead in 3D computational space.
/// </summary>
/// <remarks>
/// DOMAIN CONTEXT:
/// In molecular dynamics, molecular structures (like folded DNA origami) are modeled 
/// as thousands or millions of points in space. 
///
/// PERFORMANCE CONSTRAINT:
/// This struct is exactly 12 bytes (three 32-bit floating-point numbers). It is 
/// sequential and blittable, meaning it can be copied directly from network sockets or 
/// disk buffers without triggering heap allocations or Garbage Collector overhead.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct AtomCoordinate
{
    /// <summary>
    /// The spatial X-axis coordinate in Angstroms (1 Angstrom = 0.1 nanometers).
    /// </summary>
    public readonly float X;

    /// <summary>
    /// The spatial Y-axis coordinate in Angstroms.
    /// </summary>
    public readonly float Y;

    /// <summary>
    /// The spatial Z-axis coordinate in Angstroms.
    /// </summary>
    public readonly float Z;

    /// <summary>
    /// Initializes a new 3D spatial coordinate.
    /// </summary>
    /// <param name="x">Cartesian X position.</param>
    /// <param name="y">Cartesian Y position.</param>
    /// <param name="z">Cartesian Z position.</param>
    public AtomCoordinate(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}
```

---

### B. The "What / Why / How" Commenting Triad
Inside method bodies, nontrivial logic, algorithmic loops, and low-level memory interactions must be documented with the **What / Why / How** pattern:

```csharp
// WHAT: Reads float coordinates directly from the UTF-8 byte span.
// WHY:  Molecular trajectories reach tens of gigabytes. Converting bytes to C# strings 
//       causes massive GC pressure and kills throughput.
// HOW:  Uses Utf8Parser.TryParse to convert ASCII/UTF-8 digits to System.Single 
//       in-place without string allocations.
if (!Utf8Parser.TryParse(remainingSpan, out float xCoord, out int bytesConsumed))
{
    return false;
}
```

---

## 3. The Synthetic Biology & Nanotechnology Rosetta Stone

Whenever writing code or comments involving domain concepts, use these analogies to keep the code accessible:

| Domain Term | Plain-English Definition | Analogy / Intuition |
| :--- | :--- | :--- |
| **DNA Origami** | Nanoscale structures formed by folding a long single strand of viral DNA ("scaffold") with hundreds of short synthetic DNA strands ("staples"). | Molecular pegboard and rubber bands: staples pin specific parts of the long thread together to build 3D nanoscale shapes. |
| **Coarse-Graining** | Simplifying a simulation by grouping clusters of subatomic particles into single pseudo-atoms or "beads". | Lowering the polygon count in a 3D video game to make it run faster while preserving physical realism. |
| **oxDNA** | A simulation engine specialized for DNA nanotechnology that treats each nucleotide as a rigid bead with orientation vectors. | Simulating building blocks with magnets rather than calculating individual electrons. |
| **LAMMPS** | Large-scale Atomic/Molecular Massively Parallel Simulator; a general-purpose molecular dynamics engine. | A physics game engine optimized for billions of interacting particles. |
| **Trajectory File (`.xyz`, `.dcd`)** | A file format containing snapshots of atomic 3D coordinates over sequential time-steps. | A molecular movie file: each frame shows where every atom moved in the last femtosecond. |
| **Periodic Boundary Box (PBC)** | An artificial cubic or triclinic box where particles exiting the right side wrap around to re-enter the left side. | The screen wrap mechanic in classic arcade games like Asteroids or Pac-Man. |
| **Zero-Allocation Pipeline** | Designing algorithms to reuse pre-allocated memory pools (`ArrayPool`, `MemoryPool`) rather than allocating new objects. | Reusing washable plates instead of throwing away paper plates with every bite of food. |

---

## 4. Architectural Rules for High Performance

1. **No `string.Split()` or Regex in Hot Paths**:
   Never use string manipulation on trajectory streams. Use `ReadOnlySpan<byte>`, `SequenceReader<byte>`, and `Utf8Parser`.
2. **Buffer Management via `ArrayPool<T>` and `NativeMemory`**:
   Renting and returning coordinate buffers is mandatory. Always ensure buffers are returned in `finally` blocks.
3. **Immutability & Blittable Structs**:
   Data structures passed across threads or to GPU pipelines must be `readonly struct` with explicit `[StructLayout(LayoutKind.Sequential)]`.
4. **Independent Cryptographic Provenance**:
   Every exported simulation dataset must have an accompanying `manifest.json` containing an immutable SHA-256 checksum and ISO 8601 UTC timestamp.

---

## 5. Quality, Coverage & Accuracy Invariants

1. **100.00% Line & Branch Coverage Threshold**:
   - Zero untested code. Every class, method, branch (`if`/`else`, `switch`, ternary), and exception handler must have tests in `tests/WeaverLammpsHrt.TrajectoryParser.Tests`.
   - Never use `[ExcludeFromCodeCoverage]` to bypass writing tests.
   - Client-side JavaScript (`weaver-visualizer.js`) must achieve 100% line, branch, and function coverage via `npm test`.

2. **100% Deterministic Accuracy & Zero Warnings**:
   - Mathematical, biophysical, and pharmacokinetic models must be exact.
   - Build with zero warnings (`/warnaserror`).
   - Zero test flakiness or arbitrary thread sleeping.

3. **Markdown Documentation Math Invariants**:
   - Never wrap physical quantities, units (`50 µL`, `100 µM`), chemical formulas (`17β-estradiol`, `Mg²⁺`), or temperatures in raw single-dollar LaTeX delimiters (`$ ... $`).
   - Use clean Unicode symbols to guarantee clean rendering on GitHub.com and GitHub Pages without breaking markdown tables. Display equations must use fenced `$$ ... $$` blocks.

4. **Universal Pre-Flight Verification Gate**:
   - Before committing or concluding any pull request, contributors and AI agents must run:
     ```bash
     ./scripts/verify-all.sh
     ```
   - All 5 steps must pass cleanly.
