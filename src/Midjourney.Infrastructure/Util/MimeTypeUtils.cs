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

            return suffixList.First();
        }
    }
}