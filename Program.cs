using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
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
            Height = 380,
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

        var diagBtn = new Button
        {
            Text = "Диагностика",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        };
        diagBtn.Click += (_, _) => RunDiagnostics();
        panel.Controls.Add(diagBtn);

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
                var numbers = RecognizeRow(bmp);

                string marker = numbers.Count == 8
                    ? ""
                    : $"  [найдено {numbers.Count} вместо 8]";

                sb.AppendLine($"{name}: {string.Join(" ", numbers)}{marker}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"{name}: ОШИБКА — {ex.Message}");
            }
        }

        ocrResult.Text = sb.ToString();
    }

    private void RunDiagnostics()
    {
        var results = new List<(string name, Bitmap bmp, List<int> edges, string numbers)>();

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
                results.Add((name, new Bitmap(100, 30), new List<int>(), "(не задана)"));
                continue;
            }

            var bmp = CaptureArea(r);
            var (numbers, edges) = RecognizeRowWithEdges(bmp);
            results.Add((name, bmp, edges, string.Join(" ", numbers)));
        }

        using var diag = new DiagnosticsForm(results);
        diag.ShowDialog(this);

        ocrResult.Text =
            "ДИАГНОСТИКА завершена.\r\n\r\n" +
            string.Join("\r\n", results.Select(r => $"{r.name}: {r.numbers}"));
    }

    private static Bitmap CaptureArea(AreaRect r)
    {
        var bmp = new Bitmap(r.Width, r.Height);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(r.X, r.Y, 0, 0, new Size(r.Width, r.Height),
            CopyPixelOperation.SourceCopy);
        return bmp;
    }

    private static List<string> RecognizeRow(Bitmap row)
    {
        var (numbers, _) = RecognizeRowWithEdges(row);
        return numbers;
    }

    /// <summary>
    /// Делит строку на 8 равных частей (столбцы в Rotem одинаковой ширины)
    /// и распознаёт каждое число отдельно.
    /// </summary>
    private static (List<string> numbers, List<int> edges) RecognizeRowWithEdges(Bitmap row)
    {
        const int cells = 8;

        int w = row.Width;
        int h = row.Height;

        // Оставляем по 3 пикселя слева и справа, чтобы убрать рамку области
        int margin = 3;
        int usableWidth = w - margin * 2;

        var results = new List<string>();
        var edges = new List<int>();

        if (usableWidth <= 0)
            return (results, edges);

        string dataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

        using var engine = new TesseractEngine(dataPath, "eng", EngineMode.Default);
        engine.SetVariable("tessedit_char_whitelist", "0123456789,.");
        engine.SetVariable("classify_bln_numeric_mode", "1");

        for (int i = 0; i < cells; i++)
        {
            int cellX = margin + (int)Math.Round((double)usableWidth * i / cells);
            int cellW = (int)Math.Round((double)usableWidth / cells);

            if (cellW < 4)
            {
                results.Add("?");
                edges.Add(cellX);
                edges.Add(cellX + cellW);
                continue;
            }

            edges.Add(cellX);
            edges.Add(cellX + cellW);

            using var cell = new Bitmap(cellW, h);
            using (var g = Graphics.FromImage(cell))
            {
                g.DrawImage(row,
                    new Rectangle(0, 0, cellW, h),
                    new Rectangle(cellX, 0, cellW, h),
                    GraphicsUnit.Pixel);
            }

            string num = RecognizeCell(engine, cell);
            results.Add(string.IsNullOrEmpty(num) ? "?" : num);
        }

        return (results, edges);
    }

    private static string RecognizeCell(TesseractEngine engine, Bitmap cell)
    {
        const int scale = 4;

        using var enlarged = new Bitmap(cell.Width * scale, cell.Height * scale);
        using (var g = Graphics.FromImage(enlarged))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(cell, 0, 0, enlarged.Width, enlarged.Height);
        }

        using var bw = ToBlackOnWhite(enlarged);

        using var pix = PixConverter.ToPix(bw);
        using var page = engine.Process(pix, PageSegMode.SingleWord);

        string raw = page.GetText();
        raw = raw.Replace("\r", " ").Replace("\n", " ").Trim();

        var digits = new StringBuilder();

        foreach (char c in raw)
        {
            if (c == ',' || c == '.')
                break;

            if (char.IsDigit(c))
                digits.Append(c);
        }

        return digits.ToString();
    }

    private static Bitmap ToBlackOnWhite(Bitmap src)
    {
        var result = new Bitmap(src.Width, src.Height);

        for (int y = 0; y < src.Height; y++)
        {
            for (int x = 0; x < src.Width; x++)
            {
                var c = src.GetPixel(x, y);
                int lum = (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
                result.SetPixel(x, y, lum < 128 ? Color.Black : Color.White);
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
            if (e.Button != MouseButtons.Left) return;
            start = e.Location;
            finish = e.Location;
            dragging = true;
            Invalidate();
        };

        MouseMove += (_, e) =>
        {
            if (!dragging) return;
            finish = e.Location;
            Invalidate();
        };

        MouseUp += (_, e) =>
        {
            if (!dragging || e.Button != MouseButtons.Left) return;

            finish = e.Location;
            dragging = false;

            int left = Math.Min(start.X, finish.X);
            int top = Math.Min(start.Y, finish.Y);

            SelectedBounds = new Rectangle(
                left + Bounds.Left,
                top + Bounds.Top,
                Math.Abs(finish.X - start.X),
                Math.Abs(finish.Y - start.Y));

            DialogResult = SelectedBounds.Width > 1 && SelectedBounds.Height > 1
                ? DialogResult.OK
                : DialogResult.Cancel;

            Close();
        };

        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            DialogResult = DialogResult.Cancel;
            Close();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (!dragging) return;

        int left = Math.Min(start.X, finish.X);
        int top = Math.Min(start.Y, finish.Y);
        int width = Math.Abs(finish.X - start.X);
        int height = Math.Abs(finish.Y - start.Y);

        using var pen = new Pen(Color.Lime, 3);
        e.Graphics.DrawRectangle(pen, left, top, width, height);
    }
}

internal sealed class DiagnosticsForm : Form
{
    public DiagnosticsForm(List<(string name, Bitmap bmp, List<int> edges, string numbers)> data)
    {
        Text = "Диагностика — границы блоков";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1400;
        Height = 900;
        KeyPreview = true;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10)
        };

        for (int i = 0; i < 4; i++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        }

        foreach (var item in data)
        {
            var gb = new GroupBox
            {
                Text = $"{item.name}  →  {item.numbers}",
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 11, FontStyle.Bold)
            };

            var pb = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = DrawEdges(item.bmp, item.edges)
            };

            gb.Controls.Add(pb);
            panel.Controls.Add(gb);
        }

        Controls.Add(panel);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
        };
    }

    private static Bitmap DrawEdges(Bitmap src, List<int> edges)
    {
        int scale = 3;
        var big = new Bitmap(src.Width * scale, src.Height * scale);

        using (var g = Graphics.FromImage(big))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(src, 0, 0, big.Width, big.Height);

            using var pen = new Pen(Color.Red, 2);
            foreach (int x in edges)
            {
                int bx = x * scale;
                if (bx >= 0 && bx < big.Width)
                    g.DrawLine(pen, bx, 0, bx, big.Height);
            }
        }

        return big;
    }
}
