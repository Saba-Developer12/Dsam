using Dsam.App.Ui;
using Dsam.Core.Disassembly;
using WeifenLuo.WinFormsUI.Docking;

namespace Dsam.App.Docking;

public sealed class XrefsDockContent : DockContent
{
    private readonly BindingSource _bindingSource = new();

    public XrefsDockContent()
    {
        Text = "Xrefs";
        TabText = "Xrefs";
        HideOnClose = true;

        var grid = GridFactory.CreateReadOnlyGrid();
        grid.AutoGenerateColumns = true;
        grid.DataSource = _bindingSource;
        Controls.Add(grid);
    }

    public void Bind(IEnumerable<Xref> xrefs)
    {
        _bindingSource.DataSource = xrefs.Select(xref => new XrefRow(
            $"0x{xref.FromAddress:X16}",
            $"0x{xref.ToAddress:X16}",
            xref.Kind.ToString(),
            xref.OperandIndex)).ToList();
    }

    private sealed record XrefRow(string From, string To, string Kind, int Operand);
}
