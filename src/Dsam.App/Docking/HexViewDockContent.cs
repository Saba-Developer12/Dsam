using System.Text;
using Dsam.App.Ui;
using WeifenLuo.WinFormsUI.Docking;

namespace Dsam.App.Docking;

public sealed class HexViewDockContent : DockContent
{
    private readonly TextBox _textBox;

    public HexViewDockContent()
    {
        Text = "Hex View";
        TabText = "Hex View";
        HideOnClose = true;

        _textBox = new TextBox
        {
            BackColor = DsamColors.Grid,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            ForeColor = DsamColors.Text,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false
        };
        Controls.Add(_textBox);
    }

    public void SetBytes(ulong baseAddress, byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 4);
        for (var offset = 0; offset < bytes.Length; offset += 16)
        {
            var count = Math.Min(16, bytes.Length - offset);
            builder.Append($"0x{baseAddress + (ulong)offset:X16}  ");

            for (var i = 0; i < 16; i++)
            {
                builder.Append(i < count ? $"{bytes[offset + i]:X2} " : "   ");
            }

            builder.Append(" ");
            for (var i = 0; i < count; i++)
            {
                var value = bytes[offset + i];
                builder.Append(value is >= 32 and <= 126 ? (char)value : '.');
            }

            builder.AppendLine();
        }

        _textBox.Text = builder.ToString();
    }

    public void Clear() => _textBox.Clear();
}