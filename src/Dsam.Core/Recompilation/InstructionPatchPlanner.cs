using Iced.Intel;

namespace Dsam.Core.Recompilation;

public sealed class InstructionPatchPlanner
{
    private readonly int _bitness;

    public InstructionPatchPlanner(int bitness = 64)
    {
        _bitness = bitness;
    }

    public PatchPlanResult TryCreateInPlacePatch(InPlacePatchRequest request)
    {
        if (request.OriginalBytes.Length == 0)
        {
            return PatchPlanResult.Failure("Original byte span must not be empty.");
        }

        if (request.ReplacementInstructions.Count == 0)
        {
            return PatchPlanResult.Failure("At least one replacement instruction is required.");
        }

        using var stream = new MemoryStream();
        var writer = new StreamCodeWriter(stream);
        var block = new InstructionBlock(
            writer,
            request.ReplacementInstructions.ToArray(),
            request.Address);

        var options = BlockEncoderOptions.ReturnNewInstructionOffsets | BlockEncoderOptions.ReturnConstantOffsets;
        if (!BlockEncoder.TryEncode(_bitness, block, out var errorMessage, out _, options))
        {
            return PatchPlanResult.Failure(errorMessage);
        }

        var encoded = stream.ToArray();
        if (encoded.Length > request.OriginalBytes.Length)
        {
            return PatchPlanResult.Failure(
                $"Replacement encodes to {encoded.Length} bytes but the original span is only {request.OriginalBytes.Length} bytes. Use a code cave/trampoline relocation plan instead of an in-place patch.");
        }

        var patchedBytes = new byte[request.OriginalBytes.Length];
        encoded.CopyTo(patchedBytes, 0);
        patchedBytes.AsSpan(encoded.Length).Fill(request.PaddingByte);

        return PatchPlanResult.Success(new PatchPlan(
            request.Address,
            request.FileOffset,
            request.OriginalBytes,
            patchedBytes));
    }

    public byte[] EncodeSingleInstruction(Instruction instruction, ulong rip)
    {
        using var stream = new MemoryStream();
        var encoder = Encoder.Create(_bitness, new StreamCodeWriter(stream));
        if (!encoder.TryEncode(in instruction, rip, out _, out var errorMessage))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return stream.ToArray();
    }
}
