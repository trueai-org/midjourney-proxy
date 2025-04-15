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

using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.Services;
using Newtonsoft.Json;
using RestSharp;
using Serilog;
using System.Collections.Concurrent;

using ILogger = Serilog.ILogger;
using WebProxy = System.Net.WebProxy;

namespace Midjourney.Infrastructure.LoadBalancer
{
    /// <summary>
    /// 换脸服务
    ///
    /// TODO webhooks
    /// https://replicate.com/docs/reference/webhooks
    /// 
    /// https://github.com/deepinsight/insightface
    /// https://www.picsi.ai/
    /// https://www.picsi.ai/faceswap
    /// </summary>
    public class BaseFaceSwapInstance
    {
        protected static readonly object _lock = new();

        protected readonly ILogger _logger = Log.Logger;
        protected readonly ITaskStoreService _taskStoreService;
        protected readonly INotifyService _notifyService;

        protected readonly DiscordHelper _discordHelper;
        protected readonly HttpClient _httpClient;
        protected readonly IMemoryCache _cache;

        protected readonly ConcurrentDictionary<TaskInfo, int> _runningTasks = [];
        protected readonly ConcurrentDictionary<string, Task> _taskFutureMap = new();

        protected ConcurrentQueue<TaskInfo> _queueTasks = new();

        public BaseFaceSwapInstance(ITaskStoreService taskStoreService, INotifyService notifyService, IMemoryCache memoryCache, DiscordHelper discordHelper)
        {
            var config = GlobalConfiguration.Setting;

            WebProxy webProxy = null;
            if (!string.IsNullOrEmpty(config.Proxy?.Host))
            {
                webProxy = new WebProxy(config.Proxy.Host, config.Proxy.Port ?? 80);
            }

            var hch = new HttpClientHandler
            {
                UseProxy = webProxy != null,
                Proxy = webProxy
            };

            _httpClient = new HttpClient(hch)
            {
                Timeout = TimeSpan.FromMinutes(15),
            };

            _discordHelper = discordHelper;
            _taskStoreService = taskStoreService;
            _notifyService = notifyService;
            _cache = memoryCache;
        }

        /// <summary>
        /// 获取正在运行的任务列表。
        /// </summary>
        /// <returns>正在运行的任务列表</returns>
        public List<TaskInfo> GetRunningTasks() => _runningTasks.Keys.ToList();

        /// <summary>
        /// 获取队列中的任务列表。
        /// </summary>
        /// <returns>队列中的任务列表</returns>
        public List<TaskInfo> GetQueueTasks() => new List<TaskInfo>(_queueTasks);

        /// <summary>
        /// 退出任务并进行保存和通知。
        /// </summary>
        /// <param name="task">任务信息</param>
        public void ExitTask(TaskInfo task)
        {
            _taskFutureMap.TryRemove(task.Id, out _);

            SaveAndNotify(task);

            // 判断 _queueTasks 队列中是否存在指定任务，如果有则移除
            //if (_queueTasks.Any(c => c.Item1.Id == task.Id))
            //{
            //    _queueTasks = new ConcurrentQueue<(TaskInfo, Func<Task<Message>>)>(_queueTasks.Where(c => c.Item1.Id != task.Id));
            //}

            // 判断 _queueTasks 队列中是否存在指定任务，如果有则移除
            // 使用线程安全的方式移除
            if (_queueTasks.Any(c => c.Id == task.Id))
            {
                // 移除 _queueTasks 队列中指定的任务
                var tempQueue = new ConcurrentQueue<TaskInfo>();

                // 将不需要移除的元素加入到临时队列中
                while (_queueTasks.TryDequeue(out var item))
                {
                    if (item.Id != task.Id)
                    {
                        tempQueue.Enqueue(item);
                    }
                }

                // 交换队列引用
                _queueTasks = tempQueue;
            }
        }

        /// <summary>
        /// 获取正在运行的任务Future映射。
        /// </summary>
        /// <returns>任务Future映射</returns>
        public Dictionary<string, Task> GetRunningFutures() => new Dictionary<string, Task>(_taskFutureMap);

