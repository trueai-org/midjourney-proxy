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

using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Serilog;

using CopyObjectRequest = Amazon.S3.Model.CopyObjectRequest;
using DeleteObjectRequest = Amazon.S3.Model.DeleteObjectRequest;
using GetObjectMetadataRequest = Amazon.S3.Model.GetObjectMetadataRequest;
using GetObjectRequest = Amazon.S3.Model.GetObjectRequest;
using PutObjectRequest = Amazon.S3.Model.PutObjectRequest;

namespace Midjourney.Base.Storage
{
    /// <summary>
    /// S3 兼容存储服务 (支持 MinIO)
    /// </summary>
    public class S3StorageService : IStorageService
    {
        private readonly S3StorageOptions _s3Options;
        private readonly ILogger _logger;

        public S3StorageService()
        {
            _s3Options = GlobalConfiguration.Setting.S3Storage;
            _logger = Log.Logger;
        }

        /// <summary>
        /// 获取 S3 客户端
        /// </summary>
        /// <returns></returns>
        public AmazonS3Client GetClient()
        {
            var credentials = new BasicAWSCredentials(_s3Options.AccessKey, _s3Options.SecretKey);

            var config = new AmazonS3Config
            {
                ServiceURL = _s3Options.Endpoint,
                ForcePathStyle = _s3Options.ForcePathStyle,
                UseHttp = !_s3Options.UseHttps,
            };

            // 如果不是 AWS，设置自定义区域
            if (!_s3Options.Endpoint.Contains("amazonaws.com"))
            {
                config.AuthenticationRegion = _s3Options.Region;
            }
            else
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(_s3Options.Region);
            }

            return new AmazonS3Client(credentials, config);
        }

        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="mediaBinaryStream">文件流</param>
        /// <param name="key">存储路径</param>
        /// <param name="mimeType">MIME类型</param>
        /// <returns></returns>
        public UploadResult SaveAsync(Stream mediaBinaryStream, string key, string mimeType)
        {
            if (mediaBinaryStream == null || mediaBinaryStream.Length <= 0)
            {
                throw new ArgumentNullException(nameof(mediaBinaryStream));
            }

            try
            {
                var client = GetClient();
                var fileName = Path.GetFileName(key);
                var fileSize = mediaBinaryStream.Length;

                var request = new PutObjectRequest
                {
                    Key = key.TrimStart('/'),
                    ContentType = mimeType,
                    InputStream = mediaBinaryStream,
                    BucketName = _s3Options.Bucket,
                    //DisablePayloadSigning = true,

                    // 不设置 DisablePayloadSigning，或者置为 false
                    // DisablePayloadSigning = false,
                    ServerSideEncryptionMethod = ServerSideEncryptionMethod.None
                };

                // 设置公共读取权限（如果不使用预签名URL）
                if (!_s3Options.EnablePresignedUrl)
                {
                    request.CannedACL = S3CannedACL.PublicRead;
                }

                var response = client.PutObjectAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();

                if (response.HttpStatusCode == HttpStatusCode.OK)
                {
                    _logger.Information("S3上传成功: {Key}, ETag: {ETag}", key, response.ETag);

                    // 构建访问URL
                    string accessUrl;
                    if (_s3Options.EnablePresignedUrl && _s3Options.ExpiredMinutes > 0)
                    {
                        // 使用预签名URL
                        accessUrl = GetSignKey(key, _s3Options.ExpiredMinutes).ToString();
                    }
                    else if (!string.IsNullOrWhiteSpace(_s3Options.CustomCdn))
                    {
                        // 使用自定义CDN域名
                        accessUrl = $"{_s3Options.CustomCdn.TrimEnd('/')}/{_s3Options.Bucket}/{key.TrimStart('/')}";
                    }
                    else
                    {
                        // 使用默认S3 URL
                        if (_s3Options.ForcePathStyle)
                        {
                            accessUrl = $"{_s3Options.Endpoint.TrimEnd('/')}/{_s3Options.Bucket}/{key.TrimStart('/')}";
                        }
                        else
                        {
                            var baseUrl = _s3Options.Endpoint.Replace("://", $"://{_s3Options.Bucket}.");
                            accessUrl = $"{baseUrl.TrimEnd('/')}/{key.TrimStart('/')}";
                        }
                    }

                    return new UploadResult
                    {
                        FileName = fileName,
                        Key = key.TrimStart('/'),
                        Path = key.TrimStart('/'),
                        Size = fileSize,
                        Md5 = response.ETag?.Trim('"'),
                        Id = response.ETag,
                        ContentType = mimeType,
                        Url = accessUrl
                    };
                }
                else
                {
                    throw new Exception($"S3上传失败，状态码: {response.HttpStatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "S3上传文件异常: {Key}", key);
                throw;
            }
        }

        /// <summary>
        /// 删除文件
        /// </summary>
        /// <param name="isDeleteMedia">是否删除媒体记录</param>
        /// <param name="keys">文件键名数组</param>
        /// <returns></returns>
        public async Task DeleteAsync(bool isDeleteMedia = false, params string[] keys)
        {
            var client = GetClient();

            foreach (var key in keys)
            {
                try
                {
                    _logger.Information("S3删除文件: {Key}", key);

                    var request = new DeleteObjectRequest
                    {
                        BucketName = _s3Options.Bucket,
                        Key = key.TrimStart('/'),
                    };

                    await client.DeleteObjectAsync(request);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "S3删除文件异常: {Key}", key);
                }
            }
        }

        /// <summary>
        /// 获取文件流
        /// </summary>
        /// <param name="key">文件键名</param>
        /// <returns></returns>
        public Stream GetObject(string key)
        {
            try
            {
                var client = GetClient();
                var request = new GetObjectRequest
                {
                    BucketName = _s3Options.Bucket,
                    Key = key.TrimStart('/'),
                };

                var response = client.GetObjectAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
                return response.ResponseStream;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "S3获取文件异常: {Key}", key);
                throw;
            }
        }

