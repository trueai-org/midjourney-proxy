#nullable enable

using Midjourney.Base.Models;

namespace Midjourney.Base.Util
{
    /// <summary>
    /// Stable task correlation fallbacks for final Discord messages that no
    /// longer carry the original message or interaction id.
    /// </summary>
    public static class ConservativeTaskMatcher
    {
        public static TaskInfo? FindUniqueBySeed(
            IEnumerable<TaskInfo> candidates,
            string? seed,
            out int matchCount)
        {
            matchCount = 0;
            if (string.IsNullOrWhiteSpace(seed))
            {
                return null;
            }

            TaskInfo? match = null;
            foreach (var candidate in candidates)
            {
                if (!string.Equals(candidate.Seed, seed, StringComparison.Ordinal))
                {
                    continue;
                }

                matchCount++;
                match = matchCount == 1 ? candidate : null;
            }

            return matchCount == 1 ? match : null;
        }
    }
}
