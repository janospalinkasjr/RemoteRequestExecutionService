using System.Text.RegularExpressions;

namespace RemoteExec.Api.Core.Security
{
    public static class LogSanitizer
    {
        private static readonly Regex AuthorizationHeaderRegex = new Regex(
            @"(?i)(""Authorization""\s*:\s*"")([^""]+)("")", 
            RegexOptions.Compiled);

        private static readonly Regex PasswordJsonRegex = new Regex(
            @"(?i)(""password""\s*:\s*"")([^""]+)("")", 
            RegexOptions.Compiled);

        public static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var sanitized = input;
            
            sanitized = AuthorizationHeaderRegex.Replace(sanitized, "$1***REDACTED***$3");

            sanitized = PasswordJsonRegex.Replace(sanitized, "$1***REDACTED***$3");

            return sanitized;
        }
    }
}