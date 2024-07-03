using Microsoft.Extensions.Caching.Distributed;
using System.Text;

namespace Midjourney.Infrastructure.Services
{
    ///// <summary>
    ///// Redis任务存储服务实现类。
    ///// </summary>
    //public class RedisTaskStoreServiceImpl : ITaskStoreService
    //{
    //    private const string KeyPrefix = "mj-task-store::";
    //    private readonly TimeSpan _timeout;
    //    private readonly IDistributedCache _distributedCache;

    //    /// <summary>
    //    /// 初始化 RedisTaskStoreServiceImpl 类的新实例。
    //    /// </summary>
    //    /// <param name="timeout">任务超时时间。</param>
    //    /// <param name="distributedCache">分布式缓存实例。</param>
    //    public RedisTaskStoreServiceImpl(TimeSpan timeout, IDistributedCache distributedCache)
    //    {
    //        _timeout = timeout;
    //        _distributedCache = distributedCache;
    //    }

    //    /// <inheritdoc />
    //    public void Save(TaskInfo task)
    //    {
    //        var serializedTask = Serialize(task);
    //        _distributedCache.SetString(GetRedisKey(task.Id), serializedTask, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _timeout });
    //    }

    //    /// <inheritdoc />
    //    public void Delete(string id)
    //    {
    //        _distributedCache.Remove(GetRedisKey(id));
    //    }

    //    /// <inheritdoc />
    //    public TaskInfo Get(string id)
    //    {
    //        var serializedTask = _distributedCache.GetString(GetRedisKey(id));
    //        return string.IsNullOrEmpty(serializedTask) ? null : Deserialize<TaskInfo>(serializedTask);
    //    }

    //    /// <inheritdoc />
    //    public List<TaskInfo> List()
    //    {
    //        // 获取所有任务键
    //        var keys = _distributedCache.ScanKeys(KeyPrefix + "*");
    //        return keys.Select(key => Deserialize<TaskInfo>(_distributedCache.GetString(key))).Where(task => task != null).ToList();
    //    }

    //    /// <inheritdoc />
    //    public List<TaskInfo> List(TaskCondition condition)
    //    {
    //        return List().Where(task => condition.Matches(task)).ToList();
    //    }

    //    /// <inheritdoc />
    //    public TaskInfo FindOne(TaskCondition condition)
    //    {
    //        return List().FirstOrDefault(task => condition.Matches(task));
    //    }

    //    private string GetRedisKey(string id) => $"{KeyPrefix}{id}";

    //    private string Serialize(object obj)
    //    {
    //        return System.Text.Json.JsonSerializer.Serialize(obj);
    //    }

    //    private T Deserialize<T>(string serializedObj)
    //    {
    //        return System.Text.Json.JsonSerializer.Deserialize<T>(serializedObj);
    //    }
    //}
}