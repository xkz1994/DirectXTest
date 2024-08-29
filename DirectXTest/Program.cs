// ReSharper disable LocalizableElement

using System.Reflection;

namespace DirectXTest;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#if NET
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
#endif
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var form = new Form
        {
            Text = $"Select Screenshot V{version}",
            Width = 600,
            Height = 400,
            MinimizeBox = false,
            MaximizeBox = false,
            TopMost = true,
            KeyPreview = true,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Icon = Properties.Resources.Screenshot // 设置窗体图标
        };
        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) form.Close(); // 按 Esc 键时关闭窗体
        };

        var primaryScreen = Screen.AllScreens.FirstOrDefault(t => Equals(t, Screen.PrimaryScreen) == false) ?? Screen.PrimaryScreen;
        if (primaryScreen is null) throw new InvalidOperationException("No primary screen found.");

        // 计算居中位置
        var screenBounds = primaryScreen.Bounds;
        var formWidth = form.Width;
        var formHeight = form.Height;
        var centerX = screenBounds.Left + (screenBounds.Width - formWidth) / 2;
        var centerY = screenBounds.Top + (screenBounds.Height - formHeight) / 2;

        form.StartPosition = FormStartPosition.Manual; // 需要手动设置窗体位置
        form.Location = new Point(centerX, centerY);

        // 创建 TableLayoutPanel
        var tableLayoutPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2, // 假设最多有两列
            RowCount = (int)Math.Ceiling((double)Screen.AllScreens.Length / 2), // 行数根据显示器数量计算
            AutoSize = true
        };

        // 设置列和行的大小
        tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        for (var i = 0; i < tableLayoutPanel.RowCount; i++)
        {
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F)); // 按钮高度
        }

        foreach (var (i, screen) in Screen.AllScreens.OrderBy(t => t.DeviceName).Select((t, i) => (i, t)))
        {
            var button = new Button
            {
                Text = screen.DeviceName,
                Dock = DockStyle.Fill, // 按钮填充其所在单元格
                Margin = new Padding(20)
            };

            button.Click += (_, _) =>
            {
                CaptureScreen(screen);
                MessageBox.Show("Screenshot captured and copied to clipboard.", "Screenshot Captured", MessageBoxButtons.OK, MessageBoxIcon.Information);
                form.Close();
            };

            tableLayoutPanel.Controls.Add(button, i % 2, i / 2); // 按行列位置添加按钮
        }

        form.Controls.Add(tableLayoutPanel);
        Application.Run(form);
    }
 
    private static void CaptureScreen(Screen screen)
    {
        // ReSharper disable once UnusedVariable
        var (bitmap, i) = DxScreenCaptureUtil.Capture(screen.Bounds);
        if (bitmap is null) throw new InvalidOperationException("Failed to capture screen.");
        // 将图片放到剪切板中
        Clipboard.SetImage(bitmap);
    }
}