using Midjourney.Infrastructure.Domain;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Net.Http.Headers;
using System.Reflection;
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
        public DiscordServiceImpl(DiscordAccount account,
            HttpClient httpClient,
            DiscordHelper discordHelper,
            Dictionary<string, string> paramsMap)
        {
            _account = account;
            _httpClient = httpClient;
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

        public async Task<Message> ImagineAsync(string prompt, string nonce)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["imagine"], nonce);
            JObject paramsJson = JObject.Parse(paramsStr);
            paramsJson["data"]["options"][0]["value"] = prompt;
            return await PostJsonAndCheckStatusAsync(paramsJson.ToString());
        }

        public async Task<Message> UpscaleAsync(string messageId, int index, string messageHash, int messageFlags, string nonce)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["upscale"], nonce)
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

        public async Task<Message> VariationAsync(string messageId, int index, string messageHash, int messageFlags, string nonce)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["variation"], nonce)
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
        /// <returns></returns>
        public async Task<Message> ActionAsync(string messageId, string customId, int messageFlags, string nonce)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["action"], nonce)
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
        /// <returns></returns>
        public async Task<Message> SeedAsync(string jobId, string nonce)
        {
            var paramsStr = _paramsMap["seed"]
              .Replace("$channel_id", _account.PrivateChannelId)
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
        /// <param name="jobId"></param>
        /// <param name="nonce"></param>
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
                return Message.Of((int)response.StatusCode, "请求失败");
            }
            catch (HttpRequestException e)
            {
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
        /// <returns></returns>
        public async Task<Message> ZoomAsync(string messageId, string customId, string prompt, string nonce)
        {
            customId = customId.Replace("MJ::CustomZoom::", "MJ::OutpaintCustomZoomModal::");

            string paramsStr = ReplaceInteractionParams(_paramsMap["zoom"], nonce)
                .Replace("$message_id", messageId)
                .Replace("$prompt", prompt);

            var obj = JObject.Parse(paramsStr);

            obj["data"]["custom_id"] = customId;

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 局部重绘
        /// </summary>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="maskBase64"></param>
        /// <returns></returns>
        public async Task<Message> InpaintAsync(string customId, string prompt, string maskBase64)
        {
            try
            {
                customId = customId.Replace("MJ::iframe::", "");

                // mask.replace(/^data:.+?;base64,/, ''),
                maskBase64 = maskBase64.Replace("data:image/png;base64,", "");

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

        public async Task<Message> RerollAsync(string messageId, string messageHash, int messageFlags, string nonce)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["reroll"], nonce)
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

        public async Task<Message> DescribeAsync(string finalFileName, string nonce)
        {
            string fileName = finalFileName.Substring(finalFileName.LastIndexOf("/") + 1);
            string paramsStr = ReplaceInteractionParams(_paramsMap["describe"], nonce)
                .Replace("$file_name", fileName)
                .Replace("$final_file_name", finalFileName);
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        public async Task<Message> BlendAsync(List<string> finalFileNames, BlendDimensions dimensions, string nonce)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["blend"], nonce);
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
                return Message.Of((int)response.StatusCode, paramsStr.Substring(0, Math.Min(paramsStr.Length, 100)));
            }
            catch (HttpRequestException e)
            {
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