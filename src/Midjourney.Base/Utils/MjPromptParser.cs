using System.Text.RegularExpressions;

namespace Midjourney.Base.Util
{
    /// <summary>
    /// Midjourney 参数实体
    /// </summary>
    public class MjParameter
    {
        /// <summary>参数名称（小写）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>参数值（布尔参数为 null）</summary>
        public string Value { get; set; }

        /// <summary>是否为布尔标志参数（无值）</summary>
        public bool IsFlag => Value == null;

        /// <summary>字符串表示形式</summary>
        public override string ToString() => IsFlag ? $"--{Name}" : $"--{Name} {Value}";
    }

    /// <summary>
    /// Midjourney 参数定义信息
    /// </summary>
    public class MjParamInfo
    {
        /// <summary>参数全名</summary>
        public string FullName { get; set; } = string.Empty;

        /// <summary>参数短名/别名（无则为空）</summary>
        public string ShortName { get; set; } = string.Empty;

        /// <summary>参数中文说明</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>参数英文说明</summary>
        public string DescriptionEn { get; set; } = string.Empty;

        /// <summary>取值范围</summary>
        public string ValueRange { get; set; } = string.Empty;

        /// <summary>默认值</summary>
        public string DefaultValue { get; set; } = string.Empty;

        /// <summary>是否为布尔标志参数</summary>
        public bool IsFlag { get; set; }

        /// <summary>支持的版本（如 "V6+", "V7"）</summary>
        public string SupportedVersions { get; set; } = "All";
    }

    /// <summary>
    /// Midjourney 提示词解析结果
    /// </summary>
    public class MjParseResult
    {
        /// <summary>干净的提示词（移除所有参数后的纯文本）</summary>
        public string CleanPrompt { get; set; } = string.Empty;

        /// <summary>
        /// 干净的提示词，并将 url 替换为固定 link
        /// </summary>
        public string CleanPromptNormalized =>
            Regex.Replace(CleanPrompt, @"https?://[-a-zA-Z0-9+&@#/%?=~_|!:,.;]*[-a-zA-Z0-9+&@#/%=~_|]", "<link>")
            .Replace("<<link>>", "<link>")
            .Replace(" -- ", " ")
            .Replace("  ", " ").Trim();

        /// <summary>所有解析出的参数列表（支持重复参数）</summary>
        public List<MjParameter> Parameters { get; set; } = new();

        /// <summary>原始提示词</summary>
        public string OriginalPrompt { get; set; } = string.Empty;

        /// <summary>
        /// 按参数名分组的参数值（相同参数名的值合并为列表）
        /// </summary>
        public Dictionary<string, List<string>> GroupedParams => Parameters
            .GroupBy(p => p.Name.ToLower())
            .ToDictionary(g => g.Key, g => g.Select(p => p.Value ?? "true").ToList());

        /// <summary>获取指定参数的所有值</summary>
        /// <param name="name">参数名（支持全名或别名）</param>
        public List<string> GetValues(string name)
        {
            var fullName = MjPromptParser.GetFullName(name);
            return Parameters
                .Where(p => MjPromptParser.GetFullName(p.Name) == fullName)
                .Select(p => p.Value ?? "true")
                .ToList();
        }

        /// <summary>获取指定参数的第一个值</summary>
        /// <param name="name">参数名（支持全名或别名）</param>
        public string GetValue(string name)
        {
            var fullName = MjPromptParser.GetFullName(name);
            return Parameters
                .FirstOrDefault(p => MjPromptParser.GetFullName(p.Name) == fullName)?.Value;
        }

        /// <summary>检查是否存在指定参数</summary>
        /// <param name="name">参数名（支持全名或别名）</param>
        public bool HasParam(string name)
        {
            var fullName = MjPromptParser.GetFullName(name);
            return Parameters.Any(p => MjPromptParser.GetFullName(p.Name) == fullName);
        }

        /// <summary>
        /// 获取 seed 值，多个 seed 参数时只有第一个生效，因此只需要获取第一个即可
        /// </summary>
        /// <returns></returns>
        public long? GetSeed()
        {
            var seedValue = GetValue("seed");
            if (long.TryParse(seedValue, out var seed))
                return seed;
            return null;
        }

