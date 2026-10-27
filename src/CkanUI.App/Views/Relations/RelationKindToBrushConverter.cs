using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CkanUI.ViewModels.Relations;

namespace CkanUI.App.Views.Relations;

/// <summary>
/// 关系种类枚举转主题画刷转换器（F-28 / UI-18 九色图例）。
/// 优先从 Application 资源字典查找语义 Token，回退至九色基准调色板。
/// </summary>
public sealed class RelationKindToBrushConverter : IValueConverter
{
    public static readonly RelationKindToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not RelationKind kind)
        {
            return TryGetBrush("CkanTextPrimaryBrush", "#11161D");
        }

        return kind switch
        {
            RelationKind.Depends => TryGetBrush("CkanRelationDependsBrush", "CkanAccentBrush", "#0A66C2"),
            RelationKind.Recommends => TryGetBrush("CkanRelationRecommendsBrush", "CkanSuccessBrush", "#1B7F3B"),
            RelationKind.Suggests => TryGetBrush("CkanRelationSuggestsBrush", "CkanWarningBrush", "#B26A00"),
            RelationKind.Supports => TryGetBrush("CkanRelationSupportsBrush", "CkanAccentBrush", "#6366F1"),
            RelationKind.Conflicts => TryGetBrush("CkanRelationConflictsBrush", "CkanDangerBrush", "#B3261E"),
            RelationKind.Provides => TryGetBrush("CkanRelationProvidesBrush", "CkanTextSecondaryBrush", "#0284C7"),
            RelationKind.Virtual => TryGetBrush("CkanRelationVirtualBrush", "CkanTextSecondaryBrush", "#0D9488"),
            RelationKind.Incompatible => TryGetBrush("CkanRelationIncompatibleBrush", "CkanDangerBrush", "#DC2626"),
            RelationKind.Installed => TryGetBrush("CkanRelationInstalledBrush", "CkanSuccessBrush", "#16A34A"),
            _ => TryGetBrush("CkanTextPrimaryBrush", "#11161D")
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    private static IBrush TryGetBrush(string key, string fallbackHex)
    {
        if (Application.Current is { } app && app.TryGetResource(key, null, out var res) && res is IBrush brush)
        {
            return brush;
        }

        return SolidColorBrush.Parse(fallbackHex);
    }

    private static IBrush TryGetBrush(string primaryKey, string fallbackKey, string fallbackHex)
    {
        if (Application.Current is { } app)
        {
            if (app.TryGetResource(primaryKey, null, out var res1) && res1 is IBrush brush1)
            {
                return brush1;
            }

            if (app.TryGetResource(fallbackKey, null, out var res2) && res2 is IBrush brush2)
            {
                return brush2;
            }
        }

        return SolidColorBrush.Parse(fallbackHex);
    }
}
