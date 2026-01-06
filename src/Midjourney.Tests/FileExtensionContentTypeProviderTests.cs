using Microsoft.AspNetCore.StaticFiles;
using Midjourney.Base;
using Midjourney.Base.Util;
using SkiaSharp;
using Xunit.Abstractions;

namespace Midjourney.Tests
{
    /// <summary>
    /// 文件扩展名与内容类型提供者测试类
    /// </summary>
    public class FileExtensionContentTypeProviderTests : BaseTests
    {
        private readonly TestOutputWrapper _output;
        private readonly FileExtensionContentTypeProvider _provider;

        public FileExtensionContentTypeProviderTests(ITestOutputHelper output)
        {
            _output = new TestOutputWrapper(output);
            _provider = new FileExtensionContentTypeProvider();
        }

        #region 基础功能测试

        [Fact]
        public void TryGetContentType_ShouldReturnCorrectMimeType_ForJpgFile()
        {
            // Arrange - 准备测试数据
            string fileName = "file.jpg";

            // Act - 执行测试
            bool result = _provider.TryGetContentType(fileName, out var mimeType);

            // Assert - 验证结果
            Assert.True(result);
            Assert.Equal("image/jpeg", mimeType);

            _output.WriteLine($"文件名: {fileName} => MIME 类型: {mimeType}");
        }

        [Fact]
        public void TryGetContentType_ShouldReturnCorrectMimeType_ForPdfFile()
        {
            // Arrange - 准备测试数据
            string fileName = "document.pdf";

            // Act - 执行测试
            bool result = _provider.TryGetContentType(fileName, out var mimeType);

            // Assert - 验证结果
            Assert.True(result);
            Assert.Equal("application/pdf", mimeType);

            _output.WriteLine($"文件名: {fileName} => MIME 类型: {mimeType}");
        }

        [Fact]
        public void TryGetContentType_ShouldReturnFalse_ForUnknownExtension()
        {
            // Arrange - 准备未知扩展名的文件
            string fileName = "file.unknown";

            // Act - 执行测试
            bool result = _provider.TryGetContentType(fileName, out var mimeType);

            // Assert - 验证未知扩展名应该返回 false
            Assert.False(result);
            Assert.Null(mimeType);

            _output.WriteLine($"未知文件扩展名: {fileName} => 结果: {result}, MIME 类型: {mimeType ?? "null"}");
        }

        [Theory]
        [InlineData("file.png", "image/png")]
        [InlineData("file.jpg", "image/jpeg")]
        [InlineData("file.jpeg", "image/jpeg")]
        [InlineData("file.gif", "image/gif")]
        [InlineData("file.webp", "image/webp")]
        [InlineData("file.html", "text/html")]
        [InlineData("file.json", "application/json")]
        [InlineData("file.txt", "text/plain")]
        [InlineData("file.css", "text/css")]
        [InlineData("file.js", "text/javascript")]
        [InlineData("file.mp4", "video/mp4")]
        [InlineData("file.mp3", "audio/mpeg")]
        public void TryGetContentType_ShouldReturnCorrectMimeType_ForVariousExtensions(
            string fileName, string expectedMimeType)
        {
            // Act - 执行测试
            bool result = _provider.TryGetContentType(fileName, out var mimeType);

            // Assert - 验证结果
            Assert.True(result);
            Assert.Equal(expectedMimeType, mimeType);

            _output.WriteLine($"扩展名测试 - 文件:  {fileName,-20} => MIME:  {mimeType}");
        }

        #endregion 基础功能测试

        #region 反向查找测试

        [Fact]
        public void ReverseLookup_ShouldReturnCorrectExtension_ForImageJpegMimeType()
        {
            // Arrange - 准备 MIME 类型
            string targetMimeType = "image/jpeg";

            // Act - 执行反向查找
            var reverseLookups = _provider.Mappings.Where(x => x.Value == targetMimeType).Select(c => c.Key).ToList();

            var reverseLookup = string.Concat(reverseLookups);

            // Assert - 验证结果
            Assert.NotNull(reverseLookups);

            Assert.Contains(".jpg", reverseLookup);

            _output.WriteLine($"反向查找 - MIME:  {targetMimeType} => 扩展名: {reverseLookup}");
        }

