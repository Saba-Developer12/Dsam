using Dsam.App.Ui;
using Dsam.Core.Binary;
using WeifenLuo.WinFormsUI.Docking;

namespace Dsam.App.Docking;

public sealed class SectionsDockContent : DockContent
{
    private readonly BindingSource _bindingSource = new();

    public SectionsDockContent()
    {
        Text = "Segments";
        TabText = "Segments";
        HideOnClose = true;

        var grid = GridFactory.CreateReadOnlyGrid();
        grid.AutoGenerateColumns = true;
        grid.DataSource = _bindingSource;
        Controls.Add(grid);
    }

    public void Bind(IEnumerable<BinarySection> sections)
    {
        _bindingSource.DataSource = sections.Select(section => new SectionRow(
            section.Name,
            $"0x{section.VirtualAddress:X16}",
            $"0x{section.EndVirtualAddress:X16}",
            $"0x{section.FileOffset:X}",
            $"0x{section.FileSize:X}",
            section.IsExecutable,
            section.ContainsCode)).ToList();
    }

    private sealed record SectionRow(
        string Name,
        string Start,
        string End,
        string FileOffset,
        string FileSize,
        bool Execute,
        bool Code);
}
