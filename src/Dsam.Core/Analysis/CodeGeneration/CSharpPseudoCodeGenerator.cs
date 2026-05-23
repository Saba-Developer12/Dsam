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

        builder.AppendLine("// Dsam decompiler pseudocode.");
        builder.AppendLine("// Generated as valid C#-shaped code for editor readability.");
        AppendAnalysisComments(builder, dominators, patterns);
        builder.AppendLine();
        builder.AppendLine("namespace Dsam.Decompiled");
        builder.AppendLine("{");
        builder.AppendLine("    public static class DecompiledProgram");
        builder.AppendLine("    {");
        builder.AppendLine($"        public static ulong {entryMethodName}()");
        builder.AppendLine("        {");
        builder.AppendLine("            var cpu = new CpuState();");
        builder.AppendLine("            IMemory memory = new NullMemory();");
        builder.AppendLine($"            return {FormatBlockMethodName(function.EntryAddress)}(cpu, memory);");
        builder.AppendLine("        }");
        builder.AppendLine();

        foreach (var block in function.Blocks)
        {
            AppendBlockMethod(builder, block);
        }

        AppendHelpers(builder);
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
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
        builder.AppendLine($"        private static ulong {FormatBlockMethodName(block.SourceAddress)}(CpuState cpu, IMemory memory)");
        builder.AppendLine("        {");

        foreach (var statement in block.Statements)
        {
            builder.AppendLine($"            {FormatStatement(statement)}");
        }

        if (block.Terminator is not null)
        {
            foreach (var line in FormatTerminator(block.Terminator))
            {
                builder.AppendLine($"            {line}");
            }
        }
        else
        {
            builder.AppendLine("            return cpu.Rax;");
        }

        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static string FormatStatement(IrStatement statement) =>
        statement switch
        {
            IrAssemblyComment comment => $"// 0x{comment.Address:X16}: {comment.Text}",
            IrAssign assign => FormatAssign(assign),
            IrStore store => store.SizeInBytes == 8
                ? $"memory.Write64({FormatAddress(store.Address)}, {FormatExpression(store.Value)});"
                : $"// TODO: write {store.SizeInBytes} bytes to {FormatAddress(store.Address)} = {FormatExpression(store.Value)};",
            IrCall call => FormatCall(call),
            IrCompare compare => FormatCompare(compare),
            IrComment comment => $"// 0x{comment.Address:X16}: {comment.Text}",
            _ => $"// TODO: unsupported IR statement: {statement.GetType().Name}"
        };

    private static IEnumerable<string> FormatTerminator(IrTerminator terminator)
    {
        switch (terminator)
        {
            case IrBranch branch:
                yield return $"return {SanitizeBlockMethodName(branch.TargetBlock)}(cpu, memory);";
                break;

            case IrConditionalBranch branch:
                yield return $"if ({FormatFlagCondition(branch.Condition)})";
                yield return "{";
                yield return $"    return {SanitizeBlockMethodName(branch.TrueTargetBlock)}(cpu, memory);";
                yield return "}";
                yield return $"return {SanitizeBlockMethodName(branch.FalseTargetBlock)}(cpu, memory);";
                break;

            case IrIndirectBranch branch:
                yield return $"// TODO: indirect branch target {FormatExpression(branch.Target)}";
                yield return "return cpu.Rax;";
                break;

            case IrReturn { Value: null }:
                yield return "return cpu.Rax;";
                break;

            case IrReturn { Value: { } value }:
                yield return $"return {FormatExpression(value)};";
                break;

            case IrUnresolvedTerminator unresolved:
                yield return $"// TODO: unresolved terminator: {EscapeComment(unresolved.Reason)}";
                yield return "return cpu.Rax;";
                break;

            default:
                yield return $"// TODO: unsupported terminator: {terminator.GetType().Name}";
                yield return "return cpu.Rax;";
                break;
        }
    }

    private static string FormatAssign(IrAssign assign) =>
        assign.Destination is IrMemoryAddress address
            ? $"memory.Write64({FormatAddress(address)}, {FormatExpression(assign.Source)});"
            : $"{FormatExpression(assign.Destination)} = {FormatExpression(assign.Source)};";

    private static string FormatCall(IrCall call)
    {
        var callExpression = $"Cpu.Call({FormatExpression(call.Target)})";
        return call.Destination switch
        {
            null => $"{callExpression};",
            IrMemoryAddress address => $"memory.Write64({FormatAddress(address)}, {callExpression});",
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
            IrMemoryAddress address => $"memory.Read64({FormatAddress(address)})",
            _ => "0UL"
        };

    private static string FormatCompare(IrCompare compare)
    {
        var left = FormatExpression(compare.Left);
        var right = FormatExpression(compare.Right);
        return compare.Operation == "test"
            ? $"cpu.Flags.Equal = ({left} == {right}); cpu.Flags.NotEqual = !cpu.Flags.Equal;"
            : $"cpu.Flags.Equal = ({left} == {right}); cpu.Flags.NotEqual = !cpu.Flags.Equal;";
    }

    private static string FormatAddress(IrExpression expression) =>
        expression is IrMemoryAddress address
            ? FormatAddress(address)
            : FormatExpression(expression);

    private static string FormatAddress(IrMemoryAddress address)
    {
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
            parts.Add(address.Scale == 1 ? index : $"{index} * {address.Scale}UL");
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
            "eq" => "cpu.Flags.Equal",
            "ne" => "cpu.Flags.NotEqual",
            "gt" => "cpu.Flags.Greater",
            "ge" => "cpu.Flags.GreaterOrEqual",
            "lt" => "cpu.Flags.Less",
            "le" => "cpu.Flags.LessOrEqual",
            "ugt" => "cpu.Flags.UnsignedGreater",
            "uge" => "cpu.Flags.UnsignedGreaterOrEqual",
            "ult" => "cpu.Flags.UnsignedLess",
            "ule" => "cpu.Flags.UnsignedLessOrEqual",
            "sign" => "cpu.Flags.Sign",
            "not_sign" => "!cpu.Flags.Sign",
            "parity" => "cpu.Flags.Parity",
            "not_parity" => "!cpu.Flags.Parity",
            _ => "cpu.Flags.Unknown"
        };

    private static string FormatRegister(string registerName) =>
        registerName.ToLowerInvariant() switch
        {
            "rax" or "eax" or "ax" or "al" => "cpu.Rax",
            "rbx" or "ebx" or "bx" or "bl" => "cpu.Rbx",
            "rcx" or "ecx" or "cx" or "cl" => "cpu.Rcx",
            "rdx" or "edx" or "dx" or "dl" => "cpu.Rdx",
            "rsi" or "esi" or "si" or "sil" => "cpu.Rsi",
            "rdi" or "edi" or "di" or "dil" => "cpu.Rdi",
            "rbp" or "ebp" or "bp" or "bpl" => "cpu.BasePointer",
            "rsp" or "esp" or "sp" or "spl" => "cpu.StackPointer",
            "r8" or "r8d" or "r8w" or "r8b" => "cpu.R8",
            "r9" or "r9d" or "r9w" or "r9b" => "cpu.R9",
            "r10" or "r10d" or "r10w" or "r10b" => "cpu.R10",
            "r11" or "r11d" or "r11w" or "r11b" => "cpu.R11",
            "r12" or "r12d" or "r12w" or "r12b" => "cpu.R12",
            "r13" or "r13d" or "r13w" or "r13b" => "cpu.R13",
            "r14" or "r14d" or "r14w" or "r14b" => "cpu.R14",
            "r15" or "r15d" or "r15w" or "r15b" => "cpu.R15",
            _ => $"cpu.{FormatIdentifier(registerName)}"
        };

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

    private static void AppendHelpers(StringBuilder builder)
    {
        builder.AppendLine("        private sealed class CpuState");
        builder.AppendLine("        {");
        builder.AppendLine("            public ulong Rax;");
        builder.AppendLine("            public ulong Rbx;");
        builder.AppendLine("            public ulong Rcx;");
        builder.AppendLine("            public ulong Rdx;");
        builder.AppendLine("            public ulong Rsi;");
        builder.AppendLine("            public ulong Rdi;");
        builder.AppendLine("            public ulong BasePointer;");
        builder.AppendLine("            public ulong StackPointer;");
        builder.AppendLine("            public ulong R8;");
        builder.AppendLine("            public ulong R9;");
        builder.AppendLine("            public ulong R10;");
        builder.AppendLine("            public ulong R11;");
        builder.AppendLine("            public ulong R12;");
        builder.AppendLine("            public ulong R13;");
        builder.AppendLine("            public ulong R14;");
        builder.AppendLine("            public ulong R15;");
        builder.AppendLine("            public CpuFlags Flags = new CpuFlags();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private sealed class CpuFlags");
        builder.AppendLine("        {");
        builder.AppendLine("            public bool Equal;");
        builder.AppendLine("            public bool NotEqual;");
        builder.AppendLine("            public bool Greater;");
        builder.AppendLine("            public bool GreaterOrEqual;");
        builder.AppendLine("            public bool Less;");
        builder.AppendLine("            public bool LessOrEqual;");
        builder.AppendLine("            public bool UnsignedGreater;");
        builder.AppendLine("            public bool UnsignedGreaterOrEqual;");
        builder.AppendLine("            public bool UnsignedLess;");
        builder.AppendLine("            public bool UnsignedLessOrEqual;");
        builder.AppendLine("            public bool Sign;");
        builder.AppendLine("            public bool Parity;");
        builder.AppendLine("            public bool Unknown;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private interface IMemory");
        builder.AppendLine("        {");
        builder.AppendLine("            ulong Read64(ulong address);");
        builder.AppendLine("            void Write64(ulong address, ulong value);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private sealed class NullMemory : IMemory");
        builder.AppendLine("        {");
        builder.AppendLine("            public ulong Read64(ulong address) => 0UL;");
        builder.AppendLine("            public void Write64(ulong address, ulong value)");
        builder.AppendLine("            {");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static class Cpu");
        builder.AppendLine("        {");
        builder.AppendLine("            public static ulong Call(ulong address) => 0UL;");
        builder.AppendLine("        }");
    }
}
