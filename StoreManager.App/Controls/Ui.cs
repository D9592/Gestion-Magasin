namespace StoreManager.App.Controls;

internal static class Ui
{
    public static readonly Color Canvas = Color.FromArgb(245, 247, 250);
    public static readonly Color Ink = Color.FromArgb(31, 41, 55);
    public static readonly Color Muted = Color.FromArgb(107, 114, 128);
    public static readonly Color Blue = Color.FromArgb(37, 99, 235);
    public static readonly Color Border = Color.FromArgb(229, 233, 240);
    public static readonly Color Red = Color.FromArgb(185, 28, 28);
    public static readonly Color Orange = Color.FromArgb(217, 119, 6);
    public static readonly Color Green = Color.FromArgb(5, 150, 105);

    public static Label Label(string text, int size = 10, bool bold = false, Color? color = null) => new()
    {
        Text = text, AutoSize = true, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color ?? Ink, Margin = new Padding(0, 0, 0, 6)
    };

    public static Panel Card(Control content, int padding = 18)
    {
        var panel = new Panel { BackColor = Color.White, Padding = new Padding(padding), Margin = new Padding(0, 0, 14, 14), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        panel.Controls.Add(content);
        content.Dock = DockStyle.Fill;
        return panel;
    }

    public static Button Button(string text, bool primary = false) => new()
    {
        Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = primary ? Blue : Color.White,
        ForeColor = primary ? Color.White : Ink, Font = new Font("Segoe UI", 9, FontStyle.Bold),
        Padding = new Padding(12, 7, 12, 7), Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand
    };

    public static TableLayoutPanel Grid(int columns = 1)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = columns, RowCount = 0, BackColor = Color.Transparent, Padding = new Padding(0) };
        for (var i = 0; i < columns; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        return grid;
    }
}
