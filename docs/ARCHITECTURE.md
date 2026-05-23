# Dsam Architecture

Dsam is split into three projects so the UI can stay responsive while the analysis code remains testable.

- `Dsam.App`: WinForms shell, DockPanelSuite workspace, and view orchestration.
- `Dsam.Core`: PE loading, memory-mapped binary reads, Iced decoding, xref extraction, and patch planning.
- `Dsam.Data`: SQLite-backed analysis database that acts as the IDB equivalent.

## Docked UI

`MainForm` owns the process-level workspace and the dock layout. It creates one `DockPanel` with `VS2015DarkTheme`, then docks:

- `DisassemblyDockContent` as the central document.
- `HexViewDockContent` at the bottom.
- `RegistersDockContent` on the right.
- `SectionsDockContent` on the left.
- `XrefsDockContent` under registers.

The views are deliberately thin. They accept already-shaped model data and raise selection events. `MainForm` reacts to those events by updating correlated panes, such as showing the selected instruction bytes in the hex view and its outgoing xrefs in the xref pane.

For very large listings, evolve `DisassemblyDockContent` toward `DataGridView.VirtualMode`: keep decoded windows in an address-indexed cache and request more rows from `IDisassemblyService` as the user scrolls.

## Binary Access

`MemoryMappedBinaryImage` wraps `MemoryMappedFile` and exposes small reads by file offset. The rest of the system never loads the entire executable into RAM. `PortableExecutableLoader` reads the PE headers, records image base, entry point, bitness, sections, and creates a `BinaryAddressMap` for RVA/VA-to-file-offset translation.

`MemoryMappedCodeReader` adapts the memory-mapped file to Iced's `CodeReader` using a 64 KB sliding window. This keeps decode hot paths sequential and avoids allocating a large byte array per section.

## Disassembly

`IcedDisassemblyService` accepts a `DisassemblyRequest` containing the binary, section, start address, bitness, and decode limits. It creates an Iced `Decoder`, formats each instruction with `NasmFormatter`, captures original bytes, and runs `XrefExtractor`.

`XrefExtractor` records:

- direct branch/call targets as code xrefs;
- RIP-relative memory operands as data xrefs.

Future analysis passes should build on this by adding recursive function discovery, CFG recovery, import/export naming, switch-table detection, and stack-frame inference. Those passes should write results through `IAnalysisStore` rather than directly touching SQLite.

## IDB Store

`SqliteAnalysisStore` initializes a sidecar database at `*.dsam.idb`. It uses WAL mode and stores unsigned addresses as fixed-width hexadecimal text so addresses sort predictably and do not collide with SQLite's signed integer limits.

Current tables:

- `labels`: user, auto, import, export, and function labels.
- `comments`: regular and repeatable comments.
- `functions`: function ranges and prototype text.
- `basic_blocks`: function-owned block ranges.
- `instructions`: decoded instruction rows and original bytes.
- `xrefs`: indexed code/data cross-references.

Use the store as the source of truth for user annotations and analysis products. The binary file remains read-only until an explicit export/patch operation.

## Recompilation And Patching

`InstructionPatchPlanner` uses Iced `BlockEncoder` for replacement instruction blocks. `BlockEncoder` is important because it can adjust branches inside the edited block when instruction lengths change.

The safe in-place patch rule is strict:

1. Decode the original instruction span and keep its bytes.
2. Build replacement `Instruction` values.
3. Encode replacements at the original virtual address.
4. If encoded bytes fit in the original span, write them and fill remaining bytes with NOP.
5. If encoded bytes do not fit, fail the in-place plan and use a relocation strategy.

The relocation strategy should allocate or reuse a code cave, write the expanded replacement block there, append a jump back to the original fall-through address, then replace the original span with a jump to the cave. Afterward, re-run xref extraction for the affected range and update the IDB store. Patching should be written to a copy via `BinaryPatchWriter.ApplyToCopyAsync`, never directly to the original input file.

## Decompilation

The decompiler spine lives under `Dsam.Core.Analysis` and builds on decoded instructions rather than the patching layer:

- `Analysis.ControlFlow`: creates function CFGs, basic blocks, edges, dominators, backedges, and natural loops.
- `Analysis.IR`: lifts common x64 instructions into a small architecture-neutral IR while preserving unsupported instructions as comments.
- `Analysis.Patterns`: identifies prologues, epilogues, direct call sites, and switch-table candidates.
- `Analysis.Decompilation`: wires CFG, dominator analysis, pattern analysis, IR lifting, and pseudocode generation.
- `Analysis.CodeGeneration`: emits an early C#-style pseudocode view.

This path is intentionally separate from `Recompilation`. Decompilation answers "what does this function mean?", while recompilation answers "how can this binary be patched safely?" Later, the two can share CFG and IR metadata for safer patch previews.

## Current Open Flow

`MainForm.OpenBinaryAsync` performs the initial wire-up:

1. Load PE metadata with `PortableExecutableLoader`.
2. Open the file through `MemoryMappedBinaryImage`.
3. Create and initialize `SqliteAnalysisStore`.
4. Decode from entry point or the first executable section.
5. Bind disassembly, hex, registers, sections, and xrefs.
6. Persist decoded instructions and xrefs to the sidecar IDB.

This gives you a working professional-grade spine: the UI is dockable, binary access is streaming, decoding is isolated, xrefs are explicit, and recompilation starts from conservative patch plans.
