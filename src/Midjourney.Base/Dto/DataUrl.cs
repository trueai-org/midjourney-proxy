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
using Midjourney.Base.Util;
using Serilog;

namespace Midjourney.Base
{
    /// <summary>
    /// Data URL 信息
    /// </summary>
    public class DataUrl
    {
        /// <summary>
        /// Mime 类型
        /// </summary>
        public string MimeType { get; set; }

        /// <summary>
        /// 数据
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// 链接
        /// </summary>
        public string Url { get; set; }

        public DataUrl()
        {
        }

        /// <summary>
        /// 通过 mimeType 和数据构造 DataUrl 实例
        /// </summary>
        /// <param name="mimeType"></param>
        /// <param name="data"></param>
        public DataUrl(string mimeType, byte[] data)
        {
            MimeType = mimeType;
            Data = data;
        }

        /// <summary>
        /// 通过 DataUrl 信息生成唯一文件名
        /// </summary>
        /// <returns></returns>
        public async Task<string> GenerateFileName()
        {
            var ext = await MjImageHelper.GuessFileSuffix(this);
            return $"{Guid.NewGuid():N}{ext}";
        }

        /// <summary>
        /// 解析 DataUrl 字符串
        /// </summary>
        /// <param name="dataUrl"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        /// <exception cref="FormatException"></exception>
        public static DataUrl Parse(string dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl))
            {
                return null;
            }

            if (dataUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return new DataUrl
                {
                    Url = dataUrl
                };
            }

            var setting = GlobalConfiguration.Setting;
            if (!setting.EnableUserCustomUploadBase64)
            {
                // 不允许用户上传 base64 数据
                throw new LogicException("disabled upload base64");
            }

            var match = Regex.Match(dataUrl, @"data:(?<type>.+?);base64,(?<data>.+)");
            if (!match.Success)
            {
                // 说明没有前缀，直接尝试解析为 base64 字符串
                match = Regex.Match(dataUrl, @"^(?<data>.+)$");
                if (!match.Success)
                {
                    Log.Warning("Invalid data URL format: {DataUrl}", dataUrl);

                    throw new FormatException("Invalid data URL format");
                }

                var defaultMimeType = "image/png";
                return new DataUrl(defaultMimeType, Convert.FromBase64String(match.Groups["data"].Value));
            }

            string mimeType = match.Groups["type"].Value;
            byte[] data = Convert.FromBase64String(match.Groups["data"].Value);

            return new DataUrl(mimeType, data);
        }

        /// <summary>
        /// 转换 Base64 字符串数组为 DataUrl 列表
        /// </summary>
        /// <param name="base64Array"></param>
        /// <returns></returns>
        public static List<DataUrl> ConvertBase64Array(List<string> base64Array)
        {
            if (base64Array == null || base64Array.Count == 0)
            {
                return new List<DataUrl>();
            }

            var dataUrlList = new List<DataUrl>();
            foreach (var base64 in base64Array)
            {
                if (!string.IsNullOrWhiteSpace(base64))
                {
                    var dataUrl = DataUrl.Parse(base64);
                    dataUrlList.Add(dataUrl);
                }
            }
            return dataUrlList;
        }
    }
}