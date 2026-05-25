using Dsam.App.Ui;
using WeifenLuo.WinFormsUI.Docking;

namespace Dsam.App.Docking;

public sealed class PseudocodeDockContent : DockContent
{
    private readonly TextBox _textBox;

    public PseudocodeDockContent()
    {
        Text = "Pseudocode";
        TabText = "Pseudocode";
        HideOnClose = true;

        _textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false
        };

        _textBox.BackColor = DsamColors.Grid;
        _textBox.ForeColor = DsamColors.Text;
        Controls.Add(_textBox);
        SetText("// Open a binary to generate C# pseudocode.");
    }

    public void SetText(string text) => _textBox.Text = text;

    public void Clear() => SetText("// No pseudocode available.");
}