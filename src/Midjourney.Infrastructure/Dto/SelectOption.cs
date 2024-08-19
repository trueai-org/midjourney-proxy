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