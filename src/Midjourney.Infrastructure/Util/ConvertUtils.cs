using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Util
{
    public static class ConvertUtils
    {
        /**
         * content正则匹配prompt和进度.
         */
        public const string CONTENT_REGEX = ".*?\\*\\*(.*)\\*\\*.+<@\\d+> \\((.*?)\\)";

        public static ContentParseData ParseContent(string content)
        {
            return ParseContent(content, CONTENT_REGEX);
        }

        public static ContentParseData ParseContent(string content, string regex)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }
            var matcher = Regex.Match(content, regex);
            if (!matcher.Success)
            {
                return null;
            }
            var parseData = new ContentParseData
            {
                Prompt = matcher.Groups[1].Value,
                Status = matcher.Groups[2].Value
            };
            return parseData;
        }

        public static List<DataUrl> ConvertBase64Array(List<string> base64Array)
        {
            if (base64Array == null || base64Array.Count == 0)
            {
                return new List<DataUrl>();
            }

            var dataUrlList = new List<DataUrl>();
            foreach (var base64 in base64Array)
            {
                var dataUrl = DataUrl.Parse(base64);
                dataUrlList.Add(dataUrl);
            }
            return dataUrlList;
        }

        public static string GetPrimaryPrompt(string prompt)
        {
            var matcher = Regex.Replace(prompt, @"\x20+--[a-z]+.*$", "", RegexOptions.IgnoreCase);
            var regex = @"https?://[-a-zA-Z0-9+&@#/%?=~_|!:,.;]*[-a-zA-Z0-9+&@#/%=~_|]";
            matcher = Regex.Replace(matcher, regex, "<link>");
            return matcher.Replace("<<link>>", "<link>");
        }

        public static TaskChangeParams ConvertChangeParams(string content)
        {
            var split = content.Split(' ').ToList();
            if (split.Count != 2)
            {
                return null;
            }
            var action = split[1].ToLower();
            var changeParams = new TaskChangeParams
            {
                Id = split[0]
            };
            if (action.StartsWith('u'))
            {
                changeParams.Action = TaskAction.UPSCALE;
            }
            else if (action.StartsWith('v'))
            {
                changeParams.Action = TaskAction.VARIATION;
            }
            else if (action.Equals("r"))
            {
                changeParams.Action = TaskAction.REROLL;
                return changeParams;
            }
            else
            {
                return null;
            }
            try
            {
                var index = int.Parse(action.Substring(1, 1));
                if (index < 1 || index > 4)
                {
                    return null;
                }
                changeParams.Index = index;
            }
            catch (Exception)
            {
                return null;
            }
            return changeParams;
        }
    }

    public class ContentParseData
    {
        public string Prompt { get; set; }
        public string Status { get; set; }
    }

    public class TaskChangeParams
    {
        public string Id { get; set; }
        public TaskAction Action { get; set; }
        public int Index { get; set; }
    }
}