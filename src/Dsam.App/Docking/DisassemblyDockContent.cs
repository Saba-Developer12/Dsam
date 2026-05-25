using Dsam.App.Ui;
using Dsam.Core.Disassembly;
using WeifenLuo.WinFormsUI.Docking;

namespace Dsam.App.Docking;

public sealed class DisassemblyDockContent : DockContent
{
    private readonly DataGridView _grid;
    private readonly BindingSource _bindingSource = new();
    private IReadOnlyList<DecodedInstruction> _instructions = Array.Empty<DecodedInstruction>();

    public DisassemblyDockContent()
    {
        Text = "Disassembly";
        TabText = "Disassembly";
        HideOnClose = true;

        _grid = GridFactory.CreateReadOnlyGrid();
        _grid.AutoGenerateColumns = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(InstructionRow.Address),
            HeaderText = "Address",
            FillWeight = 20
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(InstructionRow.Bytes),
            HeaderText = "Bytes",
            FillWeight = 25
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(InstructionRow.Text),
            HeaderText = "Instruction",
            FillWeight = 55
        });
        _grid.DataSource = _bindingSource;
        _grid.CellFormatting += (_, e) =>
        {
            e.CellStyle.Font = new Font("Consolas", 9F);
        };
        _grid.SelectionChanged += (_, _) => RaiseCurrentInstructionChanged();
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = DsamColors.Header;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = DsamColors.Text;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        Controls.Add(_grid);
    }

    public event EventHandler<DecodedInstruction>? CurrentInstructionChanged;

    public void Bind(IReadOnlyList<DecodedInstruction> instructions)
    {
        _instructions = instructions;
        _bindingSource.DataSource = instructions.Select(InstructionRow.From).ToList();
        if (_grid.Rows.Count > 0)
        {
            _grid.Rows[0].Selected = true;
        }
    }

    private void RaiseCurrentInstructionChanged()
    {
        if (_grid.CurrentRow?.Index is not int index || index < 0 || index >= _instructions.Count)
        {
            return;
        }

        CurrentInstructionChanged?.Invoke(this, _instructions[index]);
    }

    private sealed record InstructionRow(string Address, string Bytes, string Text)
    {
        public static InstructionRow From(DecodedInstruction instruction) =>
            new(
                $"0x{instruction.Address:X16}",
                BitConverter.ToString(instruction.Bytes).Replace("-", " "),
                instruction.Text);
    }
}