        [Fact]
        public void ReverseLookup_ShouldReturnCorrectExtension_ForApplicationPdfMimeType()
        {
            // Arrange - 准备 MIME 类型
            string targetMimeType = "application/pdf";

            // Act - 执行反向查找
            var reverseLookup = _provider.Mappings
                .FirstOrDefault(x => x.Value == targetMimeType).Key;

            // Assert - 验证结果
            Assert.NotNull(reverseLookup);
            Assert.Equal(".pdf", reverseLookup);

            _output.WriteLine($"反向查找 - MIME: {targetMimeType} => 扩展名: {reverseLookup}");
        }

        [Fact]
        public void ReverseLookup_ShouldReturnNull_ForUnknownMimeType()
        {
            // Arrange - 准备未知的 MIME 类型
            string unknownMimeType = "application/unknown-type";

            // Act - 执行反向查找
            var reverseLookup = _provider.Mappings
                .FirstOrDefault(x => x.Value == unknownMimeType).Key;

            // Assert - 验证未知 MIME 类型应该返回 null
            Assert.Null(reverseLookup);

            _output.WriteLine($"未知 MIME 类型:  {unknownMimeType} => 扩展名: {reverseLookup ?? "null"}");
        }

        #endregion 反向查找测试

        #region 集合和映射测试

        [Fact]
        public void Mappings_ShouldContainCommonMimeTypes()
        {
            // Assert - 验证映射表包含常见的 MIME 类型
            Assert.True(_provider.Mappings.Count > 0);
            Assert.Contains(_provider.Mappings, m => m.Value == "image/jpeg");
            Assert.Contains(_provider.Mappings, m => m.Value == "application/pdf");
            Assert.Contains(_provider.Mappings, m => m.Value == "text/html");

            _output.WriteLine($"映射表总数: {_provider.Mappings.Count} 个");
            _output.WriteLine("包含常见 MIME 类型:  image/jpeg, application/pdf, text/html");
        }

        [Fact]
        public void TryGetContentType_ShouldBeCaseInsensitive()
        {
            // Arrange - 准备不同大小写的文件名
            string upperCaseFile = "FILE.JPG";
            string lowerCaseFile = "file.jpg";
            string mixedCaseFile = "File.JpG";

            // Act - 执行测试
            bool upperResult = _provider.TryGetContentType(upperCaseFile, out var upperMimeType);
            bool lowerResult = _provider.TryGetContentType(lowerCaseFile, out var lowerMimeType);
            bool mixedResult = _provider.TryGetContentType(mixedCaseFile, out var mixedMimeType);

            // Assert - 验证大小写不敏感
            Assert.True(upperResult);
            Assert.True(lowerResult);
            Assert.True(mixedResult);
            Assert.Equal("image/jpeg", upperMimeType);
            Assert.Equal("image/jpeg", lowerMimeType);
            Assert.Equal("image/jpeg", mixedMimeType);

            _output.WriteLine("=== 大小写敏感性测试 ===");
            _output.WriteLine($"大写:  {upperCaseFile} => {upperMimeType}");
            _output.WriteLine($"小写: {lowerCaseFile} => {lowerMimeType}");
            _output.WriteLine($"混合: {mixedCaseFile} => {mixedMimeType}");
        }

        #endregion 集合和映射测试

        #region 输出所有映射关系

        [Fact]
        public void OutputAllMappings_GroupedByCategory()
        {
            // 输出所有映射关系，按类别分组
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("所有 MIME 类型映射关系（按类别分组）");
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("");

            // 按 MIME 类型的主类别分组
            var groupedMappings = _provider.Mappings
                .GroupBy(m => m.Value.Split('/')[0])
                .OrderBy(g => g.Key);

            foreach (var group in groupedMappings)
            {
                _output.WriteLine($"【{group.Key.ToUpper()} 类型】 共 {group.Count()} 个");
                _output.WriteLine("-".PadRight(100, '-'));

                var sortedItems = group.OrderBy(m => m.Key);
                foreach (var mapping in sortedItems)
                {
                    _output.WriteLine($"  {mapping.Key,-20} => {mapping.Value}");
                }

                _output.WriteLine("");
            }

            _output.WriteLine($"总计: {_provider.Mappings.Count} 个映射关系");
        }

