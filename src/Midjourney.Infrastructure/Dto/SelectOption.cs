namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// 下拉选项
    /// </summary>
    public class SelectOption : SelectOption<string>
    {
    }

    /// <summary>
    /// 包含子级的下拉选项
    /// </summary>
    public class SelectChildrenOption : SelectOption<string>
    {
        public List<SelectOption> Children { get; set; } = new List<SelectOption>();
    }

    /// <summary>
    /// 包含子级的下拉选项
    /// </summary>
    public class SelectChildrenOption<T> : SelectOption<T>
    {
        public List<SelectOption> Children { get; set; } = new List<SelectOption>();
    }

    /// <summary>
    /// 包含子级的下拉选项
    /// </summary>
    public class SelectChildrenWithSort<T> : SelectOption<T>
    {
        /// <summary>
        /// 子级
        /// </summary>
        public List<SelectChildrenWithSort<T>> Children { get; set; } = new List<SelectChildrenWithSort<T>>();

        /// <summary>
        /// 序号
        /// </summary>
        public int Sort { get; set; }
    }

    /// <summary>
    /// 下拉选项
    /// </summary>
    /// <typeparam name="TValue"></typeparam>
    public class SelectOption<TValue>
    {
        /// <summary>
        /// 名称
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// 值
        /// </summary>
        public TValue Value { get; set; }

        /// <summary>
        /// 是否禁用
        /// </summary>
        public bool Disabled { get; set; }

        /// <summary>
        /// 是否默认
        /// </summary>
        public bool Default { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 图片
        /// </summary>
        public string Image { get; set; }

        /// <summary>
        /// 数量
        /// </summary>
        public int Count { get; set; }
    }

    /// <summary>
    /// 下拉选项（区分类型）
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SelectOptionWithType<T> : SelectOption<T>
    {
        /// <summary>
        /// 类型
        /// </summary>
        public int? Type { get; set; }
    }

    /// <summary>
    /// 扩展时间类型下拉框
    /// </summary>
    public class SelectOptionOnTime : SelectOption
    {
        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime? StartOn { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime? EndOn { get; set; }
    }
}