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

using System.Linq.Expressions;
using FreeSql;
using Microsoft.Data.SqlClient;

namespace Midjourney.Infrastructure.Data
{
    public static class FreeSqlExtensions
    {
        /// <summary>
        /// 根据 ID 返回 T1 实体第一条记录，记录不存在时返回 null
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="select"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public static T GetById<T>(this ISelect<T> select, string id) where T : IBaseId
        {
            return select.Where(c => c.Id == id).First();
        }

        /// <summary>
        /// 根据 ID 返回 T1 实体第一条记录，记录不存在时返回 null
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public static T GetById<T>(this IFreeSql freeSql, string id) where T : class, IBaseId
        {
            return freeSql.Select<T>().GetById(id);
        }

        /// <summary>
        /// 根据 ID 返回 T1 实体第一条记录，记录不存在时返回 null
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="select"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public static T Get<T>(this ISelect<T> select, string id) where T : IBaseId
        {
            return select.GetById(id);
        }

        /// <summary>
        /// 根据 ID 返回 T1 实体第一条记录，记录不存在时返回 null
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public static T Get<T>(this IFreeSql freeSql, string id) where T : class, IBaseId
        {
            return freeSql.Select<T>().GetById(id);
        }

        /// <summary>
        /// 执行SQL查询，返回 T1 实体所有字段的第一条记录，记录不存在时返回 null
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="exp"></param>
        /// <returns></returns>
        public static T Get<T>(this IFreeSql freeSql, Expression<Func<T, bool>> exp) where T : class, IBaseId
        {
            return freeSql.Select<T>().Where(exp).First();
        }

        /// <summary>
        /// 查询的记录数量
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="exp"></param>
        /// <returns></returns>
        public static int Count<T>(this IFreeSql freeSql, Expression<Func<T, bool>> exp) where T : class
        {
            return (int)freeSql.Select<T>().Where(exp).Count();
        }

        /// <summary>
        /// 执行SQL查询，是否有记录
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="exp"></param>
        /// <returns></returns>
        public static bool Exists<T>(this IFreeSql freeSql, Expression<Func<T, bool>> exp) where T : class
        {
            return freeSql.Select<T>().Where(exp).Any();
        }

        /// <summary>
        /// 执行SQL查询，是否有记录
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="exp"></param>
        /// <returns></returns>
        public static bool Any<T>(this IFreeSql freeSql, Expression<Func<T, bool>> exp) where T : class
        {
            return freeSql.Select<T>().Where(exp).Any();
        }

        /// <summary>
        /// 根据 IDS 返回 T1 实体所有字段的记录，记录不存在时返回 Count 为 0 的列表，如果参数没有值则返回空列表
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="ids"></param>
        /// <returns></returns>
        public static List<T> GetList<T>(this IFreeSql freeSql, string[] ids) where T : class, IBaseId
        {
            if (ids != null && ids.Length > 0)
            {
                ids = ids.Where(c => string.IsNullOrWhiteSpace(c)).Distinct().ToArray();
                if (ids.Length > 0)
                {
                    return freeSql.Select<T>().Where(c => ids.Contains(c.Id)).ToList();
                }
            }
            return new List<T>();
        }

        /// <summary>
        /// IDS 以 , 分割，根据 IDS 返回 T1 实体所有字段的记录，记录不存在时返回 Count 为 0 的列表，如果参数没有值则返回空列表
        /// 提示：此方案不推荐，建议声明类型传递数组 int[] ids
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="ids"></param>
        /// <returns></returns>
        public static List<T> GetList<T>(this IFreeSql freeSql, string ids) where T : class, IBaseId
        {
            if (!string.IsNullOrWhiteSpace(ids))
            {
                var idsArray = ids.Split(',')
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToArray();
                return freeSql.GetList<T>(idsArray);
            }
            return new List<T>();
        }

        /// <summary>
        /// 根据 IDS 返回 T1 实体所有字段的记录，记录不存在时返回 Count 为 0 的列表，如果参数没有值则返回空列表
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="ids"></param>
        /// <returns></returns>
        public static List<T> GetList<T>(this IFreeSql freeSql, IEnumerable<string> ids) where T : class, IBaseId
        {
            if (ids != null && ids.Any())
            {
                var array = ids.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToArray();
                if (array.Length > 0)
                {
                    return freeSql.Select<T>().Where(c => array.Contains(c.Id)).ToList();
                }
            }
            return new List<T>();
        }

        /// <summary>
        /// 根据表达式获取实体列表
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="exp"></param>
        /// <returns></returns>
        public static List<T> GetList<T>(this IFreeSql freeSql, Expression<Func<T, bool>> exp) where T : class
        {
            return freeSql.Select<T>().Where(exp).ToList();
        }

