using System.Runtime.CompilerServices;
using Dsam.Core.Binary;
using Iced.Intel;

namespace Dsam.Core.Disassembly;

public sealed class IcedDisassemblyService : IDisassemblyService
{
    public async IAsyncEnumerable<DecodedInstruction> DecodeAsync(
        DisassemblyRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!request.Section.TryVirtualAddressToFileOffset(request.StartAddress, out var startOffset))
        {
            throw new ArgumentOutOfRangeException(nameof(request.StartAddress), "Start address is outside the selected section.");
        }

        var endAddress = Math.Min(request.EndAddress ?? request.Section.EndVirtualAddress, request.Section.EndVirtualAddress);
        if (endAddress <= request.StartAddress)
        {
            yield break;
        }

        var bytesToDecode = checked((long)(endAddress - request.StartAddress));
        var codeReader = new MemoryMappedCodeReader(request.Image, startOffset, bytesToDecode);
        var decoder = Decoder.Create(request.Bitness, codeReader, request.StartAddress, DecoderOptions.None);
        var formatter = new NasmFormatter();
        var formatterOutput = new StringOutput();

        for (var index = 0; index < request.MaxInstructions && decoder.IP < endAddress; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            decoder.Decode(out var instruction);
            if (instruction.Length == 0)
            {
                yield break;
            }

            formatterOutput.Reset();
            formatter.Format(in instruction, formatterOutput);

            var address = instruction.IP;
            var fileOffset = checked(startOffset + (long)(address - request.StartAddress));
            var bytes = request.Image.ReadBytes(fileOffset, instruction.Length);
            var xrefs = XrefExtractor.Extract(instruction).ToArray();

            yield return new DecodedInstruction(
                address,
                fileOffset,
                instruction.Length,
                instruction.Mnemonic.ToString(),
                formatterOutput.ToStringAndReset(),
                bytes,
                instruction,
                xrefs);

            if (request.StopAtControlFlowBarrier && IsControlFlowBarrier(instruction.FlowControl))
            {
                yield break;
            }

            if ((index & 0x7F) == 0x7F)
            {
                await Task.Yield();
            }
        }
    }

    private static bool IsControlFlowBarrier(FlowControl flowControl) =>
        flowControl is FlowControl.UnconditionalBranch
            or FlowControl.IndirectBranch
            or FlowControl.Return
            or FlowControl.Exception
            or FlowControl.Interrupt;
}
