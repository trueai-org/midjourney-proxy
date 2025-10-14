using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Midjourney.Base.Dto
{
    /// <summary>
    /// 创建个性化配置 -p
    /// </summary>
    public class ProfileCreateDto
    {
        /// <summary>
        /// 名称 Profile #3
        /// </summary>
        [JsonPropertyName("title")]
        [Required]
        public string Title { get; set; }

        /// <summary>
        /// 服务 main | niji
        /// </summary>
        [JsonPropertyName("service")]
        [Required]
        public string Service { get; set; }

        /// <summary>
        /// 版本 6 | 7
        /// </summary>
        [JsonPropertyName("version")]
        [Required]
        public string Version { get; set; }
    }

    /// <summary>
    /// 创建个性化配置返回 -p
    /// </summary>
    public class ProfileCreateResultDto : ProfileCreateDto
    {
        /// <summary>
        /// 配置ID
        /// </summary>
        [JsonPropertyName("profile_id")]
        public string ProfileId { get; set; }

        ///// <summary>
        ///// 创建时间
        ///// </summary>
        //[JsonPropertyName("created")]
        //public string Created { get; set; }

        ///// <summary>
        ///// 评分数量
        ///// </summary>
        //[JsonPropertyName("rankingCount")]
        //public int RankingCount { get; set; }
    }

    /// <summary>
    /// 创建个性化配置 - 跳过
    /// </summary>
    public class ProfileSkipDto
    {
        /// <summary>
        /// 配置ID
        /// </summary>
        [JsonPropertyName("profile_id")]
        public string ProfileId { get; set; }
    }

    /// <summary>
    /// 创建个性化配置 - 评分
    /// </summary>
    public class ProfileRateDto
    {
        /// <summary>
        ///
        /// </summary>
        public string JobId { get; set; }

        /// <summary>
        /// 配置ID
        /// </summary>
        public string ProfileId { get; set; }
    }

    /// <summary>
    /// 创建个性化配置 -p
    /// </summary>
    public class ProfileSkipResultDto
    {
        /// <summary>
        ///
        /// </summary>
        [JsonPropertyName("jobId")]
        public string JobId { get; set; }

        /// <summary>
        ///
        /// </summary>
        [JsonPropertyName("imageUrl")]
        public string ImageUrl { get; set; }
    }

    /// <summary>
    /// 获取随机配对的响应对象
    /// </summary>
    public class ProfileGetRandomPairsResponse
    {
        /// <summary>
        /// 后端版本号
        /// </summary>
        [JsonPropertyName("backendVersion")]
        public int BackendVersion { get; set; }

        /// <summary>
        /// 配对列表
        /// </summary>
        [JsonPropertyName("pairs")]
        public List<Pair> Pairs { get; set; }
    }

    /// <summary>
    /// 创建个性化配置 - 评分
    /// </summary>
    public class ProfileCreateRateResponse
    {
        /// <summary>
        /// success
        /// </summary>
        [JsonPropertyName("status")]
        public string Status { get; set; }

        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess => Status == "success";
    }

    /// <summary>
    /// 配对对象
    /// </summary>
    public class Pair
    {
        /// <summary>
        /// 作业列表
        /// </summary>
        [JsonPropertyName("jobs")]
        public List<Job> Jobs { get; set; }

        /// <summary>
        /// 元数据
        /// </summary>
        [JsonPropertyName("metadata")]
        public Metadata Metadata { get; set; }
    }

    /// <summary>
    /// 作业对象
    /// </summary>
    public class Job
    {
        /// <summary>
        /// 后端标识
        /// </summary>
        [JsonPropertyName("backend")]
        public string Backend { get; set; }

        /// <summary>
        /// 存储桶前缀
        /// </summary>
        [JsonPropertyName("bucket_prefix")]
        public string BucketPrefix { get; set; }

        /// <summary>
        /// 数据集编号
        /// </summary>
        [JsonPropertyName("dataset_num")]
        public int DatasetNum { get; set; }

        /// <summary>
        /// 事件信息
        /// </summary>
        [JsonPropertyName("event")]
        public Event Event { get; set; }

        /// <summary>
        /// 文件扩展名
        /// </summary>
        [JsonPropertyName("file_ext")]
        public string FileExt { get; set; }

        /// <summary>
        /// 作业唯一标识符
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// 父网格索引
        /// </summary>
        [JsonPropertyName("parent_grid")]
        public int ParentGrid { get; set; }

        /// <summary>
        /// 父作业ID
        /// </summary>
        [JsonPropertyName("parent_id")]
        public string ParentId { get; set; }

        /// <summary>
        /// 提示词
        /// </summary>
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; }

        /// <summary>
        /// 作业类型
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }
    }

    /// <summary>
    /// 事件对象
    /// </summary>
    public class Event
    {
        /// <summary>
        /// 图像高度
        /// </summary>
        [JsonPropertyName("height")]
        public string Height { get; set; }

        /// <summary>
        /// 图像宽度
        /// </summary>
        [JsonPropertyName("width")]
        public string Width { get; set; }
    }

    /// <summary>
    /// 元数据对象
    /// </summary>
    public class Metadata
    {
        /// <summary>
        /// 数据集编号
        /// </summary>
        [JsonPropertyName("dataset_num")]
        public int DatasetNum { get; set; }

        /// <summary>
        /// 配对类型
        /// </summary>
        [JsonPropertyName("pair_type")]
        public string PairType { get; set; }

        /// <summary>
        /// 树路径
        /// </summary>
        [JsonPropertyName("tree_path")]
        public string TreePath { get; set; }
    }
}