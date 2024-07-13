using System.Text.Json.Serialization;

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// 按钮组件自定义属性。
    /// </summary>
    public class CustomComponentModel
    {
        public string CustomId { get; set; } = string.Empty;

        public string Emoji { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public int Style { get; set; }

        public int Type { get; set; }
    }
}