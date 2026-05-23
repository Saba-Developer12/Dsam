using Dsam.Core.Analysis.CodeGeneration;
using Dsam.Core.Analysis.ControlFlow;
using Dsam.Core.Analysis.IR;
using Dsam.Core.Analysis.Patterns;
using Dsam.Core.Disassembly;

namespace Dsam.Core.Analysis.Decompilation;

public interface IDecompilerPipeline
{
    DecompilationResult Decompile(DecompilationRequest request);
}

public sealed class DecompilerPipeline : IDecompilerPipeline
{
    private readonly ControlFlowGraphBuilder _cfgBuilder = new();
    private readonly DominatorAnalysis _dominatorAnalysis = new();
    private readonly X64InstructionLifter _lifter = new();
    private readonly FunctionPatternAnalyzer _patternAnalyzer = new();
    private readonly CSharpPseudoCodeGenerator _codeGenerator = new();

    public DecompilationResult Decompile(DecompilationRequest request)
    {
        var cfg = _cfgBuilder.Build(request.FunctionEntry, request.Instructions);
        var dominators = _dominatorAnalysis.Analyze(cfg);
        var patterns = _patternAnalyzer.Analyze(cfg);
        var ir = _lifter.Lift(cfg);
        var csharp = _codeGenerator.Generate(ir, cfg, dominators, patterns);

        return new DecompilationResult(cfg, dominators, patterns, ir, csharp);
    }
}

public sealed record DecompilationRequest(
    ulong FunctionEntry,
    IReadOnlyList<DecodedInstruction> Instructions);

public sealed record DecompilationResult(
    ControlFlowGraph ControlFlowGraph,
    DominatorResult Dominators,
    FunctionPatternSummary Patterns,
    IrFunction Ir,
    string CSharpPseudocode);
