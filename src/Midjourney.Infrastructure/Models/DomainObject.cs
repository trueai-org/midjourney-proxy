using Midjourney.Infrastructure.Services;
using System.Runtime.Serialization;

using JsonIgnoreAttribute = Newtonsoft.Json.JsonIgnoreAttribute;

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// 基础领域对象类，支持扩展属性和线程同步操作。
    /// </summary>
    [DataContract]
    public class DomainObject : ISerializable, IBaseId
    {
        [JsonIgnore]
        private readonly object _lock = new object();

        private Dictionary<string, object> _properties;

        /// <summary>
        /// 对象ID。
        /// </summary>
        [DataMember]
        public string Id { get; set; }

        /// <summary>
        /// 暂停当前线程，等待唤醒。
        /// </summary>
        public void Sleep()
        {
            lock (_lock)
            {
                Monitor.Wait(_lock);
            }
        }

        /// <summary>
        /// 唤醒所有等待当前对象锁的线程。
        /// </summary>
        public void Awake()
        {
            lock (_lock)
            {
                Monitor.PulseAll(_lock);
            }
        }

        /// <summary>
        /// 设置扩展属性。
        /// </summary>
        /// <param name="name">属性名称。</param>
        /// <param name="value">属性值。</param>
        /// <returns>当前对象实例。</returns>
        public DomainObject SetProperty(string name, object value)
        {
            Properties[name] = value;
            return this;
        }

        /// <summary>
        /// 移除扩展属性。
        /// </summary>
        /// <param name="name">属性名称。</param>
        /// <returns>当前对象实例。</returns>
        public DomainObject RemoveProperty(string name)
        {
            Properties.Remove(name);
            return this;
        }

        /// <summary>
        /// 获取扩展属性值。
        /// </summary>
        /// <param name="name">属性名称。</param>
        /// <returns>属性值。</returns>
        public object GetProperty(string name)
        {
            Properties.TryGetValue(name, out var value);
            return value;
        }

        /// <summary>
        /// 获取泛型扩展属性值。
        /// </summary>
        /// <typeparam name="T">属性类型。</typeparam>
        /// <param name="name">属性名称。</param>
        /// <returns>属性值。</returns>
        public T GetPropertyGeneric<T>(string name)
        {
            return (T)GetProperty(name);
        }

        /// <summary>
        /// 获取扩展属性值，并指定默认值。
        /// </summary>
        /// <typeparam name="T">属性类型。</typeparam>
        /// <param name="name">属性名称。</param>
        /// <param name="defaultValue">默认值。</param>
        /// <returns>属性值或默认值。</returns>
        public T GetProperty<T>(string name, T defaultValue)
        {
            return Properties.TryGetValue(name, out var value) ? (T)value : defaultValue;
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("Id", Id);
            info.AddValue("Properties", Properties);
        }

        /// <summary>
        /// 获取或初始化扩展属性字典。
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, object> Properties
        {
            get => _properties ??= new Dictionary<string, object>();
            set => _properties = value;
        }

        /// <summary>
        /// 克隆这个对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Clone<T>()
        {
            return (T)MemberwiseClone();
        }
    }
}