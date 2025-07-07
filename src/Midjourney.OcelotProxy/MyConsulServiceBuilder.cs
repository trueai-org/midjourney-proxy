using Consul;
using Ocelot.Logging;
using Ocelot.Provider.Consul.Interfaces;
using Ocelot.Provider.Consul;

namespace Midjourney.OcelotProxy
{
    /// <summary>
    /// 根据官方文档的建议，您需要创建自定义的 ConsulServiceBuilder 来使用服务的实际地址而不是节点名称。
    /// https://ocelot.readthedocs.io/en/latest/features/servicediscovery.html#consul-service-builder
    /// </summary>
    public class MyConsulServiceBuilder : DefaultConsulServiceBuilder
    {
        public MyConsulServiceBuilder(IHttpContextAccessor contextAccessor, IConsulClientFactory clientFactory, IOcelotLoggerFactory loggerFactory)
            : base(contextAccessor, clientFactory, loggerFactory) { }

        // 使用服务的实际 IP 地址作为下游主机名，而不是节点名
        protected override string GetDownstreamHost(ServiceEntry entry, Node node)
            => entry.Service.Address;

        ///// <summary>
        ///// 使用服务的实际 IP 地址作为下游主机名
        ///// </summary>
        //protected override string GetDownstreamHost(ServiceEntry entry, Node node)
        //{
        //    var serviceAddress = entry.Service.Address;
        //    var nodeName = node?.Name;

        //    _logger.LogDebug($"Service ID: {entry.Service.ID}, Service Address: {serviceAddress}, Node Name: {nodeName}");

        //    // 优先使用服务地址，如果为空则使用节点名
        //    var host = !string.IsNullOrEmpty(serviceAddress) ? serviceAddress : nodeName;

        //    _logger.LogInformation($"选择的下游主机: {host} (Service: {entry.Service.Service})");

        //    return host;
        //}

        ///// <summary>
        ///// 获取下游端口
        ///// </summary>
        //protected override int GetDownstreamPort(ServiceEntry entry)
        //{
        //    var port = entry.Service.Port;
        //    _logger.LogDebug($"下游端口: {port} (Service: {entry.Service.Service})");
        //    return port;
        //}
    }
}
