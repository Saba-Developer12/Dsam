using System.Reflection.PortableExecutable;

namespace Dsam.Core.Binary;

public sealed class PortableExecutableLoader
{
    public BinaryImageDescriptor Load(string filePath)
    {
        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);

        var headers = reader.PEHeaders;
        var peHeader = headers.PEHeader ?? throw new InvalidDataException("The file does not contain a PE header.");
        var is64Bit = peHeader.Magic == PEMagic.PE32Plus;
        var imageBase = peHeader.ImageBase;
        var entryPoint = imageBase + (uint)peHeader.AddressOfEntryPoint;

        var sections = headers.SectionHeaders
            .Select(section => new BinarySection(
                section.Name,
                imageBase + (uint)section.VirtualAddress,
                (uint)section.VirtualSize,
                section.PointerToRawData,
                section.SizeOfRawData,
                section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute),
                section.SectionCharacteristics.HasFlag(SectionCharacteristics.ContainsCode)))
            .ToArray();

        return new BinaryImageDescriptor(
            Path.GetFullPath(filePath),
            imageBase,
            entryPoint,
            is64Bit,
            sections);
    }
}