        [Fact]
        public void OutputAllMappings_Alphabetically()
        {
            // 按扩展名字母顺序输出所有映射
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("所有 MIME 类型映射关系（按扩展名字母顺序）");
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("");

            var sortedMappings = _provider.Mappings.OrderBy(m => m.Key);

            int count = 0;
            foreach (var mapping in sortedMappings)
            {
                count++;
                _output.WriteLine($"{count,4}. {mapping.Key,-20} => {mapping.Value}");
            }

            _output.WriteLine("");
            _output.WriteLine($"总计: {count} 个映射关系");
        }

        [Fact]
        public void OutputImageMimeTypes()
        {
            // 输出所有图片类型的 MIME 映射
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("所有图片类型 MIME 映射");
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("");

            var imageMappings = _provider.Mappings
                .Where(m => m.Value.StartsWith("image/"))
                .OrderBy(m => m.Key);

            foreach (var mapping in imageMappings)
            {
                _output.WriteLine($"  {mapping.Key,-20} => {mapping.Value}");
            }

            _output.WriteLine("");
            _output.WriteLine($"图片类型总计: {imageMappings.Count()} 个");
        }

        [Fact]
        public void OutputVideoMimeTypes()
        {
            // 输出所有视频类型的 MIME 映射
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("所有视频类型 MIME 映射");
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("");

            var videoMappings = _provider.Mappings
                .Where(m => m.Value.StartsWith("video/"))
                .OrderBy(m => m.Key);

            foreach (var mapping in videoMappings)
            {
                _output.WriteLine($"  {mapping.Key,-20} => {mapping.Value}");
            }

            _output.WriteLine("");
            _output.WriteLine($"视频类型总计: {videoMappings.Count()} 个");
        }

        [Fact]
        public void OutputAudioMimeTypes()
        {
            // 输出所有音频类型的 MIME 映射
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("所有音频类型 MIME 映射");
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("");

            var audioMappings = _provider.Mappings
                .Where(m => m.Value.StartsWith("audio/"))
                .OrderBy(m => m.Key);

            foreach (var mapping in audioMappings)
            {
                _output.WriteLine($"  {mapping.Key,-20} => {mapping.Value}");
            }

            _output.WriteLine("");
            _output.WriteLine($"音频类型总计: {audioMappings.Count()} 个");
        }

        [Fact]
        public void OutputApplicationMimeTypes()
        {
            // 输出所有应用程序类型的 MIME 映射
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("所有应用程序类型 MIME 映射");
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("");

            var appMappings = _provider.Mappings
                .Where(m => m.Value.StartsWith("application/"))
                .OrderBy(m => m.Key);

            foreach (var mapping in appMappings)
            {
                _output.WriteLine($"  {mapping.Key,-20} => {mapping.Value}");
            }

            _output.WriteLine("");
            _output.WriteLine($"应用程序类型总计:  {appMappings.Count()} 个");
        }

        [Fact]
        public void OutputTextMimeTypes()
        {
            // 输出所有文本类型的 MIME 映射
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("所有文本类型 MIME 映射");
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("");

            var textMappings = _provider.Mappings
                .Where(m => m.Value.StartsWith("text/"))
                .OrderBy(m => m.Key);

            foreach (var mapping in textMappings)
            {
                _output.WriteLine($"  {mapping.Key,-20} => {mapping.Value}");
            }

            _output.WriteLine("");
            _output.WriteLine($"文本类型总计: {textMappings.Count()} 个");
        }

