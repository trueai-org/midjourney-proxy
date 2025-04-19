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

using Aliyun.OSS;
using IdGen;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Serilog;
using System.Collections.Concurrent;
using System.Text;

using ILogger = Serilog.ILogger;

namespace Midjourney.Infrastructure.Storage
{
    /// <summary>
    /// 阿里云存储服务
    /// </summary>
    public class AliyunOssStorageService : IStorageService
    {
        /// <summary>
        /// ID 生成器，每毫秒生成 65536 条，超出上限可能会造成重复
        /// 使用 1 位生成器，16 位序列，示例：14589105147281408
        /// Id's/ms per generator : 65536
        /// Id's/ms total         : 65536
        /// Wraparound interval   : 1628906.02:45:55.3280000
        /// Wraparound date       : 6474-10-17 02:45:55
        /// </summary>
        private static readonly IdGenerator _idGenerator = new(0, new IdGeneratorOptions(new IdStructure(47, 0, 16)));

        /// <summary>
        /// ID 生成器计数器
        /// </summary>
        private static readonly ConcurrentDictionary<long, int> _counter = new();

        private readonly ILogger _logger;
        private readonly AliyunOssOptions _ossOptions;

        private readonly string _bucketName;
        private readonly string _accessKeyId;
        private readonly string _accessKeySecret;
        private readonly string _endpoint;

        public AliyunOssStorageService()
        {
            _logger = Log.Logger;

            var ossOptions = GlobalConfiguration.Setting.AliyunOss;

            _ossOptions = ossOptions;
            _bucketName = ossOptions.BucketName!;
            _accessKeyId = ossOptions.AccessKeyId!;
            _accessKeySecret = ossOptions.AccessKeySecret!;
            _endpoint = ossOptions.Endpoint!;
        }

        public AliyunOssOptions Options => _ossOptions;

        /// <summary>
        /// 水印 base64 编码
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        public static string Base64Encode(string content)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            return Convert.ToBase64String(bytes).TrimEnd('=').Trim().Replace("+", "-").Replace("/", "_");
        }

        /// <summary>
        /// 获取文件存储路径 key
        /// 格式1：{prefix}/xxx/xxx/xxx/yyyyMMddHHmmssfff{suffix}.xxx
        /// 格式2：{path}/yyyyMMddHHmmssfff{suffix}.xxx
        /// </summary>
        /// <param name="originalFileName">原文件名，不可为空</param>
        /// <param name="pathPrefix">文件路径前缀</param>
        /// <param name="path">如果外部传递了 path 则使用此路径值作为 key，并且忽略 prefix 参数</param>
        /// <returns></returns>
        private static string GetKey(string originalFileName, string pathPrefix = null, string path = null, string fileName = null, bool? isDateKeyPrefix = true)
        {
            // 生成后缀唯一随机数
            var now = DateTime.Now;
            var suffix = string.Empty;
            do
            {
                now = DateTime.Now;

                // 初始化 ID 计数器
                var ms = new DateTimeOffset(now).ToUnixTimeMilliseconds();
                if (!_counter.IsEmpty)
                {
                    var last = _counter.Last();
                    if (last.Key < ms)
                    {
                        _counter.Clear();
                    }
                }

                // 如果 1ms 超出 65536 条则进入等待
                var max = _counter.AddOrUpdate(ms, 1, (k, v) => ++v);
                if (max > 65536)
                {
                    Thread.Sleep(1);
                    continue;
                }

                // 取最后 5 位作为随机数
                var id = _idGenerator.CreateId().ToString();
                if (id.Length > 5)
                {
                    suffix = id.Substring(id.Length - 5);
                }
            } while (string.IsNullOrWhiteSpace(suffix));

            var key = string.Empty;

            path = path?.TrimPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                // 如果存在外部路径
                key += $"{path}/";
            }
            else
            {
                // 如果不存在外部路径，则判断使用前缀
                pathPrefix = pathPrefix?.TrimPath();
                if (!string.IsNullOrWhiteSpace(pathPrefix))
                {
                    key += $"{pathPrefix}/";
                }

                // 计算路径，默认 3 级目录，如果每级 999 个文件夹，则最大支持 997,002,999 个文件
                var guid = $"{Guid.NewGuid():N}";
                for (int k = 0; k < 4; k++)
                {
                    key += Convert.ToInt32(guid.Substring(k * 2, 2), 16).ToString().PadLeft(3, '0') + "/";
                }
            }

            // 计算扩展名
            var exten = Path.GetExtension(originalFileName)?.Trim().ToLower();
            if (exten?.IndexOf('?') > 0)
            {
                exten = exten.Substring(0, exten.IndexOf('?'));
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var fname = fileName.Trim();
                key += !string.IsNullOrWhiteSpace(exten) && fname.EndsWith(exten) ? fname : fname + exten;
            }
            else
            {
                key += $"{(isDateKeyPrefix == true ? now.ToString("yyyyMMddHHmmssfff") : now.ToString("HHmmssfff"))}{suffix}{exten}";
            }