        /// <summary>
        /// 查询条件，Where(a => a.Id > 10)，支持导航对象查询，Where(a => a.Author.Email == "2881099@qq.com")
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="exp"></param>
        /// <returns></returns>
        public static ISelect<T> Select<T>(this IFreeSql freeSql, Expression<Func<T, bool>> exp) where T : class
        {
            return freeSql.Select<T>().Where(exp);
        }

        /// <summary>
        /// 新增
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        public static int Add<T>(this IFreeSql freeSql, T model) where T : class, IBaseId
        {
            return freeSql.Insert(model).ExecuteAffrows();
        }

        /// <summary>
        /// 新增 - 返回新增后的对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        public static T AddEntity<T>(this IFreeSql freeSql, T model) where T : class
        {
            if (model != null)
            {
                return freeSql.Insert(model).ExecuteInserted()?.FirstOrDefault();
            }
            return null;
        }

        /// <summary>
        /// 根据主键更新 - 返回受影响的行数 - 根据 ID 主键更新对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        public static int UpdateById<T>(this IFreeSql freeSql, T model) where T : class, IBaseId
        {
            if (model != null)
            {
                return freeSql.Update<T>().SetSource(model).Where(c => c.Id == model.Id).ExecuteAffrows();
            }
            return 0;
        }

        /// <summary>
        /// 根据主键更新 - 返回受影响的行数 - 实体必须定义主键 - 主键必须是 int 类型
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        public static int Update<T>(this IFreeSql freeSql, T model) where T : class, IBaseId
        {
            if (model != null)
            {
                return freeSql.Update<T>().SetSource(model).ExecuteAffrows();
            }
            return 0;
        }

        /// <summary>
        /// 根据主键更新 - 返回受影响的行数 - 实体必须定义主键
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        public static int UpdateEntity<T>(this IFreeSql freeSql, T model) where T : class
        {
            if (model != null)
            {
                return freeSql.Update<T>().SetSource(model).ExecuteAffrows();
            }
            return 0;
        }

        /// <summary>
        /// 删除 - 根据主键 - 返回受影响的行数
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        public static int Delete<T>(this IFreeSql freeSql, T model) where T : class, IBaseId
        {
            if (model != null)
            {
                return freeSql.Delete<T>().Where(c => c.Id == model.Id).ExecuteAffrows();
            }
            return 0;
        }

        /// <summary>
        /// 删除 - 根据主键 - 返回受影响的行数
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public static int Delete<T>(this IFreeSql freeSql, string id) where T : class, IBaseId
        {
            return freeSql.Delete<T>().Where(c => c.Id == id).ExecuteAffrows();
        }

        /// <summary>
        /// 执行SQL语句，返回影响的行数
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="exp"></param>
        /// <returns></returns>
        public static int Delete<T>(this IFreeSql freeSql, Expression<Func<T, bool>> exp) where T : class
        {
            return freeSql.Delete<T>().Where(exp).ExecuteAffrows();
        }

        /// <summary>
        /// 删除 - 根据主键 - 返回受影响的行数
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="freeSql"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public static int DeleteById<T>(this IFreeSql freeSql, string id) where T : class, IBaseId
        {
            return freeSql.Delete<T>().Where(c => c.Id == id).ExecuteAffrows();
        }

        /// <summary>
        /// 分页
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="select"></param>
        /// <param name="pageIndex"></param>
        /// <param name="pageSize"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static List<T> ToPageList<T>(this ISelect<T> select, int pageIndex, int pageSize, out int count) where T : IBaseId
        {
            var list = select.Count(out long total).Page(pageIndex, pageSize).ToList();
            count = (int)total;
            return list;
        }

        /// <summary>
        /// 分页
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="select"></param>
        /// <param name="pageIndex"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        public static List<T> ToPageList<T>(this ISelect<T> select, int pageIndex, int pageSize) where T : IBaseId
        {
            return select.Page(pageIndex, pageSize).ToList();
        }

        /// <summary>
        /// SqlParameter 转字典对象参数，用于 FreeSql 参数
        /// </summary>
        /// <param name="ps"></param>
        /// <returns></returns>
        public static Dictionary<object, object> ToFreeSqlParams(this SqlParameter[] ps)
        {
            var dic = new Dictionary<object, object>();
            if (ps == null || ps.Length <= 0)
            {
                return dic;
            }

            foreach (var item in ps)
            {
                dic.TryAdd(item.ParameterName, item.Value);
            }
            return dic;
        }
    }
}