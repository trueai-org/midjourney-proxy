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
using System.Text.Json;
using Newtonsoft.Json;

namespace Midjourney.Base
{
    public static class JsonExtensions
    {
        /// <summary>
        /// 使用 Newtonsoft.Json 序列化对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <param name="jsonSerializerSettings"></param>
        /// <returns></returns>
        public static string ToJson<T>(this T obj, JsonSerializerSettings jsonSerializerSettings = null)
        {
            return JsonConvert.SerializeObject(obj, jsonSerializerSettings);
        }

        /// <summary>
        /// 使用 Newtonsoft.Json 反序列化对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <param name="jsonSerializerSettings"></param>
        /// <returns></returns>
        public static T ToObject<T>(this string json, JsonSerializerSettings jsonSerializerSettings = null)
        {
            return JsonConvert.DeserializeObject<T>(json, jsonSerializerSettings);
        }

        /// <summary>
        /// 使用 System.Text.Json 序列化对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <param name="jsonSerializerOptions"></param>
        /// <returns></returns>
        public static string ToSystemTextJson<T>(this T obj, JsonSerializerOptions jsonSerializerOptions = null)
        {
            return System.Text.Json.JsonSerializer.Serialize(obj, jsonSerializerOptions);
        }

        /// <summary>
        /// 使用 System.Text.Json 反序列化对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public static T ToSystemTextObject<T>(this string json, System.Text.Json.JsonSerializerOptions options = null)
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, options);
        }

        /// <summary>
        /// 使用 System.Text.Json 反序列化对象，失败时不抛异常
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <param name="result"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public static bool TryToSystemTextObject<T>(this string json, out T result, System.Text.Json.JsonSerializerOptions options = null)
        {
            try
            {
                result = System.Text.Json.JsonSerializer.Deserialize<T>(json, options);
                return true;
            }
            catch
            {
                result = default;
                return false;
            }
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