using Dsam.Core.Disassembly;

namespace Dsam.Core.Analysis;

public enum LabelKind
{
    User,
    Auto,
    Import,
    Export,
    Function
}

public enum CommentKind
{
    Regular,
    Repeatable,
    Anterior,
    Posterior
}

public enum FunctionAnalysisStatus
{
    Pending,
    Analyzed,
    UserDefined
}

public sealed record Label(ulong Address, string Name, LabelKind Kind = LabelKind.User);

public sealed record Comment(ulong Address, string Text, CommentKind Kind = CommentKind.Regular);

public sealed record BasicBlock(ulong StartAddress, ulong EndAddress);

public sealed record FunctionAnalysis(
    ulong EntryAddress,
    string Name,
    ulong StartAddress,
    ulong EndAddress,
    FunctionAnalysisStatus Status = FunctionAnalysisStatus.Pending,
    string? Prototype = null,
    IReadOnlyList<BasicBlock>? BasicBlocks = null);

public interface IAnalysisStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task UpsertLabelAsync(Label label, CancellationToken cancellationToken = default);

    Task UpsertCommentAsync(Comment comment, CancellationToken cancellationToken = default);

    Task UpsertFunctionAsync(FunctionAnalysis function, CancellationToken cancellationToken = default);

    Task SaveInstructionsAsync(IEnumerable<DecodedInstruction> instructions, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Xref>> GetXrefsToAsync(ulong address, CancellationToken cancellationToken = default);
}
