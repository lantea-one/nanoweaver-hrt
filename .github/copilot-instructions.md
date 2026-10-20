# GitHub Copilot Instructions: HRT-Lab Project

## Context
HRT-Lab is a decentralized, open-source synthetic biology and DNA origami research laboratory platform.
The primary codebase includes a .NET 10 zero-allocation trajectory parsing microservice, an embedded web UI for cryptographic dataset transfer, and Docker infrastructure supporting Intel Arc A770, NVIDIA, AMD Radeon, and Intel iGPU hardware acceleration for LAMMPS and oxDNA.

## Mandatory Coding & Commenting Directives
1. **100% Docblock Mandate**:
   - Every class, struct, record, interface, method, property, field (including `private` and `internal` members) must have complete XML doc comments:
     `/// <summary>`, `/// <param>`, `/// <returns>`, `/// <remarks>`.
2. **What / Why / How Inline Triad**:
   - When suggesting method bodies, algorithms, or byte-slicing loops, include inline comments:
     `// WHAT: [action]`
     `// WHY: [biological or memory-efficiency rationale]`
     `// HOW: [mechanism used]`
3. **Zero Heap Allocations in Trajectory Hot Paths**:
   - Prefer `ReadOnlySpan<byte>`, `ReadOnlySequence<byte>`, `ArrayPool<T>.Shared`, and `Utf8Parser`.
   - Never suggest `string.Split()` or string concatenations when parsing XYZ or DCD simulation coordinates.
4. **Plain English Explanations**:
   - Demystify scientific concepts (DNA origami, coarse-graining, nucleotides, Angstroms, periodic boundary conditions) so anyone can read and modify the code.