        /// <summary>
        /// 获取文件流及内容类型
        /// </summary>
        /// <param name="key">文件键名</param>
        /// <param name="contentType">输出内容类型</param>
        /// <returns></returns>
        public Stream GetObject(string key, out string contentType)
        {
            try
            {
                var client = GetClient();
                var request = new GetObjectRequest
                {
                    BucketName = _s3Options.Bucket,
                    Key = key.TrimStart('/'),
                };

                var response = client.GetObjectAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
                contentType = response.Headers.ContentType;
                return response.ResponseStream;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "S3获取文件异常: {Key}", key);
                contentType = null;
                throw;
            }
        }

        /// <summary>
        /// 移动或复制文件
        /// </summary>
        /// <param name="key">源文件键名</param>
        /// <param name="newKey">目标文件键名</param>
        /// <param name="isCopy">是否为复制操作</param>
        /// <returns></returns>
        public async Task MoveAsync(string key, string newKey, bool isCopy = false)
        {
            try
            {
                var client = GetClient();
                var request = new CopyObjectRequest
                {
                    SourceBucket = _s3Options.Bucket,
                    SourceKey = key.TrimStart('/'),
                    DestinationBucket = _s3Options.Bucket,
                    DestinationKey = newKey.TrimStart('/'),
                };

                // 设置公共读取权限（如果不使用预签名URL）
                if (!_s3Options.EnablePresignedUrl)
                {
                    request.CannedACL = S3CannedACL.PublicRead;
                }

                await client.CopyObjectAsync(request);

                // 如果是移动操作，删除源文件
                if (!isCopy)
                {
                    await client.DeleteObjectAsync(_s3Options.Bucket, key.TrimStart('/'));
                }

                _logger.Information("S3文件{Operation}成功: {Key} -> {NewKey}", isCopy ? "复制" : "移动", key, newKey);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "S3文件{Operation}异常: {Key} -> {NewKey}", isCopy ? "复制" : "移动", key, newKey);
                throw;
            }
        }

        /// <summary>
        /// 检查文件是否存在
        /// </summary>
        /// <param name="key">文件键名</param>
        /// <returns></returns>
        public async Task<bool> ExistsAsync(string key)
        {
            try
            {
                var client = GetClient();
                var request = new GetObjectMetadataRequest
                {
                    BucketName = _s3Options.Bucket,
                    Key = key.TrimStart('/'),
                };

                var result = await client.GetObjectMetadataAsync(request);
                return !string.IsNullOrWhiteSpace(result?.ETag);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "S3检查文件存在异常: {Key}", key);
                return false;
            }
        }

        /// <summary>
        /// 获取预签名URL
        /// </summary>
        /// <param name="key">文件键名</param>
        /// <param name="expiredMinutes">过期时间（分钟）</param>
        /// <returns></returns>
        public Uri GetSignKey(string key, int expiredMinutes)
        {
            try
            {
                if (expiredMinutes <= 0)
                {
                    // 返回公共访问URL
                    if (!string.IsNullOrWhiteSpace(_s3Options.CustomCdn))
                    {
                        return new Uri($"{_s3Options.CustomCdn.TrimEnd('/')}/{_s3Options.Bucket}/{key.TrimStart('/')}");
                    }
                    else if (_s3Options.ForcePathStyle)
                    {
                        return new Uri($"{_s3Options.Endpoint.TrimEnd('/')}/{_s3Options.Bucket}/{key.TrimStart('/')}");
                    }
                    else
                    {
                        var baseUrl = _s3Options.Endpoint.Replace("://", $"://{_s3Options.Bucket}.");
                        return new Uri($"{baseUrl.TrimEnd('/')}/{key.TrimStart('/')}");
                    }
                }

                var client = GetClient();
                var request = new GetPreSignedUrlRequest
                {
                    BucketName = _s3Options.Bucket,
                    Key = key.TrimStart('/'),
                    Verb = HttpVerb.GET,
                    Expires = DateTime.UtcNow.AddMinutes(expiredMinutes),
                    Protocol = _s3Options.UseHttps ? Protocol.HTTPS : Protocol.HTTP,
                };

                var presignedUrl = client.GetPreSignedURL(request);
                return new Uri(presignedUrl);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "S3生成预签名URL异常: {Key}", key);
                throw;
            }
        }

        public void Overwrite(Stream mediaBinaryStream, string key, string mimeType)
        {
            SaveAsync(mediaBinaryStream, key, mimeType);
        }

        public string GetCustomCdn()
        {
            return $"{_s3Options.CustomCdn}/{_s3Options.Bucket}";
        }
    }
}