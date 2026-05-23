using Dsam.App.Docking;
using Dsam.App.Ui;
using Dsam.App.Workspace;
using Dsam.Core.Analysis.Decompilation;
using Dsam.Core.Binary;
using Dsam.Core.Disassembly;
using Dsam.Data.Sqlite;
using WeifenLuo.WinFormsUI.Docking;

namespace Dsam.App;

public sealed class MainForm : Form
{
    private readonly DockPanel _dockPanel = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new("Ready");
    private readonly ToolStripProgressBar _progressBar = new() { Visible = false, Style = ProgressBarStyle.Marquee };

    private readonly PortableExecutableLoader _peLoader = new();
    private readonly IDisassemblyService _disassemblyService = new IcedDisassemblyService();
    private readonly IDecompilerPipeline _decompilerPipeline = new DecompilerPipeline();

    private readonly DisassemblyDockContent _disassemblyView = new();
    private readonly PseudocodeDockContent _pseudocodeView = new();
    private readonly HexViewDockContent _hexView = new();
    private readonly RegistersDockContent _registersView = new();
    private readonly SectionsDockContent _sectionsView = new();
    private readonly XrefsDockContent _xrefsView = new();

    private DsamWorkspace? _workspace;
    private CancellationTokenSource? _loadCancellation;

    public MainForm()
    {
        Text = "Dsam";
        Width = 1400;
        Height = 900;
        MinimumSize = new Size(1000, 650);
        BackColor = DsamColors.Window;

        Controls.Add(CreateDockPanel());
        Controls.Add(CreateStatusStrip());
        Controls.Add(CreateMenu());

        MainMenuStrip = Controls.OfType<MenuStrip>().First();
        _disassemblyView.CurrentInstructionChanged += DisassemblyView_CurrentInstructionChanged;
        Shown += (_, _) => ShowDefaultLayout();
        FormClosing += (_, _) => DisposeWorkspace();
    }

    private MenuStrip CreateMenu()
    {
        var menu = new MenuStrip
        {
            BackColor = DsamColors.Header,
            ForeColor = DsamColors.Text
        };

        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add("Open Binary...", null, async (_, _) => await OpenBinaryAsync());
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("Exit", null, (_, _) => Close());

        var viewMenu = new ToolStripMenuItem("&View");
        viewMenu.DropDownItems.Add("Disassembly", null, (_, _) => ShowContent(_disassemblyView, DockState.Document));
        viewMenu.DropDownItems.Add("Pseudocode", null, (_, _) => ShowContent(_pseudocodeView, DockState.Document));
        viewMenu.DropDownItems.Add("Hex View", null, (_, _) => ShowContent(_hexView, DockState.DockBottom));
        viewMenu.DropDownItems.Add("Registers", null, (_, _) => ShowContent(_registersView, DockState.DockRight));
        viewMenu.DropDownItems.Add("Segments", null, (_, _) => ShowContent(_sectionsView, DockState.DockLeft));
        viewMenu.DropDownItems.Add("Xrefs", null, (_, _) => ShowContent(_xrefsView, DockState.DockRight));

        menu.Items.Add(fileMenu);
        menu.Items.Add(viewMenu);
        return menu;
    }

    private DockPanel CreateDockPanel()
    {
        _dockPanel.Dock = DockStyle.Fill;
        _dockPanel.DocumentStyle = DocumentStyle.DockingWindow;
        _dockPanel.Theme = new VS2015DarkTheme();
        return _dockPanel;
    }

    private StatusStrip CreateStatusStrip()
    {
        _statusStrip.BackColor = DsamColors.Header;
        _statusStrip.ForeColor = DsamColors.Text;
        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
        _statusStrip.Items.Add(_progressBar);
        return _statusStrip;
    }

    private void ShowDefaultLayout()
    {
        _disassemblyView.Show(_dockPanel, DockState.Document);
        _pseudocodeView.Show(_dockPanel, DockState.Document);
        _sectionsView.Show(_dockPanel, DockState.DockLeft);
        _hexView.Show(_dockPanel, DockState.DockBottom);
        _registersView.Show(_dockPanel, DockState.DockRight);
        _xrefsView.Show(_registersView.Pane, DockAlignment.Bottom, 0.45);
        _disassemblyView.Activate();
    }

