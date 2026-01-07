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

namespace Midjourney.Base
{
    /// <summary>
    /// 泛型单例基类（使用 Lazy 实现）。
    /// </summary>
    /// <typeparam name="T">单例类的类型。</typeparam>
    public abstract class SingletonBase<T> where T : SingletonBase<T>, new()
    {
        // 使用 Lazy<T> 确保线程安全和延迟初始化
        private static readonly Lazy<T> _instance = new(() => new T(), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// 受保护的构造函数以防止外部实例化。
        /// </summary>
        protected SingletonBase()
        {
            // 防止通过反射创建多个实例
            if (_instance.IsValueCreated)
            {
                throw new InvalidOperationException($"类型 {typeof(T).Name} 的实例已存在。");
            }
        }

        /// <summary>
        /// 获取单例实例。
        /// </summary>
        public static T Instance => _instance.Value;
    }
}