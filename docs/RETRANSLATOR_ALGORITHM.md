# Dsam Retranslator Algorithm

This design belongs above `InstructionPatchPlanner`. `InstructionPatchPlanner` is the conservative in-place encoder; the `Retranslator` is the larger component that owns instruction lists, address remapping, relocation records, code caves, and PE section reconstruction.

## Key Rule

Do not recalculate every relative offset after a patch just because it appears after the patched address. Relative offsets are local formulas:

```text
encoded_displacement = target_address - address_of_next_instruction
```

Only instructions whose own address changes, whose target address changes, or whose encoded length changes need re-encoding. In fixed-layout patching, following instructions keep the same address, so most relative offsets do not change.

## Core Models

The retranlator should work on a mixed list of code and raw data blocks. Non-code bytes must remain explicit so the reconstructed PE preserves layout.

```csharp
public enum BinaryBlockKind
{
    Code,
    Data,
    Padding,
    CodeCave
}

public sealed class RelocatableInstruction
{
    public required Instruction Instruction { get; init; }
    public required ulong OriginalAddress { get; init; }
    public required long OriginalFileOffset { get; init; }
    public required int OriginalSize { get; init; }
    public required byte[] OriginalBytes { get; init; }

    public ulong NewAddress { get; set; }
    public int NewSize { get; set; }
    public byte[]? EncodedBytes { get; set; }

    public List<InstructionReference> References { get; } = [];
}

public sealed record InstructionReference(
    ulong FromOriginalAddress,
    ulong OriginalTargetAddress,
    ReferenceKind Kind,
    int OperandIndex);

public enum ReferenceKind
{
    DirectBranch,
    DirectCall,
    RipRelativeData
}

public sealed class AddressMap
{
    public Dictionary<ulong, ulong> OldToNew { get; } = [];
    public Dictionary<ulong, ulong> NewToOld { get; } = [];

    public ulong ResolveTarget(ulong originalTarget) =>
        OldToNew.TryGetValue(originalTarget, out var movedTarget)
            ? movedTarget
            : originalTarget;
}

public sealed record RelocationEntry(
    ulong SourceOriginalAddress,
    ulong SourceNewAddress,
    ulong TargetOriginalAddress,
    ulong TargetNewAddress,
    ReferenceKind Kind,
    int OperandIndex);
```

## 1. Instruction Decoding

Decode executable ranges only. Treat non-executable bytes as raw data blocks. Decoding every byte in the file as code will produce false instructions and will corrupt reconstruction.

Algorithm:

1. Load PE metadata with `PortableExecutableLoader`.
2. Open the input through `MemoryMappedBinaryImage`.
3. For each executable section, create a `Decoder` starting at `section.VirtualAddress`.
4. Decode until the section end or until the analysis layer says a code range ends.
5. For every decoded instruction, capture original VA, file offset, length, bytes, and Iced `Instruction`.
6. Extract references:
   - `NearBranch16/32/64` operands become direct branch/call references.
   - `instruction.IsIPRelativeMemoryOperand` becomes a RIP-relative data reference.
7. Store the list in address order.

Sketch:

```csharp
var list = new List<RelocatableInstruction>();

await foreach (var decoded in disassemblyService.DecodeAsync(request, ct))
{
    var node = new RelocatableInstruction
    {
        Instruction = decoded.Instruction,
        OriginalAddress = decoded.Address,
        OriginalFileOffset = decoded.FileOffset,
        OriginalSize = decoded.Length,
        OriginalBytes = decoded.Bytes,
        NewAddress = decoded.Address,
        NewSize = decoded.Length,
        EncodedBytes = decoded.Bytes
    };

    foreach (var xref in decoded.Xrefs)
    {
        node.References.Add(new InstructionReference(
            xref.FromAddress,
            xref.ToAddress,
            xref.Kind == XrefKind.CodeCall
                ? ReferenceKind.DirectCall
                : xref.Kind == XrefKind.CodeBranch
                    ? ReferenceKind.DirectBranch
                    : ReferenceKind.RipRelativeData,
            xref.OperandIndex));
    }

    list.Add(node);
}
```

## 2. Patch Planning

When the user replaces a NOP sequence with a JMP/CALL or any other sequence:

