using RemoteExec.Api.Core.Security;
using Xunit;

namespace RemoteExec.Tests.Unit.Infrastructure.Security
{
    public class LogSanitizerTests
    {
        [Fact]
        public void Sanitize_MasksAuthorizationHeader()
        {
            // Arrange
            var input = "GET /api/resource HTTP/1.1\r\n\"Authorization\": \"Bearer seyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9\"\r\nHost: example.com";
            
            // Act
            var result = LogSanitizer.Sanitize(input);

            // Assert
            Assert.DoesNotContain("seyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", result);
            Assert.Contains("\"Authorization\": \"***REDACTED***\"", result);
        }

        [Fact]
        public void Sanitize_MasksPasswordJson()
        {
            // Arrange
            var input = "{ \"username\": \"admin\", \"password\": \"SuperSecret123!\", \"role\": \"admin\" }";
            
            // Act
            var result = LogSanitizer.Sanitize(input);

            // Assert
            Assert.DoesNotContain("SuperSecret123!", result);
            Assert.Contains("\"password\": \"***REDACTED***\"", result);
            Assert.Contains("\"username\": \"admin\"", result);
        }

        [Fact]
        public void Sanitize_ReturnsOriginalIfSafe()
        {
            // Arrange
            var input = "{ \"message\": \"Hello World\", \"status\": \"ok\" }";
            
            // Act
            var result = LogSanitizer.Sanitize(input);

            // Assert
            Assert.Equal(input, result);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void Sanitize_HandlesEmptyOrNull(string? input)
        {
            // Act
            var result = LogSanitizer.Sanitize(input!);

            // Assert
            Assert.Equal(input, result);
        }

        [Fact]
        public void Sanitize_MasksCaseInsensitive()
        {
             // Arrange
            var input = "{ \"PASSWORD\": \"Secret\" }";
            
            // Act
            var result = LogSanitizer.Sanitize(input);

            // Assert
            Assert.Contains("\"PASSWORD\": \"***REDACTED***\"", result);
        }
    }
}
