using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Midjourney.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Midjourney.Infrastructure
{
    public class BeanConfig
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ProxyProperties _properties;

        //public BeanConfig(IServiceProvider serviceProvider, IOptions<ProxyProperties> options)
        //{
        //    _serviceProvider = serviceProvider;
        //    _properties = options.Value;
        //}

        //public TranslateService TranslateService()
        //{
        //    return _properties.TranslateWay switch
        //    {
        //        TranslateWay.Baidu => new BaiduTranslateServiceImpl(_properties.BaiduTranslate),
        //        TranslateWay.Gpt => new GPTTranslateServiceImpl(_properties),
        //        _ => new NoTranslateServiceImpl()
        //    };
        //}

        //public TaskStoreService TaskStoreService(IConnectionMultiplexer redisConnection)
        //{
        //    var type = _properties.TaskStore.Type;
        //    var timeout = _properties.TaskStore.Timeout;

        //    return type switch
        //    {
        //        TaskStoreType.InMemory => new InMemoryTaskStoreServiceImpl(timeout),
        //        TaskStoreType.Redis => new RedisTaskStoreServiceImpl(timeout, TaskRedisTemplate(redisConnection)),
        //        _ => throw new NotSupportedException($"Unsupported TaskStoreType: {type}")
        //    };
        //}

        //public IDatabase TaskRedisTemplate(IConnectionMultiplexer redisConnection)
        //{
        //    return redisConnection.GetDatabase();
        //}

        //public HttpClient RestTemplate()
        //{
        //    return new HttpClient();
        //}

        //public IRule LoadBalancerRule()
        //{
        //    var ruleClassName = $"{typeof(IRule).Namespace}.{_properties.AccountChooseRule}";
        //    return (IRule)Activator.CreateInstance(Type.GetType(ruleClassName));
        //}

        //public IEnumerable<MessageHandler> MessageHandlers()
        //{
        //    return _serviceProvider.GetServices<MessageHandler>();
        //}
    }
}