        /// <summary>获取版本号</summary>
        /// <returns>版本号字符串，如 "7", "6.1"</returns>
        public string GetVersion() => GetValue("version") ?? GetValue("v");

        /// <summary>获取宽高比</summary>
        /// <returns>宽高比字符串，如 "16:9"</returns>
        public string GetAspectRatio() => GetValue("aspect") ?? GetValue("ar");

        /// <summary>获取风格化值</summary>
        public int? GetStylize()
        {
            var val = GetValue("stylize") ?? GetValue("s");
            return int.TryParse(val, out var s) ? s : null;
        }

        /// <summary>获取混乱度值</summary>
        public int? GetChaos()
        {
            var val = GetValue("chaos") ?? GetValue("c");
            return int.TryParse(val, out var c) ? c : null;
        }

        /// <summary>获取怪异度值</summary>
        public int? GetWeird()
        {
            var val = GetValue("weird") ?? GetValue("w");
            return int.TryParse(val, out var w) ? w : null;
        }

        /// <summary>获取质量值</summary>
        public double? GetQuality()
        {
            var val = GetValue("quality") ?? GetValue("q");
            return double.TryParse(val, out var q) ? q : null;
        }

        /// <summary>获取所有 --no 排除项</summary>
        public List<string> GetNoItems()
        {
            var noValues = GetValues("no");
            return noValues
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        /// <summary>是否为原始模式</summary>
        public bool IsRawMode => HasParam("raw");

        /// <summary>是否为平铺模式</summary>
        public bool IsTileMode => HasParam("tile");

        /// <summary>是否为涡轮模式</summary>
        public bool IsTurboMode => HasParam("turbo");

        /// <summary>是否为放松模式</summary>
        public bool IsRelaxMode => HasParam("relax");

        /// <summary>是否为快速模式</summary>
        public bool IsFastMode => HasParam("fast");

        /// <summary>是否为草稿模式</summary>
        public bool IsDraftMode => HasParam("draft");

        /// <summary>是否使用 Niji 模式</summary>
        public bool IsNijiMode => HasParam("niji");
    }

    /// <summary>
    /// Midjourney 提示词参数解析器
    /// <para>官方文档: https://docs.midjourney.com/hc/en-us/articles/32859204029709-Parameter-List</para>
    /// </summary>
    public static class MjPromptParser
    {
        #region 官方参数定义