        [Fact]
        public void OutputStatisticsByCategory()
        {
            // 输出按类别的统计信息
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("MIME 类型统计（按主类别）");
            _output.WriteLine("=".PadRight(100, '='));
            _output.WriteLine("");

            var statistics = _provider.Mappings
                .GroupBy(m => m.Value.Split('/')[0])
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .OrderByDescending(s => s.Count);

            _output.WriteLine($"{"类别",-20} {"数量",10} {"占比",10}");
            _output.WriteLine("-".PadRight(50, '-'));

            int total = _provider.Mappings.Count;
            foreach (var stat in statistics)
            {
                double percentage = (double)stat.Count / total * 100;
                _output.WriteLine($"{stat.Category,-20} {stat.Count,10} {percentage,9: F2}%");
            }

            _output.WriteLine("-".PadRight(50, '-'));
            _output.WriteLine($"{"总计",-20} {total,10} {"100.00%",10}");
        }

        #endregion 输出所有映射关系

        #region 特定格式测试

        [Theory]
        [InlineData(".jpg", "image/jpeg", "JPEG 图片")]
        [InlineData(".jpeg", "image/jpeg", "JPEG 图片")]
        [InlineData(".png", "image/png", "PNG 图片")]
        [InlineData(".gif", "image/gif", "GIF 图片")]
        [InlineData(".bmp", "image/bmp", "BMP 图片")]
        [InlineData(".svg", "image/svg+xml", "SVG 矢量图")]
        [InlineData(".webp", "image/webp", "WebP 图片")]
        [InlineData(".ico", "image/x-icon", "图标文件")]
        public void TestImageFormats(string extension, string expectedMimeType, string description)
        {
            // 测试常见图片格式
            string fileName = $"test{extension}";
            bool result = _provider.TryGetContentType(fileName, out var mimeType);

            Assert.True(result);
            Assert.Equal(expectedMimeType, mimeType);

            _output.WriteLine($"{description,-15} - 扩展名: {extension,-10} => MIME: {mimeType}");
        }

        [Theory]
        [InlineData(".mp4", "video/mp4", "MP4 视频")]
        [InlineData(".avi", "video/x-msvideo", "AVI 视频")]
        [InlineData(".mov", "video/quicktime", "QuickTime 视频")]
        [InlineData(".wmv", "video/x-ms-wmv", "WMV 视频")]
        [InlineData(".flv", "video/x-flv", "FLV 视频")]
        [InlineData(".webm", "video/webm", "WebM 视频")]
        public void TestVideoFormats(string extension, string expectedMimeType, string description)
        {
            // 测试常见视频格式
            string fileName = $"test{extension}";
            bool result = _provider.TryGetContentType(fileName, out var mimeType);

            Assert.True(result);
            Assert.Equal(expectedMimeType, mimeType);

            _output.WriteLine($"{description,-15} - 扩展名: {extension,-10} => MIME: {mimeType}");
        }

        //[Theory]
        //[InlineData(".mp3", "audio/mpeg", "MP3 音频")]
        //[InlineData(".wav", "audio/wav", "WAV 音频")]
        //[InlineData(".ogg", "audio/ogg", "OGG 音频")]
        //[InlineData(".m4a", "audio/mp4", "M4A 音频")]
        //[InlineData(".wma", "audio/x-ms-wma", "WMA 音频")]
        //public void TestAudioFormats(string extension, string expectedMimeType, string description)
        //{
        //    // 测试常见音频格式
        //    string fileName = $"test{extension}";
        //    bool result = _provider.TryGetContentType(fileName, out var mimeType);

        //    Assert.True(result);
        //    Assert.Equal(expectedMimeType, mimeType);

        //    _output.WriteLine($"{description,-15} - 扩展名: {extension,-10} => MIME: {mimeType}");
        //}

