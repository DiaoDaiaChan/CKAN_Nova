using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CkanUI.Ui.Converters;

namespace CkanUI.Ui.Controls;

/// <summary>
/// 图片信息流卡片：上方图片、下方标题/副标题/元信息，可带左上角版本徽标。
/// </summary>
/// <remarks>
/// 旗帜页用 <see cref="ImagePath"/> 走本地缩略图缓存；SpaceDock 页用 <see cref="ImageSource"/>
/// 绑定异步下载好的位图。两种来源共用同一张卡片，避免两套模板各写一遍。
/// </remarks>
public sealed partial class ImageFeedCard : UserControl
{
    private static readonly PathToThumbnailConverter ThumbnailConverter = new();

    /// <summary>供 XAML/ViewLocator 使用的无参构造。</summary>
    public ImageFeedCard()
    {
        InitializeComponent();
    }

    /// <summary>本地图片路径；<see cref="ImageSource"/> 为空时才使用。</summary>
    public static readonly StyledProperty<string?> ImagePathProperty =
        AvaloniaProperty.Register<ImageFeedCard, string?>(nameof(ImagePath));

    /// <summary>已经解码好的图片；优先于 <see cref="ImagePath"/>。</summary>
    public static readonly StyledProperty<IImage?> ImageSourceProperty =
        AvaloniaProperty.Register<ImageFeedCard, IImage?>(nameof(ImageSource));

    /// <summary>卡片标题。</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ImageFeedCard, string?>(nameof(Title));

    /// <summary>副标题（作者、包名等）。</summary>
    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<ImageFeedCard, string?>(nameof(Subtitle));

    /// <summary>底部元信息（下载量、版本等）。</summary>
    public static readonly StyledProperty<string?> MetaTextProperty =
        AvaloniaProperty.Register<ImageFeedCard, string?>(nameof(MetaText));

    /// <summary>左上角徽标文字。</summary>
    public static readonly StyledProperty<string?> BadgeTextProperty =
        AvaloniaProperty.Register<ImageFeedCard, string?>(nameof(BadgeText));

    /// <summary>是否显示徽标。</summary>
    public static readonly StyledProperty<bool> IsBadgeVisibleProperty =
        AvaloniaProperty.Register<ImageFeedCard, bool>(nameof(IsBadgeVisible));

    /// <summary>无图时显示的占位文字。</summary>
    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<ImageFeedCard, string?>(nameof(PlaceholderText));

    /// <summary>图片区域高度。</summary>
    public static readonly StyledProperty<double> ImageHeightProperty =
        AvaloniaProperty.Register<ImageFeedCard, double>(nameof(ImageHeight), 96d);

    /// <summary>是否处于选中状态。</summary>
    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<ImageFeedCard, bool>(nameof(IsSelected));

    /// <summary>实际显示到 Image 控件上的图片。</summary>
    public static readonly DirectProperty<ImageFeedCard, IImage?> DisplayImageProperty =
        AvaloniaProperty.RegisterDirect<ImageFeedCard, IImage?>(
            nameof(DisplayImage),
            static card => card.DisplayImage);

    private IImage? _displayImage;

    /// <summary>卡片标题。</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>副标题。</summary>
    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>底部元信息。</summary>
    public string? MetaText
    {
        get => GetValue(MetaTextProperty);
        set => SetValue(MetaTextProperty, value);
    }

    /// <summary>左上角徽标文字。</summary>
    public string? BadgeText
    {
        get => GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    /// <summary>是否显示徽标。</summary>
    public bool IsBadgeVisible
    {
        get => GetValue(IsBadgeVisibleProperty);
        set => SetValue(IsBadgeVisibleProperty, value);
    }

    /// <summary>无图时的占位文字。</summary>
    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>图片区域高度。</summary>
    public double ImageHeight
    {
        get => GetValue(ImageHeightProperty);
        set => SetValue(ImageHeightProperty, value);
    }

    /// <summary>是否处于选中状态。</summary>
    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>本地图片路径。</summary>
    public string? ImagePath
    {
        get => GetValue(ImagePathProperty);
        set => SetValue(ImagePathProperty, value);
    }

    /// <summary>外部图片源。</summary>
    public IImage? ImageSource
    {
        get => GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    /// <summary>实际显示到界面上的图片。</summary>
    public IImage? DisplayImage
    {
        get => _displayImage;
        private set => SetAndRaise(DisplayImageProperty, ref _displayImage, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ImagePathProperty || change.Property == ImageSourceProperty)
        {
            DisplayImage = ImageSource ?? DecodePath(ImagePath);
        }
    }

    private static IImage? DecodePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return ThumbnailConverter.Convert(path, typeof(IImage), 320, CultureInfo.InvariantCulture) as IImage;
    }
}
