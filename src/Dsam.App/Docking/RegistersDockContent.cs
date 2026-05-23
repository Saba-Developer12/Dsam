using Dsam.App.Ui;
using WeifenLuo.WinFormsUI.Docking;

namespace Dsam.App.Docking;

public sealed class RegistersDockContent : DockContent
{
    private static readonly string[] RegisterNames =
    [
        "RAX", "RBX", "RCX", "RDX", "RSI", "RDI", "RBP", "RSP",
        "R8", "R9", "R10", "R11", "R12", "R13", "R14", "R15",
        "RIP", "RFLAGS"
    ];

    private readonly ListView _listView;

    public RegistersDockContent()
    {
        Text = "Registers";
        TabText = "Registers";
        HideOnClose = true;

        _listView = new ListView
        {
            BackColor = DsamColors.Grid,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            ForeColor = DsamColors.Text,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            View = View.Details
        };
        _listView.Columns.Add("Register", 80);
        _listView.Columns.Add("Value", 170);
        Controls.Add(_listView);

        Reset();
    }

    public void Reset()
    {
        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (var register in RegisterNames)
        {
            _listView.Items.Add(new ListViewItem([register, "unknown"]));
        }
        _listView.EndUpdate();
    }
}
