using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Midjourney.API.Models
{
    /// <summary>
    /// 悠船账号导入行
    /// </summary>
    public class YouChuanImportRow
    {
        /// <summary>
        /// 预览状态：新增
        /// </summary>
        public const string StatusAdd = "add";

        /// <summary>
        /// 预览状态：更新已存在的账号
        /// </summary>
        public const string StatusUpdate = "update";

        /// <summary>
        /// 预览状态：无效行，前端不可勾选
        /// </summary>
        public const string StatusInvalid = "invalid";

        /// <summary>
        /// Excel 行号
        /// </summary>
        public int RowNumber { get; set; }

        /// <summary>
        /// 虚拟号/登录账号。已去除空格、横线和国家码前缀，用于重复账号匹配。
        /// </summary>
        public string LoginAccount { get; set; }

        /// <summary>
        /// 虚拟 SmsCode/登录密码。纯数字时悠船按短信验证码登录，非纯数字时按密码登录。
        /// </summary>
        public string LoginPassword { get; set; }

        /// <summary>
        /// 套餐名称
        /// </summary>
        public string PlanName { get; set; }

        /// <summary>
        /// 套餐周期
        /// </summary>
        public string PlanCycle { get; set; }

        /// <summary>
        /// 预览状态，取值见 <see cref="StatusAdd"/>、<see cref="StatusUpdate"/>、<see cref="StatusInvalid"/>。
        /// </summary>
        public string Status { get; set; } = StatusAdd;

        /// <summary>
        /// 无效原因或更新说明；有效行可为空。
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 已存在的悠船账号 ID，仅更新行有值。
        /// </summary>
        public string ExistingAccountId { get; set; }
    }

    /// <summary>
    /// 悠船账号导入预览结果
    /// </summary>
    public class YouChuanImportPreviewResult
    {
        /// <summary>
        /// 上传的 Excel 文件名，仅用于前端展示
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// 解析出的预览行；预览阶段不写数据库
        /// </summary>
        public List<YouChuanImportRow> Rows { get; set; } = new();
    }

    /// <summary>
    /// 悠船账号导入参数。
    /// 默认值与前端导入弹窗保持一致：并发 12、队列 48、慢速并发 3、慢速队列 12、权重 1、超时 20 分钟。
    /// </summary>
    public class YouChuanImportOptions
    {
        /// <summary>
        /// 并发数
        /// </summary>
        public int CoreSize { get; set; } = 12;

        /// <summary>
        /// 队列数
        /// </summary>
        public int QueueSize { get; set; } = 48;

        /// <summary>
        /// 并发数（慢速）
        /// </summary>
        public int RelaxCoreSize { get; set; } = 3;

        /// <summary>
        /// 队列数（慢速）
        /// </summary>
        public int RelaxQueueSize { get; set; } = 12;

        /// <summary>
        /// 权重
        /// </summary>
        public int Weight { get; set; } = 1;

        /// <summary>
        /// 开启 Blend
        /// </summary>
        public bool IsBlend { get; set; } = true;

        /// <summary>
        /// 开启 Describe
        /// </summary>
        public bool IsDescribe { get; set; } = true;

        /// <summary>
        /// 日绘图限制（快速）
        /// </summary>
        public int DayDrawLimit { get; set; } = -1;

        /// <summary>
        /// 日绘图限制（慢速）
        /// </summary>
        public int DayRelaxDrawLimit { get; set; } = -1;

        /// <summary>
        /// 任务超时时间（分钟）
        /// </summary>
        public int TimeoutMinutes { get; set; } = 20;
    }

    /// <summary>
    /// 悠船账号导入请求
    /// </summary>
    public class YouChuanImportRequest
    {
        /// <summary>
        /// 用户在预览中勾选并确认导入的行；重复虚拟号按更新处理
        /// </summary>
        public List<YouChuanImportRow> Rows { get; set; } = new();

        /// <summary>
        /// 账号池与任务参数；未传时使用默认值
        /// </summary>
        public YouChuanImportOptions Options { get; set; } = new();
    }

    /// <summary>
    /// 悠船账号导入结果。批量导入采用逐行处理，允许部分成功、部分失败。
    /// </summary>
    public class YouChuanImportResult
    {
        /// <summary>
        /// 新增账号数
        /// </summary>
        public int Created { get; set; }

        /// <summary>
        /// 更新账号数
        /// </summary>
        public int Updated { get; set; }

        /// <summary>
        /// 失败行数（包含参数校验失败和写入失败）
        /// </summary>
        public int Failed { get; set; }

        /// <summary>
        /// 失败说明，包含 Excel 行号和可读原因；不包含 SmsCode/密码
        /// </summary>
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// 悠船账号导入限制。前后端需保持一致：文件 20MB、单次 2000 行。
    /// </summary>
    internal static class YouChuanImportLimits
    {
        /// <summary>
        /// 允许上传的 Excel 文件大小（20MB）
        /// </summary>
        public const long MaxFileBytes = 20L * 1024 * 1024;

        /// <summary>
        /// 单次最多导入的数据行数
        /// </summary>
        public const int MaxRows = 2000;

        /// <summary>
        /// 单个压缩包条目解压后的最大字节数，避免异常文件占用过多内存
        /// </summary>
        public const long MaxExcelEntryBytes = 30L * 1024 * 1024;
    }

    /// <summary>
    /// 悠船账号 Excel 解析器。
    /// 仅依赖 .NET 内置的 ZipArchive + XML，支持 .xlsx 模板。
    /// </summary>
    internal static class YouChuanAccountExcelParser
    {
        private const string FieldPhone = "phone";
        private const string FieldPassword = "password";
        private const string FieldPlanName = "planName";
        private const string FieldPlanCycle = "planCycle";

        private const int HeaderScanRows = 20;

        private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>
        /// 常见表头别名。键为 <see cref="NormalizeHeader"/> 后的值；新增模板列名时同步补充 <see cref="ResolveField"/> 的包含匹配。
        /// </summary>
        private static readonly Dictionary<string, string> HeaderAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["虚拟号"] = FieldPhone,
            ["虚拟号码"] = FieldPhone,
            ["手机号"] = FieldPhone,
            ["手机号码"] = FieldPhone,
            ["手机"] = FieldPhone,
            ["账号"] = FieldPhone,
            ["账号手机号"] = FieldPhone,
            ["phone"] = FieldPhone,
            ["mobile"] = FieldPhone,
            ["account"] = FieldPhone,

            ["虚拟smscode"] = FieldPassword,
            ["smscode"] = FieldPassword,
            ["sms"] = FieldPassword,
            ["短信验证码"] = FieldPassword,
            ["验证码"] = FieldPassword,
            ["登录密码"] = FieldPassword,
            ["密码"] = FieldPassword,
            ["password"] = FieldPassword,

            ["套餐名称"] = FieldPlanName,
            ["套餐"] = FieldPlanName,
            ["plan"] = FieldPlanName,
            ["planname"] = FieldPlanName,

            ["套餐周期"] = FieldPlanCycle,
            ["周期"] = FieldPlanCycle,
            ["cycle"] = FieldPlanCycle,
            ["plancycle"] = FieldPlanCycle,
        };

        /// <summary>
        /// 解析 Excel 文件。支持 .xlsx（OpenXML）格式，会自动在工作簿中寻找包含悠船表头的工作表。
        /// </summary>
        /// <param name="stream">Excel 文件流；调用方负责释放</param>
        /// <returns>解析出的导入行，包含有效行和无效行</returns>
        /// <exception cref="LogicException">文件为空、格式不支持、表头无法识别或数据超过限制时抛出</exception>
        public static List<YouChuanImportRow> Parse(Stream stream)
        {
            if (stream == null || !stream.CanRead)
            {
                throw new LogicException("请选择要导入的 Excel 文件");
            }

            try
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

                var sharedStrings = LoadSharedStrings(archive);
                var sheetPaths = GetWorksheetPaths(archive);

                if (sheetPaths.Count == 0)
                {
                    throw new LogicException("Excel 文件中未找到工作表");
                }

                // 多工作表时依次尝试；找到悠船模板后直接返回。
                // ParseWorksheet 返回 null 表示当前表没有可识别表头，返回空集合表示有表头但没有数据。
                var templateFound = false;
                foreach (var sheetPath in sheetPaths)
                {
                    var sheet = TryLoadXml(archive, sheetPath);
                    if (sheet == null)
                    {
                        continue;
                    }

                    var rows = ParseWorksheet(sheet, sharedStrings);
                    if (rows == null)
                    {
                        continue;
                    }

                    templateFound = true;
                    if (rows.Count > 0)
                    {
                        return rows;
                    }
                }

                throw new LogicException(templateFound
                    ? "Excel 中没有可导入的数据"
                    : "未识别到悠船账号模板，请确认 Excel 包含「虚拟号」和「虚拟 SmsCode」列");
            }
            catch (LogicException)
            {
                throw;
            }
            catch (InvalidDataException)
            {
                throw new LogicException("文件不是有效的 .xlsx 文件");
            }
            catch (Exception)
            {
                throw new LogicException("Excel 解析失败，请确认文件格式是否正确");
            }
        }

        /// <summary>
        /// 规范化虚拟号：处理 Excel 数值格式，去空格/横线/加号，并去掉常见的 0086/86 国家码前缀。
        /// </summary>
        /// <param name="value">Excel 单元格原始值</param>
        /// <returns>仅保留数字的虚拟号；空值返回空字符串</returns>
        public static string NormalizePhone(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = NormalizeNumberLikeValue(value);
            text = Regex.Replace(text, @"[\s\-]", "");

            if (text.StartsWith("+"))
            {
                text = text[1..];
            }

            if (text.StartsWith("0086", StringComparison.Ordinal) && text.Length > 4)
            {
                text = text[4..];
            }
            else if (text.StartsWith("86", StringComparison.Ordinal) && text.Length == 13)
            {
                text = text[2..];
            }

            return Regex.Replace(text, @"\D", "");
        }

        /// <summary>
        /// 规范化 SmsCode/密码：保留文本密码，数值型和科学计数法单元格还原为整数字符串。
        /// </summary>
        /// <param name="value">Excel 单元格原始值</param>
        /// <returns>去掉首尾空白的 SmsCode/密码；空值返回空字符串</returns>
        public static string NormalizePassword(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizeNumberLikeValue(value);
        }

        /// <summary>
        /// 解析单个工作表。
        /// 返回 null 表示当前工作表没有悠船表头；返回空集合表示表头存在但没有数据行。
        /// 同一文件内重复虚拟号只保留第一行，后续行标记为无效并禁用勾选。
        /// </summary>
        private static List<YouChuanImportRow> ParseWorksheet(XDocument sheet, List<string> sharedStrings)
        {
            var sheetRows = ReadSheetRows(sheet, sharedStrings);
            if (sheetRows.Count == 0)
            {
                return null;
            }

            var header = DetectHeader(sheetRows);
            if (header == null)
            {
                return null;
            }

            var (headerIndex, columns) = header.Value;
            var result = new List<YouChuanImportRow>();

            // 文件内去重：同一个虚拟号出现多次时，后续行标记无效，避免一次导入生成重复账号。
            var seenPhones = new HashSet<string>(StringComparer.Ordinal);

            for (var i = headerIndex + 1; i < sheetRows.Count; i++)
            {
                var source = sheetRows[i];
                var phone = NormalizePhone(GetCell(source.Cells, columns, FieldPhone));
                var password = NormalizePassword(GetCell(source.Cells, columns, FieldPassword));
                var planName = GetCell(source.Cells, columns, FieldPlanName)?.Trim();
                var planCycle = GetCell(source.Cells, columns, FieldPlanCycle)?.Trim();

                // 整行空白直接跳过；不要把它算进 2000 行限制。
                if (string.IsNullOrWhiteSpace(phone)
                    && string.IsNullOrWhiteSpace(password)
                    && string.IsNullOrWhiteSpace(planName)
                    && string.IsNullOrWhiteSpace(planCycle))
                {
                    continue;
                }

                if (result.Count >= YouChuanImportLimits.MaxRows)
                {
                    throw new LogicException($"单次最多支持导入 {YouChuanImportLimits.MaxRows} 行数据");
                }

                var row = new YouChuanImportRow
                {
                    RowNumber = source.RowNumber,
                    LoginAccount = phone,
                    LoginPassword = password,
                    PlanName = planName,
                    PlanCycle = planCycle,
                    Status = YouChuanImportRow.StatusAdd,
                };

                if (string.IsNullOrWhiteSpace(phone))
                {
                    row.Status = YouChuanImportRow.StatusInvalid;
                    row.Message = "虚拟号为空";
                }
                else if (phone.Length < 5)
                {
                    row.Status = YouChuanImportRow.StatusInvalid;
                    row.Message = "虚拟号格式不正确";
                }
                else if (string.IsNullOrWhiteSpace(password))
                {
                    row.Status = YouChuanImportRow.StatusInvalid;
                    row.Message = "虚拟 SmsCode 为空";
                }
                else if (!seenPhones.Add(phone))
                {
                    row.Status = YouChuanImportRow.StatusInvalid;
                    row.Message = "文件内虚拟号重复";
                }

                result.Add(row);
            }

            return result;
        }

        /// <summary>
        /// 在前 <see cref="HeaderScanRows"/> 行内寻找表头。
        /// 同时包含虚拟号和 SmsCode/密码列的行才视为表头，并优先选择识别列最多的一行，以兼容标题行、空行和说明行。
        /// </summary>
        private static (int HeaderIndex, Dictionary<string, int> Columns)? DetectHeader(List<SheetRow> rows)
        {
            (int HeaderIndex, Dictionary<string, int> Columns)? best = null;
            var bestScore = 0;

            var max = Math.Min(rows.Count, HeaderScanRows);
            for (var i = 0; i < max; i++)
            {
                var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var (columnIndex, value) in rows[i].Cells)
                {
                    var header = NormalizeHeader(value);
                    if (string.IsNullOrWhiteSpace(header))
                    {
                        continue;
                    }

                    var field = ResolveField(header);
                    if (field != null && !columns.ContainsKey(field))
                    {
                        columns[field] = columnIndex;
                    }
                }

                if (!columns.ContainsKey(FieldPhone) || !columns.ContainsKey(FieldPassword))
                {
                    continue;
                }

                if (columns.Count > bestScore)
                {
                    bestScore = columns.Count;
                    best = (i, columns);
                }
            }

            return best;
        }

        /// <summary>
        /// 将归一化后的表头映射到内部字段。
        /// 先精确匹配别名，再做包含匹配，兼容“虚拟号（必填）”“SMS 验证码”等模板变体。
        /// </summary>
        private static string ResolveField(string header)
        {
            if (HeaderAliases.TryGetValue(header, out var field))
            {
                return field;
            }

            if (header.Contains("虚拟号") || header.Contains("手机号") || header.Contains("手机号码") || header == "账号")
            {
                return FieldPhone;
            }

            if (header.Contains("sms") || header.Contains("验证码") || header.Contains("密码"))
            {
                return FieldPassword;
            }

            if (header.Contains("套餐") && header.Contains("名称"))
            {
                return FieldPlanName;
            }

            if (header.Contains("周期"))
            {
                return FieldPlanCycle;
            }

            return null;
        }

        private static string GetCell(Dictionary<int, string> cells, Dictionary<string, int> columns, string field)
        {
            return columns.TryGetValue(field, out var index) && cells.TryGetValue(index, out var value) ? value : null;
        }

        /// <summary>
        /// 将 sheetData 转成“行号 + 列索引”的稀疏单元格字典。
        /// 行号优先取 Excel 的 r 属性；缺失时按 XML 顺序回退，保证预览行号可定位。
        /// </summary>
        private static List<SheetRow> ReadSheetRows(XDocument sheet, List<string> sharedStrings)
        {
            var result = new List<SheetRow>();
            var rowElements = sheet.Descendants(MainNs + "row").ToList();

            for (var i = 0; i < rowElements.Count; i++)
            {
                var rowElement = rowElements[i];
                var rowNumber = (int?)rowElement.Attribute("r") ?? i + 1;
                var cells = new Dictionary<int, string>();
                var fallbackColumn = 0;

                foreach (var cellElement in rowElement.Elements(MainNs + "c"))
                {
                    var reference = (string)cellElement.Attribute("r");
                    var columnIndex = GetColumnIndex(reference) ?? fallbackColumn;
                    fallbackColumn = columnIndex + 1;

                    var value = GetCellText(cellElement, sharedStrings);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        cells[columnIndex] = value;
                    }
                }

                result.Add(new SheetRow { RowNumber = rowNumber, Cells = cells });
            }

            return result;
        }

        /// <summary>
        /// 读取单元格文本：共享字符串、内联字符串、布尔值和普通数值都按 xlsx 的 t 属性处理。
        /// 公式单元格没有专门的 t 类型，使用其缓存值 v。
        /// </summary>
        private static string GetCellText(XElement cell, List<string> sharedStrings)
        {
            var type = (string)cell.Attribute("t");

            if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase))
            {
                var indexText = cell.Element(MainNs + "v")?.Value;
                if (int.TryParse(indexText, out var index) && index >= 0 && index < sharedStrings.Count)
                {
                    return sharedStrings[index];
                }

                return null;
            }

            if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
            {
                var inline = cell.Element(MainNs + "is");
                return inline == null ? null : string.Concat(inline.Descendants(MainNs + "t").Select(c => c.Value));
            }

            if (string.Equals(type, "b", StringComparison.OrdinalIgnoreCase))
            {
                return cell.Element(MainNs + "v")?.Value == "1" ? "TRUE" : "FALSE";
            }

            return cell.Element(MainNs + "v")?.Value;
        }

        /// <summary>
        /// 将 A1 / AA10 这类列引用转换成从 0 开始的列索引；只解析字母部分，忽略行号。
        /// </summary>
        private static int? GetColumnIndex(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }

            var index = 0;
            foreach (var ch in reference)
            {
                if (!char.IsLetter(ch))
                {
                    break;
                }

                index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }

            return index > 0 ? index - 1 : null;
        }

        /// <summary>
        /// 归一化表头：去首尾空白、转小写，并移除空格、全半角标点和常见分隔符，降低模板差异导致的识别失败。
        /// </summary>
        private static string NormalizeHeader(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = value.Trim().ToLowerInvariant();
            return Regex.Replace(text, @"[\s:：,，;；_\-/\\|()（）\[\]【】]+", "");
        }

        /// <summary>
        /// Excel 可能把手机号/SmsCode 存成数值或科学计数法（如 1.0999044062E+10），这里还原成整数字符串。
        /// 纯文本（包括前导 0）保持不变，避免破坏验证码。
        /// </summary>
        private static string NormalizeNumberLikeValue(string value)
        {
            var text = value.Trim();

            if (Regex.IsMatch(text, @"^[+-]?\d+\.\d+$")
                || Regex.IsMatch(text, @"^[+-]?\d+(\.\d+)?[eE][+-]?\d+$"))
            {
                if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    && number == decimal.Truncate(number)
                    && number >= 0)
                {
                    return number.ToString("0", CultureInfo.InvariantCulture);
                }
            }

            return text;
        }

        /// <summary>
        /// 读取 sharedStrings.xml。富文本单元格会拼接同一 &lt;si&gt; 下的所有 &lt;t&gt; 节点；
        /// 某些 xlsx 使用 inlineStr，此时不需要共享字符串表。
        /// </summary>
        private static List<string> LoadSharedStrings(ZipArchive archive)
        {
            var document = TryLoadXml(archive, "xl/sharedStrings.xml");
            if (document == null)
            {
                return new List<string>();
            }

            return document.Descendants(MainNs + "si")
                .Select(item => string.Concat(item.Descendants(MainNs + "t").Select(c => c.Value)))
                .ToList();
        }

        /// <summary>
        /// 通过 workbook.xml + workbook.xml.rels 按工作簿顺序获取工作表路径；
        /// 结构缺失时回退到 xl/worksheets/*.xml，保证不规范的 xlsx 也能尝试解析。
        /// </summary>
        private static List<string> GetWorksheetPaths(ZipArchive archive)
        {
            var paths = new List<string>();
            var workbook = TryLoadXml(archive, "xl/workbook.xml");
            var relationships = TryLoadXml(archive, "xl/_rels/workbook.xml.rels");

            if (workbook != null && relationships != null)
            {
                var targets = relationships.Descendants(PackageRelationshipsNs + "Relationship")
                    .Where(c => string.Equals((string)c.Attribute("Type"), "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", StringComparison.OrdinalIgnoreCase))
                    .Where(c => c.Attribute("Id") != null && c.Attribute("Target") != null)
                    .ToDictionary(c => (string)c.Attribute("Id"), c => (string)c.Attribute("Target"), StringComparer.OrdinalIgnoreCase);

                foreach (var sheet in workbook.Descendants(MainNs + "sheet"))
                {
                    var relationId = (string)sheet.Attribute(RelationshipsNs + "id");
                    if (string.IsNullOrWhiteSpace(relationId) || !targets.TryGetValue(relationId, out var target) || string.IsNullOrWhiteSpace(target))
                    {
                        continue;
                    }

                    var path = target.StartsWith("/", StringComparison.Ordinal)
                        ? target.TrimStart('/')
                        : target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
                            ? target
                            : "xl/" + target;

                    if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        paths.Add(path);
                    }
                }
            }

            if (paths.Count == 0)
            {
                paths.AddRange(archive.Entries
                    .Select(c => c.FullName)
                    .Where(c => c.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) && c.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase));
            }

            return paths;
        }

        /// <summary>
        /// 读取 zip 中的 XML 条目。条目不存在返回 null；未压缩大小超过限制直接拒绝，避免异常文件占用过多内存。
        /// </summary>
        private static XDocument TryLoadXml(ZipArchive archive, string path)
        {
            var entry = archive.Entries.FirstOrDefault(c => string.Equals(c.FullName, path, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                return null;
            }

            if (entry.Length > YouChuanImportLimits.MaxExcelEntryBytes)
            {
                throw new LogicException("Excel 文件内容过大");
            }

            using var stream = entry.Open();
            return XDocument.Load(stream);
        }

        /// <summary>
        /// 工作表行：Excel 行号 + 列索引到文本的稀疏映射
        /// </summary>
        private class SheetRow
        {
            /// <summary>
            /// Excel 行号（1 开始），用于预览和错误提示
            /// </summary>
            public int RowNumber { get; set; }

            /// <summary>
            /// 列索引（0 开始）到单元格文本的映射；空单元格不写入
            /// </summary>
            public Dictionary<int, string> Cells { get; set; } = new();
        }
    }
}
