using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Converters;

public sealed class StatusToBrushConverter : IValueConverter
{
    public static readonly StatusToBrushConverter Instance = new();

    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush FaultBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush StoppedBrush = new SolidColorBrush(Color.Parse("#94A3B8"));
    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#64748B"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is EquipmentStatus status)
        {
            return status switch
            {
                EquipmentStatus.Running => RunningBrush,
                EquipmentStatus.Warning => WarningBrush,
                EquipmentStatus.Fault => FaultBrush,
                EquipmentStatus.Maintenance => WarningBrush,
                EquipmentStatus.Offline => OfflineBrush,
                _ => StoppedBrush
            };
        }

        if (value is AlarmSeverity sev)
        {
            return sev switch
            {
                AlarmSeverity.Critical => FaultBrush,
                AlarmSeverity.High => WarningBrush,
                AlarmSeverity.Medium => new SolidColorBrush(Color.Parse("#38BDF8")),
                _ => StoppedBrush
            };
        }

        if (value is StartupCheckState check)
        {
            return check switch
            {
                StartupCheckState.Passed => RunningBrush,
                StartupCheckState.Warning => WarningBrush,
                StartupCheckState.Failed => FaultBrush,
                StartupCheckState.Running => new SolidColorBrush(Color.Parse("#38BDF8")),
                _ => OfflineBrush
            };
        }

        return StoppedBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToActiveNavBrushConverter : IValueConverter
{
    public static readonly BoolToActiveNavBrushConverter Instance = new();

    private static readonly IBrush ActiveBg = new SolidColorBrush(Color.Parse("#1E293B"));
    private static readonly IBrush InactiveBg = Brushes.Transparent;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ActiveBg : InactiveBg;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ProgressToWidthConverter : IValueConverter
{
    public static readonly ProgressToWidthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double progress)
        {
            double multiplier = 3.0;
            if (parameter is double d)
            {
                multiplier = d;
            }
            else if (parameter is string s &&
                     double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                multiplier = parsed;
            }

            return Math.Clamp(progress, 0.0, 100.0) * multiplier;
        }
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
