using System.Text;
using Dsam.Core.Analysis.ControlFlow;
using Dsam.Core.Analysis.IR;
using Dsam.Core.Analysis.Patterns;

namespace Dsam.Core.Analysis.CodeGeneration;

public sealed class CSharpPseudoCodeGenerator
{
    public string Generate(
        IrFunction function,
        ControlFlowGraph cfg,
        DominatorResult dominators,
        FunctionPatternSummary patterns)
    {
        var builder = new StringBuilder();
        var entryMethodName = FormatFunctionName(function.EntryAddress);
        var localVariables = FindLocalVariables(function);

        builder.AppendLine("// Dsam decompiler pseudocode.");
        builder.AppendLine("// Generated as valid C#-shaped code for editor readability.");
        AppendAnalysisComments(builder, dominators, patterns);
        builder.AppendLine();
        builder.AppendLine("namespace Dsam.Decompiled");
        builder.AppendLine("{");
        builder.AppendLine("    public static class DecompiledProgram");
        builder.AppendLine("    {");
        builder.AppendLine($"        public static void {entryMethodName}()");
        builder.AppendLine("        {");
        builder.AppendLine($"            {FormatBlockMethodName(function.EntryAddress)}();");
        builder.AppendLine("        }");
        builder.AppendLine();

        foreach (var block in function.Blocks)
        {
            AppendBlockMethod(builder, block);
        }

        AppendHelpers(builder, localVariables);
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static HashSet<string> FindLocalVariables(IrFunction function)
    {
        var locals = new HashSet<string>();

        void Collect(IrExpression? expr)
        {
            if (expr is null) return;

            if (expr is IrMemoryAddress memAddr && memAddr.Base?.Name == "rbp" && memAddr.Displacement < 0)
            {
                locals.Add(FormatAddress(memAddr));
            }
            else if (expr is IrBinaryExpression binary)
            {
                Collect(binary.Left);
                Collect(binary.Right);
            }
        }

        foreach (var block in function.Blocks)
        {
            foreach (var statement in block.Statements)
            {
                switch (statement)
                {
                    case IrAssign assign:
                        Collect(assign.Destination);
                        Collect(assign.Source);
                        break;
                    case IrStore store:
                        Collect(store.Address);
                        Collect(store.Value);
                        break;
                    case IrCompare compare:
                        Collect(compare.Left);
                        Collect(compare.Right);
                        break;
                    case IrCall call:
                        Collect(call.Destination);
                        Collect(call.Target);
                        break;
                }
            }
            if (block.Terminator is IrIndirectBranch indirect)
            {
                Collect(indirect.Target);
            }
            else if (block.Terminator is IrReturn ret && ret.Value is not null)
            {
                Collect(ret.Value);
            }
        }
        return locals;
    }

    private static void AppendAnalysisComments(
        StringBuilder builder,
        DominatorResult dominators,
        FunctionPatternSummary patterns)
    {
        if (patterns.Prologue is not null)
        {
            builder.AppendLine($"// Prologue: framePointer={patterns.Prologue.UsesFramePointer}, stack=0x{patterns.Prologue.StackAllocation:X}");
        }

        if (patterns.SwitchCandidates.Count > 0)
        {
            builder.AppendLine($"// Switch candidates: {patterns.SwitchCandidates.Count}");
        }

        if (dominators.NaturalLoops.Count > 0)
        {
            builder.AppendLine($"// Natural loops: {dominators.NaturalLoops.Count}");
        }
    }

    private static void AppendBlockMethod(StringBuilder builder, IrBasicBlock block)
    {
        builder.AppendLine($"        private static void {FormatBlockMethodName(block.SourceAddress)}()");
        builder.AppendLine("        {");
        foreach (var statement in block.Statements)
        {
            var formatted = FormatStatement(statement);
            if (!string.IsNullOrEmpty(formatted))
            {
                builder.AppendLine($"            {formatted}");
            }
        }

        if (block.Terminator is not null)
        {
            foreach (var line in FormatTerminator(block.Terminator))
            {
                builder.AppendLine($"            {line}");
            }
        }
        // No explicit return needed anymore as control flow is handled by block calls.

        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static string? FormatStatement(IrStatement statement) =>
        statement switch
        {
            IrAssemblyComment => null,
            IrComment => null,
            IrAssign assign => FormatAssign(assign),
            IrStore store => store.SizeInBytes == 8
                ? $"Memory.Write64({FormatAddress(store.Address)}, {FormatExpression(store.Value)});"
                : null, // Ignore writes of other sizes for now
            IrCall call => FormatCall(call),
            IrCompare compare => FormatCompare(compare),
            _ => null // Ignore unsupported statements
        };

    private static IEnumerable<string> FormatTerminator(IrTerminator terminator)
    {
        switch (terminator)
        {
            case IrBranch branch:
                yield return $"{SanitizeBlockMethodName(branch.TargetBlock)}();";
                break;
            case IrConditionalBranch branch:
                yield return $"if ({FormatFlagCondition(branch.Condition)})";
                yield return "{";
                yield return $"    {SanitizeBlockMethodName(branch.TrueTargetBlock)}();";
                yield return "}";
                yield return "else";
                yield return "{";
                yield return $"    {SanitizeBlockMethodName(branch.FalseTargetBlock)}();";
                yield return "}";
                break;

            case IrIndirectBranch branch:
                yield return $"// TODO: indirect branch target {FormatExpression(branch.Target)}";
                yield return "return;";
                break;

            case IrReturn { Value: null }:
                yield return "return;";
                break;

            case IrReturn { Value: { } value }:
                yield return $"rax = {FormatExpression(value)};";
                yield return "return;";
                break;

            case IrUnresolvedTerminator unresolved:
                yield return $"// TODO: unresolved terminator: {EscapeComment(unresolved.Reason)}";
                yield return "return;";
                break;

            default:
                yield return $"// TODO: unsupported terminator: {terminator.GetType().Name}";
                yield return "return;";
                break;
        }
    }

    private static string FormatAssign(IrAssign assign)
    {
        var sourceExpression = FormatExpression(assign.Source);

        if (assign.Destination is IrRegister destRegister)
        {
            return $"{FormatRegister(destRegister.Name)} = {sourceExpression};";
        }

        if (assign.Destination is IrMemoryAddress destAddress)
        {
            // Check for stack variables (e.g., [rbp - 8])
            if (destAddress.Base?.Name == "rbp" && destAddress.Displacement < 0)
            {
                return $"{FormatAddress(destAddress)} = {sourceExpression};";
            }
            
            // Handle other memory writes
            return $"Memory.Write64({FormatAddress(destAddress)}, {sourceExpression});";
        }

        // Fallback for other destination types
        return $"{FormatExpression(assign.Destination)} = {sourceExpression};";
    }

    private static string FormatCall(IrCall call)
    {
        var callExpression = $"Call({FormatExpression(call.Target)})";
        return call.Destination switch
        {
            null => $"{callExpression};",
            IrMemoryAddress address => $"Memory.Write64({FormatAddress(address)}, {callExpression});",
            _ => $"{FormatExpression(call.Destination)} = {callExpression};"
        };
    }

    private static string FormatExpression(IrExpression expression) =>
        expression switch
        {
            IrRegister register => FormatRegister(register.Name),
            IrTemporary temporary => FormatIdentifier(temporary.Name),
            IrConstant constant => $"0x{constant.Value:X}UL",
            IrBinaryExpression binary => $"({FormatExpression(binary.Left)} {binary.Operation} {FormatExpression(binary.Right)})",
            IrMemoryAddress address when address.Base?.Name == "rbp" => FormatAddress(address),
            IrMemoryAddress address => $"Memory.Read64({FormatAddress(address)})",
            _ => "0UL"
        };

    private static string FormatCompare(IrCompare compare)
    {
        var left = FormatExpression(compare.Left);
        var right = FormatExpression(compare.Right);
        return $"equal = ({left} == {right}); not_equal = !equal;";
    }

    private static string FormatAddress(IrExpression expression) =>
        expression is IrMemoryAddress address
            ? FormatAddress(address)
            : FormatExpression(expression);

    private static string FormatAddress(IrMemoryAddress address)
    {
        // Check for stack variables (e.g., [rbp - 8])
        if (address.Base?.Name == "rbp" && address.Index is null && address.Displacement < 0)
        {
            return $"local_{Math.Abs(address.Displacement):X}";
        }

        if (address.AbsoluteAddress is not null)
        {
            return $"0x{address.AbsoluteAddress.Value:X16}UL";
        }

        var parts = new List<string>();
        if (address.Base is not null)
        {
            parts.Add(FormatRegister(address.Base.Name));
        }

        if (address.Index is not null)
        {
            var index = FormatRegister(address.Index.Name);
            parts.Add(address.Scale == 1 ? index : $"({index} * {address.Scale}UL)");
        }

        if (address.Displacement != 0)
        {
            parts.Add(address.Displacement > 0
                ? $"0x{address.Displacement:X}UL"
                : $"-0x{Math.Abs(address.Displacement):X}UL");
        }

        return parts.Count == 0 ? "0UL" : string.Join(" + ", parts);
    }

    private static string FormatFlagCondition(string condition) =>
        condition switch
        {
            "eq" => "equal",
            "ne" => "not_equal",
            "gt" => "greater",
            "ge" => "greater || equal",
            "lt" => "less",
            "le" => "less || equal",
            "sign" => "sign",
            "not_sign" => "!sign",
            "parity" => "parity",
            "not_parity" => "!parity",
            _ => "false // unknown flag"
        };

    private static string FormatRegister(string registerName) =>
        FormatIdentifier(registerName.ToLowerInvariant());

    private static string FormatFunctionName(ulong address) => $"Sub_{address:X16}";

    private static string FormatBlockMethodName(ulong address) => $"Block_{address:X16}";

    private static string SanitizeBlockMethodName(string blockName) =>
        blockName.StartsWith("loc_", StringComparison.OrdinalIgnoreCase)
            ? $"Block_{blockName[4..]}"
            : FormatIdentifier(blockName);



    private static string FormatIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length + 1);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isValid = index == 0
                ? character == '_' || char.IsLetter(character)
                : character == '_' || char.IsLetterOrDigit(character);
            builder.Append(isValid ? character : '_');
        }

        if (builder.Length == 0 || char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    private static string EscapeComment(string value) =>
        value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static void AppendHelpers(StringBuilder builder, HashSet<string> localVariables)
    {
        builder.AppendLine("        // Registers");
        builder.AppendLine("        private static ulong rax, rbx, rcx, rdx, rsi, rdi, rbp, rsp;");
        builder.AppendLine("        private static ulong r8, r9, r10, r11, r12, r13, r14, r15;");
        builder.AppendLine();

        if (localVariables.Count > 0)
        {
            builder.AppendLine("        // Local variables");
            builder.AppendLine($"        private static ulong {string.Join(", ", localVariables)};");
            builder.AppendLine();
        }

        builder.AppendLine("        // Flags");
        builder.AppendLine("        private static bool equal, not_equal, greater, less, sign, parity;");
        builder.AppendLine();
        builder.AppendLine("        private static class Memory");
        builder.AppendLine("        {");
        builder.AppendLine("            public static ulong Read64(ulong address) => 0UL; // Placeholder");
        builder.AppendLine("            public static void Write64(ulong address, ulong value) { } // Placeholder");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static ulong Call(ulong address) => 0UL; // Placeholder");
    }
}