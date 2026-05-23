using Iced.Intel;

namespace Dsam.Core.Recompilation;

public sealed record InPlacePatchRequest(
    ulong Address,
    long FileOffset,
    byte[] OriginalBytes,
    IReadOnlyList<Instruction> ReplacementInstructions,
    byte PaddingByte = 0x90);
