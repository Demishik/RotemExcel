using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
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
    private readonly AreasConfig areas = AreasConfig.Load();
    private readonly Label areasStatus = new();
    private readonly TextBox ocrResult = new();

    public MainForm()
    {
        Text = "RotemExcel — калибровка и OCR";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 900;
        Height = 650;

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 260,
            Padding = new Padding(10),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        panel.Controls.Add(MakeButton("Область: вода Н-1", "water_h1"));
        panel.Controls.Add(MakeButton("Область: корм Н-1", "feed_h1"));
        panel.Controls.Add(MakeButton("Область: вода Н-2", "water_h2"));
        panel.Controls.Add(MakeButton("Область: корм Н-2", "feed_h2"));

        var recognizeBtn = new Button
        {
            Text = "Распознать всё",
            AutoSize = true,
            Margin = new Padding(0, 15, 0, 0),
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        };
        recognizeBtn.Click += (_, _) => RecognizeAll();
        panel.Controls.Add(recognizeBtn);

        areasStatus.Dock = DockStyle.Top;
        areasStatus.Height = 150;
        areasStatus.Padding = new Padding(15);
        areasStatus.Font = new Font("Consolas", 10);
        areasStatus.TextAlign = ContentAlignment.TopLeft;
        areasStatus.BorderStyle = BorderStyle.FixedSingle;

        ocrResult.Dock = DockStyle.Fill;
        ocrResult.Multiline = true;
        ocrResult.ReadOnly = true;
        ocrResult.ScrollBars = ScrollBars.Vertical;
        ocrResult.Font = new Font("Consolas", 12);

        Controls.Add(ocrResult);
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
            Margin = new Padding(0, 0, 0, 6)
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

        areasStatus.Text = string.Join(Environment.NewLine, lines);
    }

    private void RecognizeAll()
    {
        var sb = new StringBuilder();

        foreach (var key in new[] { "water_h1", "feed_h1", "water_h2", "feed_h2" })
        {
            string name = key switch
            {
                "water_h1" => "вода Н-1",
                "feed_h1"  => "корм Н-1",
                "water_h2" => "вода Н-2",
                "feed_h2"  => "корм Н-2",
                _ => key
            };

            var r = areas.Get(key);

            if (!r.IsSet)
            {
                sb.AppendLine($"{name}: (область не задана)");
                continue;
            }

            try
            {
                using var bmp = CaptureArea(r);
                string text = RunOcr(bmp);
                sb.AppendLine($"{name}: {text}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"{name}: ОШИБКА — {ex.Message}");
            }
        }

        ocrResult.Text = sb.ToString();
    }

    private static Bitmap CaptureArea(AreaRect r)
    {
        var bmp = new Bitmap(r.Width, r.Height);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(r.X, r.Y, 0, 0, new Size(r.Width, r.Height),
            CopyPixelOperation.SourceCopy);
        return bmp;
    }

    private static string RunOcr(Bitmap source)
    {
        int scale = 3;
        using var enlarged = new Bitmap(source.Width * scale, source.Height * scale);

        using (var g = Graphics.FromImage(enlarged))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, enlarged.Width, enlarged.Height);
        }

        using var gray = ToGrayAndThreshold(enlarged);

        string dataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

        using var engine = new TesseractEngine(dataPath, "eng", EngineMode.Default);
        engine.SetVariable("tessedit_char_whitelist", "0123456789,.");
        engine.SetVariable("classify_bln_numeric_mode", "1");

        using var pix = PixConverter.ToPix(gray);
        using var page = engine.Process(pix, PageSegMode.SingleLine);

        string raw = page.GetText();
        raw = raw.Replace("\r", " ").Replace("\n", " ").Trim();

        var sb = new StringBuilder();
        bool skip = false;

        foreach (char c in raw)
        {
            if (c == ',' || c == '.')
            {
                skip = true;
                continue;
            }

            if (skip)
            {
                if (c == ' ')
                {
                    skip = false;
                    sb.Append(' ');
                }
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    private static Bitmap ToGrayAndThreshold(Bitmap src)
    {
        var result = new Bitmap(src.Width, src.Height);

        int[] histogram = new int[256];
        int total = src.Width * src.Height;

        var grayValues = new byte[total];
        int idx = 0;

        for (int y = 0; y < src.Height; y++)
        {
            for (int x = 0; x < src.Width; x++)
            {
                var c = src.GetPixel(x, y);
                byte v = (byte)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
                grayValues[idx++] = v;
                histogram[v]++;
            }
        }

        double sum = 0;
        for (int i = 0; i < 256; i++) sum += i * histogram[i];

        double sumB = 0;
        int wB = 0;
        int threshold = 0;
        double maxVariance = 0;

        for (int t = 0; t < 256; t++)
        {
            wB += histogram[t];
            if (wB == 0) continue;

            int wF = total - wB;
            if (wF == 0) break;

            sumB += t * histogram[t];
            double mB = sumB / wB;
            double mF = (sum - sumB) / wF;
            double variance = (double)wB * wF * (mB - mF) * (mB - mF);

            if (variance > maxVariance)
            {
                maxVariance = variance;
                threshold = t;
            }
        }

        idx = 0;
        for (int y = 0; y < src.Height; y++)
        {
            for (int x = 0; x < src.Width; x++)
            {
                byte v = grayValues[idx++];
                result.SetPixel(x, y, v > threshold ? Color.Black : Color.White);
            }
        }

        return result;
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
