using System.Text;

namespace Midjourney.Infrastructure.Util
{
    public static class WordsUtils
    {
        private static readonly List<string> WORDS;
        private static readonly List<string> WORDS_FULL;

        static WordsUtils()
        {
            List<string> lines;

            var assembly = typeof(WordsUtils).Assembly;
            var assemblyName = assembly.GetName().Name;
            var resourceStream = assembly.GetManifestResourceStream($"{assemblyName}.Resources.words.txt");

            if (resourceStream != null)
            {
                using (var reader = new StreamReader(resourceStream, Encoding.UTF8))
                {
                    lines = reader.ReadToEnd().Split(new[] { "\r\n", "\r", "\n", "," }, StringSplitOptions.None).ToList();
                }
            }
            else
            {
                lines = new List<string>();
            }

            WORDS = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

            // Full words
            var resourceStream2 = assembly.GetManifestResourceStream($"{assemblyName}.Resources.wordsfull.txt");
            if (resourceStream2 != null)
            {
                using (var reader = new StreamReader(resourceStream2, Encoding.UTF8))
                {
                    lines = reader.ReadToEnd().Split(new[] { "\r\n", "\r", "\n", "," }, StringSplitOptions.None).ToList();
                }
            }
            else
            {
                lines = new List<string>();
            }

            WORDS_FULL = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        }

        public static List<string> GetWords()
        {
            return WORDS;
        }

        public static List<string> GetWordsFull()
        {
            return WORDS_FULL;
        }
    }
}