        //[Theory]
        //[InlineData(".pdf", "application/pdf", "PDF 文档")]
        //[InlineData(".doc", "application/msword", "Word 文档")]
        //[InlineData(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "Word 文档 (新)")]
        //[InlineData(".xls", "application/vnd. ms-excel", "Excel 表格")]
        //[InlineData(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Excel 表格 (新)")]
        //[InlineData(".ppt", "application/vnd.ms-powerpoint", "PowerPoint")]
        //[InlineData(".pptx", "application/vnd.openxmlformats-officedocument.presentationml. presentation", "PowerPoint (新)")]
        //public void TestDocumentFormats(string extension, string expectedMimeType, string description)
        //{
        //    // 测试常见文档格式
        //    string fileName = $"test{extension}";
        //    bool result = _provider.TryGetContentType(fileName, out var mimeType);

        //    Assert.True(result);
        //    Assert.Equal(expectedMimeType, mimeType);

        //    _output.WriteLine($"{description,-20} - 扩展名:  {extension,-10} => MIME: {mimeType}");
        //}

        //[Theory]
        //[InlineData(".zip", "application/zip", "ZIP 压缩包")]
        //[InlineData(".rar", "application/vnd.rar", "RAR 压缩包")]
        //[InlineData(".7z", "application/x-7z-compressed", "7Z 压缩包")]
        //[InlineData(".tar", "application/x-tar", "TAR 归档")]
        //[InlineData(".gz", "application/gzip", "GZ 压缩")]
        //public void TestArchiveFormats(string extension, string expectedMimeType, string description)
        //{
        //    // 测试常见压缩格式
        //    string fileName = $"test{extension}";
        //    bool result = _provider.TryGetContentType(fileName, out var mimeType);

        //    Assert.True(result);
        //    Assert.Equal(expectedMimeType, mimeType);

        //    _output.WriteLine($"{description,-15} - 扩展名:  {extension,-10} => MIME: {mimeType}");
        //}

        //[Theory]
        //[InlineData(".txt", "text/plain", "纯文本")]
        //[InlineData(".html", "text/html", "HTML")]
        //[InlineData(".css", "text/css", "CSS")]
        //[InlineData(".js", "text/javascript", "JavaScript")]
        //[InlineData(".json", "application/json", "JSON")]
        //[InlineData(".xml", "application/xml", "XML")]
        //[InlineData(".csv", "text/csv", "CSV")]
        //public void TestTextFormats(string extension, string expectedMimeType, string description)
        //{
        //    // 测试常见文本格式
        //    string fileName = $"test{extension}";
        //    bool result = _provider.TryGetContentType(fileName, out var mimeType);

        //    Assert.True(result);
        //    Assert.Equal(expectedMimeType, mimeType);

        //    _output.WriteLine($"{description,-15} - 扩展名: {extension,-10} => MIME: {mimeType}");
        //}

        #endregion 特定格式测试

        #region 边界情况测试

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("noextension")]
        [InlineData(". ")]
        [InlineData(".. ")]
        public void TestEdgeCases_InvalidFileNames(string fileName)
        {
            // 测试边界情况：无效的文件名
            bool result = _provider.TryGetContentType(fileName, out var mimeType);

            _output.WriteLine($"边界测试 - 文件名: '{fileName}' => 结果:  {result}, MIME: {mimeType ?? "null"}");
        }

        [Fact]
        public void TestFileNameWithMultipleDots()
        {
            // 测试包含多个点号的文件名
            string fileName = "my. file.name.with.dots.pdf";
            bool result = _provider.TryGetContentType(fileName, out var mimeType);

            Assert.True(result);
            Assert.Equal("application/pdf", mimeType);

            _output.WriteLine($"多点号文件名:  {fileName} => MIME: {mimeType}");
        }

        [Fact]
        public void TestFileNameWithPath()
        {
            // 测试包含路径的文件名
            string fileName = "/path/to/file/document.pdf";
            bool result = _provider.TryGetContentType(fileName, out var mimeType);

            Assert.True(result);
            Assert.Equal("application/pdf", mimeType);

            _output.WriteLine($"包含路径的文件名: {fileName} => MIME: {mimeType}");
        }

        #endregion 边界情况测试

        #region 自定义扩展测试

