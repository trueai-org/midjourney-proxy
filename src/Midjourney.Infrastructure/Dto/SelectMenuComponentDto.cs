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
using System.Text.Json.Serialization;

namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// 代表一个 Discord 消息更新事件。
    /// </summary>
    public class MessageUpdateEvent
    {
        /// <summary>
        /// 事件类型。
        /// </summary>
        [JsonPropertyName("t")]
        public string Type { get; set; }

        /// <summary>
        /// 序列号。
        /// </summary>
        [JsonPropertyName("s")]
        public int Sequence { get; set; }

        /// <summary>
        /// 操作码。
        /// </summary>
        [JsonPropertyName("op")]
        public int OperationCode { get; set; }

        /// <summary>
        /// 事件数据。
        /// </summary>
        [JsonPropertyName("d")]
        public EventData Data { get; set; }
    }

    /// <summary>
    /// 事件数据。
    /// </summary>
    public class EventData : DomainObject
    {
        /// <summary>
        /// Webhook ID。
        /// </summary>
        [JsonPropertyName("webhook_id")]
        public string WebhookId { get; set; }

        /// <summary>
        /// 消息类型。
        /// </summary>
        [JsonPropertyName("type")]
        public int Type { get; set; }

        /// <summary>
        /// 是否使用 TTS。
        /// </summary>
        [JsonPropertyName("tts")]
        public bool Tts { get; set; }

        /// <summary>
        /// 时间戳。
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 位置。
        /// </summary>
        [JsonPropertyName("position")]
        public int Position { get; set; }

        /// <summary>
        /// 是否固定。
        /// </summary>
        [JsonPropertyName("pinned")]
        public bool Pinned { get; set; }

        /// <summary>
        /// 提及的用户列表。
        /// </summary>
        [JsonPropertyName("mentions")]
        public List<InteractionUser> Mentions { get; set; } = new List<InteractionUser>();

        /// <summary>
        /// 提及的角色列表。
        /// </summary>
        [JsonPropertyName("mention_roles")]
        public List<string> MentionRoles { get; set; }

        /// <summary>
        /// 是否提及所有人。
        /// </summary>
        [JsonPropertyName("mention_everyone")]
        public bool MentionEveryone { get; set; }

        /// <summary>
        /// 交互元数据。
        /// </summary>
        [JsonPropertyName("interaction_metadata")]
        public InteractionMetadata InteractionMetadata { get; set; }

        /// <summary>
        /// 交互信息。
        /// </summary>
        [JsonPropertyName("interaction")]
        public Interaction Interaction { get; set; }

        /// <summary>
        /// 消息 ID。
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// 标志位。
        /// </summary>
        [JsonPropertyName("flags")]
        public int Flags { get; set; }

        /// <summary>
        /// 嵌入对象列表。
        /// </summary>
        [JsonPropertyName("embeds")]
        public List<EventDataEmbed> Embeds { get; set; } = new List<EventDataEmbed>();

        /// <summary>
        /// 编辑时间戳。
        /// </summary>
        [JsonPropertyName("edited_timestamp")]
        public DateTime? EditedTimestamp { get; set; }

        /// <summary>
        /// 消息内容。
        /// </summary>
        [JsonPropertyName("content")]
        public string Content { get; set; }

        /// <summary>
        /// 组件列表。
        /// </summary>
        [JsonPropertyName("components")]
        public List<Component> Components { get; set; }

        /// <summary>
        /// 频道 ID。
        /// </summary>
        [JsonPropertyName("channel_id")]
        public string ChannelId { get; set; }

        /// <summary>
        /// 作者信息。
        /// </summary>
        [JsonPropertyName("author")]
        public InteractionUser Author { get; set; }

        /// <summary>
        /// 附件列表。
        /// </summary>
        [JsonPropertyName("attachments")]
        public List<Attachment> Attachments { get; set; } = new List<Attachment>();

        /// <summary>
        /// 应用 ID。
        /// </summary>
        [JsonPropertyName("application_id")]
        public string ApplicationId { get; set; }

        /// <summary>
        /// 服务器ID。
        /// </summary>
        [JsonPropertyName("guild_id")]
        public string GuildId { get; set; }
    }

    public class EventDataEmbed
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        ///// <inheritdoc/>
        //public string Url { get; set; }

        ///// <inheritdoc/>
        //public string Title { get; set; }

        ///// <inheritdoc/>
        //public DateTimeOffset? Timestamp { get; set; }

        ///// <inheritdoc/>
        //public dynamic Color { get; set; }

  
        [JsonPropertyName("image")]
        public EventDataEmbedImage Image { get; set; }

        [JsonPropertyName("footer")]
        public EventDataEmbedFooter Footer { get; set; }
    }

    public class EventDataEmbedFooter
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }
    }

    public class EventDataEmbedImage
    {
        /// <summary>
        ///     Gets the URL of the image.
        /// </summary>
        /// <returns>
        ///     A string containing the URL of the image.
        /// </returns>
        [JsonPropertyName("url")]
        public string Url { get; set; }

        /// <summary>
        ///     Gets a proxied URL of this image.
        /// </summary>
        /// <returns>
        ///     A string containing the proxied URL of this image.
        /// </returns>
        [JsonPropertyName("proxy_url")]
        public string ProxyUrl { get; set; }

        /// <summary>
        ///     Gets the height of this image.
        /// </summary>
        /// <returns>
        ///     A <see cref="int"/> representing the height of this image if it can be retrieved; otherwise
        ///     <see langword="null" />.
        /// </returns>
        [JsonPropertyName("height")]
        public int? Height { get; set; }

        /// <summary>
        ///     Gets the width of this image.
        /// </summary>
        /// <returns>
        ///     A <see cref="int"/> representing the width of this image if it can be retrieved; otherwise
        ///     <see langword="null" />.
        /// </returns>
        [JsonPropertyName("width")]
        public int? Width { get; set; }
    }

    /// <summary>
    /// 交互元数据。
    /// </summary>
    public class InteractionMetadata
    {
        /// <summary>
        /// 用户信息。
        /// </summary>
        [JsonPropertyName("user")]
        public InteractionUser User { get; set; }

        /// <summary>
        /// 类型。
        /// </summary>
        [JsonPropertyName("type")]
        public int Type { get; set; }

        /// <summary>
        /// 名称。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// ID。
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// 授权集成所有者。
        /// </summary>
        [JsonPropertyName("authorizing_integration_owners")]
        public Dictionary<string, string> AuthorizingIntegrationOwners { get; set; }
    }

    /// <summary>
    /// 交互信息。
    /// </summary>
    public class Interaction
    {
        /// <summary>
        /// 用户信息。
        /// </summary>
        [JsonPropertyName("user")]
        public InteractionUser User { get; set; }

        /// <summary>
        /// 类型。
        /// </summary>
        [JsonPropertyName("type")]
        public int Type { get; set; }

        /// <summary>
        /// 名称。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// ID。
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }
    }

    /// <summary>
    /// 用户信息。
    /// </summary>
    public class InteractionUser
    {
        /// <summary>
        /// 用户名。
        /// </summary>
        [JsonPropertyName("username")]
        public string Username { get; set; }

        /// <summary>
        /// 公开标志。
        /// </summary>
        [JsonPropertyName("public_flags")]
        public int PublicFlags { get; set; }

        /// <summary>
        /// 用户 ID。
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// 全局名称。
        /// </summary>
        [JsonPropertyName("global_name")]
        public string GlobalName { get; set; }

        /// <summary>
        /// 鉴别器。
        /// </summary>
        [JsonPropertyName("discriminator")]
        public string Discriminator { get; set; }

        /// <summary>
        /// 家族。
        /// </summary>
        [JsonPropertyName("clan")]
        public string Clan { get; set; }

        /// <summary>
        /// 头像装饰数据。
        /// </summary>
        [JsonPropertyName("avatar_decoration_data")]
        public string AvatarDecorationData { get; set; }

        /// <summary>
        /// 头像。
        /// </summary>
        [JsonPropertyName("avatar")]
        public string Avatar { get; set; }

        /// <summary>
        /// 是否是机器人。
        /// </summary>
        [JsonPropertyName("bot")]
        public bool? Bot { get; set; }
    }

    /// <summary>
    /// 组件信息。
    /// </summary>
    public class Component
    {
        /// <summary>
        /// 组件类型。
        /// </summary>
        [JsonPropertyName("type")]
        public int Type { get; set; }

        /// <summary>
        /// 组件 ID。
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// 子组件列表。
        /// </summary>
        [JsonPropertyName("components")]
        public List<Component> Components { get; set; }

        /// <summary>
        /// 占位符。
        /// </summary>
        [JsonPropertyName("placeholder")]
        public string Placeholder { get; set; }

        /// <summary>
        /// 选项列表。
        /// </summary>
        [JsonPropertyName("options")]
        public List<Option> Options { get; set; }

        /// <summary>
        /// 最小选择值。
        /// </summary>
        [JsonPropertyName("min_values")]
        public int? MinValues { get; set; }

        /// <summary>
        /// 最大选择值。
        /// </summary>
        [JsonPropertyName("max_values")]
        public int? MaxValues { get; set; }

        /// <summary>
        /// 自定义 ID。
        /// </summary>
        [JsonPropertyName("custom_id")]
        public string CustomId { get; set; }

        /// <summary>
        /// 按钮样式。
        /// </summary>
        [JsonPropertyName("style")]
        public int? Style { get; set; }

        /// <summary>
        /// 标签。
        /// </summary>
        [JsonPropertyName("label")]
        public string Label { get; set; }

        /// <summary>
        /// 表情。
        /// </summary>
        [JsonPropertyName("emoji")]
        public Emoji Emoji { get; set; }

        /// <summary>
        /// URL。
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; }

        /// <summary>
        /// 是否禁用。
        /// </summary>
        [JsonPropertyName("disabled")]
        public bool? Disabled { get; set; }
    }

    /// <summary>
    /// 选项信息。
    /// </summary>
    public class Option
    {
        /// <summary>
        /// 选项值。
        /// </summary>
        [JsonPropertyName("value")]
        public string Value { get; set; }

        /// <summary>
        /// 标签。
        /// </summary>
        [JsonPropertyName("label")]
        public string Label { get; set; }

        /// <summary>
        /// 描述。
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// 默认选项。
        /// </summary>
        [JsonPropertyName("default")]
        public bool? Default { get; set; }

        /// <summary>
        /// 表情。
        /// </summary>
        [JsonPropertyName("emoji")]
        public Emoji Emoji { get; set; }
    }

    /// <summary>
    /// 表情信息。
    /// </summary>
    public class Emoji
    {
        /// <summary>
        /// 表情名称。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    /// <summary>
    /// 附件信息。
    /// </summary>
    public class Attachment
    {
        /// <summary>
        /// 宽度。
        /// </summary>
        [JsonPropertyName("width")]
        public int Width { get; set; }

        /// <summary>
        /// URL。
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; }

        /// <summary>
        /// 大小。
        /// </summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>
        /// 代理URL。
        /// </summary>
        [JsonPropertyName("proxy_url")]
        public string ProxyUrl { get; set; }

        /// <summary>
        /// 占位版本。
        /// </summary>
        [JsonPropertyName("placeholder_version")]
        public int PlaceholderVersion { get; set; }

        /// <summary>
        /// 占位符。
        /// </summary>
        [JsonPropertyName("placeholder")]
        public string Placeholder { get; set; }

        /// <summary>
        /// ID。
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// 高度。
        /// </summary>
        [JsonPropertyName("height")]
        public int Height { get; set; }

        /// <summary>
        /// 文件名。
        /// </summary>
        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        /// <summary>
        /// 内容类型。
        /// </summary>
        [JsonPropertyName("content_type")]
        public string ContentType { get; set; }
    }
}