        /// <summary>
        /// Midjourney 官方参数定义表
        /// </summary>
        public static readonly Dictionary<string, MjParamInfo> ParamDefinitions = new()
        {
            // ==================== 版本控制 Version Control ====================
            ["version"] = new()
            {
                FullName = "version",
                ShortName = "v",
                Description = "模型版本",
                DescriptionEn = "Midjourney model version",
                ValueRange = "1, 2, 3, 4, 5, 5.1, 5.2, 6, 6.1, 7",
                DefaultValue = "7",
                SupportedVersions = "All"
            },
            ["niji"] = new()
            {
                FullName = "niji",
                ShortName = "",
                Description = "动漫/二次元模式 (Midjourney与Spellbrush合作开发)",
                DescriptionEn = "Anime-style model co-developed with Spellbrush",
                ValueRange = "4, 5, 6",
                DefaultValue = "",
                SupportedVersions = "Niji专用"
            },
            ["sv"] = new()
            {
                FullName = "sv",
                ShortName = "",
                Description = "使用旧版本的 sref 风格系统",
                DescriptionEn = "Use older style reference system version",
                ValueRange = "4",
                DefaultValue = "",
                SupportedVersions = "V7"
            },

            // ==================== 图像尺寸 Image Dimensions ====================
            ["aspect"] = new()
            {
                FullName = "aspect",
                ShortName = "ar",
                Description = "宽高比 (不支持小数，用整数比例如 139:100 代替 1.39:1)",
                DescriptionEn = "Aspect ratio of generated image",
                ValueRange = "如 1:1, 16:9, 2:3, 9:16, 4:5",
                DefaultValue = "1:1",
                SupportedVersions = "All"
            },

            // ==================== 生成控制 Generation Control ====================
            ["chaos"] = new()
            {
                FullName = "chaos",
                ShortName = "c",
                Description = "变化度/多样性 (值越高，四张图之间差异越大)",
                DescriptionEn = "How varied the initial image grid is",
                ValueRange = "0-100",
                DefaultValue = "0",
                SupportedVersions = "All"
            },
            ["stylize"] = new()
            {
                FullName = "stylize",
                ShortName = "s",
                Description = "风格化程度 (值越高AI艺术风格越强，但可能偏离提示词)",
                DescriptionEn = "How strongly Midjourney's default aesthetic is applied",
                ValueRange = "0-1000",
                DefaultValue = "100",
                SupportedVersions = "All"
            },
            ["weird"] = new()
            {
                FullName = "weird",
                ShortName = "w",
                Description = "怪异程度 (生成奇特、非常规的图像)",
                DescriptionEn = "Explore unusual and quirky aesthetics",
                ValueRange = "0-3000",
                DefaultValue = "0",
                SupportedVersions = "V5.2+"
            },
            ["quality"] = new()
            {
                FullName = "quality",
                ShortName = "q",
                Description = "生成质量 (影响渲染时间和细节，不影响分辨率)",
                DescriptionEn = "Rendering time spent generating image",
                ValueRange = ".25, .5, 1 (V7支持 4)",
                DefaultValue = "1",
                SupportedVersions = "All"
            },
            ["seed"] = new()
            {
                FullName = "seed",
                ShortName = "",
                Description = "随机种子 (相同种子+相同提示词=相似结果)",
                DescriptionEn = "Seed number for reproducible results",
                ValueRange = "0-4294967295",
                DefaultValue = "随机",
                SupportedVersions = "All"
            },
            ["stop"] = new()
            {
                FullName = "stop",
                ShortName = "",
                Description = "提前停止生成 (产生模糊/不完整的图像)",
                DescriptionEn = "Stop job partway through, creating blurrier results",
                ValueRange = "10-100",
                DefaultValue = "100",
                SupportedVersions = "All"
            },
            ["repeat"] = new()
            {
                FullName = "repeat",
                ShortName = "r",
                Description = "重复生成次数 (批量生成同一提示词)",
                DescriptionEn = "Run a job multiple times from single prompt",
                ValueRange = "1-40 (取决于订阅计划)",
                DefaultValue = "1",
                SupportedVersions = "All"
            },

            // ==================== 参考图像 V6 Reference (cref/sref) ====================
            ["cref"] = new()
            {
                FullName = "cref",
                ShortName = "",
                Description = "角色参考 (Character Reference, 保持角色一致性)",
                DescriptionEn = "Character reference for consistent characters",
                ValueRange = "图片URL (支持多个，空格分隔)",
                DefaultValue = "",
                SupportedVersions = "V6, Niji 6"
            },
            ["cw"] = new()
            {
                FullName = "cw",
                ShortName = "",
                Description = "角色权重 (0=仅面部, 100=面部+发型+服装)",
                DescriptionEn = "Character weight, how much character reference influences",
                ValueRange = "0-100",
                DefaultValue = "100",
                SupportedVersions = "V6, Niji 6"
            },
            ["sref"] = new()
            {
                FullName = "sref",
                ShortName = "",
                Description = "风格参考 (Style Reference, 匹配参考图的视觉风格)",
                DescriptionEn = "Style reference to match look and feel of image",
                ValueRange = "图片URL/sref代码/random",
                DefaultValue = "",
                SupportedVersions = "V6+"
            },
            ["sw"] = new()
            {
                FullName = "sw",
                ShortName = "",
                Description = "风格权重 (控制风格参考的影响强度)",
                DescriptionEn = "Style weight for style reference influence",
                ValueRange = "0-1000",
                DefaultValue = "100",
                SupportedVersions = "V6+"
            },
            ["iw"] = new()
            {
                FullName = "iw",
                ShortName = "",
                Description = "图片权重 (图片提示词相对于文本的权重)",
                DescriptionEn = "Image weight relative to text weight",
                ValueRange = "0-3",
                DefaultValue = "1",
                SupportedVersions = "V5+"
            },

            // ==================== V7 全能参考 Omni Reference ====================
            // https://docs.midjourney.com/hc/en-us/articles/36285124473997-Omni-Reference
            // https://updates.midjourney.com/omni-reference-oref/
            ["oref"] = new()
            {
                FullName = "oref",
                ShortName = "",
                Description = "全能参考 (Omni Reference, 将人物/物体/载具放入图像)",
                DescriptionEn = "Omni reference to put a person or object into images",
                ValueRange = "图片URL",
                DefaultValue = "",
                SupportedVersions = "V7"
            },
            ["ow"] = new()
            {
                FullName = "ow",
                ShortName = "",
                Description = "全能权重 (Omni Weight, 控制参考图像的影响强度)",
                DescriptionEn = "Omni weight for reference influence strength",
                ValueRange = "0-1000 (建议不超过400)",
                DefaultValue = "100",
                SupportedVersions = "V7"
            },

            // ==================== 负面提示 Negative Prompt ====================
            ["no"] = new()
            {
                FullName = "no",
                ShortName = "",
                Description = "排除元素 (告诉AI不要生成的内容)",
                DescriptionEn = "Negative prompt, elements to exclude",
                ValueRange = "文本 (多个用逗号分隔)",
                DefaultValue = "",
                SupportedVersions = "All"
            },

            // ==================== 风格模式 Style Mode ====================
            // 适用于 v4+, niji
            // v 4 值 4a, 4b, 4c
            // niji 5 值 cute, scenic, expressive, original
            ["style"] = new()
            {
                FullName = "style",
                ShortName = "",
                Description = "风格预设 (不同版本支持不同风格 v 4 / niji 5)",
                DescriptionEn = "Style preset for different aesthetics",
                ValueRange = "raw (V5.1+), cute/scenic/expressive/original (Niji)",
                DefaultValue = "",
                SupportedVersions = "V4+, V5.1+, Niji"
            },
            ["raw"] = new()
            {
                FullName = "raw",
                ShortName = "",
                Description = "原始模式 (减少AI美化，更贴近提示词，适合写实风格)",
                DescriptionEn = "Raw mode with less automatic beautification",
                ValueRange = "",
                DefaultValue = "",
                IsFlag = true,
                SupportedVersions = "V5.1+"
            },

            // ==================== 速度模式 Speed Mode ====================
            ["turbo"] = new()
            {
                FullName = "turbo",
                ShortName = "",
                Description = "涡轮模式 (4倍速度，消耗2倍GPU时间)",
                DescriptionEn = "Turbo mode, 4x faster but uses 2x GPU time",
                ValueRange = "",
                DefaultValue = "",
                IsFlag = true,
                SupportedVersions = "V5+"
            },
            ["fast"] = new()
            {
                FullName = "fast",
                ShortName = "",
                Description = "快速模式 (使用快速GPU时间)",
                DescriptionEn = "Fast mode using fast GPU time",
                ValueRange = "",
                DefaultValue = "",
                IsFlag = true,
                SupportedVersions = "All"
            },
            ["relax"] = new()
            {
                FullName = "relax",
                ShortName = "",
                Description = "放松模式 (不消耗GPU时间，但需排队)",
                DescriptionEn = "Relax mode, queued without GPU time cost",
                ValueRange = "",
                DefaultValue = "",
                IsFlag = true,
                SupportedVersions = "Standard+订阅"
            },
            ["draft"] = new()
            {
                FullName = "draft",
                ShortName = "",
                Description = "草稿模式 (V7新增, 10倍速度, 一半GPU消耗, 低分辨率)",
                DescriptionEn = "Draft mode, 10x faster at half GPU cost",
                ValueRange = "",
                DefaultValue = "",
                IsFlag = true,
                SupportedVersions = "V7"
            },
            ["stealth"] = new()
            {
                FullName = "stealth",
                ShortName = "",
                Description = "隐私模式",
                DescriptionEn = "Make your creations private on the Midjourney website",
                ValueRange = "",
                DefaultValue = "",
                IsFlag = true,
                SupportedVersions = ""
            },
            ["public"] = new()
            {
                FullName = "public",
                ShortName = "",
                Description = "公开模式",
                DescriptionEn = "Make your creations public on the Midjourney website",
                ValueRange = "",
                DefaultValue = "",
                IsFlag = true,
                SupportedVersions = ""
            },
            // ==================== 其他功能 Other Features ====================
            ["tile"] = new()
            {
                FullName = "tile",
                ShortName = "",
                Description = "平铺模式 (生成可无缝拼接的纹理图案)",
                DescriptionEn = "Generate seamless tiling patterns",
                ValueRange = "",
                DefaultValue = "",
                IsFlag = true,
                SupportedVersions = "All"
            },
            ["video"] = new()
            {
                FullName = "video",
                ShortName = "",
                Description = "保存生成过程视频",
                DescriptionEn = "Save a progress video of generation",
                ValueRange = "",
                DefaultValue = "",
                IsFlag = true,
                SupportedVersions = "V5以下"
            },

            // ==================== 个性化 Personalization ====================
            ["profile"] = new()
            {
                FullName = "profile",
                ShortName = "p",
                Description = "个性化风格 (使用您的个人偏好模型)",
                DescriptionEn = "Use personalized model based on your preferences",
                ValueRange = "可选配置文件代码",
                DefaultValue = "",
                SupportedVersions = "V6+"
            },

            // ==================== 视频生成参数 Video Generation ====================
            // 来源: https://docs.midjourney.com/hc/en-us/articles/37460773864589-Video
            ["motion"] = new()
            {
                FullName = "motion",
                ShortName = "",
                Description = "视频动态程度 (low=静态场景/慢动作, high=大幅运动/可能产生不真实效果)",
                DescriptionEn = "Amount of motion in video. Low for still scenes, high for big movements",
                ValueRange = "low, high",
                DefaultValue = "low",
                SupportedVersions = "Video"
            },
            ["loop"] = new()
            {
                FullName = "loop",
                ShortName = "",
                Description = "循环视频 (结束帧与起始帧相同，创建无缝循环)",
                DescriptionEn = "Create looping video where end frame matches start frame",
                ValueRange = "",
                DefaultValue = "",
                IsFlag = true,
                SupportedVersions = "Video"
            },
            ["end"] = new()
            {
                FullName = "end",
                ShortName = "",
                Description = "视频结束帧 (指定结束帧图片URL)",
                DescriptionEn = "Set a custom end frame image for video",
                ValueRange = "图片URL",
                DefaultValue = "",
                SupportedVersions = "Video"
            },
            ["bs"] = new()
            {
                FullName = "bs",
                ShortName = "",
                Description = "视频批量大小 (每个提示词生成的视频数量)",
                DescriptionEn = "Batch size - number of videos generated from each prompt",
                ValueRange = "1-4",
                DefaultValue = "1",
                SupportedVersions = "Video"
            },

            // ==================== V7 实验性美学参数 V7 Experimental Aesthetics ====================
            // 来源: Midjourney官方Twitter @midjourney 2025-04-30
            // 来源: https://updates.midjourney.com/v7-update-editor-and-exp/
            ["exp"] = new()
            {
                FullName = "exp",
                ShortName = "",
                Description = "实验性美学参数 (增强细节、动态感和色调映射，高值会压制--stylize和--p)",
                DescriptionEn = "Experimental aesthetics, pumps up details, dynamism and tone-mapping",
                ValueRange = "0-100 (建议5/10/25/50，与其他参数混用时建议≤25)",
                DefaultValue = "0",
                SupportedVersions = "V7"
            },
        };

