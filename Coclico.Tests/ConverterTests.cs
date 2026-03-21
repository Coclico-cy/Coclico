using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Coclico.Converters;
using Xunit;

namespace Coclico.Tests
{
    public class HexToBrushConverterTests
    {
        private readonly HexToBrushConverter _sut = new();
        private static readonly CultureInfo _ci = CultureInfo.InvariantCulture;

        [Theory]
        [InlineData("#FF0000")]
        [InlineData("#00FF00")]
        [InlineData("#0000FF")]
        [InlineData("#7C3AED")]
        [InlineData("#BE185D")]
        [InlineData("#F97316")]
        public void ValidHex_ReturnsFrozenSolidColorBrush(string hex)
        {
            var result = _sut.Convert(hex, typeof(Brush), null!, _ci);
            var brush = Assert.IsType<SolidColorBrush>(result);
            Assert.True(brush.IsFrozen);
        }

        [Fact]
        public void ValidHex_CorrectColorValue()
        {
            var result = _sut.Convert("#FF0000", typeof(Brush), null!, _ci) as SolidColorBrush;
            Assert.NotNull(result);
            Assert.Equal(Colors.Red, result!.Color);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-color")]
        [InlineData("GGGGGG")]
        public void InvalidHex_ReturnsFallbackBrush(object? value)
        {
            var input = value;
            var result = _sut.Convert(input!, typeof(Brush), null!, _ci);
            Assert.IsType<SolidColorBrush>(result);
        }

        [Fact]
        public void NonStringValue_ReturnsFallbackBrush()
        {
            var result = _sut.Convert(42, typeof(Brush), null!, _ci);
            Assert.IsType<SolidColorBrush>(result);
        }

        [Fact]
        public void ConvertBack_ThrowsNotSupportedException()
        {
            Assert.Throws<NotSupportedException>(() =>
                _sut.ConvertBack(null!, typeof(string), null!, _ci));
        }
    }

    public class BooleanToVisibilityInvertedConverterTests
    {
        private readonly BooleanToVisibilityInvertedConverter _sut = new();
        private static readonly CultureInfo _ci = CultureInfo.InvariantCulture;

        [Fact]
        public void True_ReturnsCollapsed()
        {
            var result = _sut.Convert(true, typeof(Visibility), null!, _ci);
            Assert.Equal(Visibility.Collapsed, result);
        }

        [Fact]
        public void False_ReturnsVisible()
        {
            var result = _sut.Convert(false, typeof(Visibility), null!, _ci);
            Assert.Equal(Visibility.Visible, result);
        }

        [Fact]
        public void Null_DefaultsToVisible()
        {
            var result = _sut.Convert(null, typeof(Visibility), null!, _ci);
            Assert.Equal(Visibility.Visible, result);
        }

        [Fact]
        public void NonBool_DefaultsToVisible()
        {
            var result = _sut.Convert("string", typeof(Visibility), null!, _ci);
            Assert.Equal(Visibility.Visible, result);
        }

        [Fact]
        public void ConvertBack_Collapsed_ReturnsTrue()
        {
            var result = _sut.ConvertBack(Visibility.Collapsed, typeof(bool), null!, _ci);
            Assert.Equal(true, result);
        }

        [Fact]
        public void ConvertBack_Visible_ReturnsFalse()
        {
            var result = _sut.ConvertBack(Visibility.Visible, typeof(bool), null!, _ci);
            Assert.Equal(false, result);
        }

        [Fact]
        public void ConvertBack_NonVisibility_ThrowsNotSupportedException()
        {
            Assert.Throws<NotSupportedException>(() =>
                _sut.ConvertBack("not-visibility", typeof(bool), null!, _ci));
        }
    }

    public class EqualityToBooleanConverterTests
    {
        private readonly EqualityToBooleanConverter _sut = new();
        private static readonly CultureInfo _ci = CultureInfo.InvariantCulture;

        [Fact]
        public void EqualStrings_ReturnsTrue()
        {
            var result = _sut.Convert(new object[] { "hello", "hello" }, typeof(bool), null!, _ci);
            Assert.Equal(true, result);
        }

