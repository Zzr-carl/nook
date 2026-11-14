using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;

namespace Nook
{
    /// <summary>卡片几何。三档尺寸共用一套模板，靠这组数值切换。</summary>
    public sealed class CardMetrics
    {
        public double CardWidth { get; init; }
        public double CardHeight { get; init; }
        public double IconSide { get; init; }
        public double IconGap { get; init; }
        public double LabelFont { get; init; }
        public double LabelMaxHeight { get; init; }
        public double LineHeight { get; init; }
        public double Radius { get; init; }
        public double LabelMargin { get; init; }
        public double PlusSide { get; init; }

        /// <summary>图标顶部偏移。固定值，保证名称占一行还是两行都不影响图标位置。</summary>
        public double IconTop => (CardHeight - IconSide - IconGap - LabelMaxHeight) / 2;

        public System.Windows.Thickness IconTopMargin => new(0, IconTop, 0, 0);
        public System.Windows.Thickness IconMargin => new(0, 0, 0, IconGap);
        public System.Windows.Thickness LabelPadding => new(LabelMargin, 0, LabelMargin, 0);

        public static CardMetrics For(string? size) => size switch
        {
            "small" => new CardMetrics
            {
                CardWidth = 80, CardHeight = 82, IconSide = 30, IconGap = 5,
                LabelFont = 9.0, LabelMaxHeight = 24, LineHeight = 11,
                Radius = 11, LabelMargin = 4, PlusSide = 13
            },
            "large" => new CardMetrics
            {
                CardWidth = 112, CardHeight = 128, IconSide = 58, IconGap = 9,
                LabelFont = 11.5, LabelMaxHeight = 34, LineHeight = 16,
                Radius = 16, LabelMargin = 9, PlusSide = 21
            },
            _ => new CardMetrics
            {
                CardWidth = 90, CardHeight = 102, IconSide = 40, IconGap = 7,
                LabelFont = 10.0, LabelMaxHeight = 29, LineHeight = 13.5,
                Radius = 13, LabelMargin = 4, PlusSide = 17
            }
        };
    }

    public sealed class DrawerItem : INotifyPropertyChanged
    {
        private ImageSource? _icon;
        private bool _isSelected;
        private bool _isMissing;

        public DrawerItem(string path, bool isAddTile = false, string? iconCache = null,
                          CardMetrics? metrics = null)
        {
            Path = path;
            IsAddTile = isAddTile;
            IconCachePath = iconCache;
            _metrics = metrics ?? CardMetrics.For(null);

            if (isAddTile)
            {
                Name = "添加";
                return;
            }

            if (ShellItem.IsShellPath(path))
            {
                Name = ShellItem.GetDisplayName(path) ?? "系统对象";
                return;
            }

            Name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(Name)) Name = path;

            string ext = System.IO.Path.GetExtension(Name);
            if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
                Name = System.IO.Path.GetFileNameWithoutExtension(Name);

            IsMissing = !ShellItem.IsShellPath(path) && !File.Exists(path) && !Directory.Exists(path);
        }

        public string Path { get; }
        public string Name { get; }
        public bool IsAddTile { get; }
        public string? IconCachePath { get; }

        private CardMetrics _metrics;
        public CardMetrics Metrics => _metrics;

        public void MetricsOverride(CardMetrics metrics) => _metrics = metrics;

        public bool IsMissing
        {
            get => _isMissing;
            set
            {
                if (_isMissing == value) return;
                _isMissing = value;
                OnPropertyChanged(nameof(IsMissing));
            }
        }

        public ImageSource? Icon
        {
            get => _icon;
            set
            {
                if (ReferenceEquals(_icon, value)) return;
                _icon = value;
                OnPropertyChanged(nameof(Icon));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
