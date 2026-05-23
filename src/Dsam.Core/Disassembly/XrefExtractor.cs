using Iced.Intel;

namespace Dsam.Core.Disassembly;

public static class XrefExtractor
{
    public static IEnumerable<Xref> Extract(Instruction instruction)
    {
        if (TryGetDirectBranchOperand(instruction, out var branchOperandIndex))
        {
            var kind = instruction.FlowControl is FlowControl.Call
                ? XrefKind.CodeCall
                : XrefKind.CodeBranch;

            yield return new Xref(
                instruction.IP,
                instruction.NearBranchTarget,
                kind,
                branchOperandIndex);
        }

        if (instruction.IsIPRelativeMemoryOperand)
        {
            yield return new Xref(
                instruction.IP,
                instruction.IPRelativeMemoryAddress,
                XrefKind.Data,
                FindMemoryOperandIndex(instruction));
        }
    }

    private static bool TryGetDirectBranchOperand(Instruction instruction, out int operandIndex)
    {
        for (var index = 0; index < instruction.OpCount; index++)
        {
            var kind = instruction.GetOpKind(index);
            if (kind is OpKind.NearBranch16 or OpKind.NearBranch32 or OpKind.NearBranch64)
            {
                operandIndex = index;
                return true;
            }
        }

        operandIndex = -1;
        return false;
    }

    private static int FindMemoryOperandIndex(Instruction instruction)
    {
        for (var index = 0; index < instruction.OpCount; index++)
        {
            if (instruction.GetOpKind(index) == OpKind.Memory)
            {
                return index;
            }
        }

        return -1;
    }
}
