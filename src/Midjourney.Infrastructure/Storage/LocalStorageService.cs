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

using Serilog;

namespace Midjourney.Infrastructure.Storage
{
    /// <summary>
    /// 本地存储服务
    /// </summary>
    public class LocalStorageService : IStorageService
    {
        private readonly ILogger _logger;

        public LocalStorageService()
        {
            _logger = Log.Logger;
        }

        /// <summary>
        /// 保存文件到本地存储
        /// </summary>
        public UploadResult SaveAsync(Stream mediaBinaryStream, string key, string mimeType)
        {
            if (mediaBinaryStream == null || mediaBinaryStream.Length <= 0)
                throw new ArgumentNullException(nameof(mediaBinaryStream));

            var filePath = GetFilePath(key);
            var directory = Path.GetDirectoryName(filePath);

            // 创建目标目录
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 保存文件
            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                mediaBinaryStream.CopyTo(fileStream);
            }

            _logger.Information("文件已保存到本地存储: {FilePath}", filePath);


            var opt = GlobalConfiguration.Setting.LocalStorage;

            return new UploadResult
            {
                FileName = Path.GetFileName(key),
                Key = key,
                Path = filePath,
                Size = mediaBinaryStream.Length,
                ContentType = mimeType,
                Url = $"{opt.CustomCdn}/{key}"
            };
        }

        /// <summary>
        /// 删除本地存储的文件
        /// </summary>
        public async Task DeleteAsync(bool isDeleteMedia = false, params string[] keys)
        {
            foreach (var key in keys)
            {
                var filePath = GetFilePath(key);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.Information("已删除文件: {FilePath}", filePath);
                }
                else
                {
                    _logger.Warning("文件不存在: {FilePath}", filePath);
                }
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// 获取文件流
        /// </summary>
        public Stream GetObject(string key)
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
            {
                _logger.Error("文件不存在: {FilePath}", filePath);
                throw new FileNotFoundException("文件不存在", key);
            }

            return new FileStream(filePath, FileMode.Open, FileAccess.Read);
        }

        /// <summary>
        /// 获取文件流及内容类型
        /// </summary>
        public Stream GetObject(string key, out string contentType)
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
            {
                _logger.Error("文件不存在: {FilePath}", filePath);
                throw new FileNotFoundException("文件不存在", key);
            }

            contentType = MimeKit.MimeTypes.GetMimeType(Path.GetFileName(filePath));
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = "image/png";
            }

            return new FileStream(filePath, FileMode.Open, FileAccess.Read);
        }

        /// <summary>
        /// 移动文件
        /// </summary>
        public async Task MoveAsync(string key, string newKey, bool isCopy = false)
        {
            var sourcePath = GetFilePath(key);
            var destinationPath = GetFilePath(newKey);

            if (!File.Exists(sourcePath))
            {
                _logger.Warning("源文件不存在: {SourcePath}", sourcePath);
                return;
            }

            if (!Directory.Exists(Path.GetDirectoryName(destinationPath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            }

            if (isCopy)
            {
                File.Copy(sourcePath, destinationPath);
                _logger.Information("已复制文件到新位置: {DestinationPath}", destinationPath);
            }
            else
            {
                File.Move(sourcePath, destinationPath);
                _logger.Information("已移动文件到新位置: {DestinationPath}", destinationPath);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 判断文件是否存在
        /// </summary>
        public async Task<bool> ExistsAsync(string key)
        {
            var filePath = GetFilePath(key);
            bool exists = File.Exists(filePath);
            _logger.Information("文件存在状态: {Key} - {Exists}", key, exists);
            return await Task.FromResult(exists);
        }

        /// <summary>
        /// 覆盖保存文件
        /// </summary>
        public void Overwrite(Stream mediaBinaryStream, string key, string mimeType)
        {
            SaveAsync(mediaBinaryStream, key, mimeType);
        }

        /// <summary>
        /// 生成本地文件的访问路径（模拟签名 URL）
        /// </summary>
        public Uri GetSignKey(string key, int minutes = 60)
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("文件不存在", key);
            }

            // 生成一个模拟的本地 URL（例如：file:// 本地文件路径）
            return new Uri($"file://{filePath}");
        }

        /// <summary>
        /// 获取文件的完整存储路径
        /// </summary>
        private string GetFilePath(string key)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", key.Replace("/", Path.DirectorySeparatorChar.ToString()));
        }

        public string GetCustomCdn()
        {
            return GlobalConfiguration.Setting.LocalStorage.CustomCdn;
        }
    }
}
