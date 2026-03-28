#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class InstalledProgramsServiceTests
    {
        [Fact]
        public void New_IsNotNull()
        {
            Assert.NotNull(new InstalledProgramsService());
        }

        [Fact]
        public void GetCategories_ReturnsNonNull()
        {
            var cats = new InstalledProgramsService().GetCategories();
            Assert.NotNull(cats);
        }

        [Fact]
        public void GetFilterGroups_ReturnsNonNull()
        {
            var groups = new InstalledProgramsService().GetFilterGroups();
            Assert.NotNull(groups);
        }

        [Fact]
        public void GetMemoryCacheIconPaths_ReturnsNonNull()
        {
            var paths = new InstalledProgramsService().GetMemoryCacheIconPaths();
            Assert.NotNull(paths);
        }

        [Fact]
        public void ProgramInfo_DefaultValues()
        {
            var info = new InstalledProgramsService.ProgramInfo();
            Assert.Equal(string.Empty, info.Name);
            Assert.Equal(string.Empty, info.InstallPath);
            Assert.Equal(string.Empty, info.ExePath);
            Assert.Equal("Windows", info.Source);
            Assert.Equal(string.Empty, info.Publisher);
            Assert.Equal(string.Empty, info.Version);
            Assert.Equal(0L, info.SizeBytes);
        }

        [Fact]
        public void DetectGames_WithEmptyList_ReturnsEmpty()
        {
            var games = InstalledProgramsService.DetectGames(new System.Collections.Generic.List<InstalledProgramsService.ProgramInfo>());
            Assert.NotNull(games);
            Assert.Empty(games);
        }

        [Fact]
        public void FilterGroup_DefaultIsStaticFalse()
        {
            var fg = new InstalledProgramsService.FilterGroup();
            Assert.False(fg.IsStatic);
        }

        [Fact]
        public async Task GetAllInstalledProgramsAsync_ReturnsNonNull()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var programs = await new InstalledProgramsService().GetAllInstalledProgramsAsync(
                forceRefresh: false, cancellationToken: cts.Token);
            Assert.NotNull(programs);
        }
    }
}
