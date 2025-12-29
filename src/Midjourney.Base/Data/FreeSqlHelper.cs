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

using FreeSql;
using FreeSql.Aop;
using Serilog;

namespace Midjourney.Base.Data
{
    /// <summary>
    /// FreeSql 帮助类 - 使用前必须配置初始化
    /// </summary>
    public class FreeSqlHelper
    {
        /// <summary>
        /// 允许的数据库类型
        /// </summary>
        public static readonly DatabaseType[] AllowedDatabaseTypes =
        [
            DatabaseType.SQLite,
            DatabaseType.MySQL,
            DatabaseType.PostgreSQL,
            DatabaseType.SQLServer,
        ];

        private static IFreeSql _freeSql;

        public static IFreeSql FreeSql => _freeSql;

        /// <summary>
        /// 配置 FreeSql
        /// </summary>
        /// <param name="freeSql"></param>
        public static void Configure(IFreeSql freeSql)
        {
            _freeSql = freeSql;
        }

        /// <summary>
        /// 初始化 FreeSql
        /// </summary>
        public static IFreeSql Init(DatabaseType databaseType, string databaseConnectionString = null, bool autoSyncStructure = false)
        {
            if (!AllowedDatabaseTypes.Contains(databaseType))
            {
                return null;
            }

            if (databaseType == DatabaseType.SQLite)
            {
                databaseConnectionString = @"Data Source=data/mj_sqlite.db";
            }

            if (string.IsNullOrWhiteSpace(databaseConnectionString))
            {
                databaseConnectionString = GlobalConfiguration.Setting?.DatabaseConnectionString;
            }

            if (string.IsNullOrWhiteSpace(databaseConnectionString))
            {
                return null;
            }

            var fsqlBuilder = new FreeSqlBuilder()
                .UseLazyLoading(true); // 开启延时加载功能

            // 生产环境注意
            // 自动同步实体结构到数据库
            if (autoSyncStructure)
            {
                fsqlBuilder.UseAutoSyncStructure(true)
                      // 监视 SQL 命令对象
                      .UseMonitorCommand(cmd =>
                      {
                          Log.Information(cmd.CommandText);
                      });
            }

#if DEBUG
            fsqlBuilder.UseAutoSyncStructure(true)
                  // 监视 SQL 命令对象
                  .UseMonitorCommand(cmd =>
                  {
                      //Log.Debug(cmd.CommandText);
                  });
#endif

            switch (databaseType)
            {
                //case DatabaseType.LiteDB:
                //case DatabaseType.MongoDB:
                //    {
                //        return null;
                //    }
                case DatabaseType.SQLite:
                    {
                        // Data Source=|DataDirectory|\document.db; Attachs=xxxtb.db; Pooling=true;Min Pool Size=1
                        fsqlBuilder.UseConnectionString(DataType.Sqlite, databaseConnectionString);
                    }
                    break;

                case DatabaseType.MySQL:
                    {
                        // Data Source=192.168.3.241;Port=3306;User ID=root;Password=root; Initial Catalog=mj;Charset=utf8mb4; SslMode=none;Min pool size=1
                        fsqlBuilder.UseConnectionString(DataType.MySql, databaseConnectionString);
                    }
                    break;

                case DatabaseType.PostgreSQL:
                    {
                        // Host=192.168.164.10;Port=5432;Username=postgres;Password=123456; Database=tedb;ArrayNullabilityMode=Always;Pooling=true;Minimum Pool Size=1
                        fsqlBuilder.UseConnectionString(DataType.PostgreSQL, databaseConnectionString);
                    }
                    break;

                case DatabaseType.SQLServer:
                    {
                        // Data Source=.;User Id=sa;Password=123456;Initial Catalog=freesqlTest;Encrypt=True;TrustServerCertificate=True;Pooling=true;Min Pool Size=1
                        fsqlBuilder.UseConnectionString(DataType.SqlServer, databaseConnectionString);
                    }
                    break;

                default:
                    break;
            }

            var fsql = fsqlBuilder.Build();

            fsql.UseJsonMap(); // 开启功能

            // 日志
            fsql.Aop.CurdAfter += (s, e) =>
            {
                if (e.CurdType == CurdType.Select)
                {
                    if (e.ElapsedMilliseconds > 2000)
                    {
                        Log.Warning("SQL Slow Query: {Sql} Elapsed: {ElapsedMilliseconds}ms > 2000ms", e.Sql, e.ElapsedMilliseconds);
                    }
                    else if (e.ElapsedMilliseconds > 500)
                    {
                        Log.Warning("SQL Slow Query: {Sql} Elapsed: {ElapsedMilliseconds}ms > 500ms", e.Sql, e.ElapsedMilliseconds);
                    }
                }
            };

            // 请务必定义成 Singleton 单例模式
            //services.AddSingleton(fsql);

            return fsql;
        }

