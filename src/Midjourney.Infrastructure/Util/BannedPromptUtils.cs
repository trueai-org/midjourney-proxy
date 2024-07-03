using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Util
{
    public static class BannedPromptUtils
    {
        private static readonly List<string> BANNED_WORDS;

        static BannedPromptUtils()
        {
            List<string> lines;
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources/bannedWords.txt");
            if (File.Exists(path))
            {
                lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
            }
            else
            {
                var assembly = typeof(BannedPromptUtils).Assembly;
                var assemblyName = assembly.GetName().Name;
                var resourceStream = assembly.GetManifestResourceStream($"{assembly}.Resources.bannedWords.txt");
                if (resourceStream != null)
                {
                    using (var reader = new StreamReader(resourceStream, Encoding.UTF8))
                    {
                        lines = reader.ReadToEnd().Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();
                    }
                }
                else
                {
                    lines = new List<string>();
                }
            }

            BANNED_WORDS = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        }

        public static void CheckBanned(string promptEn)
        {
            string finalPromptEn = promptEn.ToLower(CultureInfo.InvariantCulture);
            foreach (string word in BANNED_WORDS)
            {
                var regex = new Regex($"\\b{Regex.Escape(word)}\\b", RegexOptions.IgnoreCase);
                var match = regex.Match(finalPromptEn);
                if (match.Success)
                {
                    int index = finalPromptEn.IndexOf(word, StringComparison.OrdinalIgnoreCase);

                    throw new BannedPromptException(promptEn.Substring(index, word.Length));
                }
            }
        }
    }

    public class BannedPromptException : Exception
    {
        public BannedPromptException(string message) : base(message)
        {
        }
    }
}