        [Fact]
        public void UnequalStrings_ReturnsFalse()
        {
            var result = _sut.Convert(new object[] { "hello", "world" }, typeof(bool), null!, _ci);
            Assert.Equal(false, result);
        }

        [Fact]
        public void EqualIntegers_ReturnsTrue()
        {
            var result = _sut.Convert(new object[] { 42, 42 }, typeof(bool), null!, _ci);
            Assert.Equal(true, result);
        }

        [Fact]
        public void BothNull_ReturnsTrue()
        {
            var result = _sut.Convert(new object?[] { null, null }, typeof(bool), null!, _ci);
            Assert.Equal(true, result);
        }

        [Fact]
        public void OneNull_ReturnsFalse()
        {
            var result = _sut.Convert(new object?[] { "value", null }, typeof(bool), null!, _ci);
            Assert.Equal(false, result);
        }

        [Fact]
        public void TooFewValues_ReturnsFalse()
        {
            var result = _sut.Convert(new object[] { "only_one" }, typeof(bool), null!, _ci);
            Assert.Equal(false, result);
        }

        [Fact]
        public void NullArray_ReturnsFalse()
        {
            var result = _sut.Convert(null!, typeof(bool), null!, _ci);
            Assert.Equal(false, result);
        }

        [Fact]
        public void ConvertBack_ThrowsNotSupportedException()
        {
            Assert.Throws<NotSupportedException>(() =>
                _sut.ConvertBack(true, Array.Empty<Type>(), null!, _ci));
        }
    }

    public class ObjectEqualsConverterTests
    {
        private readonly ObjectEqualsConverter _sut = new();
        private static readonly CultureInfo _ci = CultureInfo.InvariantCulture;

        [Fact]
        public void ValueEqualsParameter_ReturnsTrue()
        {
            var result = _sut.Convert("Dark", typeof(bool), "Dark", _ci);
            Assert.Equal(true, result);
        }

        [Fact]
        public void ValueNotEqualsParameter_ReturnsFalse()
        {
            var result = _sut.Convert("Light", typeof(bool), "Dark", _ci);
            Assert.Equal(false, result);
        }

        [Fact]
        public void BothNull_ReturnsTrue()
        {
            var result = _sut.Convert(null, typeof(bool), null!, _ci);
            Assert.Equal(true, result);
        }

        [Fact]
        public void IntegerEquality_ReturnsTrue()
        {
            var result = _sut.Convert(5, typeof(bool), 5, _ci);
            Assert.Equal(true, result);
        }

        [Fact]
        public void IntegerInequality_ReturnsFalse()
        {
            var result = _sut.Convert(3, typeof(bool), 5, _ci);
            Assert.Equal(false, result);
        }

        [Fact]
        public void ConvertBack_ThrowsNotSupportedException()
        {
            Assert.Throws<NotSupportedException>(() =>
                _sut.ConvertBack(true, typeof(string), null!, _ci));
        }
    }

    public class StringFirstCharConverterTests
    {
        private readonly StringFirstCharConverter _sut = new();
        private static readonly CultureInfo _ci = CultureInfo.InvariantCulture;

        [Theory]
        [InlineData("Alice", "A")]
        [InlineData("bob", "B")]
        [InlineData("z", "Z")]
        [InlineData("coclico", "C")]
        [InlineData("Ã©toile", "Ã")]
        public void ValidString_ReturnsUpperFirstChar(string input, string expected)
        {
            var result = _sut.Convert(input, typeof(string), null!, _ci);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void NullValue_ReturnsQuestionMark()
        {
            var result = _sut.Convert(null!, typeof(string), null!, _ci);
            Assert.Equal("?", result);
        }

        [Fact]
        public void EmptyString_ReturnsQuestionMark()
        {
            var result = _sut.Convert("", typeof(string), null!, _ci);
            Assert.Equal("?", result);
        }

        [Fact]
        public void NonStringValue_ReturnsQuestionMark()
        {
            var result = _sut.Convert(42, typeof(string), null!, _ci);
            Assert.Equal("?", result);
        }

        [Fact]
        public void ConvertBack_ThrowsNotSupportedException()
        {
            Assert.Throws<NotSupportedException>(() =>
                _sut.ConvertBack("A", typeof(string), null!, _ci));
        }
    }
}
