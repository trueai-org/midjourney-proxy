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

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// Discord 辅助类，用于处理 Discord 相关的 URL 和操作。
    /// </summary>
    public class DiscordHelper
    {
        private readonly ProxyProperties _properties;

        /// <summary>
        /// 初始化 DiscordHelper 类的新实例。
        /// </summary>
        public DiscordHelper()
        {
            _properties = GlobalConfiguration.Setting;
        }

        /// <summary>
        /// DISCORD_SERVER_URL.
        /// </summary>
        public const string DISCORD_SERVER_URL = "https://discord.com";

        /// <summary>
        /// DISCORD_CDN_URL.
        /// </summary>
        public const string DISCORD_CDN_URL = "https://cdn.discordapp.com";

        /// <summary>
        /// DISCORD_WSS_URL.
        /// </summary>
        public const string DISCORD_WSS_URL = "wss://gateway.discord.gg";

        /// <summary>
        /// DISCORD_UPLOAD_URL.
        /// </summary>
        public const string DISCORD_UPLOAD_URL = "https://discord-attachments-uploads-prd.storage.googleapis.com";

        /// <summary>
        /// 身份验证地址如果 200 则是正常
        /// </summary>
        public const string DISCORD_VAL_URL = "https://discord.com/api/v9/users/@me/billing/country-code";

        /// <summary>
        /// ME 渠道
        /// </summary>
        public const string ME_CHANNELS_URL = "https://discord.com/api/v9/users/@me/channels";

        /// <summary>
        /// 获取 Discord 服务器 URL。
        /// </summary>
        /// <returns>Discord 服务器 URL。</returns>
        public string GetServer()
        {
            if (string.IsNullOrWhiteSpace(_properties.NgDiscord.Server))
            {
                return DISCORD_SERVER_URL;
            }

            string serverUrl = _properties.NgDiscord.Server;
            return serverUrl.EndsWith("/") ? serverUrl.Substring(0, serverUrl.Length - 1) : serverUrl;
        }

        /// <summary>
        /// 获取 Discord CDN URL。
        /// </summary>
        /// <returns>Discord CDN URL。</returns>
        public string GetCdn()
        {
            if (string.IsNullOrWhiteSpace(_properties.NgDiscord.Cdn))
            {
                return DISCORD_CDN_URL;
            }

            string cdnUrl = _properties.NgDiscord.Cdn;
            return cdnUrl.EndsWith("/") ? cdnUrl.Substring(0, cdnUrl.Length - 1) : cdnUrl;
        }

        ///// <summary>
        ///// 获取自定义 CDN URL
        ///// </summary>
        ///// <returns></returns>
        //public string GetCustomCdn()
        //{
        //    if (string.IsNullOrWhiteSpace(_properties.NgDiscord.CustomCdn))
        //    {
        //        return string.Empty;
        //    }

        //    string cdnUrl = _properties.NgDiscord.CustomCdn;
        //    return cdnUrl.EndsWith("/") ? cdnUrl.Substring(0, cdnUrl.Length - 1) : cdnUrl;
        //}

        ///// <summary>
        ///// 获取是否保存到本地。
        ///// </summary>
        ///// <returns></returns>
        //public bool GetSaveToLocal()
        //{
        //    return _properties.NgDiscord.SaveToLocal == true;
        //}

        /// <summary>
        /// 获取 Discord WebSocket URL。
        /// </summary>
        /// <returns>Discord WebSocket URL。</returns>
        public string GetWss()
        {
            if (string.IsNullOrWhiteSpace(_properties.NgDiscord.Wss))
            {
                return DISCORD_WSS_URL;
            }

            string wssUrl = _properties.NgDiscord.Wss;
            return wssUrl.EndsWith("/") ? wssUrl.Substring(0, wssUrl.Length - 1) : wssUrl;
        }

        /// <summary>
        /// 获取 Discord Resume WebSocket URL。
        /// </summary>
        /// <returns>Discord Resume WebSocket URL。</returns>
        public string GetResumeWss()
        {
            if (string.IsNullOrWhiteSpace(_properties.NgDiscord.ResumeWss))
            {
                return null;
            }

            string resumeWss = _properties.NgDiscord.ResumeWss;
            return resumeWss.EndsWith("/") ? resumeWss.Substring(0, resumeWss.Length - 1) : resumeWss;
        }

        /// <summary>
        /// 获取 Discord 上传 URL。
        /// </summary>
        /// <param name="uploadUrl">原始上传 URL。</param>
        /// <returns>处理后的上传 URL。</returns>
        public string GetDiscordUploadUrl(string uploadUrl)
        {
            if (string.IsNullOrWhiteSpace(_properties.NgDiscord.UploadServer) || string.IsNullOrWhiteSpace(uploadUrl))
            {
                return uploadUrl;
            }

            string uploadServer = _properties.NgDiscord.UploadServer;
            if (uploadServer.EndsWith("/"))
            {
                uploadServer = uploadServer.Substring(0, uploadServer.Length - 1);
            }

            return uploadUrl.Replace(DISCORD_UPLOAD_URL, uploadServer);
        }

        /// <summary>
        /// 获取图像 URL 中的消息哈希。
        /// </summary>
        /// <param name="imageUrl">图像 URL。</param>
        /// <returns>消息哈希。</returns>
        public string GetMessageHash(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return null;
            }

            if (imageUrl.EndsWith("_grid_0.webp"))
            {
                int hashStartIndex = imageUrl.LastIndexOf("/");
                if (hashStartIndex < 0)
                {
                    return null;
                }
                return imageUrl.Substring(hashStartIndex + 1, imageUrl.Length - hashStartIndex - 1 - "_grid_0.webp".Length);
            }

            int startIndex = imageUrl.LastIndexOf("_");
            if (startIndex < 0)
            {
                return null;
            }

            return imageUrl.Substring(startIndex + 1).Split('.')[0];
        }
    }
}