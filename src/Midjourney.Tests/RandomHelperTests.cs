using Midjourney.Base.Util;
using Xunit.Abstractions;

namespace Midjourney.Tests
{
    /// <summary>
    /// RandomHelper 单元测试
    /// </summary>
    public class RandomHelperTests : BaseTests
    {
        private readonly TestOutputWrapper _output;

        public RandomHelperTests(ITestOutputHelper output)
        {
            _output = new TestOutputWrapper(output);
        }

        #region RandomNumbers 测试

        [Fact]
        public void RandomNumbers_ShouldReturnCorrectLength()
        {
            // Arrange
            int length = 10;

            // Act
            var result = RandomHelper.RandomNumbers(length);

            // Assert
            Assert.Equal(length, result.Length);
            _output.WriteLine($"生成的随机数字:  {result}");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(20)]
        [InlineData(50)]
        [InlineData(100)]
        public void RandomNumbers_ShouldReturnCorrectLength_ForVariousLengths(int length)
        {
            // Act
            var result = RandomHelper.RandomNumbers(length);

            // Assert
            Assert.Equal(length, result.Length);
            _output.WriteLine($"长度 {length,3}:  {result}");
        }

        [Fact]
        public void RandomNumbers_ShouldContainOnlyDigits()
        {
            // Arrange
            int length = 20;

            // Act
            var result = RandomHelper.RandomNumbers(length);

            // Assert
            Assert.Matches(@"^\d+$", result);
            _output.WriteLine($"验证纯数字: {result}");
        }

        [Fact]
        public void RandomNumbers_ShouldThrowException_WhenLengthIsZero()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                RandomHelper.RandomNumbers(0));