        public void AddRunningTask(TaskInfo task)
        {
            _runningTasks.TryAdd(task, 0);
        }

        public void RemoveRunningTask(TaskInfo task)
        {
            _runningTasks.TryRemove(task, out _);
        }

        /// <summary>
        /// 异步保存和通知任务。
        /// </summary>
        /// <param name="task">任务信息</param>
        /// <returns>异步任务</returns>
        public async Task AsyncSaveAndNotify(TaskInfo task) => await Task.Run(() => SaveAndNotify(task));

        /// <summary>
        /// 保存并通知任务状态变化。
        /// </summary>
        /// <param name="task">任务信息</param>
        public void SaveAndNotify(TaskInfo task)
        {
            _taskStoreService.Save(task);
            _notifyService.NotifyTaskChange(task);
        }

        /// <summary>
        /// 查找符合条件的正在运行的任务。
        /// </summary>
        /// <param name="condition">条件</param>
        /// <returns>符合条件的正在运行的任务列表</returns>
        public IEnumerable<TaskInfo> FindRunningTask(Func<TaskInfo, bool> condition)
        {
            return GetRunningTasks().Where(condition);
        }

        /// <summary>
        /// 根据ID获取正在运行的任务。
        /// </summary>
        /// <param name="id">任务ID</param>
        /// <returns>任务信息</returns>
        public TaskInfo GetRunningTask(string id)
        {
            return GetRunningTasks().FirstOrDefault(t => id == t.Id);
        }

        /// <summary>
        /// 根据 ID 获取历史任务
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public TaskInfo GetTask(string id)
        {
            return _taskStoreService.Get(id);
        }

        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="localPath"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<ReplicateFileResponse> UploadFile(string localPath, string token)
        {
            var options = new RestClientOptions()
            {
                MaxTimeout = -1,
            };
            var client = new RestClient(options);
            var request = new RestRequest("https://api.replicate.com/v1/files", Method.Post);
            request.AddHeader("Authorization", $"Bearer {token}");
            request.AlwaysMultipartFormData = true;
            request.AddFile("content", localPath);
            var response = await client.ExecuteAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return JsonConvert.DeserializeObject<ReplicateFileResponse>(response.Content);
            }
            else
            {
                throw new Exception(response.Content);
            }

            //var request = new HttpRequestMessage(HttpMethod.Post, "https://api.replicate.com/v1/files");
            //request.Headers.Add("Authorization", $"Bearer {token}");
            //var content = new MultipartFormDataContent();
            //content.Add(new StreamContent(File.OpenRead(localPath)), "content", Path.GetFileName(localPath));
            //request.Content = content;
            //var response = await _httpClient.SendAsync(request);
            //response.EnsureSuccessStatusCode();
            //var json = await response.Content.ReadAsStringAsync();
            //return JsonConvert.DeserializeObject<ReplicateFileResponse>(json);

            //var request = new HttpRequestMessage(HttpMethod.Post, "https://api.replicate.com/v1/files");
            //request.Headers.Add("Authorization", $"Bearer {token}");

            //// 读取文件内容为字节数组
            //var fileBytes = await File.ReadAllBytesAsync(localPath);

            //// 使用 ByteArrayContent 代替 StreamContent
            //var content = new MultipartFormDataContent();
            //content.Add(new ByteArrayContent(fileBytes), "content", Path.GetFileName(localPath));

            //request.Content = content;

            //// 发送请求并确保成功
            //var response = await _httpClient.SendAsync(request);
            //response.EnsureSuccessStatusCode();

            //// 读取响应内容并反序列化为对象
            //var json = await response.Content.ReadAsStringAsync();
            //return JsonConvert.DeserializeObject<ReplicateFileResponse>(json);
        }

