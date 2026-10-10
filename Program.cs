using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RotemExcel;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly AreasConfig areas = AreasConfig.Load();
    private readonly Label areasStatus = new();

    public MainForm()
    {
        Text = "RotemExcel — калибровка областей";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 700;
        Height = 400;

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 200,
            Padding = new Padding(10),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        panel.Controls.Add(MakeButton("Область: вода Н-1", "water_h1"));
        panel.Controls.Add(MakeButton("Область: корм Н-1", "feed_h1"));
        panel.Controls.Add(MakeButton("Область: вода Н-2", "water_h2"));
        panel.Controls.Add(MakeButton("Область: корм Н-2", "feed_h2"));

        areasStatus.Dock = DockStyle.Fill;
        areasStatus.Padding = new Padding(15);
        areasStatus.Font = new Font("Consolas", 11);
        areasStatus.TextAlign = ContentAlignment.TopLeft;

        Controls.Add(areasStatus);
        Controls.Add(panel);

        RefreshStatus();
    }

    private Button MakeButton(string text, string key)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        btn.Click += (_, _) => PickArea(key);
        return btn;
    }

    private void PickArea(string key)
    {
        using var selector = new SelectionForm();

        if (selector.ShowDialog(this) != DialogResult.OK)
            return;

        var r = selector.SelectedBounds;

        if (r.Width < 2 || r.Height < 2)
            return;

        areas.Set(key, new AreaRect
        {
            X = r.X,
            Y = r.Y,
            Width = r.Width,
            Height = r.Height
        });

        areas.Save();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var lines = new List<string>
        {
            "Сохранённые области (areas.json):",
            ""
        };

        foreach (var key in new[] { "water_h1", "feed_h1", "water_h2", "feed_h2" })
        {
            var r = areas.Get(key);
            string name = key switch
            {
                "water_h1" => "вода Н-1",
                "feed_h1"  => "корм Н-1",
                "water_h2" => "вода Н-2",
                "feed_h2"  => "корм Н-2",
                _ => key
            };

            lines.Add(r.IsSet
                ? $"{name}: {r}"
                : $"{name}: (не задана)");
        }

        lines.Add("");
        lines.Add("Нажми кнопку — выдели мышкой область на экране.");
        lines.Add("Esc — отменить выделение.");

        areasStatus.Text = string.Join(Environment.NewLine, lines);
    }
}

internal sealed class SelectionForm : Form
{
    private Point start;
    private Point finish;
    private bool dragging;

    public Rectangle SelectedBounds { get; private set; }

    public SelectionForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
        Opacity = 0.30;
        KeyPreview = true;

        MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            start = e.Location;
            finish = e.Location;
            dragging = true;
            Invalidate();
        };

        MouseMove += (_, e) =>
        {
            if (!dragging)
                return;

            finish = e.Location;
            Invalidate();
        };

        MouseUp += (_, e) =>
        {
            if (!dragging || e.Button != MouseButtons.Left)
                return;

            finish = e.Location;
            dragging = false;

            int left = Math.Min(start.X, finish.X);
            int top = Math.Min(start.Y, finish.Y);

            SelectedBounds = new Rectangle(
                left + Bounds.Left,
                top + Bounds.Top,
                Math.Abs(finish.X - start.X),
                Math.Abs(finish.Y - start.Y));

            DialogResult = SelectedBounds.Width > 1 &&
                           SelectedBounds.Height > 1
                ? DialogResult.OK
                : DialogResult.Cancel;

            Close();
        };

        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape)
                return;

            DialogResult = DialogResult.Cancel;
            Close();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (!dragging)
            return;

        int left = Math.Min(start.X, finish.X);
        int top = Math.Min(start.Y, finish.Y);
        int width = Math.Abs(finish.X - start.X);
        int height = Math.Abs(finish.Y - start.Y);

        using var pen = new Pen(Color.Lime, 3);
        e.Graphics.DrawRectangle(pen, left, top, width, height);
    }
}
