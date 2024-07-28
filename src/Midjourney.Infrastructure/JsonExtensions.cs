using Newtonsoft.Json;

namespace Midjourney.Infrastructure
{
    public static class JsonExtensions
    {
        public static string ToJson<T>(this T obj)
        {
            return JsonConvert.SerializeObject(obj);
        }

        public static T ToObject<T>(this string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

        /// <summary>
        /// 通过 Serialize 方式获取一个深度拷贝的对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <returns></returns>
        public static T DeepClone<T>(this T value)
        {
            var json = value.ToJson();
            if (!string.IsNullOrWhiteSpace(json))
            {
                return json.ToObject<T>();
            }
            return default;
        }
    }
}