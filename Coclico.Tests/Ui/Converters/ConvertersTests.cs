using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Coclico.Converters;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class ConvertersTests
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    [Fact]
    public void EqualityMulti_EqualValues_ReturnsTrue()
    {
        var converter = new EqualityToBoolConverter();

        object result = converter.Convert(new object?[] { "meme", "meme" }, typeof(bool), parameter: null!, Fr);

        Assert.True((bool)result);
    }

    [Fact]
    public void EqualityMulti_DifferentValues_ReturnsFalse()
    {
        var converter = new EqualityToBoolConverter();

        object result = converter.Convert(new object?[] { "a", "b" }, typeof(bool), parameter: null!, Fr);

        Assert.False((bool)result);
    }

    [Fact]
    public void EqualityMulti_UnsetValue_ReturnsFalse()
    {
        var converter = new EqualityToBoolConverter();

        object result = converter.Convert(
            new object?[] { DependencyProperty.UnsetValue, "a" }, typeof(bool), parameter: null!, Fr);

        Assert.False((bool)result);
    }

    [Fact]
    public void EqualityMulti_NullArray_ReturnsFalse()
    {
        var converter = new EqualityToBoolConverter();

        object result = converter.Convert(values: null, typeof(bool), parameter: null!, Fr);

        Assert.False((bool)result);
    }

    [Fact]
    public void EqualityMulti_SingleValue_ReturnsFalse()
    {
        var converter = new EqualityToBoolConverter();

        object result = converter.Convert(new object?[] { "a" }, typeof(bool), parameter: null!, Fr);

        Assert.False((bool)result);
    }

    [Fact]
    public void EqualityMulti_ThreeUnequalValues_ReturnsFalse()
    {
        var converter = new EqualityToBoolConverter();

        object result = converter.Convert(new object?[] { "a", "b", "c" }, typeof(bool), parameter: null!, Fr);

        Assert.False((bool)result);
    }

    [Fact]
    public void EqualitySingle_CaseInsensitiveComparisonWithParameter()
    {
        var converter = new EqualityToBoolConverter();

        object result = converter.Convert("EN", typeof(bool), "en", Fr);

        Assert.True((bool)result);
    }

    [Theory]
    [InlineData(null, "en")]
    [InlineData("en", null)]
    public void EqualitySingle_NullValueOrParameter_ReturnsFalse(object? value, object? parameter)
    {
        var converter = new EqualityToBoolConverter();

        object result = converter.Convert(value!, typeof(bool), parameter!, Fr);

        Assert.False((bool)result);
    }

    [Fact]
    public void EqualitySingle_ConvertBackParsesEnumTargetType()
    {
        var converter = new EqualityToBoolConverter();

        object? result = converter.ConvertBack(true, typeof(LaunchMode), "Tray", Fr);

        Assert.Equal(LaunchMode.Tray, result);
    }

    [Fact]
    public void EqualitySingle_ConvertBackWithFalse_ReturnsDoNothing()
    {
        var converter = new EqualityToBoolConverter();

        object? result = converter.ConvertBack(false, typeof(LaunchMode), "Tray", Fr);

        Assert.Equal(Binding.DoNothing, result);
    }

    [Theory]
    [InlineData("martin", "M")]
    [InlineData("  elodie  ", "E")]
    [InlineData("école", "É")]
    public void FirstChar_ReturnsUppercasedInitial(string input, string expected)
    {
        var converter = new FirstCharConverter();

        object result = converter.Convert(input, typeof(string), parameter: null!, Fr);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FirstChar_EmptyOrNull_ReturnsQuestionMark(object? input)
    {
        var converter = new FirstCharConverter();

        object result = converter.Convert(input!, typeof(string), parameter: null!, Fr);

        Assert.Equal("?", result);
    }

    [Fact]
    public void FirstChar_ConvertBack_Throws()
    {
        var converter = new FirstCharConverter();

        Assert.Throws<NotSupportedException>(
            () => converter.ConvertBack("M", typeof(string), parameter: null!, Fr));
    }

    [Fact]
    public void HexBrush_ValidHex_ReturnsFrozenBrushWithThatColor()
    {
        var converter = new HexToBrushConverter();

        object result = converter.Convert("#FFFF0000", typeof(Brush), parameter: null!, Fr);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.FromRgb(255, 0, 0), brush.Color);
        Assert.True(brush.IsFrozen);
    }

    [Theory]
    [InlineData("not-a-color")]
    [InlineData(42)]
    public void HexBrush_InvalidInput_ReturnsGrayFallback(object input)
    {
        var converter = new HexToBrushConverter();

        object result = converter.Convert(input, typeof(Brush), parameter: null!, Fr);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.Gray, brush.Color);
    }

    [Fact]
    public void HexBrush_SameHexTwice_ReturnsCachedInstance()
    {
        var converter = new HexToBrushConverter();

        object first = converter.Convert("#FF00FF00", typeof(Brush), parameter: null!, Fr);
        object second = converter.Convert("#FF00FF00", typeof(Brush), parameter: null!, Fr);

        Assert.Same(first, second);
    }

    [Fact]
    public void HexBrush_ConvertBack_Throws()
    {
        var converter = new HexToBrushConverter();

        Assert.Throws<NotSupportedException>(
            () => converter.ConvertBack(Brushes.Red, typeof(string), parameter: null!, Fr));
    }

    [Theory]
    [InlineData(true, Visibility.Collapsed)]
    [InlineData(false, Visibility.Visible)]
    [InlineData(null, Visibility.Visible)]
    public void InverseBool_TrueCollapses_FalseOrNullShows(object? input, Visibility expected)
    {
        var converter = new InverseBoolToVisibilityConverter();

        object result = converter.Convert(input!, typeof(Visibility), parameter: null!, Fr);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(Visibility.Visible, false)]
    [InlineData(Visibility.Collapsed, true)]
    [InlineData(Visibility.Hidden, true)]
    public void InverseBool_ConvertBackInverts(Visibility input, bool expected)
    {
        var converter = new InverseBoolToVisibilityConverter();

        object result = converter.ConvertBack(input, typeof(bool), parameter: null!, Fr);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void InverseBool_ConvertBackNonVisibility_Throws()
    {
        var converter = new InverseBoolToVisibilityConverter();

        Assert.Throws<NotSupportedException>(
            () => converter.ConvertBack(33, typeof(bool), parameter: null!, Fr));
    }
}
