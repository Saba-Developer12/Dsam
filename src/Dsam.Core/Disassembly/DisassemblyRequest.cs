using Dsam.Core.Binary;

namespace Dsam.Core.Disassembly;

public sealed record DisassemblyRequest(
    IBinaryImage Image,
    BinarySection Section,
    ulong StartAddress,
    int Bitness = 64,
    int MaxInstructions = 4096,
    ulong? EndAddress = null,
    bool StopAtControlFlowBarrier = false);