        /// <summary>
        /// 验证并配置数据库连接
        /// </summary>
        /// <returns></returns>
        public static bool VerifyConfigure()
        {
            var setting = GlobalConfiguration.Setting;
            var isSuccess = Verify(setting.DatabaseType, setting.DatabaseConnectionString);
            if (isSuccess)
            {
                // 验证成功后，确认配置当前数据库
                var freeSql = Init(setting.DatabaseType, setting.DatabaseConnectionString, false);
                if (freeSql != null)
                {
                    Configure(freeSql);
                }
            }

            return isSuccess;
        }

        /// <summary>
        /// 验证数据库连接
        /// </summary>
        /// <param name="databaseType"></param>
        /// <param name="databaseConnectionString"></param>
        /// <param name="isConfigure">验证成功后是否配置</param>
        /// <returns></returns>
        public static bool Verify(DatabaseType databaseType, string databaseConnectionString)
        {
            try
            {
                switch (databaseType)
                {
                    //case DatabaseType.MongoDB:
                    //    {
                    //        return MongoHelper.Verify(databaseConnectionString, databaseName);
                    //    }
                    case DatabaseType.SQLite:
                    case DatabaseType.MySQL:
                    case DatabaseType.PostgreSQL:
                    case DatabaseType.SQLServer:
                        {
                            // 首次初始化，并同步实体结构
                            var freeSql = Init(databaseType, databaseConnectionString, true);
                            if (freeSql != null)
                            {
                                var obj = freeSql.Ado.ExecuteScalar("SELECT 1");
                                var succees = obj != null && obj.ToString() == "1";
                                if (succees)
                                {
                                    // 同步实体结构

                                    // 注意：
                                    // 1. MySQL InnoDB 存储引擎的限制 1 行 65535 字节
                                    // 这个错误提示 "Row size too large" 表示你创建的表 DiscordAccount 的一行数据大小超过了 MySQL InnoDB 存储引擎的限制（65535 字节）。
                                    // 即使你已经使用了 TEXT 和 LONGTEXT 类型，但这些类型在计算行大小时仍然会占用一部分空间（指针大小，通常很小），而其他 VARCHAR 类型的列仍然会直接计入行大小。

                                    // 2. PostgreSQL 需要启动扩展支持字典类型
                                    // CREATE EXTENSION hstore;

                                    // 第一批
                                    freeSql.CodeFirst.SyncStructure(typeof(User), typeof(BannedWord));

                                    // 第二批
                                    freeSql.CodeFirst.SyncStructure(typeof(DiscordAccount), typeof(DomainTag));

                                    // 第三批
                                    freeSql.CodeFirst.SyncStructure(typeof(TaskInfo));

                                    // 第四批
                                    freeSql.CodeFirst.SyncStructure(typeof(PersonalizeTag));
                                }

                                return succees;
                            }
                            return false;
                        }
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "数据库连接验证失败");
            }

            return false;
        }
    }
}