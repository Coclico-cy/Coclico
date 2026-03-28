#nullable enable
using Coclico.Services.Algorithms;
using Xunit;

namespace Coclico.Tests
{
    public class IsolationForestTests
    {
        [Fact]
        public void DetectsObviousAnomaly()
        {
            var detector = new IsolationForestDetector(numTrees: 50, subsampleSize: 32);
            // Feed 50 normal samples
            for (int i = 0; i < 50; i++)
                detector.Observe(50 + Random.Shared.NextDouble() * 10, 60 + Random.Shared.NextDouble() * 5);
            // Feed an anomaly
            var (score, isAnomaly, _) = detector.Observe(99.0, 99.0);
            Assert.True(score > 0.5);
        }

        [Fact]
        public void NotReadyBeforeMinSamples()
        {
            var detector = new IsolationForestDetector(numTrees: 10, subsampleSize: 16);
            var (score, _, _) = detector.Observe(50.0, 60.0);
            Assert.Equal(-1, score);
        }
    }

    public class TDigestTests
    {
        [Fact]
        public void MedianIsAccurate()
        {
            var td = new TDigest();
            for (int i = 1; i <= 100; i++) td.Add(i);
            var median = td.Quantile(0.5);
            Assert.InRange(median, 48, 52);
        }

        [Fact]
        public void P99IsAccurate()
        {
            var td = new TDigest();
            for (int i = 1; i <= 1000; i++) td.Add(i);
            var p99 = td.Quantile(0.99);
            Assert.InRange(p99, 985, 999);
        }

        [Fact]
        public void QuantileAvailableAfterAdditions()
        {
            var td = new TDigest();
            for (int i = 0; i < 50; i++) td.Add(i);
            // Quantile() forces a flush — verifies the digest is usable
            var q50 = td.Quantile(0.5);
            Assert.True(q50 > 0 && q50 < 50);
        }
    }

    public class SimpleStemmerTests
    {
        [Theory]
        [InlineData("nettoyages", "nettoy")]
        [InlineData("applications", "applic")]
        [InlineData("cleaning", "clean")]
        public void StemsCorrectly(string input, string expected)
        {
            Assert.Equal(expected, SimpleStemmer.Stem(input));
        }

        [Theory]
        [InlineData("le")]
        [InlineData("the")]
        [InlineData("de")]
        public void DetectsStopWords(string word)
        {
            Assert.True(SimpleStemmer.IsStopWord(word));
        }

        [Fact]
        public void ShortWordNotStemmed()
        {
            Assert.Equal("cpu", SimpleStemmer.Stem("cpu"));
        }
    }

    public class MethodReplacerTests
    {
        [Fact]
        public void ReplacesCorrectMethod()
        {
            var source = @"
class Foo {
    void Bar() { Console.WriteLine(""old""); }
    void Baz() { Console.WriteLine(""keep""); }
}";
            var newMethod = @"void Bar() { Console.WriteLine(""new""); }";
            var result = MethodReplacer.ReplaceMethod(source, "Foo", "Bar", newMethod);
            Assert.NotNull(result);
            Assert.Contains("\"new\"", result);
            Assert.Contains("\"keep\"", result);
        }

        [Fact]
        public void ReturnsNullIfMethodNotFound()
        {
            var source = "class Foo { void Bar() { } }";
            var result = MethodReplacer.ReplaceMethod(source, "Foo", "NonExistent", "void X() {}");
            Assert.Null(result);
        }

        [Fact]
        public void ReturnsNullIfClassNotFound()
        {
            var source = "class Foo { void Bar() { } }";
            var result = MethodReplacer.ReplaceMethod(source, "WrongClass", "Bar", "void Bar() {}");
            Assert.Null(result);
        }
    }
}
