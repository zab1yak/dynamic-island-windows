using System.Windows.Data;
using DynamicIsland.UI.Controls;

namespace DynamicIsland.Tests.UI;

public sealed class NumberStepperTests
{
    private sealed class Source
    {
        public int Value { get; set; } = 450;
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("STA test failed", failure);
        }
    }

    [Fact]
    public void A_bound_value_is_not_clamped_before_the_bounds_are_set()
    {
        OnSta(() =>
        {
            // XAML order in the pages: Value binding first, then Minimum and Maximum (v0.2.1: 450 ms became 100 ms).
            var source = new Source();
            var stepper = new NumberStepper();
            stepper.SetBinding(NumberStepper.ValueProperty, new Binding(nameof(Source.Value)) { Source = source, Mode = BindingMode.TwoWay });
            stepper.Minimum = 100;
            stepper.Maximum = 5000;

            Assert.Equal(450, stepper.Value);
            Assert.Equal(450, source.Value);
        });
    }

    [Fact]
    public void Out_of_range_values_are_clamped_once_the_bounds_are_known()
    {
        OnSta(() =>
        {
            var stepper = new NumberStepper { Value = 9000 };
            stepper.Minimum = 0;
            stepper.Maximum = 2000;
            Assert.Equal(2000, stepper.Value);
        });
    }
}
