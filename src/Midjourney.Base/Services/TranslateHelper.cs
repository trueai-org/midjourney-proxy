using Midjourney.Base.Services;

namespace Midjourney.Base
{
    /// <summary>
    /// 翻译助手。
    /// </summary>
    public class TranslateHelper
    {
        /// <summary>
        /// 翻译服务。
        /// </summary>
        public static ITranslateService Instance { get; private set; }

        /// <summary>
        /// 初始化翻译服务。
        /// </summary>
        /// <param name="translateService"></param>
        public static void Initialize(ITranslateService translateService)
        {
            Instance = translateService;
        }
    }
}