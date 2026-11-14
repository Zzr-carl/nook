using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nook
{
    /// <summary>悬停刻度时的小预览卡：名字、条目数、前几个图标。不抢焦点。</summary>
    public partial class HintWindow : Window
    {
        private const int MaxIcons = 6;
        private const double IconSide = 22;

        public HintWindow()
        {
            InitializeComponent();
        }

        public void ShowFor(DrawerData data, double tickCenterX, double top)
        {
            try
            {
            NameText.Text = data.Name;
            CountText.Text = $"{data.Items.Count} 项";

            Icons.Children.Clear();

            int shown = 0;

            foreach (var item in data.Items)
            {
                if (shown >= MaxIcons) break;

                var src = IconMemory.Get(IconMemory.KeyFor(item.IconCache, item.Path))
                          ?? IconSnapshot.Load(item.IconCache, 64);

                if (src == null) continue;

                Icons.Children.Add(new Image
                {
                    Source = src,
                    Width = IconSide,
                    Height = IconSide,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 7, 0)
                });

                shown++;
            }

            if (shown == 0) Icons.Visibility = Visibility.Collapsed;
            else Icons.Visibility = Visibility.Visible;

            // 尺寸要等窗口真正显示后才是准的，所以先透明显示、量完再定位，避免偏一格
            Opacity = 0;
            if (!IsVisible) Show();
            UpdateLayout();

            var box = SystemParameters.WorkArea;
            double w = ActualWidth;
            double left = Math.Max(box.Left + 6, Math.Min(tickCenterX - w / 2, box.Right - 6 - w));

            Left = left;
            Top = top;
            Opacity = 1;

            }
            catch (Exception ex)
            {
                Log.Write("hint FAILED: " + ex);
            }
        }

        public void HideHint()
        {
            if (IsVisible) Hide();
        }
    }
}