        /// <summary>
        /// 参数别名映射表 (短名 -> 全名)
        /// </summary>
        private static readonly Dictionary<string, string> AliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["v"] = "version",
            ["ar"] = "aspect",
            ["c"] = "chaos",
            ["s"] = "stylize",
            ["w"] = "weird",
            ["q"] = "quality",
            ["r"] = "repeat",
            ["p"] = "profile",
        };

        #endregion 官方参数定义

        #region 核心方法

        /// <summary>
        /// 获取参数全名 (将别名转换为全名)
        /// </summary>
        /// <param name="param">参数名 (可以是全名或别名)</param>
        /// <returns>参数全名</returns>
        /// <example>
        /// GetFullName("v")  // 返回 "version"
        /// GetFullName("ar") // 返回 "aspect"
        /// GetFullName("seed") // 返回 "seed"
        /// </example>
        public static string GetFullName(string param)
        {
            var lower = param.ToLower();
            return AliasMap.TryGetValue(lower, out var full) ? full : lower;
        }

        /// <summary>
        /// 获取参数详细信息
        /// </summary>
        /// <param name="param">参数名 (支持全名或别名)</param>
        /// <returns>参数信息，不存在则返回 null</returns>
        public static MjParamInfo GetParamInfo(string param) =>
            ParamDefinitions.TryGetValue(GetFullName(param), out var info) ? info : null;

