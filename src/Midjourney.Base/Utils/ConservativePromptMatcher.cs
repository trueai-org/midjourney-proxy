namespace Midjourney.Base.Util
{
    /// <summary>
    /// Conservative fallback for Discord bot messages whose rendered prompt
    /// was truncated. Exact message/interaction ids and exact prompt matches
    /// must be attempted before this matcher.
    /// </summary>
    public static class ConservativePromptMatcher
    {
        public const int MinimumComparableLength = 256;
        public const double MinimumCoverage = 0.5;

        public static bool IsMatch(string submittedPrompt, string returnedPrompt)
        {
            var submitted = Normalize(submittedPrompt);
            var returned = Normalize(returnedPrompt);
            if (string.IsNullOrWhiteSpace(submitted) || string.IsNullOrWhiteSpace(returned))
            {
                return false;
            }

            if (submitted.Equals(returned, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var shorter = submitted.Length <= returned.Length ? submitted : returned;
            var longer = submitted.Length <= returned.Length ? returned : submitted;
            shorter = TrimTruncationMarker(shorter);

            if (shorter.Length < MinimumComparableLength)
            {
                return false;
            }

            if ((double)shorter.Length / longer.Length < MinimumCoverage)
            {
                return false;
            }

            return longer.StartsWith(shorter, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string prompt)
        {
            return MjPromptParser.Parse(prompt)?.CleanPromptNormalized?.Trim() ?? string.Empty;
        }

        private static string TrimTruncationMarker(string prompt)
        {
            var value = prompt.TrimEnd().TrimEnd('\u2026').TrimEnd();
            while (value.EndsWith("...", StringComparison.Ordinal))
            {
                value = value[..^3].TrimEnd();
            }
            return value;
        }
    }
}
