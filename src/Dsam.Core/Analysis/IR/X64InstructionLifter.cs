using Dsam.Core.Analysis.ControlFlow;
using Dsam.Core.Disassembly;
using Iced.Intel;

namespace Dsam.Core.Analysis.IR;

public sealed class X64InstructionLifter
{
    public IrFunction Lift(ControlFlowGraph graph)
    {
        var function = new IrFunction(graph.FunctionEntry);
        var blockNames = graph.Blocks.ToDictionary(block => block.StartAddress, block => FormatBlockName(block.StartAddress));

        foreach (var block in graph.Blocks)
        {
            var irBlock = new IrBasicBlock(blockNames[block.StartAddress], block.StartAddress);
            foreach (var decoded in block.Instructions)
            {
                if (decoded == block.Terminator && IsBranchTerminator(decoded.Instruction.FlowControl))
                {
                    irBlock.Statements.Add(new IrAssemblyComment(decoded.Address, decoded.Text));
                    continue;
                }

                LiftInstruction(decoded, irBlock.Statements);
            }

            irBlock.Terminator = LiftTerminator(graph, block, blockNames);
            function.Blocks.Add(irBlock);
        }

        return function;
    }

    private static void LiftInstruction(DecodedInstruction decoded, List<IrStatement> statements)
    {
        var instruction = decoded.Instruction;
        statements.Add(new IrAssemblyComment(decoded.Address, decoded.Text));

        switch (instruction.Mnemonic)
        {
            case Mnemonic.Mov:
                LiftMove(instruction, statements);
                break;

            case Mnemonic.Lea:
                if (instruction.OpCount >= 2)
                {
                    statements.Add(new IrAssign(ReadOperand(instruction, 0), ReadAddressOperand(instruction)));
                }
                break;

            case Mnemonic.Add:
                LiftBinaryAssignment(instruction, "+", statements);
                break;

            case Mnemonic.Sub:
                LiftBinaryAssignment(instruction, "-", statements);
                break;

            case Mnemonic.And:
                LiftBinaryAssignment(instruction, "&", statements);
                break;

            case Mnemonic.Or:
                LiftBinaryAssignment(instruction, "|", statements);
                break;

            case Mnemonic.Xor:
                LiftBinaryAssignment(instruction, "^", statements);
                break;

            case Mnemonic.Inc:
                LiftUnaryAdd(instruction, 1, statements);
                break;

            case Mnemonic.Dec:
                LiftUnaryAdd(instruction, -1, statements);
                break;

            case Mnemonic.Cmp:
                if (instruction.OpCount >= 2)
                {
                    statements.Add(new IrCompare(ReadOperand(instruction, 0), ReadOperand(instruction, 1), "cmp"));
                }
                break;

            case Mnemonic.Test:
                if (instruction.OpCount >= 2)
                {
                    statements.Add(new IrCompare(
                        new IrBinaryExpression("&", ReadOperand(instruction, 0), ReadOperand(instruction, 1), 64),
                        new IrConstant(0, 64),
                        "test"));
                }
                break;

            case Mnemonic.Call:
                statements.Add(new IrCall(null, ReadBranchOrOperand(instruction), Array.Empty<IrExpression>()));
                break;

            case Mnemonic.Push:
            case Mnemonic.Pop:
                statements.Add(new IrComment(decoded.Address, $"{instruction.Mnemonic.ToString().ToLowerInvariant()} stack operation: {decoded.Text}"));
                break;

            case Mnemonic.Nop:
                break;

            default:
                statements.Add(new IrComment(decoded.Address, $"unsupported: {decoded.Text}"));
                break;
        }
    }

    private static void LiftMove(Instruction instruction, List<IrStatement> statements)
    {
        if (instruction.OpCount < 2)
        {
            return;
        }

        var destinationKind = instruction.GetOpKind(0);
        var source = ReadOperand(instruction, 1);

        if (destinationKind == OpKind.Memory)
        {
            statements.Add(new IrStore(ReadAddressOperand(instruction), source, SizeInBytes: 8));
        }
        else
        {
            statements.Add(new IrAssign(ReadOperand(instruction, 0), source));
        }
    }

    private static void LiftBinaryAssignment(Instruction instruction, string operation, List<IrStatement> statements)
    {
        if (instruction.OpCount < 2)
        {
            return;
        }

        var destination = ReadOperand(instruction, 0);
        var source = ReadOperand(instruction, 1);
        statements.Add(new IrAssign(
            destination,
            new IrBinaryExpression(operation, destination, source, 64)));
    }

    private static void LiftUnaryAdd(Instruction instruction, long delta, List<IrStatement> statements)
    {
        if (instruction.OpCount < 1)
        {
            return;
        }

        var destination = ReadOperand(instruction, 0);
        var operation = delta >= 0 ? "+" : "-";
        statements.Add(new IrAssign(
            destination,
            new IrBinaryExpression(operation, destination, new IrConstant((ulong)Math.Abs(delta), 64), 64)));
    }

    private static IrTerminator LiftTerminator(
        ControlFlowGraph graph,
        BasicBlockNode block,
        IReadOnlyDictionary<ulong, string> blockNames)
    {
        var terminator = block.Terminator;
        if (terminator is null)
        {
            return new IrUnresolvedTerminator("empty block");
        }

        var outgoing = graph.GetOutgoingEdges(block.StartAddress).ToArray();
        return terminator.Instruction.FlowControl switch
        {
            FlowControl.ConditionalBranch => CreateConditionalTerminator(terminator, outgoing, blockNames),
            FlowControl.UnconditionalBranch => CreateBranchTerminator(outgoing, blockNames),
            FlowControl.IndirectBranch => new IrIndirectBranch(ReadBranchOrOperand(terminator.Instruction)),
            FlowControl.Return => new IrReturn(new IrRegister("rax", 64)),
            FlowControl.Interrupt => new IrReturn(null),
            FlowControl.Exception => new IrUnresolvedTerminator("exception edge"),
            _ => CreateFallthroughTerminator(outgoing, blockNames)
        };
    }

