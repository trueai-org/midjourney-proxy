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
                var resourceStream = assembly.GetManifestResourceStream($"{assemblyName}.Resources.bannedWords.txt");
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

        /// <summary>
        /// 获取禁用词列表
        /// </summary>
        /// <returns></returns>
        public static List<string> GetStrings()
        {
            return BANNED_WORDS;
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