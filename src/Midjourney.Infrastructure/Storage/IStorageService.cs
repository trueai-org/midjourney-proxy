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

namespace Midjourney.Infrastructure.Storage
{
    /// <summary>
    /// 存储服务
    /// </summary>
    public interface IStorageService
    {
        /// <summary>
        /// 上传
        /// </summary>
        /// <param name="mediaBinaryStream"></param>
        /// <param name="key"></param>
        /// <param name="mimeType"></param>
        /// <returns></returns>
        UploadResult SaveAsync(Stream mediaBinaryStream, string key, string mimeType);

        /// <summary>
        /// 删除文件
        /// </summary>
        /// <param name="isDeleteMedia">是否标识删除记录</param>
        /// <param name="keys"></param>
        /// <returns></returns>
        Task DeleteAsync(bool isDeleteMedia = false, params string[] keys);

        /// <summary>
        /// 获取文件流数据
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Stream GetObject(string key);

        ///// <summary>
        ///// 获取文件流数据,返回文件类型
        ///// </summary>
        ///// <param name="key"></param>
        ///// <param name="contentType"></param>
        ///// <returns></returns>
        //Stream GetObject(string key, out string contentType);

        /// <summary>
        /// 移动文件
        /// </summary>
        /// <param name="key"></param>
        /// <param name="newKey"></param>
        /// <param name="isCopy"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        Task MoveAsync(string key, string newKey, bool isCopy = false);

        /// <summary>
        /// 判断文件是否存在
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<bool> ExistsAsync(string key);

        /// <summary>
        /// 覆盖保存文件
        /// </summary>
        /// <param name="mediaBinaryStream"></param>
        /// <param name="key"></param>
        /// <param name="mimeType"></param>
        void Overwrite(Stream mediaBinaryStream, string key, string mimeType);

        /// <summary>
        /// 获取自定义加速域名
        /// </summary>
        /// <returns></returns>
        string GetCustomCdn();
    }
}