1. Find the covered instruction span by original VA.
2. Decode or build replacement `Instruction` values.
3. Tentatively encode the replacement at the original start VA.
4. If the encoded bytes fit the covered span, use fixed-layout patching:
   - write replacement bytes;
   - fill remaining bytes with NOP;
   - keep every following instruction at the same VA.
5. If the replacement does not fit, switch to a code-cave/trampoline plan.

Important: in fixed-layout patching, there is no global section shift. That is what makes it safe for small patches.

## 3. Address Assignment

For full reconstruction or code-cave relocation, assign new addresses before encoding final bytes.

Algorithm:

1. Start each section at its original virtual address.
2. Walk blocks in order.
3. For unchanged fixed-layout blocks, `NewAddress = OriginalAddress`.
4. For rebuilt blocks, assign `NewAddress = cursor`, then increment `cursor` by the encoded size estimate.
5. Repeat size estimation until stable. This is necessary because a short branch may grow into a near branch after movement.
6. Fill `AddressMap.OldToNew` and `AddressMap.NewToOld`.

Iced `BlockEncoder` reduces the pain here because it can optimize/fix direct branches inside the encoded block. You still need the old-to-new map to update each instruction's target before passing the block to Iced.

## 4. Relocation Table Construction

After new addresses are assigned, build relocation entries from instruction references.

```csharp
foreach (var instruction in instructions)
{
    foreach (var reference in instruction.References)
    {
        var sourceNew = addressMap.ResolveTarget(reference.FromOriginalAddress);
        var targetNew = addressMap.ResolveTarget(reference.OriginalTargetAddress);

        relocations.Add(new RelocationEntry(
            reference.FromOriginalAddress,
            sourceNew,
            reference.OriginalTargetAddress,
            targetNew,
            reference.Kind,
            reference.OperandIndex));
    }
}
```

Direct branches/calls:

```text
new_displacement = target_new_va - next_instruction_new_va
```

RIP-relative data:

```text
new_displacement = data_target_new_or_original_va - next_instruction_new_va
```

Usually, data targets outside the moved/rebuilt block keep their original VA. Data targets inside a moved block must resolve through `AddressMap`.

## 5. Rewriting Instruction Targets

Before final encoding:

1. Clone each Iced `Instruction`.
2. Set `instruction.IP` to the assigned new VA.
3. For direct branches/calls, set the near branch target to the relocated target VA.
4. For RIP-relative memory operands, preserve the absolute memory target when external, or map it when the target moved.
5. Pass the whole block to `BlockEncoder.TryEncode`.

Conceptual flow:

```csharp
var rewritten = new List<Instruction>();

foreach (var node in block.Instructions)
{
    var instruction = node.Instruction;
    instruction.IP = node.NewAddress;

    foreach (var reference in node.References)
    {
        var targetNew = addressMap.ResolveTarget(reference.OriginalTargetAddress);

        if (reference.Kind is ReferenceKind.DirectBranch or ReferenceKind.DirectCall)
        {
            instruction.NearBranch64 = targetNew;
        }
        else if (reference.Kind == ReferenceKind.RipRelativeData)
        {
            instruction.MemoryDisplacement64 = targetNew;
        }
    }

    rewritten.Add(instruction);
}

var writer = new StreamCodeWriter(outputStream);
var instructionBlock = new InstructionBlock(writer, rewritten, block.NewBaseAddress);
BlockEncoder.TryEncode(
    bitness,
    instructionBlock,
    out var error,
    out var result,
    BlockEncoderOptions.ReturnNewInstructionOffsets | BlockEncoderOptions.ReturnRelocInfos);
```

Use the actual Iced operand setter that matches the decoded operand kind. Direct near branches use the `NearBranch*` target fields. RIP-relative operands are represented by the memory displacement target; keep tests around this because malformed operand rewriting is where binary patchers earn their bruises.

## 6. Binary Reconstruction

There are two reconstruction modes.

### Fixed-Layout Patch

Use this for same-size or smaller patches.

1. Copy the original file to an output path.
2. For each patch plan, seek to `FileOffset`.
3. Write `PatchedBytes`.
4. Do not change PE headers, section sizes, alignments, imports, relocations, or certificates.

This is the safest mode and is what `BinaryPatchWriter.ApplyToCopyAsync` currently supports.

### Rebuilt Section

Use this only when you intentionally rebuild a code section.

