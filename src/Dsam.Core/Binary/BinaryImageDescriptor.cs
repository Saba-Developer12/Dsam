namespace Dsam.Core.Binary;

public sealed record BinaryImageDescriptor(
    string FilePath,
    ulong ImageBase,
    ulong EntryPoint,
    bool Is64Bit,
    IReadOnlyList<BinarySection> Sections)
{
    public BinaryAddressMap AddressMap { get; } = new(Sections);

    public BinarySection? EntryPointSection =>
        Sections.FirstOrDefault(section => section.ContainsVirtualAddress(EntryPoint));

    public BinarySection? FirstExecutableSection =>
        Sections.FirstOrDefault(section => section.IsExecutable || section.ContainsCode);
}
