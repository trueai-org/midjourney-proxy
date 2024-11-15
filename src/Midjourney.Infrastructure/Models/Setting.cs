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

using Midjourney.Infrastructure.Options;

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// 系统配置
    /// </summary>
    public class Setting : ProxyProperties
    {
        /// <summary>
        /// 全局开启垂直领域
        /// </summary>
        public bool IsVerticalDomain { get; set; }

        /// <summary>
        /// 启用 Swagger
        /// </summary>
        public bool EnableSwagger { get; set; }

        /// <summary>
        /// Banned 限流配置
        /// </summary>
        public BannedLimitingOptions BannedLimiting { get; set; } = new();

        /// <summary>
        /// 限流配置
        /// </summary>
        public IpRateLimitingOptions IpRateLimiting { get; set; }

        /// <summary>
        /// 黑名单限流配置
        /// </summary>
        public IpBlackRateLimitingOptions IpBlackRateLimiting { get; set; }

        /// <summary>
        /// 开启注册
        /// </summary>
        public bool EnableRegister { get; set; }

        /// <summary>
        /// 注册用户默认日绘图限制
        /// </summary>
        public int RegisterUserDefaultDayLimit { get; set; } = -1;

        /// <summary>
        /// 开启访客
        /// </summary>
        public bool EnableGuest { get; set; }

        /// <summary>
        /// 访客默认日绘图限制
        /// </summary>
        public int GuestDefaultDayLimit { get; set; } = -1;

        /// <summary>
        /// 首页公告
        /// </summary>
        public string Notify { get; set; }

        /// <summary>
        /// 启用启动时自动获取私信 ID 功能
        /// </summary>
        public bool EnableAutoGetPrivateId { get; set; }

        /// <summary>
        /// 启用启动时自动验证账号可用性功能
        /// </summary>
        public bool EnableAutoVerifyAccount { get; set; }

        /// <summary>
        /// 启用自动同步信息和设置
        /// </summary>
        public bool EnableAutoSyncInfoSetting { get; set; }

        /// <summary>
        /// 启用 token 自动延期
        /// </summary>
        public bool EnableAutoExtendToken { get; set; }

        /// <summary>
        /// 启用用户自定义上传 Base64
        /// </summary>
        public bool EnableUserCustomUploadBase64 { get; set; } = true;

        /// <summary>
        /// 启用转换官方链接
        /// </summary>
        public bool EnableConvertOfficialLink { get; set; } = true;

        /// <summary>
        /// 启用转换云/加速链接/OSS/COS/CDN
        /// </summary>
        public bool EnableConvertAliyunLink { get; set; }

        /// <summary>
        /// 开启 mj 翻译
        /// </summary>
        public bool EnableMjTranslate { get; set; } = true;

        /// <summary>
        /// 开启 niji 翻译
        /// </summary>
        public bool EnableNijiTranslate { get; set; } = true;

        /// <summary>
        /// 转换 Niji 为 MJ
        /// 启用后将 Niji · journey 任务自动转为 Midjourney 任务，并对任务添加 --niji 后缀（转换后出图效果是一致的）
        /// </summary>
        public bool EnableConvertNijiToMj { get; set; }

        /// <summary>
        /// 转换 --niji 为 Niji Bot
        /// 当 prompt 中包含 --niji 时，将会自动转换为 Niji·journey Bot 任务
        /// </summary>
        public bool EnableConvertNijiToNijiBot { get; set; }

        /// <summary>
        /// 开启自动登录
        /// </summary>
        public bool EnableAutoLogin { get; set; }

        /// <summary>
        /// 开启账号赞助
        /// </summary>
        public bool EnableAccountSponsor { get; set; }
    }

    /// <summary>
    /// 表示 Banned 限流配置选项，当处罚 Banned prompt detected 时，将会限制 IP 访问和非白名单用户
    /// </summary>
    public class BannedLimitingOptions
    {
        /// <summary>
        /// 是否启用 Banned 限流
        /// </summary>
        public bool Enable { get; set; }

        /// <summary>
        /// Banned 限流规则，key：当日触发次数，value：封锁时间（分钟）
        /// </summary>
        public Dictionary<int, int> Rules { get; set; } = [];
    }
}