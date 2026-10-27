using Coclico.Security.Infrastructure;
using Xunit;

namespace Coclico.Tests;

public sealed class PathValidatorTests
{
    [Theory]
    [InlineData("Coclico.exe")]
    [InlineData("Coclico.Core.dll")]
    [InlineData("runtimes/win-x64/native/cuda.dll")]
    [InlineData("sub/dir/file.txt")]
    public void SafePathsAreAccepted(string path)
    {
        Assert.True(PathValidator.IsSafeRelativePath(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../evil.dll")]
    [InlineData("a/../../evil.dll")]
    [InlineData("/absolute.dll")]
    [InlineData("\\absolute.dll")]
    [InlineData("//server/share/evil.dll")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("CON.dll")]
    [InlineData("aux.dll")]
    [InlineData("nul.txt")]
    [InlineData("COM1.dll")]
    [InlineData("trailing. ")]
    [InlineData("trailing .dll ")]
    [InlineData("bad|char.dll")]
    [InlineData("bad?char.dll")]
    [InlineData("bad<char.dll")]
    [InlineData("a//b.dll")]
    [InlineData("a/./b.dll")]
    [InlineData("..")]
    [InlineData(".")]
    public void UnsafePathsAreRejected(string path)
    {
        Assert.False(PathValidator.IsSafeRelativePath(path));
    }

    [Fact]
    public void WindowsStyleSeparatorIsAcceptedAndCompared()
    {
        Assert.True(PathValidator.IsSafeRelativePath(@"runtimes\win-x64\lib.dll"));
        Assert.Equal(PathValidator.NormalizeForCompare(@"a\B.txt"), PathValidator.NormalizeForCompare("a/b.TXT"));
    }

    [Fact]
    public void DuplicatesAreDetectedCaseInsensitively()
    {
        List<string> paths = ["Coclico.exe", "coclico.EXE", "other.dll"];
        Assert.True(PathValidator.HasDuplicates(paths, out string duplicate));
        Assert.Equal("coclico.EXE", duplicate);
    }
}
