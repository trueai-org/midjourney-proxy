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