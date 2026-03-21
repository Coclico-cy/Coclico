using System;
using System.Linq;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class SecretScanServiceTests
    {
        [Fact]
        public void ScanText_DetectsInsensitiveApiKeyPattern()
        {
            var input = "apiKey=AbCdEfGhIjKlMnOpQrStUvWxYz012345";
            var findings = SecretScanService.ScanText(input);

            Assert.NotEmpty(findings);
            var finding = findings.First();
            Assert.Equal(1, finding.LineNumber);
            Assert.Contains("api[_-]?key", finding.Pattern, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ScanText_IgnoresRegularCodeWithoutSecrets()
        {
            var input = "// This is a comments line with token word in documentation text.";
            var findings = SecretScanService.ScanText(input);

            Assert.Empty(findings);
        }

        [Fact]
        public void ScanText_DetectsBearerToken()
        {
            var input = "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9abcdef1234567890";
            var findings = SecretScanService.ScanText(input);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.LineText.Contains("Bearer"));
        }
    }
}
