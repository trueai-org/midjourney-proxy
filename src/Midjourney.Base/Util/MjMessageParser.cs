using System.Text.RegularExpressions;

namespace Midjourney.Base.Util
{
    /// <summary>
    /// Midjourney 消息解析器
    /// </summary>
    public static class MjMessageParser
    {
        /// <summary>
        /// 匹配完整的 prompt 内容
        /// </summary>
        public const string CONTENT_FULL = @"\*\*(.*)\*\*";

        /// <summary>
        /// 模式匹配 fast, relaxed, turbo 可选 stealth 模式
        /// </summary>
        public const string MODE_PATTERN = @"(fast|relaxed|turbo)(?:,\s*stealth)?";

        /// <summary>
        /// 进度匹配 (0%)/(45%)/(100%)
        /// </summary>
        public const string PROGRESS_PATTERN = @"(?:\((\d+%)\)\s*)?";

        /// <summary>
        /// Remix/Variation 变体操作
        /// Variations 消息格式: **prompt** - Variations (type) by @user (mode)
        /// emix 消息格式: **prompt** - Remix (type) by @user (mode)
        /// <para>捕获组: 1=prompt, 2=Remix|Variations, 3=type(Subtle|Strong|Region,可选), 4=userId, 5=progress(可选), 6=mode</para>
        /// </summary>
        /// <example>
        /// <![CDATA[
        /// **car --v 7.0** - Variations (Strong) by <@123456789> (fast)
        /// **car --v 7.0** - Variations (Subtle) by <@123456789> (relaxed)
        /// **car --v 7.0** - Variations (Region) by <@123456789> (turbo)
        /// **car --v 7.0** - Variations by <@123456789> (fast)
        /// **portrait --niji 6** - Variations (Strong) by <@987654321> (relaxed)
        /// **car --ar 16:9 --v 6.0 --s 750 --style raw** - Remix (Subtle) by <@1300571410770427945> (relaxed)
        /// **car --v 7.0** - Remix (Strong) by <@123456789> (fast)
        /// **car --v 7.0** - Remix (Region) by <@123456789> (turbo)
        /// **car --v 7.0** - Remix by <@123456789> (fast)
        /// ]]>
        /// </example>
        public const string VARIATIONS = @$"\*\*(.+?)\*\* - (Remix|Variations)\s*(?:\((Subtle|Strong|Region)\))?\s*by <@(\d+)>\s*{PROGRESS_PATTERN}\({MODE_PATTERN}\)";

        /// <summary>
        /// Pan 平移操作消息格式: **prompt** - Pan (direction) by @user (mode)
        /// <para>捕获组: 1=prompt, 2=direction(Left|Right|Up|Down), 3=userId, 4=progress(可选), 5=mode</para>
        /// </summary>
        /// <example>
        /// <![CDATA[
        /// **car an. --niji 6 --relax --ar 5:4** - Pan Right by <@1270516632128720930> (relaxed)
        /// **car, soft. --niji 6 --relax --ar 5:4** - Pan Left by <@1270955406314831882> (relaxed)
        /// **car --iw 2 --style raw --s 250 --relax --ar 45:103** - Pan Down by <@1275009228410458170> (relaxed)
        /// **car --relax --ar 17:31** - Pan Up by <@1240647735804301404> (relaxed)
        /// **landscape --v 7.0** - Pan Left by <@123456789> (fast)
        /// ]]>
        /// </example>
        public const string PAN = @$"\*\*(.+?)\*\* - Pan (Left|Right|Up|Down) by <@(\d+)>\s*{PROGRESS_PATTERN}\({MODE_PATTERN}\)";

        /// <summary>
        /// Zoom 缩放操作消息格式: **prompt** - Zoom Out by @user (mode)
        /// <para>捕获组: 1=prompt, 2=userId, 3=progress(可选), 4=mode</para>
        /// </summary>
        /// <example>
        /// <![CDATA[
        /// **Magical forest, purple theme, butterflys --v 6.0 --ar 16:9 --relax** - Zoom Out by <@1258981874265493627> (relaxed)
        /// **landscape --v 7.0** - Zoom Out by <@123456789> (fast)
        /// **portrait --v 6.0** - Zoom Out by <@123456789> (turbo)
        /// ]]>
        /// </example>
        public const string ZOOM = @$"\*\*(.+?)\*\* - Zoom Out by <@(\d+)>\s*{PROGRESS_PATTERN}\({MODE_PATTERN}\)";

        /// <summary>
        /// Upscale 高清/创意
        /// <para>捕获组: 1=prompt, 2=status(Upscaling|Upscaled), 3=type(Subtle|Creative|4x|2x,可选), 4=progress(可选), 5=mode</para>
        /// </summary>
        /// <example>
        /// <![CDATA[
        /// **girl --raw --v 7.0** - Upscaled by <@123> (fast)
        /// **girl --raw --v 7.0** - Upscaling by <@123> (31%) (fast)
        /// **girl --raw --v 6.0** - Upscaled (Subtle) by <@123> (relaxed)
        /// **girl --raw --v 6.0** - Upscaled (Creative) by <@123> (fast)
        /// **car --v 5.2 --ar 16:9** - Upscaled (4x) by <@1273519008452182051> (Open on website for full quality) (fast)
        /// **girl --raw --v 7.0** - Upscaling by <@123> (0%) (fast)\n-# Create, explore...
        /// ]]>
        /// </example>
        public const string UPSCALE_HD = @$"\*\*(.+?)\*\* - (Upscaling|Upscaled)\s*(?:\((Subtle|Creative|4x|2x)\))?\s*by\s*<@\d+>\s*{PROGRESS_PATTERN}(?:\([^)]*\)\s*)*\({MODE_PATTERN}\)";