            return key;
        }

        /// <summary>
        /// 删除文件
        /// </summary>
        /// <param name="isDeleteMedia">是否标识删除记录</param>
        /// <param name="keys"></param>
        /// <returns></returns>
        public async Task DeleteAsync(bool isDeleteMedia = false, params string[] keys)
        {
            foreach (var key in keys)
            {
                try
                {
                    _logger.Information("删除文件: {@key}", key);

                    var newKey = key;

                    FormatKey(ref newKey);

                    if (string.IsNullOrWhiteSpace(newKey))
                    {
                        continue;
                    }

                    newKey = newKey.Trim().Trim('/');

                    var client = new OssClient(_endpoint, _accessKeyId, _accessKeySecret);
                    var deleteObjectResult = client.DeleteObject(_bucketName, newKey);
                    if (deleteObjectResult?.HttpStatusCode != System.Net.HttpStatusCode.OK)
                    {
                        _logger.Warning("删除文件失败, {@deleteObjectResult}", deleteObjectResult);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "删除文件异常, {key}", key);
                }
            }
        }

        /// <summary>
        /// 上传
        /// </summary>
        /// <param name="mediaBinaryStream"></param>
        /// <param name="key"></param>
        /// <param name="mimeType"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        public UploadResult SaveAsync(Stream mediaBinaryStream, string key, string mimeType)
        {
            if (mediaBinaryStream == null || mediaBinaryStream?.Length <= 0)
            {
                throw new ArgumentNullException(nameof(mediaBinaryStream));
            }

            var size = mediaBinaryStream!.Length;

            var newFileName = Path.GetFileName(key);
            var client = new OssClient(_endpoint, _accessKeyId, _accessKeySecret);
            var metadata = new ObjectMetadata();

            if (!string.IsNullOrWhiteSpace(mimeType))
            {
                metadata.ContentType = mimeType;
            }

            var retryCount = 3;
            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    var opt = GlobalConfiguration.Setting.AliyunOss;

                    var objectResult = client.PutObject(_bucketName, key, mediaBinaryStream, metadata);
                    if (objectResult?.HttpStatusCode == System.Net.HttpStatusCode.OK)
                    {
                        var result = new UploadResult()
                        {
                            FileName = newFileName,
                            Path = key.Trim('/'),
                            Key = key,
                            Size = size,
                            Md5 = objectResult.ResponseMetadata["Content-MD5"],
                            Id = objectResult.ETag,
                            ContentType = mimeType,
                            Url = GetSignKey(key, opt.ExpiredMinutes).ToString()
                        };

                        _logger.Information("上传成功 {@0}", key);

                        return result;
                    }

                    //var objectResult = client.PutObject(_bucketName, key, mediaBinaryStream, metadata);
                    //if (objectResult?.HttpStatusCode == System.Net.HttpStatusCode.OK)
                    //{
                    //    // 上传成功，退出重试
                    //    break;
                    //}
                }
                catch (HttpRequestException ex)
                {
                    _logger.Warning("上传失败，重试次数: {@0}, {@1}", key, i + 1);

                    if (i >= retryCount - 1)
                    {
                        throw new Exception($"多次重试后上传仍然失败 {key}", ex);
                    }
                }

                // 重试等待
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }

            throw new Exception($"上传失败 {key}");
        }

        /// <summary>
        /// 生成带签名的URL，设置过期时间为 1 小时
        /// </summary>
        /// <param name="key"></param>
        /// <param name="minutes"></param>
        /// <returns></returns>
        public Uri GetSignKey(string key, int minutes = 60)
        {
            if (minutes <= 0)
            {
                var ossOptions = GlobalConfiguration.Setting.AliyunOss;

                return new Uri($"{ossOptions.CustomCdn}/{key}");
            }

            var client = new OssClient(_endpoint, _accessKeyId, _accessKeySecret);
            var expiration = DateTime.Now.AddMinutes(minutes);
            if (minutes <= 0)
            {
                expiration = DateTime.Now.AddMinutes(minutes);
            }

            return client.GeneratePresignedUri(_bucketName, key, expiration);
        }

        /// <summary>
        /// 阿里云覆盖保存文件
        /// </summary>
        /// <param name="mediaBinaryStream"></param>
        /// <param name="key"></param>
        /// <exception cref="Exception"></exception>
        public void Overwrite(Stream mediaBinaryStream, string key, string mimeType)
        {
            var client = new OssClient(_endpoint, _accessKeyId, _accessKeySecret);
            var metadata = new ObjectMetadata();

            if (!string.IsNullOrWhiteSpace(mimeType))
            {
                metadata.ContentType = mimeType;
            }

            // 禁止覆盖，默认上传覆盖文件
            metadata.AddHeader("x-oss-forbid-overwrite", "false");

            var objectResult = client.PutObject(_bucketName, key, mediaBinaryStream, metadata);
            if (objectResult?.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception("文件覆盖保存失败");
            }
        }

        /// <summary>
        /// 获取阿里云文件流数据
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public Stream GetObject(string key)
        {
            _logger.Information("下载文件 {@key}", key);
            FormatKey(ref key);

            if (string.IsNullOrWhiteSpace(key))
            {
                return Stream.Null;
            }

            // 创建OssClient实例。
            var client = new OssClient(_endpoint, _accessKeyId, _accessKeySecret);

            // 下载文件到流。OssObject 包含了文件的各种信息，如文件所在的存储空间、文件名、元信息以及一个输入流。
            var obj = client.GetObject(_bucketName, key);

            if (obj?.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                _logger.Error("下载文件异常 {@obj}", obj);

                throw new Exception("下载文件异常");
            }

            // 不直接返回
            //return obj.Content;

            // 使用 MemoryStream 作为缓冲
            var memoryStream = new MemoryStream();
            using (var responseStream = obj.Content)
            {
                responseStream.CopyTo(memoryStream);
            }
            memoryStream.Position = 0; // 重置流位置
            return memoryStream;
        }

        /// <summary>
        /// 获取阿里云文件流数据,返回文件类型
        /// </summary>
        /// <param name="key"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public Stream GetObject(string key, out string contentType)
        {
            _logger.Information("下载文件 {@key}", key);

            FormatKey(ref key);

            if (string.IsNullOrWhiteSpace(key))
            {
                contentType = string.Empty;
                return Stream.Null;
            }

            if (key.StartsWith("http"))
            {
                key = new Uri(key).LocalPath.TrimPath()!;
            }

            // 创建OssClient实例。
            var client = new OssClient(_endpoint, _accessKeyId, _accessKeySecret);

            // 下载文件到流。OssObject 包含了文件的各种信息，如文件所在的存储空间、文件名、元信息以及一个输入流。
            var obj = client.GetObject(_bucketName, key);

            if (obj?.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                _logger.Error("下载文件异常 {@obj}", obj);

                throw new Exception("下载文件异常");
            }
            contentType = obj.Metadata.ContentType;

            // 不直接返回
            //return obj.Content;

            // 使用 MemoryStream 作为缓冲
            var memoryStream = new MemoryStream();
            using (var responseStream = obj.Content)
            {
                responseStream.CopyTo(memoryStream);
            }
            memoryStream.Position = 0; // 重置流位置
            return memoryStream;
        }

        /// <summary>
        /// 移动文件
        /// </summary>
        /// <param name="key"></param>
        /// <param name="newKey"></param>
        /// <param name="isCopy"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task MoveAsync(string key, string newKey, bool isCopy = false)
        {
            try
            {
                FormatKey(ref key);

                FormatKey(ref newKey);

                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(newKey))
                {
                    return;
                }

                _logger.Information("移动文件 {@key}, {newKey}", key, newKey);

                // 创建OssClient实例。
                var client = new OssClient(_endpoint, _accessKeyId, _accessKeySecret);

                var req = new CopyObjectRequest(_bucketName, key, _bucketName, newKey)
                {
                    // 如果NewObjectMetadata为null则为COPY模式（即拷贝源文件的元信息），非null则为REPLACE模式（覆盖源文件的元信息）。
                    NewObjectMetadata = null
                };

                _logger.Information("复制文件: {@req}", req);

                var copyObjectResult = client.CopyObject(req);

                _logger.Information("复制结果: {@copyObjectResult}", copyObjectResult);

                if (copyObjectResult.HttpStatusCode == System.Net.HttpStatusCode.OK)
                {
                    _logger.Information("移动文件成功");

                    // 删除旧文件
                    if (!isCopy)
                    {
                        await DeleteAsync(false, key);
                    }
                }
                else
                {
                    _logger.Error("移动文件失败 {@copyObjectResult}", copyObjectResult);

                    throw new Exception("移动文件失败");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "移动文件异常 {@key}, {@newKey}", key, newKey);
            }
        }

        /// <summary>
        /// 判断文件是否在阿里云存在
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public async Task<bool> ExistsAsync(string key)
        {
            FormatKey(ref key);

            if (string.IsNullOrWhiteSpace(key))
            {
                return await Task.FromResult(false);
            }

            var client = new OssClient(_endpoint, _accessKeyId, _accessKeySecret);
            var any = client.DoesObjectExist(_bucketName, key);

            _logger.Information("验证文件是否存在 {@key}, {1}", key, any);

            return await Task.FromResult(any);
        }

        /// <summary>
        /// 格式化 key
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public void FormatKey(ref string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            key = key.TrimPath() ?? string.Empty;

            if (key.StartsWith("http") && !string.IsNullOrWhiteSpace(_ossOptions?.CustomCdn))
            {
                if (key.StartsWith(_ossOptions.CustomCdn))
                {
                    key = key.Substring(_ossOptions.CustomCdn.Length).TrimPath()!;
                }
            }
        }

        public string GetCustomCdn()
        {
            return string.IsNullOrWhiteSpace(_ossOptions?.CustomCdn) ? _ossOptions.CustomCdn : string.Empty;
        }
    }

    /// <summary>
    /// 文件上传参数
    /// </summary>
    public class UploadParam
    {
        public UploadParam(string originalFileName)
        {
            OriginalFileName = originalFileName;
        }

        /// <summary>
        /// 是否为图片
        /// </summary>
        public bool IsImage { get; set; }

        /// <summary>
        /// 是否为视频
        /// </summary>
        public bool IsVideo { get; set; }

        /// <summary>
        /// 原文件名，不可为空
        /// </summary>
        public string OriginalFileName { get; set; }

        /// <summary>
        /// MIME 类型，如果为空，则以流的方式保存文件
        /// </summary>
        public string MimeType { get; set; }

        /// <summary>
        /// 保存文件路径前缀，优先级低于 Path
        /// </summary>
        public string PathPrefix { get; set; }

        /// <summary>
        /// 是否使用 yyyyMMdd 作为 key 前缀，默认使用日期作为 key 前缀
        /// </summary>
        public bool? IsDateKeyPrefix { get; set; } = true;

        /// <summary>
        /// 文件名称（保存时的文件名称，如果外部传递了此名称，则使用此名称作为文件存储名称）
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// 保存文件路径，如果外部传递了 path 则使用此路径值作为 key，并且忽略 pathPrefix 参数
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// 文件上传唯一标识
        /// </summary>
        public string UploadId { get; set; }

        /// <summary>
        /// 加速域名
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// 来源标识/来源资源分类
        /// </summary>
        public string SourceCategory { get; set; }

        /// <summary>
        /// 操作人/上传者
        /// </summary>
        public string CreatedById { get; set; }

        /// <summary>
        /// 操作人/上传者
        /// </summary>
        public string CreatedByName { get; set; }
    }

    /// <summary>
    /// 上传结果
    /// 提示：分块上传 MD5 值返回为空
    /// </summary>
    public class UploadResult
    {
        /// <summary>
        /// 文件唯一标识
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 文件 MD5 参考值，由于分块上传会影响计算 MD5 值的影响。
        /// 不建议用户使用此MD5校验来验证数据完整性。
        /// </summary>
        public string Md5 { get; set; }

        /// <summary>
        /// 文件大小 byte
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// 新文件名
        /// 2022012021354986314784.jpg
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// 文件路径，不包含文件名
        /// UpFiles/xj/1000003/2020/202201/20
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// 文件 key
        /// UpFiles/xj/1000003/2020/202201/20/2022012021354986314784.jpg
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// 文件 key 对应的完整的 url 路径 = Host + Key
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// 原文件上传唯一标识
        /// </summary>
        public string UploadId { get; set; }

        /// <summary>
        /// 原文件名
        /// 314784.jpg
        /// </summary>
        public string OriginalFileName { get; set; }

        /// <summary>
        /// 原文件上传路径
        /// UpFiles/xj/1000003/2020/202201/20
        /// </summary>
        public string OriginalPath { get; set; }

        /// <summary>
        /// 原文件上传路径前缀
        /// UpFiles/xj
        /// </summary>
        public string OriginalPathPrefix { get; set; }

        /// <summary>
        /// 如果上传的是图片或视频，且图片需要裁剪或缩放时，所生成的图片
        /// UpFiles/xj/1000003/2020/202201/20/2022012021354986314784_w720.jpg
        /// </summary>
        public string Image { get; set; }

        /// <summary>
        /// 上传的图片或视频生成的缩略图
        /// UpFiles/xj/1000003/2020/202201/20/2022012021354986314784_w240.jpg
        /// </summary>
        public string Thumbnail { get; set; }

        /// <summary>
        /// 内容类型 image/jpeg video/mp4 等
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// 加速域名
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// 文件 hash
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// 来源标识/来源资源分类
        /// </summary>
        public string SourceCategory { get; set; }

        /// <summary>
        /// 操作人/上传者
        /// </summary>
        public string CreatedById { get; set; }

        /// <summary>
        /// 操作人/上传者
        /// </summary>
        public string CreatedByName { get; set; }
    }
}