        [Fact]
        public void TestCustomMappings()
        {
            // 测试添加自定义 MIME 类型映射
            var customProvider = new FileExtensionContentTypeProvider();

            // 添加自定义映射
            customProvider.Mappings[".custom"] = "application/x-custom";
            customProvider.Mappings[".myapp"] = "application/x-myapp";

            _output.WriteLine("=== 自定义 MIME 类型映射测试 ===");
            _output.WriteLine("");

            // 测试自定义映射
            bool result1 = customProvider.TryGetContentType("file.custom", out var mimeType1);
            bool result2 = customProvider.TryGetContentType("file.myapp", out var mimeType2);

            Assert.True(result1);
            Assert.Equal("application/x-custom", mimeType1);
            Assert.True(result2);
            Assert.Equal("application/x-myapp", mimeType2);

            _output.WriteLine($"自定义扩展 . custom => {mimeType1}");
            _output.WriteLine($"自定义扩展 .myapp => {mimeType2}");
            _output.WriteLine($"总映射数: {customProvider.Mappings.Count}");
        }

        #endregion 自定义扩展测试

        #region 性能测试

        [Fact]
        public void TestPerformance_MultipleLoookups()
        {
            // 性能测试：多次查找
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int iterations = 10000;

            for (int i = 0; i < iterations; i++)
            {
                _provider.TryGetContentType("file.jpg", out _);
                _provider.TryGetContentType("file.pdf", out _);
                _provider.TryGetContentType("file.html", out _);
            }

            stopwatch.Stop();

            _output.WriteLine("=== 性能测试 ===");
            _output.WriteLine($"执行 {iterations * 3} 次查找");
            _output.WriteLine($"总耗时: {stopwatch.ElapsedMilliseconds} ms");
            _output.WriteLine($"平均耗时:  {stopwatch.ElapsedMilliseconds / (double)(iterations * 3):F4} ms/次");
        }

        #endregion 性能测试

        [Fact]
        public void TryGetContentType_ShouldReturnCorrectMimeType_ForJpgFileByHelper()
        {
            // Arrange - 准备测试数据
            string fileName = "file.jpg";

            // Act - 使用静态辅助类
            bool result = MimeTypeHelper.TryGetMimeType(fileName, out string mimeType);

            // Assert - 验证结果
            Assert.True(result);
            Assert.Equal("image/jpeg", mimeType);

            _output.WriteLine($"文件名: {fileName} => MIME 类型: {mimeType}");
        }

        [Fact]
        public void GetMimeType_ShouldReturnDefaultForUnknownExtension()
        {
            // Arrange
            string fileName = "file.unknown";

            // Act
            string mimeType = MimeTypeHelper.GetMimeType(fileName);

            // Assert
            Assert.Equal("application/octet-stream", mimeType);

            _output.WriteLine($"未知文件:  {fileName} => 默认 MIME:  {mimeType}");
        }

        [Fact]
        public void IsImage_ShouldReturnTrue_ForImageFiles()
        {
            // Assert
            Assert.True(MimeTypeHelper.IsImage("photo.jpg"));
            Assert.True(MimeTypeHelper.IsImage("logo.png"));
            Assert.False(MimeTypeHelper.IsImage("document.pdf"));

            _output.WriteLine("图片类型判断测试通过");
        }

        [Fact]
        public void GetAllExtensions_ShouldReturnMultipleExtensions_ForImageJpeg()
        {
            // Act
            var extensions = MimeTypeHelper.GetAllExtensions("image/jpeg").ToList();

            // Assert
            Assert.Contains(".jpg", extensions);
            Assert.Contains(".jpeg", extensions);

            _output.WriteLine($"image/jpeg 的所有扩展名: {string.Join(", ", extensions)}");
        }

        [Fact]
        public void OutputAllMappings_UsingStaticHelper()
        {
            // 使用静态辅助类获取所有映射
            var mappings = MimeTypeHelper.GetAllMappings();

            _output.WriteLine($"总计:  {mappings.Count} 个映射关系");

            var groupedMappings = mappings
                .GroupBy(m => m.Value.Split('/')[0])
                .OrderBy(g => g.Key);

            foreach (var group in groupedMappings)
            {
                _output.WriteLine($"\n【{group.Key.ToUpper()}】 共 {group.Count()} 个");

                foreach (var mapping in group.OrderBy(m => m.Key).Take(10))
                {
                    _output.WriteLine($"  {mapping.Key,-15} => {mapping.Value}");
                }
            }
        }

