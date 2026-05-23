namespace Dsam.Core.Analysis.IR;

public sealed class IrFunction
{
    public IrFunction(ulong entryAddress)
    {
        EntryAddress = entryAddress;
    }

    public ulong EntryAddress { get; }

    public List<IrBasicBlock> Blocks { get; } = [];
}

public sealed class IrBasicBlock
{
    public IrBasicBlock(string name, ulong sourceAddress)
    {
        Name = name;
        SourceAddress = sourceAddress;
    }

    public string Name { get; }

    public ulong SourceAddress { get; }

    public List<IrStatement> Statements { get; } = [];

    public IrTerminator? Terminator { get; set; }
}

public abstract record IrNode;

public abstract record IrStatement : IrNode;

public abstract record IrExpression : IrNode;

public abstract record IrTerminator : IrNode;

public sealed record IrAssign(IrExpression Destination, IrExpression Source) : IrStatement;

public sealed record IrStore(IrExpression Address, IrExpression Value, int SizeInBytes) : IrStatement;

public sealed record IrCall(IrExpression? Destination, IrExpression Target, IReadOnlyList<IrExpression> Arguments) : IrStatement;

public sealed record IrCompare(IrExpression Left, IrExpression Right, string Operation) : IrStatement;

public sealed record IrAssemblyComment(ulong Address, string Text) : IrStatement;

public sealed record IrComment(ulong Address, string Text) : IrStatement;

public sealed record IrRegister(string Name, int Bits) : IrExpression;

public sealed record IrTemporary(string Name, int Bits) : IrExpression;

public sealed record IrConstant(ulong Value, int Bits) : IrExpression;

public sealed record IrBinaryExpression(string Operation, IrExpression Left, IrExpression Right, int Bits) : IrExpression;

public sealed record IrMemoryAddress(
    IrRegister? Base,
    IrRegister? Index,
    int Scale,
    long Displacement,
    ulong? AbsoluteAddress) : IrExpression;

public sealed record IrBranch(string TargetBlock) : IrTerminator;

public sealed record IrConditionalBranch(
    string Condition,
    string TrueTargetBlock,
    string FalseTargetBlock) : IrTerminator;

public sealed record IrIndirectBranch(IrExpression Target) : IrTerminator;

public sealed record IrReturn(IrExpression? Value) : IrTerminator;

public sealed record IrUnresolvedTerminator(string Reason) : IrTerminator;
