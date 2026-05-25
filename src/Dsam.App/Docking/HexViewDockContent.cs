using System.Drawing;
using Be.Windows.Forms;
using Dsam.App.Ui;
using WeifenLuo.WinFormsUI.Docking;

namespace Dsam.App.Docking;

public sealed class HexViewDockContent : DockContent
{
    private readonly HexBox _hexBox;

    public HexViewDockContent()
    {
        Text = "Hex View";
        TabText = "Hex View";
        HideOnClose = true;

        _hexBox = new HexBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9.75F),
            LineInfoVisible = true,
            StringViewVisible = true,
            UseFixedBytesPerLine = true,
            VScrollBarVisible = true,
            BytesPerLine = 16,

            // Dark theme colors
            BackColor = DsamColors.Grid,
            ForeColor = DsamColors.Text,
            InfoForeColor = DsamColors.MutedText,
            SelectionBackColor = DsamColors.Accent,
            SelectionForeColor = Color.White,
            ShadowSelectionColor = Color.FromArgb(100, 60, 188, 255)
        };
        Controls.Add(_hexBox);
    }

    public void SetBytes(ulong baseAddress, byte[] bytes)
    {
        var provider = new DynamicByteProvider(bytes ?? Array.Empty<byte>());
        _hexBox.ByteProvider = provider;
        _hexBox.LineInfoOffset = (long)baseAddress;
    }

    public void Clear() => _hexBox.ByteProvider = null;
}