    private static void ShowContent(DockContent content, DockState dockState)
    {
        if (content.DockPanel is { } panel)
        {
            content.Show(panel, dockState);
        }
    }

    private async Task OpenBinaryAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Windows binaries (*.exe;*.dll;*.sys)|*.exe;*.dll;*.sys|All files (*.*)|*.*",
            Title = "Open Binary"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();

        SetBusy($"Opening {Path.GetFileName(dialog.FileName)}...");
        try
        {
            DisposeWorkspace();

            var descriptor = _peLoader.Load(dialog.FileName);
            var binary = MemoryMappedBinaryImage.Open(dialog.FileName);
            var idbPath = Path.ChangeExtension(dialog.FileName, ".dsam.idb");
            var store = new SqliteAnalysisStore(idbPath);
            await store.InitializeAsync(_loadCancellation.Token);

            _workspace = new DsamWorkspace(descriptor, binary, store);
            _sectionsView.Bind(descriptor.Sections);
            _registersView.Reset();
            Text = $"Dsam - {Path.GetFileName(dialog.FileName)}";

            await DecodeInitialViewAsync(_workspace, _loadCancellation.Token);
            _statusLabel.Text = $"Loaded {Path.GetFileName(dialog.FileName)}";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Load cancelled";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Dsam", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "Open failed";
        }
        finally
        {
            SetBusy(null);
        }
    }

    private async Task DecodeInitialViewAsync(DsamWorkspace workspace, CancellationToken cancellationToken)
    {
        var section = workspace.Image.EntryPointSection ?? workspace.Image.FirstExecutableSection;
        if (section is null)
        {
            throw new InvalidDataException("No executable section was found.");
        }

        var startAddress = section.ContainsVirtualAddress(workspace.Image.EntryPoint)
            ? workspace.Image.EntryPoint
            : section.VirtualAddress;

        var request = new DisassemblyRequest(
            workspace.Binary,
            section,
            startAddress,
            workspace.Image.Is64Bit ? 64 : 32,
            MaxInstructions: 2048);

        var instructions = new List<DecodedInstruction>(capacity: 2048);
        await foreach (var instruction in _disassemblyService.DecodeAsync(request, cancellationToken))
        {
            instructions.Add(instruction);
        }

        _disassemblyView.Bind(instructions);
        UpdatePseudocode(startAddress, instructions);
        _xrefsView.Bind(instructions.SelectMany(instruction => instruction.Xrefs));
        await workspace.AnalysisStore.SaveInstructionsAsync(instructions, cancellationToken);

        if (section.TryVirtualAddressToFileOffset(startAddress, out var fileOffset))
        {
            var count = (int)Math.Min(512, workspace.Binary.Length - fileOffset);
            _hexView.SetBytes(startAddress, workspace.Binary.ReadBytes(fileOffset, count));
        }
    }

    private void UpdatePseudocode(ulong functionEntry, IReadOnlyList<DecodedInstruction> instructions)
    {
        if (instructions.Count == 0)
        {
            _pseudocodeView.Clear();
            return;
        }

        try
        {
            var result = _decompilerPipeline.Decompile(new DecompilationRequest(functionEntry, instructions));
            _pseudocodeView.SetText(result.CSharpPseudocode);
        }
        catch (Exception exception)
        {
            _pseudocodeView.SetText($"// Pseudocode generation failed: {exception.Message}");
        }
    }

    private void DisassemblyView_CurrentInstructionChanged(object? sender, DecodedInstruction instruction)
    {
        _hexView.SetBytes(instruction.Address, instruction.Bytes);
        _xrefsView.Bind(instruction.Xrefs);
    }

    private void SetBusy(string? text)
    {
        _progressBar.Visible = text is not null;
        if (text is not null)
        {
            _statusLabel.Text = text;
        }
    }

    private void DisposeWorkspace()
    {
        _workspace?.Dispose();
        _workspace = null;
    }
}
