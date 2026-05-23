namespace Dsam.App.Ui;

internal static class GridFactory
{
    public static DataGridView CreateReadOnlyGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = DsamColors.Grid,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            Dock = DockStyle.Fill,
            EnableHeadersVisualStyles = false,
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = DsamColors.Header;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = DsamColors.Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = DsamColors.Header;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = DsamColors.Text;
        grid.DefaultCellStyle.BackColor = DsamColors.Grid;
        grid.DefaultCellStyle.ForeColor = DsamColors.Text;
        grid.DefaultCellStyle.SelectionBackColor = DsamColors.Accent;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(33, 33, 33);
        grid.GridColor = DsamColors.Border;

        return grid;
    }
}
