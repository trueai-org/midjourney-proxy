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
namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 泛型单例基类。
    /// </summary>
    /// <typeparam name="T">单例类的类型。</typeparam>
    public abstract class SingletonBase<T> where T : SingletonBase<T>, new()
    {
        // 静态变量用于存储单例实例。
        private static T instance;

        // 用于锁定以避免在多线程环境中创建多个实例。
        private static readonly object lockObject = new();

        /// <summary>
        /// 私有构造函数以防止外部实例化。
        /// </summary>
        protected SingletonBase()
        {
            // 防止通过反射创建实例。
            if (instance != null)
            {
                throw new InvalidOperationException("只能创建一个实例。");
            }
        }

        /// <summary>
        /// 获取单例实例的静态属性。
        /// </summary>
        public static T Instance
        {
            get
            {
                // 双重检查锁定以确保只创建一个实例。
                if (instance == null)
                {
                    lock (lockObject)
                    {
                        if (instance == null)
                        {
                            instance = new T();
                        }
                    }
                }
                return instance;
            }
        }
    }
}