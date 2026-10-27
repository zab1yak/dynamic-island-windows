using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace DynamicIsland.UI.Controls;

/// <summary>Compact "− value +" number input with bounds, in the style of iOS steppers.</summary>
public sealed class NumberStepper : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(NumberStepper),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, null, (d, v) => ((NumberStepper)d).Clamp((int)v)));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(int), typeof(NumberStepper), new PropertyMetadata(0));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(NumberStepper), new PropertyMetadata(100));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(int), typeof(NumberStepper), new PropertyMetadata(1));

    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(
        nameof(Suffix), typeof(string), typeof(NumberStepper), new PropertyMetadata(string.Empty));

    static NumberStepper()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NumberStepper), new FrameworkPropertyMetadata(typeof(NumberStepper)));
    }

    public NumberStepper()
    {
        CommandBindings.Add(new CommandBinding(Increase, (_, _) => Value += Step));
        CommandBindings.Add(new CommandBinding(Decrease, (_, _) => Value -= Step));
    }

    /// <summary>Increments by <see cref="Step"/>.</summary>
    public static RoutedCommand Increase { get; } = new(nameof(Increase), typeof(NumberStepper));

    /// <summary>Decrements by <see cref="Step"/>.</summary>
    public static RoutedCommand Decrease { get; } = new(nameof(Decrease), typeof(NumberStepper));

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int Step
    {
        get => (int)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    /// <summary>Unit shown after the number ("min", "ml").</summary>
    public string Suffix
    {
        get => (string)GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    private int Clamp(int value) => Math.Clamp(value, Minimum, Math.Max(Minimum, Maximum));
}

/// <summary>Formats a stepper value with its suffix.</summary>
public sealed class ValueWithSuffixConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 ? $"{values[0]} {values[1]}".Trim() : string.Empty;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
