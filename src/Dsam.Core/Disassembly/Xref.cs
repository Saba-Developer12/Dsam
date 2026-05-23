namespace Dsam.Core.Disassembly;

public enum XrefKind
{
    CodeBranch,
    CodeCall,
    Data
}

public sealed record Xref(
    ulong FromAddress,
    ulong ToAddress,
    XrefKind Kind,
    int OperandIndex);
