
using System;
using System.Drawing;
using System.Windows.Forms;

namespace RotemExcel;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var form = new Form
        {
            Text = "RotemExcel",
            StartPosition = FormStartPosition.CenterScreen,
            Size = new Size(520, 300),
            MinimumSize = new Size(520, 300)
        };

        var title = new Label
        {
            Text = "RotemExcel — учет потребления птицы",
            Dock = DockStyle.Top,
            Height = 55,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 13, FontStyle.Bold)
        };

        var status = new Label
        {
            Text = "Программа запущена. Считывание пока не настроено.",
            Dock = DockStyle.Top,
            Height = 55,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var button = new Button
        {
            Text = "Проверить запуск",
            Width = 200,
            Height = 40,
            Anchor = AnchorStyles.None
        };

        button.Click += (_, _) =>
        {
            status.Text = "Программа работает. Готова к настройке считывания.";
        };

        var panel = new Panel
        {
            Dock = DockStyle.Fill
        };

        panel.Controls.Add(button);
        panel.Resize += (_, _) =>
        {
            button.Left = (panel.ClientSize.Width - button.Width) / 2;
            button.Top = (panel.ClientSize.Height - button.Height) / 2;
        };

        form.Controls.Add(panel);
        form.Controls.Add(status);
        form.Controls.Add(title);

        Application.Run(form);
    }
}
