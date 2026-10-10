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
            Height = 320,
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

        var showAreasBtn = new Button
        {
            Text = "Показать области",
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0),
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        };
        showAreasBtn.Click += (_, _) => ShowAreas();
        panel.Controls.Add(showAreasBtn);

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

    private void ShowAreas()
    {
        foreach (var key in new[] { "water_h1", "feed_h1", "water_h2", "feed_h2" })
        {
            var r = areas.Get(key);
            if (!r.IsSet) continue;

            using var bmp = CaptureArea(r);
            int scale = 3;
            var big = new Bitmap(bmp.Width * scale, bmp.Height * scale);
            using (var g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bmp, 0, 0, big.Width, big.Height);
            }

            string name = key switch
            {
                "water_h1" => "вода Н-1",
                "feed_h1"  => "корм Н-1",
                "water_h2" => "вода Н-2",
                "feed_h2"  => "корм Н-2",
                _ => key
            };

            var f = new Form
            {
                Text = name + " — " + r,
                Width = Math.Min(big.Width + 40, 1400),
                Height = Math.Min(big.Height + 80, 400),
                StartPosition = FormStartPosition.CenterScreen
            };
            var pb = new PictureBox
            {
                Image = big,
                SizeMode = PictureBoxSizeMode.AutoSize,
                Dock = DockStyle.Fill
            };
            f.Controls.Add(pb);
            f.Show(this);
        }
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

                string marker = numbers.Count == 8 ? "" : $"  [найдено {numbers.Count} вместо 8]";
                sb.AppendLine($"{name}: {string.Join(" ", numbers)}{marker}");
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

    /// <summary>
    /// Автоматически находит блоки цифр в строке и распознаёт каждый блок отдельно.
    /// </summary>
    private static List<string> RecognizeRow(Bitmap row)
    {
        // 1. Бинаризуем по "тёмности" — цифры тёмные, фон любой (белый, синий, жёлтый) светлее
        bool[,] dark = BuildDarkMask(row, out int darkThreshold);

        // 2. Считаем количество тёмных пикселей в каждом вертикальном столбце
        int[] columnDark = new int[row.Width];
        for (int x = 0; x < row.Width; x++)
        {
            int count = 0;
            for (int y = 0; y < row.Height; y++)
            {
                if (dark[x, y]) count++;
            }
            columnDark[x] = count;
        }

        // 3. Ищем непрерывные блоки, где есть тёмные пиксели
        //    (столбцы с нулём — это пробелы между числами)
        var blocks = new List<(int start, int end)>();
        int i = 0;
        while (i < columnDark.Length)
        {
            if (columnDark[i] > 0)
            {
                int s = i;
                while (i < columnDark.Length && columnDark[i] > 0) i++;
                int e = i - 1;
                if (e - s >= 2)
                    blocks.Add((s, e));
            }
            else i++;
        }

        // 4. Склеиваем слишком близкие блоки (если между ними 1-2 пикселя — это одна цифра или число)
        const int mergeGap = 4;
        var merged = new List<(int start, int end)>();
        foreach (var b in blocks)
        {
            if (merged.Count == 0)
            {
                merged.Add(b);
                continue;
            }

            var last = merged[^1];
            if (b.start - last.end <= mergeGap)
                merged[^1] = (last.start, b.end);
            else
                merged.Add(b);
        }

        // 5. Распознаём каждый блок
        var results = new List<string>();
        string dataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

        using var engine = new TesseractEngine(dataPath, "eng", EngineMode.Default);
        engine.SetVariable("tessedit_char_whitelist", "0123456789,.");
        engine.SetVariable("classify_bln_numeric_mode", "1");

        foreach (var (s, e) in merged)
        {
            int w = e - s + 1;
            if (w < 4) continue;

            using var cell = new Bitmap(w, row.Height);
            using (var g = Graphics.FromImage(cell))
            {
                g.DrawImage(row,
                    new Rectangle(0, 0, w, row.Height),
                    new Rectangle(s, 0, w, row.Height),
                    GraphicsUnit.Pixel);
            }

            string num = RecognizeCell(engine, cell);
            results.Add(string.IsNullOrEmpty(num) ? "?" : num);
        }

        return results;
    }

    private static string RecognizeCell(TesseractEngine engine, Bitmap cell)
    {
        int scale = 4;

        using var enlarged = new Bitmap(cell.Width * scale, cell.Height * scale);
        using (var g = Graphics.FromImage(enlarged))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(cell, 0, 0, enlarged.Width, enlarged.Height);
        }

        using var gray = ToGrayAndThreshold(enlarged);

        using var pix = PixConverter.ToPix(gray);
        using var page = engine.Process(pix, PageSegMode.SingleWord);

        string raw = page.GetText();
        raw = raw.Replace("\r", " ").Replace("\n", " ").Trim();

        var digits = new StringBuilder();
        foreach (char c in raw)
        {
            if (char.IsDigit(c)) digits.Append(c);
        }

        return digits.ToString();
    }

    /// <summary>
    /// Возвращает маску «тёмных» пикселей (цифры), независимо от цвета фона.
    /// </summary>
    private static bool[,] BuildDarkMask(Bitmap src, out int threshold)
    {
        int w = src.Width;
        int h = src.Height;
        var mask = new bool[w, h];

        // Оцениваем среднюю яркость фона — берём верхнюю строку (там нет цифр),
        // и нижнюю строку (тоже без цифр), считаем среднее.
        long sum = 0;
        int count = 0;
        for (int x = 0; x < w; x++)
        {
            var c1 = src.GetPixel(x, 0);
            sum += (int)(0.299 * c1.R + 0.587 * c1.G + 0.114 * c1.B);
            count++;

            var c2 = src.GetPixel(x, h - 1);
            sum += (int)(0.299 * c2.R + 0.587 * c2.G + 0.114 * c2.B);
            count++;
        }

        double bg = count > 0 ? (double)sum / count : 200;

        // Порог: всё, что темнее половины яркости фона, считаем цифрой
        threshold = (int)(bg * 0.5);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = src.GetPixel(x, y);
                int lum = (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
                mask[x, y] = lum < threshold;
            }
        }

        return mask;
    }

    private static Bitmap ToGrayAndThreshold(Bitmap src)
    {
        var result = new Bitmap(src.Width, src.Height);

        for (int y = 0; y < src.Height; y++)
        {
            for (int x = 0; x < src.Width; x++)
            {
                var c = src.GetPixel(x, y);
                int lum = (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
                // Тёмное — чёрное, светлое — белое (чтобы Tesseract видел чёрные цифры на белом)
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
