
using System;
using System.Drawing;
using System.Windows.Forms;
using Tesseract;

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
    private readonly TextBox result = new();
    private Bitmap? capturedImage;

    public MainForm()
    {
        Text = "RotemExcel - распознавание Rotem";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 900;
        Height = 700;

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(5)
        };

        var captureButton = new Button
        {
            Text = "Выбрать область экрана",
            AutoSize = true
        };
        captureButton.Click += (_, _) => CaptureArea();

        var ocrButton = new Button
        {
            Text = "Распознать цифры",
            AutoSize = true
        };
        ocrButton.Click += (_, _) => RecognizeNumbers();

        toolbar.Controls.Add(captureButton);
        toolbar.Controls.Add(ocrButton);

        status.Text = "1. Выбери область. 2. Нажми «Распознать цифры».";
        status.Dock = DockStyle.Top;
        status.Height = 35;
        status.TextAlign = ContentAlignment.MiddleLeft;

        preview.Dock = DockStyle.Fill;
        preview.SizeMode = PictureBoxSizeMode.Zoom;
        preview.BorderStyle = BorderStyle.FixedSingle;

        result.Dock = DockStyle.Bottom;
        result.Height = 115;
        result.Multiline = true;
        result.ReadOnly = true;
        result.ScrollBars = ScrollBars.Vertical;
        result.Font = new Font("Consolas", 12);

        Controls.Add(preview);
        Controls.Add(result);
        Controls.Add(status);
        Controls.Add(toolbar);
    }

    private void CaptureArea()
    {
        using var selector = new SelectionForm();

        if (selector.ShowDialog(this) != DialogResult.OK)
            return;

        var area = selector.SelectedBounds;
        if (area.Width < 2 || area.Height < 2)
            return;

        var bitmap = new Bitmap(area.Width, area.Height);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                area.Left, area.Top, 0, 0, area.Size,
                CopyPixelOperation.SourceCopy);
        }

        var old = capturedImage;
        capturedImage = bitmap;
        preview.Image = capturedImage;
        old?.Dispose();

        result.Clear();
        status.Text = $"Снимок получен: {area.Width} × {area.Height}";
    }

    private void RecognizeNumbers()
    {
        if (capturedImage is null)
        {
            MessageBox.Show(
                this,
                "Сначала выбери область экрана.",
                "RotemExcel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            status.Text = "Распознавание...";
            result.Clear();
            Application.DoEvents();

            string dataPath = System.IO.Path.Combine(
                AppContext.BaseDirectory, "tessdata");

            using var engine = new TesseractEngine(
                dataPath, "eng", EngineMode.Default);

            engine.SetVariable(
                "tessedit_char_whitelist",
                "0123456789.,-");

            using var pix = PixConverter.ToPix(capturedImage);
            using var page = engine.Process(pix, PageSegMode.Auto);

            string text = page.GetText();

            result.Text = string.IsNullOrWhiteSpace(text)
                ? "Цифры не распознаны. Попробуй выделить область крупнее."
                : text.Trim();

            status.Text = "Распознавание завершено. Проверь результат.";
        }
        catch (Exception ex)
        {
            status.Text = "Не удалось выполнить распознавание.";
            result.Text = ex.ToString();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        preview.Image = null;
        capturedImage?.Dispose();
        base.OnFormClosed(e);
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