    private static IrTerminator CreateConditionalTerminator(
        DecodedInstruction instruction,
        IReadOnlyList<ControlFlowEdge> outgoing,
        IReadOnlyDictionary<ulong, string> blockNames)
    {
        var trueTarget = outgoing.FirstOrDefault(edge => edge.Kind == ControlFlowEdgeKind.ConditionalTrue)?.ToBlock;
        var falseTarget = outgoing.FirstOrDefault(edge => edge.Kind == ControlFlowEdgeKind.ConditionalFalse)?.ToBlock;

        if (trueTarget is null || falseTarget is null)
        {
            return new IrUnresolvedTerminator("unresolved conditional branch");
        }

        return new IrConditionalBranch(
            FormatCondition(instruction.Instruction.Mnemonic),
            blockNames[trueTarget.Value],
            blockNames[falseTarget.Value]);
    }

    private static IrTerminator CreateBranchTerminator(
        IReadOnlyList<ControlFlowEdge> outgoing,
        IReadOnlyDictionary<ulong, string> blockNames)
    {
        var target = outgoing.FirstOrDefault(edge => edge.Kind == ControlFlowEdgeKind.Unconditional)?.ToBlock;
        return target is null
            ? new IrUnresolvedTerminator("unresolved branch")
            : new IrBranch(blockNames[target.Value]);
    }

    private static IrTerminator CreateFallthroughTerminator(
        IReadOnlyList<ControlFlowEdge> outgoing,
        IReadOnlyDictionary<ulong, string> blockNames)
    {
        var target = outgoing.FirstOrDefault(edge => edge.Kind == ControlFlowEdgeKind.Fallthrough)?.ToBlock;
        return target is null
            ? new IrReturn(null)
            : new IrBranch(blockNames[target.Value]);
    }

    private static IrExpression ReadBranchOrOperand(Instruction instruction)
    {
        for (var index = 0; index < instruction.OpCount; index++)
        {
            var kind = instruction.GetOpKind(index);
            if (kind is OpKind.NearBranch16 or OpKind.NearBranch32 or OpKind.NearBranch64)
            {
                return new IrConstant(instruction.NearBranchTarget, 64);
            }
        }

        return instruction.OpCount > 0
            ? ReadOperand(instruction, 0)
            : new IrConstant(0, 64);
    }

    private static IrExpression ReadOperand(Instruction instruction, int operandIndex)
    {
        var kind = instruction.GetOpKind(operandIndex);
        return kind switch
        {
            OpKind.Register => new IrRegister(FormatRegister(instruction.GetOpRegister(operandIndex)), 64),
            OpKind.Memory => ReadAddressOperand(instruction),
            OpKind.NearBranch16 or OpKind.NearBranch32 or OpKind.NearBranch64 => new IrConstant(instruction.NearBranchTarget, 64),
            OpKind.Immediate8 or OpKind.Immediate8_2nd or OpKind.Immediate16 or OpKind.Immediate32 or OpKind.Immediate64
                or OpKind.Immediate8to16 or OpKind.Immediate8to32 or OpKind.Immediate8to64 or OpKind.Immediate32to64
                => new IrConstant(instruction.GetImmediate(operandIndex), 64),
            _ => new IrTemporary($"op{operandIndex}_{kind}", 64)
        };
    }

    private static IrExpression ReadAddressOperand(Instruction instruction)
    {
        if (instruction.IsIPRelativeMemoryOperand)
        {
            return new IrMemoryAddress(null, null, 1, 0, instruction.IPRelativeMemoryAddress);
        }

        var baseRegister = instruction.MemoryBase == Register.None
            ? null
            : new IrRegister(FormatRegister(instruction.MemoryBase), 64);
        var indexRegister = instruction.MemoryIndex == Register.None
            ? null
            : new IrRegister(FormatRegister(instruction.MemoryIndex), 64);

        return new IrMemoryAddress(
            baseRegister,
            indexRegister,
            instruction.MemoryIndexScale,
            unchecked((long)instruction.MemoryDisplacement64),
            null);
    }

    private static string FormatCondition(Mnemonic mnemonic) =>
        mnemonic switch
        {
            Mnemonic.Je => "eq",
            Mnemonic.Jne => "ne",
            Mnemonic.Ja => "ugt",
            Mnemonic.Jae => "uge",
            Mnemonic.Jb => "ult",
            Mnemonic.Jbe => "ule",
            Mnemonic.Jg => "gt",
            Mnemonic.Jge => "ge",
            Mnemonic.Jl => "lt",
            Mnemonic.Jle => "le",
            Mnemonic.Js => "sign",
            Mnemonic.Jns => "not_sign",
            Mnemonic.Jp => "parity",
            Mnemonic.Jnp => "not_parity",
            _ => mnemonic.ToString().ToLowerInvariant()
        };

    private static string FormatBlockName(ulong address) => $"loc_{address:X16}";

    private static string FormatRegister(Register register) => register.ToString().ToLowerInvariant();

    private static bool IsBranchTerminator(FlowControl flowControl) =>
        flowControl is FlowControl.ConditionalBranch
            or FlowControl.UnconditionalBranch
            or FlowControl.IndirectBranch
            or FlowControl.Return
            or FlowControl.Exception
            or FlowControl.Interrupt;
}
