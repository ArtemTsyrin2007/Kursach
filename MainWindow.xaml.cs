using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FifteenPuzzle
{
    // ═══════════════════════════════════════════════════════════════════════
    //  MainWindow — code-behind (kept minimal per MVVM)
    // ═══════════════════════════════════════════════════════════════════════

    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;
        }

        // Persist game when the window is closed.
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _vm.SaveGame();
            base.OnClosing(e);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Value Converters
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>bool → Visibility (true = Visible).</summary>
    public class BoolToVisConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is true ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotSupportedException();
    }

    /// <summary>bool → Visibility (true = Collapsed).</summary>
    public class InverseBoolToVisConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Multi-value converter: [IsBlank, IsHighlighted] → tile background brush.
    /// </summary>
    public class TileBackgroundConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush BlankBrush =
            new(Color.FromRgb(0x0D, 0x1B, 0x2A));

        private static readonly SolidColorBrush NormalBrush =
            new(Color.FromRgb(0x0F, 0x34, 0x60));

        private static readonly SolidColorBrush HighlightBrush =
            new(Color.FromRgb(0xF3, 0x9C, 0x12));

        public object Convert(object[] values, Type t, object p, CultureInfo c)
        {
            bool isBlank = values[0] is true;
            bool isHighlighted = values[1] is true;

            if (isBlank) return BlankBrush;
            if (isHighlighted) return HighlightBrush;
            return NormalBrush;
        }

        public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c)
            => throw new NotSupportedException();
    }

    /// <summary>Grid size → tile font size (larger tiles on smaller grids).</summary>
    public class TileFontSizeConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is int size)
                return size switch
                {
                    3 => 32.0,
                    4 => 24.0,
                    5 => 18.0,
                    _ => 20.0
                };
            return 22.0;
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotSupportedException();
    }

    /// <summary>Best result int → display string.</summary>
    public class BestResultConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is int v && v > 0 ? $"BEST  {v}" : "BEST  —";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