        [Fact]
        public async Task GuessFileSuffix_ShouldUseNewResizeAPI_ForLargeImage()
        {
            // Arrange - 创建一个超大图片
            var width = 4000;
            var height = 3000;

            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Cyan);

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);

            var dataUrl = new DataUrl
            {
                Data = data.ToArray()
            };

            var originalSize = dataUrl.Data.Length;

            _output.WriteLine($"=== 新 API 大图处理测试 ===");
            _output.WriteLine($"原始尺寸: {width} x {height}");
            _output.WriteLine($"原始大小: {originalSize / 1024.0 / 1024.0:F2} MB");

            // Act
            var result = await MjImageHelper.GuessFileSuffix(dataUrl);

            // Assert
            Assert.Equal(".png", result);
            //Assert.True(dataUrl.Data.Length < originalSize);

            _output.WriteLine($"处理后大小: {dataUrl.Data.Length / 1024.0 / 1024.0:F2} MB");
            _output.WriteLine($"压缩率: {(1 - (double)dataUrl.Data.Length / originalSize) * 100:F2}%");
            _output.WriteLine($"扩展名: {result}");
        }

        [Fact]
        public async Task GuessFileSuffix_UrlWebp()
        {
            var dataUrl = new DataUrl
            {
                Url = "https://img.aitop3000.com/attachments/merges/2026/01/06/merged_d7366a9587184c45b73cc470af091c41.webp"
            };

            var result = await MjImageHelper.GuessFileSuffix(dataUrl);

            Assert.Equal(".webp", result);
        }

        [Fact]
        public async Task GuessFileSuffix_UrlWebp2()
        {
            var dataUrl = new DataUrl
            {
                Url = "https://img.aitop3000.com/attachments/2fde8050-b7ea-4327-be45-3dfc738efb40/0_0.png?x-oss-process=style/webp"
            };

            var result = await MjImageHelper.GuessFileSuffix(dataUrl);

            Assert.Equal(".webp", result);
        }

        [Fact]
        public async Task GuessFileSuffix_UrlPng()
        {
            var res = await MjImageFetchHelper.FetchFileAsync("https://img.aitop3000.com/attachments/2fde8050-b7ea-4327-be45-3dfc738efb40/0_0.png");
            var dataUrl = new DataUrl
            {
                Data = res.FileBytes
            };

            // 日志记录大小和宽高
            using var bitmap = SKBitmap.Decode(res.FileBytes);

            _output.WriteLine($"图片尺寸: {bitmap.Width} x {bitmap.Height}");
            _output.WriteLine($"图片大小: {res.FileBytes.Length / 1024.0:F2} KB");

            var result = await MjImageHelper.GuessFileSuffix(dataUrl);

            Assert.Equal(".png", result);

            //// 保存到本地临时目录
            //var tempPath = Path.Combine(Path.GetTempPath(), $"test_image{result}");

            //await File.WriteAllBytesAsync(tempPath, dataUrl.Data);
            //_output.WriteLine($"图片已保存到: {tempPath}");

            //var newFile = SKBitmap.Decode(dataUrl.Data);
            //_output.WriteLine($"新图片尺寸: {newFile.Width} x {newFile.Height}");
            //_output.WriteLine($"新图片大小: {dataUrl.Data.Length / 1024.0:F2} KB");
        }

        [Fact]
        public async Task GuessFileSuffix_UrlJpg()
        {
            var dataUrl = new DataUrl
            {
                Url = "https://4z19vixxpi.ucarecd.net/76a95bfc-585f-43cc-9380-0167d70ec754/-/format/jpeg/-/quality/lighter/"
            };

            var result = await MjImageHelper.GuessFileSuffix(dataUrl);

            Assert.Equal(".jpg", result);
        }
    }
}