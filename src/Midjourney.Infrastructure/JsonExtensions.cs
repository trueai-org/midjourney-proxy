using System.Text.Json;

namespace Midjourney.Infrastructure
{
    public static class JsonExtensions
    {
        public static string ToJson<T>(this T obj)
        {
            return JsonSerializer.Serialize(obj);
        }

        public static T ToObject<T>(this string json)
        {
            return JsonSerializer.Deserialize<T>(json);
        }
    }
}