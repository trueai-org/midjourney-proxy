using Midjourney.Infrastructure.Domain;
using Newtonsoft.Json.Linq;
using Serilog;
using System.IO;
using System.Net.Http.Headers;
using System.Reflection.Metadata;
using System.Text;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// Discord服务实现类。
    /// </summary>
    public class DiscordServiceImpl : IDiscordService
    {
        private readonly DiscordAccount _account;
        private readonly HttpClient _httpClient;
        private readonly DiscordHelper _discordHelper;
        private readonly Dictionary<string, string> _paramsMap;
        private readonly ILogger _logger;

        private readonly string _discordInteractionUrl;
        private readonly string _discordAttachmentUrl;
        private readonly string _discordMessageUrl;

        /// <summary>
        /// 初始化 DiscordServiceImpl 类的新实例。
        /// </summary>
        public DiscordServiceImpl(
            DiscordAccount account,
            DiscordHelper discordHelper,
            Dictionary<string, string> paramsMap)
        {
            _account = account;

            _httpClient = new HttpClient()
            {
                Timeout = TimeSpan.FromMinutes(10)
            };

            _paramsMap = paramsMap;
            _discordHelper = discordHelper;
            _logger = Log.Logger;

            string discordServer = _discordHelper.GetServer();
            _discordInteractionUrl = $"{discordServer}/api/v9/interactions";
            _discordAttachmentUrl = $"{discordServer}/api/v9/channels/{account.ChannelId}/attachments";
            _discordMessageUrl = $"{discordServer}/api/v9/channels/{account.ChannelId}/messages";
        }

        /// <summary>
        /// 默认会话ID。
        /// </summary>
        public string DefaultSessionId { get; set; } = "f1a313a09ce079ce252459dc70231f30";

        /// <summary>
        /// 绘画
        /// </summary>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> ImagineAsync(string prompt, string nonce, EBotType botType)
        {
            prompt = GetPrompt(prompt);

            var json = botType == EBotType.MID_JOURNEY ? _paramsMap["imagine"] : _paramsMap["imagineniji"];
            var paramsStr = ReplaceInteractionParams(json, nonce);

            JObject paramsJson = JObject.Parse(paramsStr);
            paramsJson["data"]["options"][0]["value"] = prompt;

            return await PostJsonAndCheckStatusAsync(paramsJson.ToString());
        }

        /// <summary>
        /// 放大
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="index"></param>
        /// <param name="messageHash"></param>
        /// <param name="messageFlags"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> UpscaleAsync(string messageId, int index, string messageHash, int messageFlags, string nonce, EBotType botType)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["upscale"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$index", index.ToString())
                .Replace("$message_hash", messageHash);

            var obj = JObject.Parse(paramsStr);

            if (obj.ContainsKey("message_flags"))
            {
                obj["message_flags"] = messageFlags;
            }
            else
            {
                obj.Add("message_flags", messageFlags);
            }

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 变化
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="index"></param>
        /// <param name="messageHash"></param>
        /// <param name="messageFlags"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> VariationAsync(string messageId, int index, string messageHash, int messageFlags, string nonce, EBotType botType)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["variation"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$index", index.ToString())
                .Replace("$message_hash", messageHash);
            var obj = JObject.Parse(paramsStr);

            if (obj.ContainsKey("message_flags"))
            {
                obj["message_flags"] = messageFlags;
            }
            else
            {
                obj.Add("message_flags", messageFlags);
            }

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 执行动作
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="messageFlags"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> ActionAsync(string messageId, string customId, int messageFlags, string nonce, EBotType botType)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["action"], nonce, botType)
                .Replace("$message_id", messageId);

            var obj = JObject.Parse(paramsStr);

            if (obj.ContainsKey("message_flags"))
            {
                obj["message_flags"] = messageFlags;
            }
            else
            {
                obj.Add("message_flags", messageFlags);
            }

            obj["data"]["custom_id"] = customId;

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 图片 seed 值
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> SeedAsync(string jobId, string nonce, EBotType botType)
        {
            // 私聊频道
            var json = botType == EBotType.NIJI_JOURNEY ? _paramsMap["seedniji"] : _paramsMap["seed"];
            var paramsStr = json
              .Replace("$channel_id", botType == EBotType.MID_JOURNEY ? _account.PrivateChannelId : _account.NijiBotChannelId)
              .Replace("$session_id", DefaultSessionId)
              .Replace("$nonce", nonce)
              .Replace("$job_id", jobId);

            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 图片 seed 值消息
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public async Task<Message> SeedMessagesAsync(string url)
        {
            try
            {
                // 解码
                url = System.Web.HttpUtility.UrlDecode(url);

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Content = new StringContent("", Encoding.UTF8, "application/json")
                };

                request.Headers.UserAgent.ParseAdd(_account.UserAgent);

                // 设置 request Authorization 为 UserToken，不需要 Bearer 前缀
                request.Headers.Add("Authorization", _account.UserToken);

                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return Message.Success();
                }

                _logger.Warning("Seed Http 请求执行失败 {@0}, {@1}, {@2}", url, response.StatusCode, response.Content);

                return Message.Of((int)response.StatusCode, "请求失败");
            }
            catch (HttpRequestException e)
            {
                _logger.Error(e, "Seed Http 请求执行异常 {@0}", url);
                return ConvertHttpRequestException(e);
            }
        }

        /// <summary>
        /// 自定义变焦
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> ZoomAsync(string messageId, string customId, string prompt, string nonce, EBotType botType)
        {
            customId = customId.Replace("MJ::CustomZoom::", "MJ::OutpaintCustomZoomModal::");
            prompt = GetPrompt(prompt);

            string paramsStr = ReplaceInteractionParams(_paramsMap["zoom"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$prompt", prompt);

            var obj = JObject.Parse(paramsStr);

            obj["data"]["custom_id"] = customId;

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Remix 操作
        /// </summary>
        /// <param name="action"></param>
        /// <param name="messageId"></param>
        /// <param name="modal"></param>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> RemixAsync(TaskAction action, string messageId, string modal, string customId, string prompt, string nonce, EBotType botType)
        {
            prompt = GetPrompt(prompt);

            string paramsStr = ReplaceInteractionParams(_paramsMap["remix"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$prompt", prompt)
                .Replace("$custom_id", customId)
                .Replace("$modal", modal);

            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 执行 info 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> InfoAsync(string nonce, EBotType botType)
        {
            var content = botType == EBotType.NIJI_JOURNEY ? _paramsMap["infoniji"] : _paramsMap["info"];

            var paramsStr = ReplaceInteractionParams(content, nonce);
            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 执行 settings button 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="custom_id"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> SettingButtonAsync(string nonce, string custom_id, EBotType botType)
        {
            var paramsStr = ReplaceInteractionParams(_paramsMap["settingbutton"], nonce)
                .Replace("$custom_id", custom_id);

            if (botType == EBotType.NIJI_JOURNEY)
            {
                paramsStr = paramsStr
                    .Replace("$application_id", Constants.NIJI_APPLICATION_ID)
                    .Replace("$message_id", _account.NijiSettingsMessageId);
            }
            else if (botType == EBotType.MID_JOURNEY)
            {
                paramsStr = paramsStr
                    .Replace("$application_id", Constants.MJ_APPLICATION_ID)
                    .Replace("$message_id", _account.SettingsMessageId);
            }

            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// MJ 执行 settings select 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        public async Task<Message> SettingSelectAsync(string nonce, string values)
        {
            var paramsStr = ReplaceInteractionParams(_paramsMap["settingselect"], nonce)
              .Replace("$message_id", _account.SettingsMessageId)
              .Replace("$values", values);
            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 执行 setting 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> SettingAsync(string nonce, EBotType botType)
        {
            var content = botType == EBotType.NIJI_JOURNEY ? _paramsMap["settingniji"] : _paramsMap["setting"];

            var paramsStr = ReplaceInteractionParams(content, nonce);
            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 获取 prompt 格式化
        /// </summary>
        /// <param name="prompt"></param>
        /// <returns></returns>
        public string GetPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return prompt;
            }

            // 将 2 个空格替换为 1 个空格
            prompt = prompt.Replace("  ", " ").Replace("  ", " ").Replace("  ", " ").Trim();

            if (_account.Mode != null)
            {
                // 移除 prompt 可能的的参数
                prompt = prompt.Replace("--fast", "").Replace("--relax", "").Replace("--turbo", "");

                switch (_account.Mode.Value)
                {
                    case GenerationSpeedMode.RELAX:
                        prompt += " --relax";
                        break;
                    case GenerationSpeedMode.FAST:
                        prompt += " --fast";
                        break;
                    case GenerationSpeedMode.TURBO:
                        prompt += " --turbo";
                        break;
                    default:
                        break;
                }
            }

            return prompt;
        }

        /// <summary>
        /// 局部重绘
        /// </summary>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="maskBase64"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> InpaintAsync(string customId, string prompt, string maskBase64, EBotType botType)
        {
            try
            {
                prompt = GetPrompt(prompt);

                customId = customId?.Replace("MJ::iframe::", "");

                // mask.replace(/^data:.+?;base64,/, ''),
                maskBase64 = maskBase64?.Replace("data:image/png;base64,", "");

                var obj = new
                {
                    customId = customId,
                    //full_prompt = null,
                    mask = maskBase64,
                    prompt = prompt,
                    userId = "0",
                    username = "0",
                };
                var paramsStr = Newtonsoft.Json.JsonConvert.SerializeObject(obj);
                var response = await PostJsonAsync("https://936929561302675456.discordsays.com/inpaint/api/submit-job",
                    paramsStr);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return Message.Success();
                }

                return Message.Of((int)response.StatusCode, "提交失败");
            }
            catch (HttpRequestException e)
            {
                return ConvertHttpRequestException(e);
            }
        }

        /// <summary>
        /// 重新生成
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="messageHash"></param>
        /// <param name="messageFlags"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> RerollAsync(string messageId, string messageHash, int messageFlags, string nonce, EBotType botType)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["reroll"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$message_hash", messageHash);
            var obj = JObject.Parse(paramsStr);

            if (obj.ContainsKey("message_flags"))
            {
                obj["message_flags"] = messageFlags;
            }
            else
            {
                obj.Add("message_flags", messageFlags);
            }

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 解析描述
        /// </summary>
        /// <param name="finalFileName"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> DescribeAsync(string finalFileName, string nonce, EBotType botType)
        {
            string fileName = finalFileName.Substring(finalFileName.LastIndexOf("/") + 1);

            var json = botType == EBotType.NIJI_JOURNEY ? _paramsMap["describeniji"] : _paramsMap["describe"];
            string paramsStr = ReplaceInteractionParams(json, nonce)
                .Replace("$file_name", fileName)
                .Replace("$final_file_name", finalFileName);
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 合成
        /// </summary>
        /// <param name="finalFileNames"></param>
        /// <param name="dimensions"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> BlendAsync(List<string> finalFileNames, BlendDimensions dimensions, string nonce, EBotType botType)
        {
            var json = botType == EBotType.NIJI_JOURNEY ? _paramsMap["blendniji"] : _paramsMap["blend"];

            string paramsStr = ReplaceInteractionParams(json, nonce);
            JObject paramsJson = JObject.Parse(paramsStr);
            JArray options = (JArray)paramsJson["data"]["options"];
            JArray attachments = (JArray)paramsJson["data"]["attachments"];
            for (int i = 0; i < finalFileNames.Count; i++)
            {
                string finalFileName = finalFileNames[i];
                string fileName = finalFileName.Substring(finalFileName.LastIndexOf("/") + 1);
                JObject attachment = new JObject
                {
                    ["id"] = i.ToString(),
                    ["filename"] = fileName,
                    ["uploaded_filename"] = finalFileName
                };
                attachments.Add(attachment);
                JObject option = new JObject
                {
                    ["type"] = 11,
                    ["name"] = $"image{i + 1}",
                    ["value"] = i
                };
                options.Add(option);
            }
            options.Add(new JObject
            {
                ["type"] = 3,
                ["name"] = "dimensions",
                ["value"] = $"--ar {dimensions.GetValue()}"
            });
            return await PostJsonAndCheckStatusAsync(paramsJson.ToString());
        }

        private string ReplaceInteractionParams(string paramsStr, string nonce)
        {
            return paramsStr.Replace("$guild_id", _account.GuildId)
                .Replace("$channel_id", _account.ChannelId)
                .Replace("$session_id", DefaultSessionId)
                .Replace("$nonce", nonce);
        }

        private string ReplaceInteractionParams(string paramsStr, string nonce, EBotType botType)
        {
            var str = ReplaceInteractionParams(paramsStr, nonce);

            if (botType == EBotType.NIJI_JOURNEY)
            {
                str = str.Replace("$application_id", Constants.NIJI_APPLICATION_ID);
            }
            else if (botType == EBotType.MID_JOURNEY)
            {
                str = str.Replace("$application_id", Constants.MJ_APPLICATION_ID);
            }

            return str;
        }


        public async Task<Message> UploadAsync(string fileName, DataUrl dataUrl)
        {
            try
            {
                JObject fileObj = new JObject
                {
                    ["filename"] = fileName,
                    ["file_size"] = dataUrl.Data.Length,
                    ["id"] = "0"
                };
                JObject paramsJson = new JObject
                {
                    ["files"] = new JArray { fileObj }
                };
                HttpResponseMessage response = await PostJsonAsync(_discordAttachmentUrl, paramsJson.ToString());
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error("上传图片到discord失败, status: {StatusCode}, msg: {Body}", response.StatusCode, await response.Content.ReadAsStringAsync());
                    return Message.Of(ReturnCode.VALIDATION_ERROR, "上传图片到discord失败");
                }
                JArray array = JObject.Parse(await response.Content.ReadAsStringAsync())["attachments"] as JArray;
                if (array == null || array.Count == 0)
                {
                    return Message.Of(ReturnCode.VALIDATION_ERROR, "上传图片到discord失败");
                }
                string uploadUrl = array[0]["upload_url"].ToString();
                string uploadFilename = array[0]["upload_filename"].ToString();

                await PutFileAsync(uploadUrl, dataUrl);

                return Message.Success(uploadFilename);
            }
            catch (Exception e)
            {
                _logger.Error(e, "上传图片到discord失败");

                return Message.Of(ReturnCode.FAILURE, "上传图片到discord失败");
            }
        }

        public async Task<Message> SendImageMessageAsync(string content, string finalFileName)
        {
            string fileName = finalFileName.Substring(finalFileName.LastIndexOf("/") + 1);
            string paramsStr = _paramsMap["message"]
                .Replace("$content", content)
                .Replace("$channel_id", _account.ChannelId)
                .Replace("$file_name", fileName)
                .Replace("$final_file_name", finalFileName);
            HttpResponseMessage response = await PostJsonAsync(_discordMessageUrl, paramsStr);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                _logger.Error("发送图片消息到discord失败, status: {StatusCode}, msg: {Body}", response.StatusCode, await response.Content.ReadAsStringAsync());
                return Message.Of(ReturnCode.VALIDATION_ERROR, "发送图片消息到discord失败");
            }
            JObject result = JObject.Parse(await response.Content.ReadAsStringAsync());
            JArray attachments = result["attachments"] as JArray;
            if (attachments != null && attachments.Count > 0)
            {
                return Message.Success(attachments[0]["url"].ToString());
            }
            return Message.Failure("发送图片消息到discord失败: 图片不存在");
        }

        private async Task PutFileAsync(string uploadUrl, DataUrl dataUrl)
        {
            uploadUrl = _discordHelper.GetDiscordUploadUrl(uploadUrl);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
            {
                Content = new ByteArrayContent(dataUrl.Data)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(dataUrl.MimeType);
            request.Content.Headers.ContentLength = dataUrl.Data.Length;
            request.Headers.UserAgent.ParseAdd(_account.UserAgent);
            await _httpClient.SendAsync(request);
        }

        private async Task<HttpResponseMessage> PostJsonAsync(string url, string paramsStr)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(paramsStr, Encoding.UTF8, "application/json")
            };

            request.Headers.UserAgent.ParseAdd(_account.UserAgent);

            // 设置 request Authorization 为 UserToken，不需要 Bearer 前缀
            request.Headers.Add("Authorization", _account.UserToken);

            return await _httpClient.SendAsync(request);
        }

        private async Task<Message> PostJsonAndCheckStatusAsync(string paramsStr)
        {
            try
            {
                HttpResponseMessage response = await PostJsonAsync(_discordInteractionUrl, paramsStr);
                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return Message.Success();
                }

                _logger.Warning("Http 请求执行失败 {@0}, {@1}, {@2}", paramsStr, response.StatusCode, response.Content);

                return Message.Of((int)response.StatusCode, paramsStr.Substring(0, Math.Min(paramsStr.Length, 100)));
            }
            catch (HttpRequestException e)
            {
                _logger.Error(e, "Http 请求执行异常 {@0}", paramsStr);

                return ConvertHttpRequestException(e);
            }
        }

        private Message ConvertHttpRequestException(HttpRequestException e)
        {
            try
            {
                JObject error = JObject.Parse(e.Message);
                return Message.Of(error.Value<int>("code"), error.Value<string>("message"));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, ex.Message);

                return Message.Of((int)e.StatusCode, e.Message.Substring(0, Math.Min(e.Message.Length, 100)));
            }
        }
    }
}