using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Util
{
    public static class ConvertUtils
    {
        /// <summary>
        /// content正则匹配prompt和进度.
        /// </summary>
        public const string CONTENT_REGEX = ".*?\\*\\*(.*)\\*\\*.+<@\\d+> \\((.*?)\\)";

        /// <summary>
        /// 匹配 action 的正则表达式
        /// 1. 匹配 action
        /// 2. 匹配 blend
        /// </summary>
        private const string CONTENT_REGEX_ACTION = @"\*\*(.*?)\*\* - (.*?)<@(\d+)> \((.*?)\)";

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

        public static ContentActionData ParseActionContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }
            var match = Regex.Match(content, CONTENT_REGEX_ACTION);
            if (match.Success)
            {
                string description = match.Groups[1].Value;
                string action = match.Groups[2].Value;
                string userId = match.Groups[3].Value;
                string mode = match.Groups[4].Value;

                if (TryMapToTaskAction(action, out var act))
                {
                    return new ContentActionData()
                    {
                        Prompt = description,
                        Action = act,
                        UserId = userId,
                        Mode = mode
                    };
                }
            }
            return null;
        }

        private static bool TryMapToTaskAction(string action, out TaskAction taskAction)
        {
            // 标准化action字符串
            action = action.Trim().ToUpper().Split(' ').FirstOrDefault();

            switch (action)
            {
                case "IMAGINE":
                    taskAction = TaskAction.IMAGINE;
                    break;

                case "UPSCALE":
                case "UPSCALED":
                    taskAction = TaskAction.UPSCALE;
                    break;

                case "VARIATION":
                case "VARIATIONS":
                    taskAction = TaskAction.VARIATION;
                    break;

                case "REROLL":
                    taskAction = TaskAction.REROLL;
                    break;

                case "DESCRIBE":
                    taskAction = TaskAction.DESCRIBE;
                    break;

                case "BLEND":
                    taskAction = TaskAction.BLEND;
                    break;

                case "PAN":
                    taskAction = TaskAction.PAN;
                    break;

                case "OUTPAINT":
                    taskAction = TaskAction.OUTPAINT;
                    break;

                case "INPAINT":
                    taskAction = TaskAction.INPAINT;
                    break;

                case "ZOOM":
                    taskAction = TaskAction.ZOOM;
                    break;

                case "ACTION":
                    taskAction = TaskAction.ACTION;
                    break;

                default:
                    taskAction = TaskAction.ACTION;
                    return true;
            }
            return true;
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

    public class ContentActionData
    {
        public string Prompt { get; set; }

        public TaskAction Action { get; set; } = TaskAction.ACTION;

        public string UserId { get; set; }

        /// <summary>
        ///
        /// </summary>
        public string Mode { get; set; }
    }

    public class TaskChangeParams
    {
        public string Id { get; set; }
        public TaskAction Action { get; set; }
        public int Index { get; set; }
    }
}