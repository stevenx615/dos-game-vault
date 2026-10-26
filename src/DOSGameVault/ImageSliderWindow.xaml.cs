using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
namespace DOSGameVault;
public partial class ImageSliderWindow : Window
{
    private readonly string[] images;
    private int index;
    private bool closingAnimation;
    public ImageSliderWindow(string[] images, int index)
    {
        InitializeComponent(); this.images = images; this.index = index;
        foreach (var path in images)
        {
            var button = new Button { Margin = new Thickness(3), Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
            button.Content = new RoundedBorder { Width = 72, Height = 50, CornerRadius = new CornerRadius(8), Child = new Image { Stretch = Stretch.UniformToFill, Source = new CoverImageConverter().Convert(path, typeof(ImageSource), null!, System.Globalization.CultureInfo.CurrentCulture) as ImageSource } };
            button.Click += (_, _) => { this.index = Array.IndexOf(images, path); ShowImage(); };
            ThumbnailHover.Apply(button, 8);
            Thumbnails.Children.Add(button);
        }
        ShowImage();
        var viewerContent = (UIElement)Content;
        viewerContent.Opacity = 1;
        Loaded += (_, _) =>
        {
            AnimatePicture();
        };
        Closing += (_, e) =>
        {
            if (closingAnimation) return;
            e.Cancel = true;
            closingAnimation = true;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
            fade.Completed += (_, _) => Close();
            viewerContent.BeginAnimation(OpacityProperty, fade);
        };
    }
    private void ShowImage()
    {
        PreviousButton.Visibility = NextButton.Visibility = images.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        Picture.Source = null;
        try
        {
            using var stream = File.OpenRead(images[index]);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream; image.EndInit(); image.Freeze(); Picture.Source = image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or System.IO.FileFormatException) { }
        MissingImage.Visibility = Visibility.Collapsed;
        Picture.Source ??= GalleryImages.Placeholder;
        if (IsLoaded) AnimatePicture();
        Position.Text = $"{index + 1} / {images.Length}";
        for (var i = 0; i < Thumbnails.Children.Count; i++)
            if (Thumbnails.Children[i] is Button button) { if (i == index) button.BringIntoView(); }
    }
    private void AnimatePicture()
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        Picture.RenderTransformOrigin = new Point(0.5, 0.5);
        var zoom = new ScaleTransform(1, 1);
        Picture.RenderTransform = zoom;
        var size = new DoubleAnimation(0.99, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = easing };
        zoom.BeginAnimation(ScaleTransform.ScaleXProperty, size);
        zoom.BeginAnimation(ScaleTransform.ScaleYProperty, size);
        Picture.Opacity = 1;
    }
    private void Previous(object sender, RoutedEventArgs e) { index = (index + images.Length - 1) % images.Length; ShowImage(); }
    private void Next(object sender, RoutedEventArgs e) { index = (index + 1) % images.Length; ShowImage(); }
    private void CloseViewer(object sender, RoutedEventArgs e) => Close();
    private void ViewerPointerDown(object sender, MouseButtonEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element != null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.ScrollBar or System.Windows.Controls.Primitives.Thumb) return;
        }
        if (Picture.Source is { Width: > 0, Height: > 0 } source)
        {
            var scale = Math.Min(Picture.ActualWidth / source.Width, Picture.ActualHeight / source.Height);
            var width = source.Width * scale;
            var height = source.Height * scale;
            var imageBounds = new Rect((Picture.ActualWidth - width) / 2, (Picture.ActualHeight - height) / 2, width, height);
            if (imageBounds.Contains(e.GetPosition(Picture))) return;
        }
        e.Handled = true;
        Close();
    }
    private void ViewerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.Left) Previous(this, new RoutedEventArgs());
        else if (e.Key == Key.Right) Next(this, new RoutedEventArgs());
        else return;
        e.Handled = true;
    }
}
