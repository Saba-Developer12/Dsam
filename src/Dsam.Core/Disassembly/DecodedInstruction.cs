using Iced.Intel;

namespace Dsam.Core.Disassembly;

public sealed record DecodedInstruction(
    ulong Address,
    long FileOffset,
    int Length,
    string Mnemonic,
    string Text,
    byte[] Bytes,
    Instruction Instruction,
    IReadOnlyList<Xref> Xrefs);