1. Copy all headers and non-target sections exactly.
2. Rebuild the target section into a buffer from mixed blocks:
   - unchanged data bytes;
   - encoded instruction blocks;
   - padding;
   - code caves if they live inside the section.
3. Align section raw size to `FileAlignment`.
4. Align virtual size to `SectionAlignment`.
5. Update the target section header:
   - `SizeOfRawData`;
   - `VirtualSize`;
   - section characteristics if execute/read flags changed.
6. Update PE optional header sizes if a section grows:
   - `SizeOfImage`;
   - checksum if you decide to maintain it.
7. Preserve overlay data unless explicitly stripping it.

For production, use a PE writer layer rather than ad hoc byte offsets. Dsam can initially support fixed-layout patches and code caves inside existing slack space, then add section growth later.

## 7. Code Cave Strategy

Use a code cave when replacement bytes do not fit the original span.

Find a cave:

1. Search executable sections for long runs of `0x00`, `0xCC`, or `0x90`.
2. Require enough space for:
   - replacement sequence;
   - optional saved overwritten instructions;
   - jump back;
   - alignment padding.
3. Prefer caves in the same section and within rel32 reach from the patch site.
4. If no cave is available, consider adding a new executable section as a later, higher-risk feature.

Patch flow:

1. Determine the minimum overwrite span at the original site.
   - x64 near JMP/CALL is usually 5 bytes.
   - Never split an instruction; expand the span to full instruction boundaries.
2. Copy any overwritten original instructions that still need to execute into the cave.
3. Encode the user replacement into the cave.
4. Append a jump from the cave to `original_site + overwritten_span_length`.
5. Replace the original site with `jmp cave_address`.
6. NOP-fill leftover bytes at the original site.
7. Add relocation entries for:
   - the jump from original site to cave;
   - any copied overwritten instruction with RIP-relative operands;
   - the jump back from cave to original fall-through.

Pseudo-plan:

```csharp
var cave = caveFinder.Find(requiredLength, near: patchSiteAddress);
var overwritten = instructionList.Covering(patchSiteAddress, minLength: 5);

var caveInstructions = new List<Instruction>();
caveInstructions.AddRange(userReplacement);
caveInstructions.AddRange(CloneAndRetarget(overwritten.Instructions, addressMap));
caveInstructions.Add(Instruction.CreateBranch(Code.Jmp_rel32_64, overwritten.FallThroughAddress));

var caveBytes = blockEncoder.Encode(caveInstructions, cave.Address);
var siteJump = Instruction.CreateBranch(Code.Jmp_rel32_64, cave.Address);
var siteBytes = encoder.Encode(siteJump, patchSiteAddress).PadTo(overwritten.ByteLength, 0x90);
```

## 8. Jump Address Mapping

Keep three maps:

```csharp
Dictionary<ulong, RelocatableInstruction> byOriginalAddress;
Dictionary<ulong, RelocatableInstruction> byNewAddress;
AddressMap oldToNew;
```

Resolution rules:

1. If branch target is inside rebuilt/moved code, use `oldToNew[target]`.
2. If branch target is outside rebuilt code, keep original target.
3. If target is an import thunk, export, or external API address, keep original target and record it as external.
4. If target lands in the middle of an instruction, mark the range unsafe until analysis proves it is valid obfuscated/control-flow code.
5. If target is no longer encodable in the selected instruction form, let `BlockEncoder` widen it or reject the plan.

## Recommended Dsam Service Shape

```csharp
public interface IRetranslator
{
    Task<RetranslationPlan> PlanAsync(
        BinaryImageDescriptor image,
        IBinaryImage binary,
        IReadOnlyList<InstructionEdit> edits,
        CancellationToken cancellationToken);
}

public sealed class RetranslationPlan
{
    public List<RelocatableInstruction> Instructions { get; } = [];
    public AddressMap AddressMap { get; } = new();
    public List<RelocationEntry> Relocations { get; } = [];
    public List<PatchPlan> FilePatches { get; } = [];
    public List<CodeCaveAllocation> CodeCaves { get; } = [];
    public List<string> Warnings { get; } = [];
}
```

This keeps the UI out of the hard part. The UI submits edits; the retranlator returns a plan. The plan can then be previewed, validated, written to a copy, and stored in the IDB.
