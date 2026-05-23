using Dsam.Core.Disassembly;

namespace Dsam.Core.Analysis.ControlFlow;

public sealed class ControlFlowGraph
{
    private readonly Dictionary<ulong, BasicBlockNode> _blocksByStartAddress = [];
    private readonly Dictionary<ulong, BasicBlockNode> _blocksByInstructionAddress = [];

    public ControlFlowGraph(ulong functionEntry)
    {
        FunctionEntry = functionEntry;
    }

    public ulong FunctionEntry { get; }

    public List<BasicBlockNode> Blocks { get; } = [];

    public List<ControlFlowEdge> Edges { get; } = [];

    public IReadOnlyDictionary<ulong, BasicBlockNode> BlocksByStartAddress => _blocksByStartAddress;

    public IReadOnlyDictionary<ulong, BasicBlockNode> BlocksByInstructionAddress => _blocksByInstructionAddress;

    public void RebuildIndexes()
    {
        _blocksByStartAddress.Clear();
        _blocksByInstructionAddress.Clear();

        foreach (var block in Blocks)
        {
            _blocksByStartAddress[block.StartAddress] = block;
            foreach (var instruction in block.Instructions)
            {
                _blocksByInstructionAddress[instruction.Address] = block;
            }
        }
    }

    public BasicBlockNode? GetBlock(ulong startAddress) =>
        _blocksByStartAddress.TryGetValue(startAddress, out var block)
            ? block
            : Blocks.FirstOrDefault(candidate => candidate.StartAddress == startAddress);

    public bool TryGetBlock(ulong startAddress, out BasicBlockNode block)
    {
        if (_blocksByStartAddress.TryGetValue(startAddress, out block!))
        {
            return true;
        }

        block = default!;
        return false;
    }

    public bool TryGetInstructionBlock(ulong instructionAddress, out BasicBlockNode block)
    {
        if (_blocksByInstructionAddress.TryGetValue(instructionAddress, out block!))
        {
            return true;
        }

        block = default!;
        return false;
    }

    public IEnumerable<ControlFlowEdge> GetOutgoingEdges(ulong blockStartAddress) =>
        Edges.Where(edge => edge.FromBlock == blockStartAddress);

    public IEnumerable<ControlFlowEdge> GetIncomingEdges(ulong blockStartAddress) =>
        Edges.Where(edge => edge.ToBlock == blockStartAddress);
}

public sealed class BasicBlockNode
{
    public BasicBlockNode(ulong startAddress, BasicBlockKind kind = BasicBlockKind.Internal)
    {
        StartAddress = startAddress;
        Kind = kind;
    }

    public ulong StartAddress { get; }

    public ulong EndAddress { get; set; }

    public BasicBlockKind Kind { get; }

    public bool IsPlaceholder => Kind != BasicBlockKind.Internal;

    public List<DecodedInstruction> Instructions { get; } = [];

    public DecodedInstruction? Terminator => Instructions.LastOrDefault();
}

public sealed record ControlFlowEdge(
    ulong FromBlock,
    ulong? ToBlock,
    ControlFlowEdgeKind Kind,
    ulong? TargetAddress = null,
    string? Annotation = null);

public enum BasicBlockKind
{
    Internal,
    External,
    Unresolved
}

public enum ControlFlowEdgeKind
{
    Fallthrough,
    ConditionalTrue,
    ConditionalFalse,
    Unconditional,
    ExternalCall,
    Indirect,
    Unresolved,
    Return,
    Exception
}
