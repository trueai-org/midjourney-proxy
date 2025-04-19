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

namespace Midjourney.Infrastructure.Storage
{
    /// <summary>
    /// cloudflare s2 存储服务
    /// </summary>
    public class CloudflareR2StorageService : IStorageService
    {
        private readonly CloudflareR2Options _r2Options;
        private readonly ILogger _logger;

        public CloudflareR2StorageService()
        {
            _r2Options = GlobalConfiguration.Setting.CloudflareR2;

            _logger = Log.Logger;
        }

        public AmazonS3Client GetClient()
        {
            var credentials = new BasicAWSCredentials(_r2Options.AccessKey, _r2Options.SecretKey);
            var s3Client = new AmazonS3Client(credentials, new AmazonS3Config
            {
                ServiceURL = $"https://{_r2Options.AccountId}.r2.cloudflarestorage.com",
            });
            return s3Client;
        }

        public UploadResult SaveAsync(Stream mediaBinaryStream, string key, string mimeType)
        {
            if (mediaBinaryStream == null || mediaBinaryStream?.Length <= 0)
            {
                throw new ArgumentNullException(nameof(mediaBinaryStream));
            }

            try
            {
                var client = GetClient();

                var request = new PutObjectRequest
                {
                    Key = key,
                    ContentType = mimeType,
                    InputStream = mediaBinaryStream,
                    BucketName = _r2Options.Bucket,
                    DisablePayloadSigning = true,
                };
                var response = client.PutObjectAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();

                if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
                {
                    _logger.Information("上传成功 {@0}", response.ETag);

                    return new UploadResult
                    {
                        FileName = Path.GetFileName(key),
                        Key = key,
                        Size = response.ContentLength,
                        Md5 = response.ETag,
                        ContentType = mimeType,
                        Url = GetSignKey(key, _r2Options.ExpiredMinutes).ToString()
                    };
                }
                else
                {
                    throw new Exception("上传失败");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "上传文件异常 {@key}", key);
                throw;
            }
        }

        public async Task DeleteAsync(bool isDeleteMedia = false, params string[] keys)
        {
            foreach (var key in keys)
            {
                try
                {
                    _logger.Information("删除文件: {@key}", key);
                    var client = GetClient();
                    var request = new DeleteObjectRequest
                    {
                        BucketName = _r2Options.Bucket,
                        Key = key,
                    };
                    await client.DeleteObjectAsync(request);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "删除文件异常, {key}", key);
                }
            }

            await Task.CompletedTask;
        }

        public Stream GetObject(string key)
        {
            try
            {
                var client = GetClient();
                var request = new GetObjectRequest
                {
                    BucketName = _r2Options.Bucket,
                    Key = key,
                };
                var response = client.GetObjectAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();

                return response.ResponseStream;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "下载文件异常 {@key}", key);
                throw;
            }
        }

        public async Task MoveAsync(string key, string newKey, bool isCopy = false)
        {
            try
            {
                var client = GetClient();
                var request = new CopyObjectRequest
                {
                    SourceBucket = _r2Options.Bucket,
                    SourceKey = key,
                    DestinationBucket = _r2Options.Bucket,
                    DestinationKey = newKey,
                };
                if (isCopy)
                {
                    await client.CopyObjectAsync(request);
                }
                else
                {
                    await client.CopyObjectAsync(request);
                    await client.DeleteObjectAsync(_r2Options.Bucket, key);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "移动文件异常 {@key}, {@newKey}", key, newKey);
            }
        }

        public async Task<bool> ExistsAsync(string key)
        {
            try
            {
                var client = GetClient();
                var request = new GetObjectMetadataRequest
                {
                    BucketName = _r2Options.Bucket,
                    Key = key,
                };
                var result = await client.GetObjectMetadataAsync(request);

                return !string.IsNullOrWhiteSpace(result?.ETag);
            }
            catch
            {
                return false;
            }
        }

        public void Overwrite(Stream mediaBinaryStream, string key, string mimeType)
        {
            SaveAsync(mediaBinaryStream, key, mimeType);
        }

        /// <summary>
        /// 生成带签名的 URL，设置过期时间（单位：分钟）
        /// </summary>
        /// <param name="key">文件的对象 Key</param>
        /// <param name="minutes">签名的过期时间，默认 60 分钟</param>
        /// <returns>带签名的 URL</returns>
        public Uri GetSignKey(string key, int minutes = 60)
        {
            try
            {
                if (minutes <= 0)
                {
                    return new Uri($"{_r2Options.CustomCdn}/{key}");
                }

                AWSConfigsS3.UseSignatureVersion4 = true;
                var presign = new GetPreSignedUrlRequest
                {
                    BucketName = _r2Options.Bucket,
                    Key = key,
                    Verb = HttpVerb.GET,
                    Expires = DateTime.Now.AddMinutes(minutes),
                };

                var client = GetClient();

                var presignedUrl = client.GetPreSignedURL(presign);

                return new Uri(presignedUrl);
            }
            catch (Exception ex)
            {
                throw new Exception("生成签名 URL 异常", ex);
            }
        }

        public string GetCustomCdn()
        {
            return _r2Options.CustomCdn;
        }
    }
}