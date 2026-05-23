using Dsam.Core.Analysis.ControlFlow;
using Dsam.Core.Disassembly;
using Iced.Intel;

namespace Dsam.Core.Analysis.Patterns;

public sealed class FunctionPatternAnalyzer
{
    public FunctionPatternSummary Analyze(ControlFlowGraph cfg)
    {
        var instructions = cfg.Blocks
            .SelectMany(block => block.Instructions)
            .OrderBy(instruction => instruction.Address)
            .ToArray();

        return new FunctionPatternSummary(
            DetectPrologue(instructions),
            DetectEpilogues(instructions),
            DetectSwitchCandidates(cfg),
            DetectCalls(instructions));
    }

    private static FunctionPrologue? DetectPrologue(IReadOnlyList<DecodedInstruction> instructions)
    {
        if (instructions.Count < 2)
        {
            return null;
        }

        var first = instructions[0].Instruction;
        var second = instructions[1].Instruction;
        var hasFrameSetup =
            first.Mnemonic == Mnemonic.Push
            && first.OpCount > 0
            && first.GetOpRegister(0) == Register.RBP
            && second.Mnemonic == Mnemonic.Mov
            && second.OpCount >= 2
            && second.GetOpKind(0) == OpKind.Register
            && second.GetOpKind(1) == OpKind.Register
            && second.GetOpRegister(0) == Register.RBP
            && second.GetOpRegister(1) == Register.RSP;

        if (!hasFrameSetup)
        {
            return null;
        }

        ulong stackAllocation = 0;
        if (instructions.Count >= 3)
        {
            var third = instructions[2].Instruction;
            if (third.Mnemonic == Mnemonic.Sub
                && third.OpCount >= 2
                && third.GetOpKind(0) == OpKind.Register
                && third.GetOpRegister(0) == Register.RSP)
            {
                stackAllocation = third.GetImmediate(1);
            }
        }

        return new FunctionPrologue(instructions[0].Address, UsesFramePointer: true, stackAllocation);
    }

    private static IReadOnlyList<FunctionEpilogue> DetectEpilogues(IReadOnlyList<DecodedInstruction> instructions)
    {
        var epilogues = new List<FunctionEpilogue>();
        for (var index = 0; index < instructions.Count; index++)
        {
            var instruction = instructions[index].Instruction;
            if (instruction.Mnemonic != Mnemonic.Ret)
            {
                continue;
            }

            var previous = index > 0 ? instructions[index - 1].Instruction : default;
            var kind = previous.Mnemonic switch
            {
                Mnemonic.Leave => "leave_ret",
                Mnemonic.Pop when previous.OpCount > 0 && previous.GetOpRegister(0) == Register.RBP => "pop_rbp_ret",
                _ => "ret"
            };

            epilogues.Add(new FunctionEpilogue(instructions[index].Address, kind));
        }

        return epilogues;
    }

    private static IReadOnlyList<SwitchCandidate> DetectSwitchCandidates(ControlFlowGraph cfg)
    {
        var candidates = new List<SwitchCandidate>();
        foreach (var block in cfg.Blocks)
        {
            var terminator = block.Terminator;
            if (terminator?.Instruction.FlowControl != FlowControl.IndirectBranch)
            {
                continue;
            }

            var ripRelativeReference = block.Instructions
                .SelectMany(instruction => instruction.Xrefs)
                .LastOrDefault(xref => xref.Kind == XrefKind.Data);

            candidates.Add(new SwitchCandidate(
                block.StartAddress,
                terminator.Address,
                ripRelativeReference?.ToAddress));
        }

        return candidates;
    }

    private static IReadOnlyList<CallSite> DetectCalls(IReadOnlyList<DecodedInstruction> instructions) =>
        instructions
            .SelectMany(instruction => instruction.Xrefs
                .Where(xref => xref.Kind == XrefKind.CodeCall)
                .Select(xref => new CallSite(instruction.Address, xref.ToAddress)))
            .ToArray();
}

public sealed record FunctionPatternSummary(
    FunctionPrologue? Prologue,
    IReadOnlyList<FunctionEpilogue> Epilogues,
    IReadOnlyList<SwitchCandidate> SwitchCandidates,
    IReadOnlyList<CallSite> CallSites);

public sealed record FunctionPrologue(ulong Address, bool UsesFramePointer, ulong StackAllocation);

public sealed record FunctionEpilogue(ulong Address, string Kind);

public sealed record SwitchCandidate(ulong BlockAddress, ulong IndirectJumpAddress, ulong? JumpTableAddress);

public sealed record CallSite(ulong Address, ulong TargetAddress);
