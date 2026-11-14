using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Nook
{
    /// <summary>主题色调整窗口：预设色板 + 色号输入 + 系统取色器，改动即时生效。</summary>
    public partial class AccentWindow : Window
    {
        private static readonly (string Name, string Hex)[] Presets =
        {
            ("琥珀", "#E9B872"), ("珊瑚", "#E8836F"), ("落橙", "#F0A35E"),
            ("草绿", "#9CCB86"), ("青碧", "#6FC2B0"), ("天蓝", "#7FA8D9"),
            ("紫罗兰", "#A99BD6"), ("玫粉", "#D98CA6"), ("银白", "#C9CCD1"),
        };

        private readonly DrawerManager _manager;

        public AccentWindow(DrawerManager manager)
        {
            InitializeComponent();
            _manager = manager;

            HexBox.Text = Normalize(manager.State.AccentColor);
            RenderPresets();
            UpdatePreview();
        }

        private void RenderPresets()
        {
            Swatches.Children.Clear();
            var current = Normalize(_manager.State.AccentColor);

            foreach (var (_, hex) in Presets)
            {
                var c = ParseHexOr(hex, App.AccentColor);
                bool selected = string.Equals(Normalize(hex), current, StringComparison.OrdinalIgnoreCase);

                var chip = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(c),
                    Margin = new Thickness(4),
                    Height = 34,
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    BorderBrush = selected
                        ? new SolidColorBrush(Colors.White)
                        : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                    ToolTip = hex
                };

                var hexCopy = hex;
                chip.MouseLeftButtonDown += (_, _) => ApplyHex(hexCopy);

                Swatches.Children.Add(chip);
            }
        }

        private void ApplyHex(string hex)
        {
            var c = ParseHexOr(hex, Colors.Transparent);
            if (c == Colors.Transparent && hex != "#000000") { Hint.Text = "这个颜色代码认不出来，试试 #RRGGBB"; return; }

            var normalized = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            _manager.SetAccent(normalized);

            HexBox.Text = normalized;
            Hint.Text = "已生效：刻度、强调条、边框都换好了";
            UpdatePreview();
            RenderPresets();
        }

        private void UpdatePreview()
        {
            Preview.Background = new SolidColorBrush(App.AccentColor);
        }

        private void ApplyHex_Click(object sender, RoutedEventArgs e) => ApplyHex(HexBox.Text.Trim());

        private void HexBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyHex(HexBox.Text.Trim());
        }

        private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 输入到 #RRGGBB 完整长度时实时预览，不用点应用
            var t = HexBox.Text.Trim();
            if (t.Length == 7 && t.StartsWith("#") && ParseHexOr(t, Colors.Transparent) != Colors.Transparent)
            {
                var c = ParseHexOr(t, Colors.Transparent);
                Preview.Background = new SolidColorBrush(c);
                HexBox.BorderBrush = System.Windows.Media.Brushes.Transparent;
            }
        }

        private void Picker_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.ColorDialog
            {
                FullOpen = true,
                Color = Drawing.Color.FromArgb(App.AccentColor.A, App.AccentColor.R, App.AccentColor.G, App.AccentColor.B)
            };

            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            {
                var c = dlg.Color;
                ApplyHex($"#{c.R:X2}{c.G:X2}{c.B:X2}");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1) DragMove();
        }

        private static string Normalize(string hex)
        {
            var c = ParseHexOr(hex, Colors.Transparent);
            return c == Colors.Transparent ? hex.Trim() : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        private static Color ParseHexOr(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex.Trim()); }
            catch { return fallback; }
        }
    }
}
