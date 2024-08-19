// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
// Copyright (C) 2024 trueai.org

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// Additional Terms:
// This software shall not be used for any illegal activities. 
// Users must comply with all applicable laws and regulations,
// particularly those related to image and video processing. 
// The use of this software for any form of illegal face swapping,
// invasion of privacy, or any other unlawful purposes is strictly prohibited. 
// Violation of these terms may result in termination of the license and may subject the violator to legal action.
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