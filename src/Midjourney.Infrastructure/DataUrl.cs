using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure
{
    public class DataUrl
    {
        public string MimeType { get; private set; }
        public byte[] Data { get; private set; }

        public DataUrl(string mimeType, byte[] data)
        {
            MimeType = mimeType;
            Data = data;
        }

        public static DataUrl Parse(string dataUrl)
        {
            var match = Regex.Match(dataUrl, @"data:(?<type>.+?);base64,(?<data>.+)");
            if (!match.Success)
            {
                throw new FormatException("Invalid data URL format");
            }

            string mimeType = match.Groups["type"].Value;
            byte[] data = Convert.FromBase64String(match.Groups["data"].Value);

            return new DataUrl(mimeType, data);
        }

        public override string ToString()
        {
            string base64Data = Convert.ToBase64String(Data);
            return $"data:{MimeType};base64,{base64Data}";
        }
    }
}