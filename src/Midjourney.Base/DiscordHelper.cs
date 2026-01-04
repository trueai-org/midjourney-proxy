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

using System.Text.RegularExpressions;

namespace Midjourney.Base
{
    /// <summary>
    /// Discord 辅助类，用于处理 Discord 相关的 URL 和操作。
    /// </summary>
    public class DiscordHelper
    {
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
        public static string GetServer()
        {
            if (string.IsNullOrWhiteSpace(GlobalConfiguration.Setting.NgDiscord.Server))
            {
                return DISCORD_SERVER_URL;
            }

            string serverUrl = GlobalConfiguration.Setting.NgDiscord.Server;
            return serverUrl.EndsWith("/") ? serverUrl.Substring(0, serverUrl.Length - 1) : serverUrl;
        }

        /// <summary>
        /// 获取 Discord CDN URL。
        /// </summary>
        /// <returns>Discord CDN URL。</returns>
        public static string GetCdn()
        {
            if (string.IsNullOrWhiteSpace(GlobalConfiguration.Setting.NgDiscord.Cdn))
            {
                return DISCORD_CDN_URL;
            }

            string cdnUrl = GlobalConfiguration.Setting.NgDiscord.Cdn;
            return cdnUrl.EndsWith("/") ? cdnUrl.Substring(0, cdnUrl.Length - 1) : cdnUrl;
        }

        /// <summary>
        /// 获取 Discord WebSocket URL。
        /// </summary>
        /// <returns>Discord WebSocket URL。</returns>
        public static string GetWss()
        {
            if (string.IsNullOrWhiteSpace(GlobalConfiguration.Setting.NgDiscord.Wss))
            {
                return DISCORD_WSS_URL;
            }

            string wssUrl = GlobalConfiguration.Setting.NgDiscord.Wss;
            return wssUrl.EndsWith("/") ? wssUrl.Substring(0, wssUrl.Length - 1) : wssUrl;
        }

        /// <summary>
        /// 获取 Discord Resume WebSocket URL。
        /// </summary>
        /// <returns>Discord Resume WebSocket URL。</returns>
        public static string GetResumeWss()
        {
            if (string.IsNullOrWhiteSpace(GlobalConfiguration.Setting.NgDiscord.ResumeWss))
            {
                return null;
            }

            string resumeWss = GlobalConfiguration.Setting.NgDiscord.ResumeWss;
            return resumeWss.EndsWith("/") ? resumeWss.Substring(0, resumeWss.Length - 1) : resumeWss;
        }

        /// <summary>
        /// 获取 Discord 上传 URL。
        /// </summary>
        /// <param name="uploadUrl">原始上传 URL。</param>
        /// <returns>处理后的上传 URL。</returns>
        public static string GetDiscordUploadUrl(string uploadUrl)
        {
            if (string.IsNullOrWhiteSpace(GlobalConfiguration.Setting.NgDiscord.UploadServer) || string.IsNullOrWhiteSpace(uploadUrl))
            {
                return uploadUrl;
            }

            string uploadServer = GlobalConfiguration.Setting.NgDiscord.UploadServer;
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
        public static string GetMessageHash(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return null;
            }

            // GUID 正则表达式模式
            string pattern = @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";

            if (imageUrl.EndsWith("_grid_0.webp"))
            {
                int hashStartIndex = imageUrl.LastIndexOf("/");
                if (hashStartIndex < 0)
                {
                    return null;
                }
                return imageUrl.Substring(hashStartIndex + 1, imageUrl.Length - hashStartIndex - 1 - "_grid_0.webp".Length);
            }
            // 26695e7d-3f6c-4923-a3b8-8a266a170d97_0_0.png
            else if (imageUrl.EndsWith("_0_0.png"))
            {
                int hashStartIndex = imageUrl.LastIndexOf("/");
                if (hashStartIndex < 0)
                {
                    return null;
                }
                return imageUrl.Substring(hashStartIndex + 1, imageUrl.Length - hashStartIndex - 1 - "_0_0.png".Length);
            }
            // https://cdn.midjourney.com/a7b52e11-a59b-4f8e-ac2d-d9be4993a537/0_0.png
            else if (imageUrl.EndsWith("/0_0.png"))
            {
                // 直接切分路径，取倒数第二段
                var segments = imageUrl.Split('/');
                return segments.Length >= 2 ? segments[^2] : null;
            }
            // e7321c76-becf-473b-b14d-32b846dc70ad_0.mp4
            else if (imageUrl.EndsWith("_0.mp4"))
            {
                int hashStartIndex = imageUrl.LastIndexOf("/");
                if (hashStartIndex < 0)
                {
                    return null;
                }
                return imageUrl.Substring(hashStartIndex + 1, imageUrl.Length - hashStartIndex - 1 - "_0.mp4".Length);
            }

            // 通过 GUIID 获取
            // string url = "https://cdn.discordapp.com/ephemeral-attachments/1457289299434148008/1457303109289251008/26ee489b-00d7-47da-9ee0-c6de4c93049c_3_step_7.jpeg?ex=695b82c8&is=695a3148&hm=5ff2bc5a89599067e3bcc51f5347cae80c498ff54fe4f4c60be8cdc055fa9f73&";
            else if (Regex.Match(imageUrl, pattern).Success)
            {
                var guid = Regex.Match(imageUrl, pattern).Value;
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    return guid;
                }
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