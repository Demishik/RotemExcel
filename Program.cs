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

    private static Bitmap CaptureArea(AreaRect r)
    {
        var bmp = new Bitmap(r.Width, r.Height);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(r.X, r.Y, 0, 0, new Size(r.Width, r.Height),
            CopyPixelOperation.SourceCopy);
        return bmp;
    }

    // =================================================================
    // Основная логика: находим 8 чисел в строке
    // =================================================================

    private static List<string> RecognizeRow(Bitmap row)
    {
        int w = row.Width;
        int h = row.Height;

        // 1. Otsu-бинаризация всей области → массив bool (true = цифра)
        bool[,] dark = OtsuBinarize(row);

        // 2. Профиль по горизонтали: сколько тёмных пикселей в каждом столбце
        int[] columnDark = new int[w];
        for (int x = 0; x < w; x++)
        {
            int c = 0;
            for (int y = 0; y < h; y++)
                if (dark[x, y]) c++;
            columnDark[x] = c;
        }

        // 3. Сглаживание окном 3
        int[] smooth = new int[w];
        for (int x = 0; x < w; x++)
        {
            int sum = 0, cnt = 0;
            for (int d = -1; d <= 1; d++)
            {
                int xx = x + d;
                if (xx < 0 || xx >= w) continue;
                sum += columnDark[xx];
                cnt++;
            }
            smooth[x] = cnt == 0 ? 0 : sum / cnt;
        }

        // 4. Порог "есть текст" — небольшое число, чтобы шум не резал блоки
        int noise = Math.Max(1, h / 8);
        bool[] isText = new bool[w];
        for (int x = 0; x < w; x++)
            isText[x] = smooth[x] >= noise;

        // 5. Ищем блоки (непрерывные участки isText == true)
        var blocks = new List<(int start, int end)>();
        int i = 0;
        while (i < w)
        {
            if (isText[i])
            {
                int s = i;
                while (i < w && isText[i]) i++;
                int e = i - 1;

                // Минимальная ширина блока — 4 пикселя
                if (e - s + 1 >= 4)
                    blocks.Add((s, e));
            }
            else i++;
        }

        // 6. Склеиваем блоки, между которыми зазор ≤ mergeGap
        //    (внутри одного числа между цифрами зазор маленький)
        int mergeGap = Math.Max(2, h / 6);
        var merged = MergeCloseBlocks(blocks, mergeGap);

        // 7. Принудительно сводим к 8 числам
        merged = ForceEightBlocks(merged, w);

        // 8. Распознаём каждый блок
        var results = new List<string>();
        string dataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

        using var engine = new TesseractEngine(dataPath, "eng", EngineMode.Default);
        engine.SetVariable("tessedit_char_whitelist", "0123456789,.");
        engine.SetVariable("classify_bln_numeric_mode", "1");

        foreach (var (s, e) in merged)
        {
            int bw = e - s + 1;
            if (bw < 3) { results.Add("?"); continue; }

            using var cell = new Bitmap(bw, h);
            using (var g = Graphics.FromImage(cell))
            {
                g.DrawImage(row,
                    new Rectangle(0, 0, bw, h),
                    new Rectangle(s, 0, bw, h),
                    GraphicsUnit.Pixel);
            }

            string num = RecognizeCell(engine, cell);
            results.Add(string.IsNullOrEmpty(num) ? "?" : num);
        }

        return results;
    }

    private static List<(int start, int end)> MergeCloseBlocks(
        List<(int start, int end)> blocks, int gap)
    {
        var merged = new List<(int, int)>();

        foreach (var b in blocks)
        {
            if (merged.Count == 0) { merged.Add(b); continue; }

            var last = merged[^1];
            if (b.start - last.end <= gap)
                merged[^1] = (last.start, b.end);
            else
                merged.Add(b);
        }

        return merged;
    }

    /// <summary>
    /// Принудительно сводит список блоков к ровно 8 числам.
    /// </summary>
    private static List<(int start, int end)> ForceEightBlocks(
        List<(int start, int end)> blocks, int width)
    {
        const int target = 8;

        // Если блоков больше 8 — склеиваем самые близкие пары,
        // пока не останется ровно 8.
        while (blocks.Count > target)
        {
            int bestIdx = -1;
            int bestGap = int.MaxValue;

            for (int i = 0; i < blocks.Count - 1; i++)
            {
                int gap = blocks[i + 1].start - blocks[i].end;
                if (gap < bestGap)
                {
                    bestGap = gap;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) break;

            var mergedBlock = (blocks[bestIdx].start, blocks[bestIdx + 1].end);
            blocks[bestIdx] = mergedBlock;
            blocks.RemoveAt(bestIdx + 1);
        }

        // Если блоков меньше 8 — делим самые широкие, где внутри есть провал.
        while (blocks.Count < target)
        {
            int bestIdx = -1;
            int bestWidth = 0;

            for (int i = 0; i < blocks.Count; i++)
            {
                int w = blocks[i].end - blocks[i].start + 1;
                if (w > bestWidth)
                {
                    bestWidth = w;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0 || bestWidth < 10) break;

            var b = blocks[bestIdx];
            int mid = (b.start + b.end) / 2;

            // Ищем "провал" — столбец с минимумом тёмных пикселей около центра
            // (упрощённо — просто делим пополам)
            blocks[bestIdx] = (b.start, mid);
            blocks.Insert(bestIdx + 1, (mid + 1, b.end));
        }

        return blocks;
    }

    // =================================================================
    // OCR одного числа
    // =================================================================

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
            if (char.IsDigit(c)) digits.Append(c);
        }

        return digits.ToString();
    }

    // =================================================================
    // Бинаризация
    // =================================================================

    /// <summary>
    /// Otsu-бинаризация. Возвращает массив bool: true = "тёмный" пиксель.
    /// Автоматически определяет, что считать текстом, независимо от цвета фона.
    /// </summary>
    private static bool[,] OtsuBinarize(Bitmap src)
    {
        int w = src.Width;
        int h = src.Height;

        var gray = new byte[w * h];
        int[] histogram = new int[256];

        int idx = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = src.GetPixel(x, y);
                byte v = (byte)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
                gray[idx++] = v;
                histogram[v]++;
            }
        }

        int total = w * h;
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

        // Определяем, что темнее — текст или фон. Считаем среднюю яркость
        // верхней и нижней строк (там обычно нет цифр).
        double bgSum = 0; int bgCnt = 0;
        for (int x = 0; x < w; x++)
        {
            var top = src.GetPixel(x, 0);
            bgSum += 0.299 * top.R + 0.587 * top.G + 0.114 * top.B;
            bgCnt++;

            var bot = src.GetPixel(x, h - 1);
            bgSum += 0.299 * bot.R + 0.587 * bot.G + 0.114 * bot.B;
            bgCnt++;
        }
        double bg = bgCnt > 0 ? bgSum / bgCnt : 200;

        // Если фон светлый (яркость > threshold) — текст тёмный:
        //   пиксель считается текстом, если его яркость < threshold.
        // Если фон тёмный — текст светлый:
        //   пиксель считается текстом, если его яркость > threshold.
        bool darkText = bg > threshold;

        var mask = new bool[w, h];
        idx = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte v = gray[idx++];
                mask[x, y] = darkText ? v < threshold : v > threshold;
            }
        }

        return mask;
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