        /// <summary>
        /// U 操作（U1 U2 U3 U4）（选择单张图片）
        /// <para>捕获组: 1=prompt, 2=number(1/2/3/4), 3=userId</para>
        /// </summary>
        /// <example>
        /// <![CDATA[
        /// **girl --no boy --no car , dog --raw** - Image #4 <@1323696991334694963>
        /// **a beautiful landscape --v 7.0** - Image #1 <@123456789>
        /// **cat --ar 1:1** - Image #2 <@987654321>
        /// ]]>
        /// </example>
        public const string UPSCALE_U = @"\*\*(.+?)\*\* - Image #(\d+) <@(\d+)>";

        /// <summary>
        /// 通用图像生成 / 视频生成
        /// Imagine 基础消息格式: **prompt** - @user (extra info) (mode)
        /// <para>捕获组: 1=prompt, 2=progress(可选), 3=mode</para>
        /// </summary>
        /// <example>
        /// <![CDATA[
        /// **a beautiful sunset --v 7.0** - <@123456789> (fast)
        /// **cat sitting on a chair --ar 16:9** - <@123456789> (relaxed, stealth)
        /// **<https://s.mj.run/pnvVhedeTbw> cat --fast --video 1 --aspect 1:1** - <@1091167368845213706> [(Open on website for full quality)](<https://midjourney.com/jobs/...>) (fast)
        /// **<https://s.mj.run/X-tG9-sshyk> Aerial photography --ar 16:9 --v 6.0** - <@1325403477765132341> (Open on website for full quality) (relaxed)
        /// ]]>
        /// </example>
        public const string IMAGINE_SUCCESS = @$"\*\*(.+?)\*\* - <@\d+>\s*{PROGRESS_PATTERN}(?:\s*\[[^\]]*\]\([^)]*\)|\s*\([^)]*\))*\s*\({MODE_PATTERN}\)";

        /// <summary>
        /// Reroll 消息格式（与 Imagine 相同）: **prompt** - @user (mode)
        /// <para>捕获组: 1=prompt, 2=mode</para>
        /// </summary>
        /// <example>
        /// <![CDATA[
        /// **a beautiful sunset --v 7.0** - <@123456789> (fast)
        /// **cat sitting on a chair** - <@123456789> (relaxed)
        /// ]]>
        /// </example>
        public const string REROLL_SUCCESS = IMAGINE_SUCCESS;

        /// <summary>
        /// Blend 消息格式: **prompt** - @user (mode) 或无 prompt
        /// <para>捕获组: 1=prompt(可能为空), 2=mode</para>
        /// </summary>
        /// <example>
        /// <![CDATA[
        /// **** - <@123456789> (fast)
        /// **--v 7.0 --ar 1:1** - <@123456789> (relaxed)
        /// ]]>
        /// </example>
        public const string BLEND_SUCCESS = IMAGINE_SUCCESS;

        /// <summary>
        /// Describe 图生文
        /// Describe 消息通过 Embeds 返回，无特定正则格式
        /// <![CDATA[
        /// 需要检查: message.Embeds.Count > 0 && message.Embeds[0].Image?.Url 存在
        /// ]]>
        /// </summary>
        public const string DESCRIBE_SUCCESS = null;

        /// <summary>
        /// Shorten Prompt分析
        /// Shorten 消息通过 Embeds 返回，无特定正则格式
        /// <para>需要检查: message.InteractionMetadata?.Name == "shorten"</para>
        /// <para>或: message.Embeds[0].Footer?.Text.Contains("Click on a button to imagine")</para>
        /// </summary>
        public const string SHORTEN_SUCCESS = null;

        /// <summary>
        /// 获取完整的 prompt 内容
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        public static string GetFullPrompt(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return content;
            }

            // 获取 **...** 中的内容
            var matcher = Regex.Match(content, CONTENT_FULL);

            return matcher.Success ? matcher.Groups[1].Value : content;
        }

        /// <summary>
        /// 解析消息内容，返回解析结果
        /// </summary>
        public static MessageParseResult Parse(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            // 按优先级尝试匹配各种模式
            return TryParseUpscaleHD(content)
                ?? TryParseVariations(content)
                ?? TryParsePan(content)
                ?? TryParseZoom(content)
                ?? TryParseUpscaleImageU(content)
                ?? TryParseImagine(content);
        }

