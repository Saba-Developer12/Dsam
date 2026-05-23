using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Dsam.Core.Disassembly;

public interface IDisassemblyService
{
    IAsyncEnumerable<DecodedInstruction> DecodeAsync(
        DisassemblyRequest request,
        CancellationToken cancellationToken = default);
}
