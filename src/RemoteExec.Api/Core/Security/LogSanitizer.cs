using System.Text.RegularExpressions;

namespace RemoteExec.Api.Core.Security
{
    public static class LogSanitizer
    {
        // Regex to match "Authorization": "..."
        private static readonly Regex AuthorizationHeaderRegex = new Regex(
            @"(?i)(""Authorization""\s*:\s*"")([^""]+)("")", 
            RegexOptions.Compiled);

        // Regex to match "password": "..."
        private static readonly Regex PasswordJsonRegex = new Regex(
            @"(?i)(""password""\s*:\s*"")([^""]+)("")", 
            RegexOptions.Compiled);

        public static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var sanitized = input;
            
            // Mask Authorization
            sanitized = AuthorizationHeaderRegex.Replace(sanitized, "$1***REDACTED***$3");

            // Mask Password
            sanitized = PasswordJsonRegex.Replace(sanitized, "$1***REDACTED***$3");

            return sanitized;
        }
    }
}