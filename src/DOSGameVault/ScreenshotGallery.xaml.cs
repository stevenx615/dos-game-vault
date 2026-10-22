using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media.Animation;
using System.Windows.Media;
namespace DOSGameVault;
public partial class ScreenshotGallery : UserControl
{
    public static readonly DependencyProperty IsEditorProperty = DependencyProperty.Register(nameof(IsEditor), typeof(bool), typeof(ScreenshotGallery), new PropertyMetadata(false));
    public bool IsEditor { get => (bool)GetValue(IsEditorProperty); set => SetValue(IsEditorProperty, value); }
    private MainViewModel? model;
    private GameEntry? game;
    private readonly List<string> images = [];
    private int index;
    public ScreenshotGallery()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => { if (IsLoaded) Attach(); };
    }
    private void Detach()
    {
        if (model != null) model.PropertyChanged -= ModelChanged;
        if (game != null) { game.PropertyChanged -= GameChanged; game.Screenshots.CollectionChanged -= ScreenshotsChanged; }
        model = null; game = null;
    }
    private void Attach()
    {
        Detach(); model = DataContext as MainViewModel;
        if (model != null) model.PropertyChanged += ModelChanged;
        AttachGame();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(MainViewModel.SelectedGame) or "" or null) AttachGame(); }
    private void AttachGame()
    {
        if (game != null) { game.PropertyChanged -= GameChanged; game.Screenshots.CollectionChanged -= ScreenshotsChanged; }
        game = model?.SelectedGame; index = 0;
        if (game != null) { game.PropertyChanged += GameChanged; game.Screenshots.CollectionChanged += ScreenshotsChanged; }
        Refresh();
    }
    private void GameChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(GameEntry.CoverPath)) Refresh(); }
    private void ScreenshotsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
    private string Text(string key) => Localization.Current[key];
    private void Refresh()
    {
        var selected = images.ElementAtOrDefault(index);
        images.Clear();
        if (game?.CoverPath is { } cover) images.Add(cover);
        if (game != null) images.AddRange(game.Screenshots.Where(p => !images.Contains(p, StringComparer.OrdinalIgnoreCase)));
        index = Math.Max(0, selected == null ? 0 : images.IndexOf(selected));
        ThumbnailStrip.Children.Clear(); UploadTiles.Children.Clear();
        foreach (var path in images)
        {
            var button = new Button { Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(0,0,6,0), Tag = path, ToolTip = Text("ImagesZoom") };
            button.Content = new RoundedBorder { Width = 76, Height = 54, CornerRadius = new CornerRadius(8), Child = CreateImage(path) };
            button.Click += (_, _) => { index = images.IndexOf(path); ShowImage(); OpenPreview(this, new RoutedEventArgs()); };
            ThumbnailHover.Apply(button, 8);
            ThumbnailStrip.Children.Add(button);
            UploadTiles.Children.Add(CreateTile(path));
        }
        for (var slot = images.Count; slot < MainViewModel.MaxGameImages; slot++)
            UploadTiles.Children.Add(new RoundedBorder { CornerRadius = new CornerRadius(12), Background = TryFindResource("FieldBackground") as Brush ?? new SolidColorBrush(Color.FromRgb(40,42,67)), IsHitTestVisible = false });
        AddImagesTile.IsEnabled = images.Count < MainViewModel.MaxGameImages;
        UpdateUploadTileSizes();
        DropArea.Visibility = IsEditor ? Visibility.Visible : Visibility.Collapsed;
        PreviewArea.Visibility = IsEditor ? Visibility.Collapsed : Visibility.Visible;
        DetailThumbnails.Visibility = !IsEditor && images.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowImage();
    }
    private UIElement CreateTile(string path)
    {
        var tile = new Grid { ClipToBounds = true };
        var preview = new Button { Padding = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, ToolTip = Text("ImagesZoom") };
        preview.Content = CreateImage(path);
        ThumbnailHover.Apply(preview, 12, tile);
        preview.Click += (_, _) => { index = images.IndexOf(path); ShowImage(); OpenPreview(this, new RoutedEventArgs()); };
        tile.Children.Add(preview);
        var isCover = string.Equals(path, game?.CoverPath, StringComparison.OrdinalIgnoreCase);
        var cover = CreateCircleButton("M 2,2 L 18,2 L 18,14 L 2,14 Z M 5,11 L 8,7 L 11,10 L 14,5 L 16,11 M 7,18 L 13,18", isCover ? Brushes.RoyalBlue : new SolidColorBrush(Color.FromArgb(190,24,29,47)), isCover ? Brushes.White : new SolidColorBrush(Color.FromRgb(145,152,180)), 13);
        cover.Margin = new Thickness(4,36,4,4);
        cover.ToolTip = Text(isCover ? "ImagesCoverBadge" : "ImagesSetCover");
        System.Windows.Automation.AutomationProperties.SetName(cover, (string)cover.ToolTip);
        cover.Click += async (_, _) => { if (model != null) await model.SetGameCoverAsync(path); };
        tile.Children.Add(cover);
        var delete = CreateCircleButton("M 1,1 L 15,15 M 15,1 L 1,15", new SolidColorBrush(Color.FromArgb(230,156,49,62)), Brushes.White, 11);
        delete.Margin = new Thickness(4);
        delete.ToolTip = Text("ImagesRemove");
        System.Windows.Automation.AutomationProperties.SetName(delete, Text("ImagesRemove"));
        delete.Click += async (_, _) => { if (model != null) await model.RemoveGameImageAsync(path); };
        foreach (var action in new[] { delete, cover })
        {
            UiMotion.SetInteractive(action, false);
            action.Visibility = Visibility.Hidden;
            action.Opacity = 0;
            action.RenderTransformOrigin = new Point(.5,.5);
            action.RenderTransform = new ScaleTransform(1,1);
        }
        void ShowActions(bool show)
        {
            foreach (var action in new[] { delete, cover })
            {
                action.IsHitTestVisible = show;
                if (show) action.Visibility = Visibility.Visible;
                if (!SystemParameters.ClientAreaAnimation)
                { action.Opacity = show ? 1 : 0; action.Visibility = show ? Visibility.Visible : Visibility.Hidden; continue; }
                var fade = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 160 : 110));
                if (!show) fade.Completed += (_, _) => { if (!tile.IsMouseOver) action.Visibility = Visibility.Hidden; };
                action.BeginAnimation(OpacityProperty, fade);
            }
        }
        tile.MouseEnter += (_, _) => ShowActions(true);
        tile.MouseLeave += (_, _) => ShowActions(false);
        tile.Loaded += (_, _) => { if (tile.IsMouseOver) ShowActions(true); };
        tile.Children.Add(delete);
        tile.Margin = new Thickness(0);
        return new RoundedBorder { CornerRadius = new CornerRadius(12), Child = tile };
    }
    private static Button CreateCircleButton(string geometry, Brush background, Brush foreground, double iconSize)
    {
        var template = new ControlTemplate(typeof(Button));
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        surface.SetBinding(Border.BackgroundProperty, new Binding(nameof(Button.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        surface.AppendChild(presenter); template.VisualTree = surface;
        var drawing = new Canvas { Width = 20, Height = 20 };
        drawing.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), Stroke = foreground, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
        // Normalize the drawing bounds so both active and inactive icons use the same center.
        var bounds = Geometry.Parse(geometry).Bounds;
        drawing.Children[0].RenderTransform = new TranslateTransform(10 - (bounds.Left + bounds.Width / 2), 10 - (bounds.Top + bounds.Height / 2));
        var button = new Button { Width = 26, Height = 26, Padding = new Thickness(0), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Background = background, Template = template,
            Content = new Viewbox { Width = iconSize, Height = iconSize, Stretch = Stretch.Uniform, Child = drawing } };
        button.MouseEnter += (_, _) => AnimateActionScale(button, 1.08);
        button.MouseLeave += (_, _) => AnimateActionScale(button, 1);
        button.PreviewMouseLeftButtonDown += (_, _) => AnimateActionScale(button, .92);
        button.PreviewMouseLeftButtonUp += (_, _) => AnimateActionScale(button, 1.08);
        return button;
    }
    private static void AnimateActionScale(Button button, double scale)
    {
        if (!SystemParameters.ClientAreaAnimation || button.RenderTransform is not ScaleTransform transform) return;
        var animation = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
    private void UploadTilesSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) UpdateUploadTileSizes();
    }
    private void UpdateUploadTileSizes()
    {
        var width = Math.Max(1, (UploadTiles.ActualWidth - 40) / 5);
        var rows = Math.Max(1, (int)Math.Ceiling(UploadTiles.Children.Count / 5d));
        var height = Math.Max(70, width * .62);
        DropTarget.Height = height * rows + 8 * (rows - 1);
        foreach (FrameworkElement tile in UploadTiles.Children)
        {
            tile.Width = width;
            tile.Height = height;
            var slot = UploadTiles.Children.IndexOf(tile);
            tile.Margin = new Thickness(0,0,8,slot / 5 < rows - 1 ? 8 : 0);
        }
    }
    private FrameworkElement CreateImage(string path)
    {
        var image = Load(path);
        return image == null ? new GameCoverPlaceholder { DataContext = game } : new Image { Source = image, Stretch = Stretch.UniformToFill };
    }
    private static ImageSource? Load(string path) => new CoverImageConverter().Convert(path, typeof(ImageSource), null!, System.Globalization.CultureInfo.CurrentCulture) as ImageSource;
    private void ShowImage()
    {
        Preview.Source = images.Count == 0 ? null : Load(images[index]);
        Placeholder.Visibility = Preview.Source == null ? Visibility.Visible : Visibility.Collapsed;
        PreviewButton.IsHitTestVisible = images.Count > 0;
        PreviewButton.Focusable = images.Count > 0;
        UpdateCoverArrows();
        Counter.Text = images.Count == 0 ? "" : $"{index + 1} / {images.Count}";
        Counter.Visibility = images.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        UpdateImageSize();

    }
    private void CoverPointerChanged(object sender, MouseEventArgs e) => UpdateCoverArrows();
    private void UpdateCoverArrows() => PreviousButton.Visibility = NextButton.Visibility = images.Count > 1 && PreviewArea.IsMouseOver ? Visibility.Visible : Visibility.Collapsed;
    private void PreviewSizeChanged(object sender, SizeChangedEventArgs e) { if (e.WidthChanged) UpdateImageSize(); }
    private void UpdateImageSize()
    {
        if (PreviewArea.ActualWidth > 0)
            PreviewArea.Height = Math.Clamp(PreviewArea.ActualWidth * .66, 160, 220);
    }
    private void Previous(object sender, RoutedEventArgs e) { if (images.Count > 1) index = (index + images.Count - 1) % images.Count; ShowImage(); }
    private void Next(object sender, RoutedEventArgs e) { if (images.Count > 1) index = (index + 1) % images.Count; ShowImage(); }
    private void OpenPreview(object sender, RoutedEventArgs e)
    {
        if (images.Count == 0 || Window.GetWindow(this) is not { } owner) return;
        var origin = owner.PointToScreen(new Point(0, 0));
        origin = PresentationSource.FromVisual(owner)?.CompositionTarget?.TransformFromDevice.Transform(origin) ?? origin;
        var viewer = new ImageSliderWindow(images.ToArray(), index) { Owner = owner, Left = origin.X, Top = origin.Y, Width = owner.ActualWidth, Height = owner.ActualHeight };
        viewer.ShowDialog();
    }
    private async void Upload(object sender, RoutedEventArgs e) { if (model != null) await model.AddGameImagesAsync(null, false); }
    private void ImagesDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && model?.HasSelection == true ? DragDropEffects.Copy : DragDropEffects.None;
        DropTarget.Background = new SolidColorBrush(Color.FromArgb(70,33,45,75)); e.Handled = true;
    }
    private void ImagesDragLeave(object sender, DragEventArgs e) => DropTarget.Background = Brushes.Transparent;
    private async void ImagesDrop(object sender, DragEventArgs e)
    {
        ImagesDragLeave(sender,e); e.Handled = true;
        if (model != null && e.Data.GetData(DataFormats.FileDrop) is string[] paths) await model.AddGameImagesAsync(paths, false);
    }
}