            _output.WriteLine($"长度为 0 时抛出异常: {exception.GetType().Name}");
            _output.WriteLine($"参数名:  {exception.ParamName}");
        }

        [Fact]
        public void RandomNumbers_ShouldThrowException_WhenLengthIsNegative()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                RandomHelper.RandomNumbers(-1));

            _output.WriteLine($"长度为负数时抛出异常:  {exception.GetType().Name}");
            _output.WriteLine($"参数名: {exception.ParamName}");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-10)]
        public void RandomNumbers_ShouldThrowException_ForInvalidLengths(int invalidLength)
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                RandomHelper.RandomNumbers(invalidLength));

            _output.WriteLine($"无效长度 {invalidLength}:  抛出 {exception.GetType().Name}");
        }

        [Fact]
        public void RandomNumbers_ShouldGenerateDifferentValues()
        {
            // Arrange
            int length = 5;
            int iterations = 1000;
            var results = new HashSet<string>();

            // Act
            for (int i = 0; i < iterations; i++)
            {
                results.Add(RandomHelper.RandomNumbers(length));
            }

            // Assert - 至少应该有 95% 的唯一值（考虑到极小的碰撞概率）
            var uniquePercentage = (results.Count / (double)iterations) * 100;
            Assert.True(uniquePercentage >= 95, $"唯一值比例:  {uniquePercentage}%");

            _output.WriteLine($"生成 {iterations} 次，唯一值:  {results.Count} 个");
            _output.WriteLine($"唯一值比例: {uniquePercentage:F2}%");
            _output.WriteLine($"前 10 个样本:  {string.Join(", ", results.Take(10))}");
        }

        [Fact]
        public void RandomNumbers_ShouldHaveUniformDistribution()
        {
            // Arrange
            int length = 1;
            int iterations = 10000;
            var digitCounts = new int[10];

            // Act - 生成大量单个数字，统计每个数字出现的频率
            for (int i = 0; i < iterations; i++)
            {
                var digit = int.Parse(RandomHelper.RandomNumbers(length));
                digitCounts[digit]++;
            }

            // Assert - 每个数字应该出现约 1000 次（允许 20% 的偏差）
            var expectedCount = iterations / 10.0;
            var tolerance = expectedCount * 0.2; // 20% 容差

            _output.WriteLine("=== 数字分布统计 ===");
            _output.WriteLine($"总样本数: {iterations}");
            _output.WriteLine($"期望值: {expectedCount:F0} (±{tolerance:F0})");
            _output.WriteLine("");

            for (int digit = 0; digit < 10; digit++)
            {
                var count = digitCounts[digit];
                var percentage = (count / (double)iterations) * 100;
                var deviation = Math.Abs(count - expectedCount);

                _output.WriteLine($"数字 {digit}:  {count,5} 次 ({percentage:F2}%), 偏差:  {deviation:F0}");

                Assert.True(
                    Math.Abs(count - expectedCount) <= tolerance,
                    $"数字 {digit} 分布不均匀: 期望 {expectedCount}, 实际 {count}"
                );
            }
        }

        [Fact]
        public void RandomNumbers_ShouldIncludeAllDigits_InLargeSet()
        {
            // Arrange
            int length = 10;
            int iterations = 100;
            var allDigits = new HashSet<char>();

            // Act
            for (int i = 0; i < iterations; i++)
            {
                var result = RandomHelper.RandomNumbers(length);
                foreach (var digit in result)
                {
                    allDigits.Add(digit);
                }
            }

            // Assert - 应该包含 0-9 所有数字
            Assert.Equal(10, allDigits.Count);
            _output.WriteLine($"包含的所有数字: {string.Join(", ", allDigits.OrderBy(d => d))}");
        }

        [Fact]
        public void RandomNumbers_PerformanceTest()
        {
            // Arrange
            int iterations = 100000;
            var lengths = new[] { 1, 10, 50, 100 };

            _output.WriteLine("=== 性能测试 ===");
            _output.WriteLine($"迭代次数: {iterations: N0}");
            _output.WriteLine("");

            foreach (var length in lengths)
            {
                // Act
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                for (int i = 0; i < iterations; i++)
                {
                    RandomHelper.RandomNumbers(length);
                }

                stopwatch.Stop();

                // Output
                var avgTime = stopwatch.ElapsedMilliseconds / (double)iterations;
                _output.WriteLine($"长度 {length,3}: 总耗时 {stopwatch.ElapsedMilliseconds,6} ms, " +
                                $"平均 {avgTime: F6} ms/次, " +
                                $"吞吐量 {iterations / (stopwatch.ElapsedMilliseconds / 1000.0):F0} 次/秒");
            }
        }

        #endregion RandomNumbers 测试

        #region RandomInt 测试

        [Fact]
        public void RandomInt_ShouldReturnValueInRange()
        {
            // Arrange
            int minValue = 10;
            int maxValue = 20;

            // Act
            var result = RandomHelper.RandomInt(minValue, maxValue);

            // Assert
            Assert.InRange(result, minValue, maxValue - 1);
            _output.WriteLine($"范围 [{minValue}, {maxValue}), 结果: {result}");
        }

        [Theory]
        [InlineData(0, 10)]
        [InlineData(1, 100)]
        [InlineData(-10, 10)]
        [InlineData(-100, -50)]
        [InlineData(100, 200)]
        public void RandomInt_ShouldReturnValueInRange_ForVariousRanges(int minValue, int maxValue)
        {
            // Act
            var result = RandomHelper.RandomInt(minValue, maxValue);

            // Assert
            Assert.InRange(result, minValue, maxValue - 1);
            _output.WriteLine($"范围 [{minValue,4}, {maxValue,4}), 结果: {result,4}");
        }

        [Fact]
        public void RandomInt_ShouldThrowException_WhenMinEqualsMax()
        {
            // Arrange
            int value = 10;

            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                RandomHelper.RandomInt(value, value));

            _output.WriteLine($"min == max 时抛出异常: {exception.GetType().Name}");
            _output.WriteLine($"参数名: {exception.ParamName}");
        }

        [Fact]
        public void RandomInt_ShouldThrowException_WhenMinGreaterThanMax()
        {
            // Arrange
            int minValue = 20;
            int maxValue = 10;

            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                RandomHelper.RandomInt(minValue, maxValue));

            _output.WriteLine($"min > max 时抛出异常: {exception.GetType().Name}");
            _output.WriteLine($"范围:  [{minValue}, {maxValue})");
        }

        [Fact]
        public void RandomInt_ShouldGenerateDifferentValues()
        {
            // Arrange
            int minValue = 0;
            int maxValue = 100;
            int iterations = 100;
            var results = new HashSet<int>();

            // Act
            for (int i = 0; i < iterations; i++)
            {
                results.Add(RandomHelper.RandomInt(minValue, maxValue));
            }

            // Assert - 应该有较高的唯一值比例
            var uniquePercentage = (results.Count / (double)iterations) * 100;
            Assert.True(uniquePercentage >= 50, $"唯一值比例太低: {uniquePercentage}%");

            _output.WriteLine($"生成 {iterations} 次，唯一值: {results.Count} 个");
            _output.WriteLine($"唯一值比例:  {uniquePercentage:F2}%");
        }

        [Fact]
        public void RandomInt_ShouldHaveUniformDistribution()
        {
            // Arrange
            int minValue = 0;
            int maxValue = 10;
            int iterations = 10000;
            var counts = new int[maxValue - minValue];

            // Act
            for (int i = 0; i < iterations; i++)
            {
                var value = RandomHelper.RandomInt(minValue, maxValue);
                counts[value - minValue]++;
            }

            // Assert - 每个值应该出现约 1000 次（允许 20% 的偏差）
            var expectedCount = iterations / (maxValue - minValue);
            var tolerance = expectedCount * 0.2;

            _output.WriteLine("=== 整数分布统计 ===");
            _output.WriteLine($"范围: [{minValue}, {maxValue})");
            _output.WriteLine($"总样本数:  {iterations}");
            _output.WriteLine($"期望值:  {expectedCount:F0} (±{tolerance:F0})");
            _output.WriteLine("");

            for (int i = 0; i < counts.Length; i++)
            {
                var value = minValue + i;
                var count = counts[i];
                var percentage = (count / (double)iterations) * 100;
                var deviation = Math.Abs(count - expectedCount);

                _output.WriteLine($"值 {value,2}: {count,5} 次 ({percentage:F2}%), 偏差: {deviation: F0}");

                Assert.True(
                    deviation <= tolerance,
                    $"值 {value} 分布不均匀:  期望 {expectedCount}, 实际 {count}"
                );
            }
        }

        [Fact]
        public void RandomInt_ShouldCoverFullRange_InLargeSet()
        {
            // Arrange
            int minValue = 0;
            int maxValue = 10;
            int iterations = 1000;
            var values = new HashSet<int>();

            // Act
            for (int i = 0; i < iterations; i++)
            {
                values.Add(RandomHelper.RandomInt(minValue, maxValue));
            }

            // Assert - 应该覆盖整个范围
            Assert.Equal(maxValue - minValue, values.Count);
            _output.WriteLine($"范围 [{minValue}, {maxValue}), 覆盖所有 {values.Count} 个值");
            _output.WriteLine($"值列表: {string.Join(", ", values.OrderBy(v => v))}");
        }

        [Fact]
        public void RandomInt_ShouldWorkWithNegativeRange()
        {
            // Arrange
            int minValue = -50;
            int maxValue = -10;
            int iterations = 100;

            // Act & Assert
            for (int i = 0; i < iterations; i++)
            {
                var result = RandomHelper.RandomInt(minValue, maxValue);
                Assert.InRange(result, minValue, maxValue - 1);
            }

            _output.WriteLine($"负数范围 [{minValue}, {maxValue}) 测试通过");
        }

        [Fact]
        public void RandomInt_ShouldWorkWithLargeRange()
        {
            // Arrange
            int minValue = 0;
            int maxValue = 1000000;

            // Act
            var result = RandomHelper.RandomInt(minValue, maxValue);

            // Assert
            Assert.InRange(result, minValue, maxValue - 1);
            _output.WriteLine($"大范围 [0, 1000000) 测试: {result}");
        }

        [Fact]
        public void RandomInt_PerformanceTest()
        {
            // Arrange
            int iterations = 1000000;
            var ranges = new[]
            {
                (0, 10),
                (0, 100),
                (0, 1000),
                (0, 1000000)
            };

            _output.WriteLine("=== RandomInt 性能测试 ===");
            _output.WriteLine($"迭代次数:  {iterations: N0}");
            _output.WriteLine("");

            foreach (var (min, max) in ranges)
            {
                // Act
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                for (int i = 0; i < iterations; i++)
                {
                    RandomHelper.RandomInt(min, max);
                }

                stopwatch.Stop();

                // Output
                var avgTime = stopwatch.ElapsedMilliseconds / (double)iterations;
                _output.WriteLine($"范围 [{min,7}, {max,7}): 总耗时 {stopwatch.ElapsedMilliseconds,6} ms, " +
                                $"平均 {avgTime:F6} ms/次, " +
                                $"吞吐量 {iterations / (stopwatch.ElapsedMilliseconds / 1000.0):F0} 次/秒");
            }
        }

        #endregion RandomInt 测试

        #region 综合测试

        [Fact]
        public void RandomNumbers_And_RandomInt_ShouldBeCryptographicallySecure()
        {
            // 这个测试验证生成的随机数是否具有良好的随机性
            // 通过检查连续值之间是否存在明显的模式

            // Test RandomNumbers
            _output.WriteLine("=== 密码学安全性测试 ===");
            _output.WriteLine("");
            _output.WriteLine("RandomNumbers 样本:");

            var numberSamples = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                numberSamples.Add(RandomHelper.RandomNumbers(10));
            }

            foreach (var sample in numberSamples)
            {
                _output.WriteLine($"  {sample}");
            }

            // Test RandomInt
            _output.WriteLine("");
            _output.WriteLine("RandomInt 样本 (0-99):");

            var intSamples = new List<int>();
            for (int i = 0; i < 20; i++)
            {
                intSamples.Add(RandomHelper.RandomInt(0, 100));
            }

            _output.WriteLine($"  {string.Join(", ", intSamples)}");

            // 简单的连续性检查 - 不应该有太多连续的相同值
            var consecutiveCount = 0;
            for (int i = 1; i < intSamples.Count; i++)
            {
                if (intSamples[i] == intSamples[i - 1])
                {
                    consecutiveCount++;
                }
            }

            var consecutivePercentage = (consecutiveCount / (double)intSamples.Count) * 100;
            Assert.True(consecutivePercentage < 20, $"连续相同值比例过高: {consecutivePercentage}%");

            _output.WriteLine($"");
            _output.WriteLine($"连续相同值:  {consecutiveCount}/{intSamples.Count} ({consecutivePercentage:F2}%)");
        }

        [Fact]
        public void CombinedUsage_GenerateRandomCode()
        {
            // 实际使用场景：生成验证码
            _output.WriteLine("=== 实际应用场景：生成验证码 ===");
            _output.WriteLine("");

            for (int i = 0; i < 10; i++)
            {
                var code = RandomHelper.RandomNumbers(6);
                _output.WriteLine($"验证码 {i + 1}: {code}");
            }
        }

        [Fact]
        public void CombinedUsage_GenerateRandomId()
        {
            // 实际使用场景：生成随机 ID
            _output.WriteLine("=== 实际应用场景：生成随机 ID ===");
            _output.WriteLine("");

            for (int i = 0; i < 5; i++)
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var random = RandomHelper.RandomNumbers(8);
                var id = $"{timestamp}{random}";

                _output.WriteLine($"ID {i + 1}: {id}");
            }
        }

        #endregion 综合测试
    }
}