        /// <summary>
        /// 尝试解析 Variations 格式
        /// </summary>
        public static MessageParseResult TryParseVariations(string content)
        {
            var match = Regex.Match(content, VARIATIONS);
            if (!match.Success)
                return null;

            return new MessageParseResult
            {
                Action = TaskAction.VARIATION,
                Prompt = match.Groups[1].Value,
                VariationType = match.Groups[3].Success && !string.IsNullOrEmpty(match.Groups[3].Value)
                    ? match.Groups[3].Value : null,
                UserId = match.Groups[4].Value,
                Progress = match.Groups[5].Success ? match.Groups[5].Value : null,
                Mode = match.Groups[6].Value,
                Status = "done"
            };
        }

        /// <summary>
        /// 尝试解析 Pan 操作
        /// </summary>
        public static MessageParseResult TryParsePan(string content)
        {
            var match = Regex.Match(content, PAN);
            if (!match.Success) return null;

            return new MessageParseResult
            {
                Action = TaskAction.PAN,
                Prompt = match.Groups[1].Value,
                ActionName = match.Groups[2].Value,
                UserId = match.Groups[3].Value,
                Progress = match.Groups[4].Success ? match.Groups[4].Value : null,
                Mode = match.Groups[5].Value,
                Status = "done"
            };
        }

        /// <summary>
        /// 尝试解析 Zoom 操作
        /// </summary>
        public static MessageParseResult TryParseZoom(string content)
        {
            var match = Regex.Match(content, ZOOM);
            if (!match.Success) return null;

            return new MessageParseResult
            {
                Action = TaskAction.ZOOM,
                Prompt = match.Groups[1].Value,
                UserId = match.Groups[2].Value,
                Progress = match.Groups[3].Success ? match.Groups[3].Value : null,
                Mode = match.Groups[4].Value,
                Status = "done"
            };
        }

        /// <summary>
        /// 尝试解析 Upscale 高清操作
        /// </summary>
        public static MessageParseResult TryParseUpscaleHD(string content)
        {
            var match = Regex.Match(content, UPSCALE_HD);
            if (!match.Success) return null;

            return new MessageParseResult
            {
                Action = TaskAction.UPSCALE_HD,
                Prompt = match.Groups[1].Value,
                Status = match.Groups[2].Value,
                UpscaleType = match.Groups[3].Success ? match.Groups[3].Value : null,
                Progress = match.Groups[4].Success ? match.Groups[4].Value : null,
                Mode = match.Groups[5].Value
            };
        }

        /// <summary>
        /// 尝试解析 U 操作（选择单张图片）
        /// </summary>
        public static MessageParseResult TryParseUpscaleImageU(string content)
        {
            var match = Regex.Match(content, UPSCALE_U);
            if (!match.Success) return null;

            return new MessageParseResult
            {
                Action = TaskAction.UPSCALE,
                Prompt = match.Groups[1].Value,
                ImageIndex = int.Parse(match.Groups[2].Value),
                UserId = match.Groups[3].Value,
                Status = "done"
            };
        }

        /// <summary>
        /// 尝试解析 Imagine 通用格式
        /// </summary>
        public static MessageParseResult TryParseImagine(string content)
        {
            var match = Regex.Match(content, IMAGINE_SUCCESS);
            if (!match.Success)
                return null;

            var isVideo = content.Contains("--video", StringComparison.OrdinalIgnoreCase);

            return new MessageParseResult
            {
                Action = isVideo ? TaskAction.VIDEO : TaskAction.IMAGINE,
                Prompt = match.Groups[1].Value,
                Progress = match.Groups[2].Success ? match.Groups[2].Value : null,
                Mode = match.Groups[3].Value,
            };
        }

        /// <summary>
        /// 检查是否包含等待开始标记
        /// </summary>
        public static bool IsWaitingToStart(string content)
        {
            return !string.IsNullOrWhiteSpace(content) && content.Contains("(Waiting to start)");
        }

        /// <summary>
        /// 检查是否为视频扩展消息
        /// </summary>
        public static bool IsVideoExtend(string content)
        {
            return !string.IsNullOrWhiteSpace(content) &&
                   content.Contains("extended", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 消息解析结果
    /// </summary>
    public class MessageParseResult
    {
        /// <summary>消息类型/操作类型</summary>
        public TaskAction Action { get; set; }

        /// <summary>操作名称 (Pan Left/Zoom Out by等)</summary>
        public string ActionName { get; set; }

        /// <summary>提示词</summary>
        public string Prompt { get; set; }

        /// <summary>状态 (Upscaling/Upscaled/done/Stopped等)</summary>
        public string Status { get; set; }

        /// <summary>模式 (fast/relaxed/turbo)</summary>
        public string Mode { get; set; }

        /// <summary>进度 (0%/45%/100%等)</summary>
        public string Progress { get; set; }

        /// <summary>Upscale类型 (Subtle/Creative)</summary>
        public string UpscaleType { get; set; }

        /// <summary>Variation类型 (Strong/Subtle/Region)</summary>
        public string VariationType { get; set; }

        /// <summary>选择的图片索引 (1-4)</summary>
        public int? ImageIndex { get; set; }

        /// <summary>用户ID</summary>
        public string UserId { get; set; }
    }
}