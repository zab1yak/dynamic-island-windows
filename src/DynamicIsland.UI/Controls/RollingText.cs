using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.UI.Animation;

namespace DynamicIsland.UI.Controls;

/// <summary>
/// Text whose changed characters roll vertically (old slides up and fades, new rises in), like the
/// digits of an iOS timer. Unchanged characters stay still. Use with tabular digits.
/// </summary>
public sealed class RollingText : StackPanel
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(RollingText), new PropertyMetadata(string.Empty, (d, e) => ((RollingText)d).Update((string?)e.NewValue ?? string.Empty)));

    public static readonly DependencyProperty TextStyleProperty = DependencyProperty.Register(
        nameof(TextStyle), typeof(Style), typeof(RollingText), new PropertyMetadata(null, (d, _) => ((RollingText)d).Rebuild()));

    private static readonly Duration Duration = new(TimeSpan.FromMilliseconds(380));

    private string _shown = string.Empty;

    public RollingText()
    {
        Orientation = Orientation.Horizontal;
    }

    /// <summary>Displayed text.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Style applied to every character's TextBlock.</summary>
    public Style? TextStyle
    {
        get => (Style?)GetValue(TextStyleProperty);
        set => SetValue(TextStyleProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        foreach (var ch in _shown)
        {
            Children.Add(CreateCell(ch));
        }
    }

    private void Update(string text)
    {
        if (text.Length != _shown.Length || !IsLoaded)
        {
            _shown = text;
            Rebuild();
            return;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != _shown[i])
            {
                Roll((Grid)Children[i], text[i]);
            }
        }

        _shown = text;
    }

    private Grid CreateCell(char ch)
    {
        var cell = new Grid { ClipToBounds = true };
        cell.Children.Add(CreateText(ch));
        return cell;
    }

    private TextBlock CreateText(char ch) => new()
    {
        Text = ch.ToString(),
        Style = TextStyle,
        RenderTransform = new TranslateTransform(),
        TextTrimming = TextTrimming.None,
    };

    private void Roll(Grid cell, char next)
    {
        var height = Math.Max(cell.ActualHeight, 10);
        // Drop finished leftovers so rapid changes never pile up text blocks.
        while (cell.Children.Count > 1)
        {
            cell.Children.RemoveAt(0);
        }

        var old = (TextBlock)cell.Children[0];
        var incoming = CreateText(next);
        cell.Children.Add(incoming);

        var ease = new SpringEase { DampingRatio = 0.8 };
        old.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -height * 0.7, Duration) { EasingFunction = ease });
        var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(180)));
        fadeOut.Completed += (_, _) => cell.Children.Remove(old);
        old.BeginAnimation(OpacityProperty, fadeOut);

        incoming.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(height * 0.7, 0, Duration) { EasingFunction = ease });
        incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220))));
    }
}
