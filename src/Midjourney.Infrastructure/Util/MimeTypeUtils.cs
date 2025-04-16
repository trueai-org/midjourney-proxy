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
using System.Reflection;
using System.Text;

namespace Midjourney.Infrastructure.Util
{
    /// <summary>
    /// MIME类型工具类
    /// </summary>
    public static class MimeTypeUtils
    {
        private static readonly Dictionary<string, List<string>> MimeTypeMap;

        static MimeTypeUtils()
        {
            MimeTypeMap = new Dictionary<string, List<string>>();
            var assembly = Assembly.GetExecutingAssembly();
            var assemblyName = assembly.GetName().Name;

            var resourceName = $"{assemblyName}.Resources.mime.types";
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }
                    var arr = line.Split(':');
                    MimeTypeMap[arr[0]] = arr[1].Split(' ').ToList();
                }
            }
        }

        /// <summary>
        /// 猜测文件后缀
        /// </summary>
        /// <param name="mimeType">MIME类型</param>
        /// <returns>文件后缀</returns>
        public static string GuessFileSuffix(string mimeType)
        {
            if (string.IsNullOrWhiteSpace(mimeType))
            {
                return null;
            }

            if (!MimeTypeMap.ContainsKey(mimeType))
            {
                mimeType = MimeTypeMap.Keys.FirstOrDefault(k => mimeType.StartsWith(k, StringComparison.OrdinalIgnoreCase));
            }

            if (mimeType == null || !MimeTypeMap.TryGetValue(mimeType, out var suffixList) || !suffixList.Any())
            {
                return null;
            }

            return suffixList.FirstOrDefault();
        }
    }
}