        /// <summary>
        /// 检查是否为有效的官方参数
        /// </summary>
        /// <param name="param">参数名</param>
        public static bool IsValidParam(string param) =>
            ParamDefinitions.ContainsKey(GetFullName(param));

        /// <summary>
        /// 解析 Midjourney 提示词 (支持重复参数)
        /// </summary>
        /// <param name="prompt">原始提示词</param>
        /// <returns>解析结果</returns>
        /// <example>
        /// var result = Parse("cute cat --v 7 --ar 16:9 --no dogs, text");
        /// // result.CleanPrompt = "cute cat"
        /// // result.Parameters = [{Name:"v", Value:"7"}, {Name:"ar", Value:"16:9"}, {Name:"no", Value:"dogs, text"}]
        /// </example>
        public static MjParseResult Parse(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return new MjParseResult();

            var result = new MjParseResult { OriginalPrompt = prompt };

            // 核心正则: 匹配 --参数名 和可选的值
            // (?=\s+--|$) 前瞻断言确保在下一个参数或结尾处停止
            var pattern = @"--([a-zA-Z]+)(?:\s+([^-]+?))?(?=\s+--|$)";
            var matches = Regex.Matches(prompt, pattern);
            var clean = prompt;

            foreach (Match m in matches)
            {
                result.Parameters.Add(new MjParameter
                {
                    Name = m.Groups[1].Value.ToLower(),
                    Value = m.Groups[2].Success && !string.IsNullOrWhiteSpace(m.Groups[2].Value)
                        ? m.Groups[2].Value.Trim()
                        : null
                });
                clean = clean.Replace(m.Value, " ");
            }

            result.CleanPrompt = Regex.Replace(clean, @"\s+", " ").Trim();
            return result;
        }

