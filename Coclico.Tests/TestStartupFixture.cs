#nullable enable
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class TestStartupFixture
    {
        public TestStartupFixture()
        {
            ServiceContainer.Build(null!);
        }
    }

    [CollectionDefinition("ServiceContainer")]
    public class ServiceContainerCollection : ICollectionFixture<TestStartupFixture> { }
}