        /// <summary>
        /// 进行人脸替换请求
        /// </summary>
        /// <param name="dto"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<ReplicateFaceSwapResponse> FaceSwapSubmit(ReplicateFaceSwapRequest dto, string token)
        {
            var options = new RestClientOptions()
            {
                MaxTimeout = -1,
            };
            var client = new RestClient(options);
            var request = new RestRequest("https://api.replicate.com/v1/predictions", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {token}");
            request.AddBody(new
            {
                version = dto.Version,
                input = new
                {
                    swap_image = dto.Input.SwapImage,
                    input_image = dto.Input.InputImage
                }
            }, contentType: ContentType.Json);
            var response = await client.ExecuteAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return JsonConvert.DeserializeObject<ReplicateFaceSwapResponse>(response.Content);
            }
            else
            {
                throw new Exception(response.Content);
            }

            //var request = new HttpRequestMessage(HttpMethod.Post, "https://api.replicate.com/v1/predictions");
            //request.Headers.Add("Authorization", $"Bearer {token}");
            //var req = dto.ToJson();
            //var content = new StringContent(req, null, "application/json");
            //request.Content = content;
            //var response = await _httpClient.SendAsync(request);
            //response.EnsureSuccessStatusCode();
            //var json = await response.Content.ReadAsStringAsync();
            //return JsonConvert.DeserializeObject<ReplicateFaceSwapResponse>(json);
        }

        /// <summary>
        /// 进行人脸替换请求
        /// </summary>
        /// <param name="dto"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<ReplicateFaceSwapResponse> VideoFaceSwapSubmit(object dto, string token)
        {
            var options = new RestClientOptions()
            {
                MaxTimeout = -1,
            };
            var client = new RestClient(options);
            var request = new RestRequest("https://api.replicate.com/v1/predictions", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {token}");
            request.AddBody(dto, contentType: ContentType.Json);
            var response = await client.ExecuteAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return JsonConvert.DeserializeObject<ReplicateFaceSwapResponse>(response.Content);
            }
            else
            {
                throw new Exception(response.Content);
            }
        }