        /// <summary>
        /// 仅提取参数列表
        /// </summary>
        public static List<MjParameter> ExtractParams(string prompt) => Parse(prompt).Parameters;

        /// <summary>
        /// 仅获取干净提示词 (移除所有参数)
        /// </summary>
        public static string GetCleanPrompt(string prompt) => Parse(prompt).CleanPrompt;

        /// <summary>
        /// 将解析结果中的参数转换为全名形式
        /// </summary>
        public static MjParseResult NormalizeToFullName(MjParseResult result)
        {
            return new MjParseResult
            {
                OriginalPrompt = result.OriginalPrompt,
                CleanPrompt = result.CleanPrompt,
                Parameters = result.Parameters.Select(p => new MjParameter
                {
                    Name = GetFullName(p.Name),
                    Value = p.Value
                }).ToList()
            };
        }

        /// <summary>
        /// 重建提示词
        /// </summary>
        /// <param name="result">解析结果</param>
        /// <param name="useFullName">是否使用参数全名</param>
        /// <param name="mergeNo">是否合并多个 --no 参数</param>
        public static string Rebuild(MjParseResult result, bool useFullName = false, bool mergeNo = true)
        {
            var r = useFullName ? NormalizeToFullName(result) : result;
            var parts = new List<string> { r.CleanPrompt };

            if (mergeNo)
            {
                foreach (var p in r.Parameters.Where(p => p.Name != "no"))
                    parts.Add(p.ToString());

                var noVals = r.GetValues("no");
                if (noVals.Any())
                    parts.Add($"--no {string.Join(", ", noVals)}");
            }
            else
            {
                foreach (var p in r.Parameters)
                    parts.Add(p.ToString());
            }

            return string.Join(" ", parts.Where(s => !string.IsNullOrEmpty(s)));
        }

        /// <summary>
        /// 获取参数列表及其详细信息
        /// </summary>
        public static List<(MjParameter Param, MjParamInfo Info)> GetParamsWithInfo(MjParseResult result) =>
            result.Parameters.Select(p => (p, GetParamInfo(p.Name))).ToList();

        /// <summary>
        /// 获取所有官方参数列表
        /// </summary>
        public static IEnumerable<MjParamInfo> GetAllParams() => ParamDefinitions.Values;

        #endregion 核心方法
    }
}