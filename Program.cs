
using System;
using System.Drawing;
using System.Drawing.Imaging;
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
    private readonly PictureBox preview = new();
    private readonly Label status = new();

    public MainForm()
    {
        Text = "RotemExcel - захват данных";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 850;
        Height = 600;

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 85,
            Padding = new Padding(10)
        };

        var capture = new Button
        {
            Text = "Выбрать область экрана",
            AutoSize = true,
            Height = 35
        };

        capture.Click += (_, _) => CaptureArea();

        status.Text = "Выбери область со строками воды и корма.";
        status.AutoSize = true;
        status.Padding = new Padding(10);

        top.Controls.Add(capture);
        top.Controls.Add(status);

        preview.Dock = DockStyle.Fill;
        preview.SizeMode = PictureBoxSizeMode.Zoom;
        preview.BorderStyle = BorderStyle.FixedSingle;

        Controls.Add(preview);
        Controls.Add(top);
    }

    private void CaptureArea()
    {
        using var selector = new SelectionForm();

        if (selector.ShowDialog(this) != DialogResult.OK ||
            selector.SelectedBounds.Width < 2 ||
            selector.SelectedBounds.Height < 2)
        {
            status.Text = "Захват отменён.";
            return;
        }

        var area = selector.SelectedBounds;
        var bitmap = new Bitmap(area.Width, area.Height);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                area.Left,
                area.Top,
                0,
                0,
                area.Size,
                CopyPixelOperation.SourceCopy);
        }

        var old = preview.Image;
        preview.Image = bitmap;
        old?.Dispose();

        status.Text =
            $"Область: X={area.X}, Y={area.Y}, " +
            $"ширина={area.Width}, высота={area.Height}";
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

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        start = e.Location;
        finish = e.Location;
        dragging = true;
        Invalidate();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!dragging)
            return;

        finish = e.Location;
        Invalidate();
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!dragging || e.Button != MouseButtons.Left)
            return;

        finish = e.Location;
        dragging = false;

        int left = Math.Min(start.X, finish.X);
        int top = Math.Min(start.Y, finish.Y);
        int width = Math.Abs(finish.X - start.X);
        int height = Math.Abs(finish.Y - start.Y);

        SelectedBounds = new Rectangle(
            left + Bounds.Left,
            top + Bounds.Top,
            width,
            height);

        if (width > 1 && height > 1)
            DialogResult = DialogResult.OK;
        else
            DialogResult = DialogResult.Cancel;

        Close();
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