        /// <summary>
        /// 获取换脸执行结果
        /// </summary>
        /// <param name="url"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<ReplicateFaceSwapGetResponse> FaceSwapGet(string url, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {token}");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ReplicateFaceSwapGetResponse>(json);
        }

        /// <summary>
        /// 取消任务执行
        /// </summary>
        /// <param name="url"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task FaceCancel(string url, string token)
        {
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {token}");
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    /// 表示一个文件复制响应的对象，包含了文件的基本信息、校验值、创建时间、过期时间及访问URL。
    /// </summary>
    public class ReplicateFileResponse
    {
        /// <summary>
        /// 获取或设置文件的唯一标识符。
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 获取或设置文件的名称。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 获取或设置文件的内容类型，例如 image/webp。
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// 获取或设置文件的大小，以字节为单位。
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// 获取或设置文件的ETag值，用于标识特定版本的文件内容。
        /// </summary>
        public string ETag { get; set; }

        /// <summary>
        /// 获取或设置文件的校验和，包括SHA-256和MD5值。
        /// </summary>
        public Checksums Checksums { get; set; }

        /// <summary>
        /// 获取或设置文件的元数据，通常为空。
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; }

        /// <summary>
        /// 获取或设置文件的创建时间。
        /// </summary>
        [JsonProperty("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>
        /// 获取或设置文件的过期时间。
        /// </summary>
        [JsonProperty("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }

        /// <summary>
        /// 获取或设置文件的访问URL集合。
        /// </summary>
        public ReplicateUrls Urls { get; set; }
    }

    /// <summary>
    /// 表示文件的校验和，包括SHA-256和MD5值。
    /// </summary>
    public class Checksums
    {
        /// <summary>
        /// 获取或设置文件的SHA-256校验和。
        /// </summary>
        public string Sha256 { get; set; }

        /// <summary>
        /// 获取或设置文件的MD5校验和。
        /// </summary>
        public string Md5 { get; set; }
    }

    /// <summary>
    /// 表示文件的访问URL集合，通常包含获取文件的URL。
    /// </summary>
    public class ReplicateUrls
    {
        /// <summary>
        /// 获取或设置文件的获取URL，用于下载或访问文件。
        /// </summary>
        public string Get { get; set; }

        /// <summary>
        /// 获取或设置用于取消预测操作的URL。
        /// </summary>
        public string Cancel { get; set; }
    }

    /// <summary>
    /// 人脸替换输入
    /// </summary>
    public class ReplicateFaceSwapRequest
    {
        /// <summary>
        /// 获取或设置版本信息的哈希值。
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// 获取或设置输入文件的集合，包括交换图像和输入图像的URL。
        /// </summary>
        public ReplicateFaceSwapRequestInput Input { get; set; }
    }

    /// <summary>
    /// 表示输入文件的URL集合。
    /// </summary>
    public class ReplicateFaceSwapRequestInput
    {
        /// <summary>
        /// 获取或设置交换图像的URL。
        /// </summary>
        [JsonProperty("swap_image")]
        public string SwapImage { get; set; }

        /// <summary>
        /// 获取或设置输入图像的URL。
        /// </summary>
        [JsonProperty("input_image")]
        public string InputImage { get; set; }

        /// <summary>
        /// 视频换脸 - 人脸
        /// </summary>
        [JsonProperty("target")]
        public string Target { get; set; }

        /// <summary>
        /// 视频换脸 - 源视频
        /// </summary>
        [JsonProperty("source")]
        public string Source { get; set; }
    }

    /// <summary>
    /// 人脸替换结果
    /// </summary>
    public class ReplicateFaceSwapResponse
    {
        /// <summary>
        /// 获取或设置预测操作的唯一标识符。
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 获取或设置用于执行预测的模型名称。
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// 获取或设置模型的版本哈希值。
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// 获取或设置输入文件的集合，包括输入图像和交换图像的URL。
        /// </summary>
        public ReplicateFaceSwapRequestInput Input { get; set; }

        /// <summary>
        /// 获取或设置操作日志的内容。
        /// </summary>
        public string Logs { get; set; }

        /// <summary>
        /// 获取或设置预测操作的输出内容，当前为 null。
        /// </summary>
        public dynamic Output { get; set; }

        /// <summary>
        /// 获取或设置一个值，指示数据是否已被删除。
        /// </summary>
        public bool DataRemoved { get; set; }

        /// <summary>
        /// 获取或设置操作的错误信息，如果没有错误则为 null。
        /// </summary>
        public object Error { get; set; }

        /// <summary>
        /// 获取或设置预测操作的状态，如 "starting"。
        /// starting：预测正在启动。如果此状态持续超过几秒钟，则通常是因为正在启动新的工作程序来运行预测。
        /// processing：predict()该模型的方法当前正在运行。
        /// succeeded：预测成功完成。
        /// failed：预测在处理过程中遇到错误。
        /// canceled：该预测已被其创建者取消。
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// 获取或设置预测操作的创建时间。
        /// </summary>
        [JsonProperty("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>
        /// 获取或设置预测操作的相关URL集合，包括取消和获取操作的URL。
        /// </summary>
        public ReplicateUrls Urls { get; set; }
    }

    /// <summary>
    /// 人脸替换轮询获取结果
    /// </summary>
    public class ReplicateFaceSwapGetResponse : ReplicateFaceSwapResponse
    {
        /// <summary>
        /// 完成时间
        /// </summary>
        [JsonProperty("completed_at")]
        public DateTimeOffset? CompletedAt { get; set; }

        /// <summary>
        /// 开始时间
        /// </summary>
        [JsonProperty("started_at")]
        public DateTimeOffset? StartedAt { get; set; }

        /// <summary>
        /// 计算
        /// </summary>
        public ReplicateFaceSwapGetMetrics Metrics { get; set; }
    }

    public class ReplicateFaceSwapGetMetrics
    {
        /// <summary>
        /// 属性显示预测在运行时使用的 CPU 或 GPU 时间（以秒为单位）。它不包括等待预测开始的时间
        /// </summary>
        public decimal? PredictTime { get; set; }
    }
}