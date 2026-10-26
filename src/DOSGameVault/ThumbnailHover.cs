using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DOSGameVault;

public static class ThumbnailHover
{
    public static void Apply(Button button, double radius, FrameworkElement? hoverSource = null)
    {
        UiMotion.SetInteractive(button, false);
        var template = new ControlTemplate(typeof(Button));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var clip = new FrameworkElementFactory(typeof(RoundedBorder));
        clip.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        var content = new FrameworkElementFactory(typeof(ContentPresenter), "ThumbnailContent");
        content.SetValue(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        clip.AppendChild(content);
        grid.AppendChild(clip);
        var outline = new FrameworkElementFactory(typeof(Border));
        outline.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        outline.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        outline.SetValue(UIElement.IsHitTestVisibleProperty, false);
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Transparent));
        var hover = new DataTrigger { Binding = new Binding("IsMouseOver") { Source = hoverSource ?? button }, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(106,145,255))));
        style.Triggers.Add(hover);
        outline.SetValue(FrameworkElement.StyleProperty, style);
        grid.AppendChild(outline);
        template.VisualTree = grid;
        button.Template = template;
        var zoom = new ScaleTransform(1, 1);
        void AnimateZoom(double value)
        {
            button.ApplyTemplate();
            if (template.FindName("ThumbnailContent", button) is not ContentPresenter presenter) return;
            presenter.RenderTransform = zoom;
            var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            zoom.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
        var source = hoverSource ?? button;
        source.MouseEnter += (_, _) => AnimateZoom(1.05);
        source.MouseLeave += (_, _) => AnimateZoom(1);
    }
}
