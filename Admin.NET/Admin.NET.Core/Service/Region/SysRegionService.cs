// Admin.NET 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//
// 本项目主要遵循 MIT 许可证和 Apache 许可证（版本 2.0）进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 和 LICENSE-APACHE 文件。
//
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目二次开发而产生的一切法律纠纷和责任，我们不承担任何责任！

using NewLife.Http;
using NewLife.Serialization;

namespace Admin.NET.Core.Service;

/// <summary>
/// 系统行政区域服务 🧩
/// </summary>
[ApiDescriptionSettings(Order = 310)]
public class SysRegionService : IDynamicApiController, ITransient
{
    private readonly SqlSugarRepository<SysRegion> _sysRegionRep;
    private readonly SysConfigService _sysConfigService;

    public SysRegionService(SqlSugarRepository<SysRegion> sysRegionRep, SysConfigService sysConfigService)
    {
        _sysRegionRep = sysRegionRep;
        _sysConfigService = sysConfigService;
    }

    /// <summary>
    /// 获取行政区域分页列表 🔖
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    [DisplayName("获取行政区域分页列表")]
    public async Task<SqlSugarPagedList<SysRegion>> Page(PageRegionInput input)
    {
        return await _sysRegionRep.AsQueryable()
            .WhereIF(input.Pid > 0, u => u.Pid == input.Pid || u.Id == input.Pid)
            .WhereIF(!string.IsNullOrWhiteSpace(input.Name), u => u.Name.Contains(input.Name))
            .WhereIF(!string.IsNullOrWhiteSpace(input.Code), u => u.Code.Contains(input.Code))
            .ToPagedListAsync(input.Page, input.PageSize);
    }

    /// <summary>
    /// 获取行政区域列表 🔖
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    [DisplayName("获取行政区域列表")]
    public async Task<List<SysRegion>> GetList([FromQuery] RegionInput input)
    {
        return await _sysRegionRep.GetListAsync(u => u.Pid == input.Id);
    }

    /// <summary>
    /// 获取行政区域树 🔖
    /// </summary>
    /// <returns></returns>
    [DisplayName("获取行政区域树")]
    public async Task<List<SysRegion>> GetTree()
    {
        return await _sysRegionRep.AsQueryable().ToTreeAsync(u => u.Children, u => u.Pid, null);
    }

    /// <summary>
    /// 增加行政区域 🔖
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    [ApiDescriptionSettings(Name = "Add"), HttpPost]
    [DisplayName("增加行政区域")]
    public async Task<long> AddRegion(AddRegionInput input)
    {
        input.Code = input.Code?.Trim() ?? "";
        if (input.Code.Length != 12 && input.Code.Length != 9 && input.Code.Length != 6) throw Oops.Oh(ErrorCodeEnum.R2003);

        if (input.Pid != 0)
        {
            var pRegion = await _sysRegionRep.GetFirstAsync(u => u.Id == input.Pid);
            pRegion ??= await _sysRegionRep.GetFirstAsync(u => u.Code == input.Pid.ToString());
            if (pRegion == null) throw Oops.Oh(ErrorCodeEnum.D2000);
            input.Pid = pRegion.Id;
        }

        var isExist = await _sysRegionRep.IsAnyAsync(u => u.Name == input.Name && u.Code == input.Code);
        if (isExist) throw Oops.Oh(ErrorCodeEnum.R2002);

        var sysRegion = input.Adapt<SysRegion>();
        var newRegion = await _sysRegionRep.AsInsertable(sysRegion).ExecuteReturnEntityAsync();
        return newRegion.Id;
    }

    /// <summary>
    /// 更新行政区域 🔖
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    [ApiDescriptionSettings(Name = "Update"), HttpPost]
    [DisplayName("更新行政区域")]
    public async Task UpdateRegion(UpdateRegionInput input)
    {
        input.Code = input.Code?.Trim() ?? "";
        if (input.Code.Length != 12 && input.Code.Length != 9 && input.Code.Length != 6) throw Oops.Oh(ErrorCodeEnum.R2003);

        var sysRegion = await _sysRegionRep.GetFirstAsync(u => u.Id == input.Id);
        if (sysRegion == null) throw Oops.Oh(ErrorCodeEnum.D1002);

        if (sysRegion.Pid != input.Pid && input.Pid != 0)
        {
            var pRegion = await _sysRegionRep.GetFirstAsync(u => u.Id == input.Pid);
            pRegion ??= await _sysRegionRep.GetFirstAsync(u => u.Code == input.Pid.ToString());
            if (pRegion == null) throw Oops.Oh(ErrorCodeEnum.D2000);

            input.Pid = pRegion.Id;
            var regionTreeList = await _sysRegionRep.AsQueryable().ToChildListAsync(u => u.Pid, input.Id, true);
            var childIdList = regionTreeList.Select(u => u.Id).ToList();
            if (childIdList.Contains(input.Pid)) throw Oops.Oh(ErrorCodeEnum.R2004);
        }

        if (input.Id == input.Pid) throw Oops.Oh(ErrorCodeEnum.R2001);

        var isExist = await _sysRegionRep.IsAnyAsync(u => (u.Name == input.Name && u.Code == input.Code) && u.Id != sysRegion.Id);
        if (isExist) throw Oops.Oh(ErrorCodeEnum.R2002);

        //// 父Id不能为自己的子节点
        //var regionTreeList = await _sysRegionRep.AsQueryable().ToChildListAsync(u => u.Pid, input.Id, true);
        //var childIdList = regionTreeList.Select(u => u.Id).ToList();
        //if (childIdList.Contains(input.Pid))
        //    throw Oops.Oh(ErrorCodeEnum.R2001);

        await _sysRegionRep.AsUpdateable(input.Adapt<SysRegion>()).IgnoreColumns(true).ExecuteCommandAsync();
    }

    /// <summary>
    /// 删除行政区域 🔖
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    [ApiDescriptionSettings(Name = "Delete"), HttpPost]
    [DisplayName("删除行政区域")]
    public async Task DeleteRegion(DeleteRegionInput input)
    {
        var regionTreeList = await _sysRegionRep.AsQueryable().ToChildListAsync(u => u.Pid, input.Id, true);
        var regionIdList = regionTreeList.Select(u => u.Id).ToList();
        await _sysRegionRep.DeleteAsync(u => regionIdList.Contains(u.Id));
    }

    /// <summary>
    /// 同步行政区域 🔖
    /// </summary>
    /// <returns></returns>
    [DisplayName("同步行政区域")]
    public async Task Sync()
    {
        var syncLevel = await _sysConfigService.GetConfigValue<int>(ConfigConst.SysRegionSyncLevel);
        if (syncLevel is < 1 or > 5) syncLevel = 3;//默认区县级

        await _sysRegionRep.AsTenant().UseTranAsync(async () =>
        {
            await _sysRegionRep.DeleteAsync(u => u.Id > 0);
            await SyncByMap(syncLevel);
        }, err =>
        {
            throw Oops.Oh(ErrorCodeEnum.R2005, err.Message);
        });

        // var context = BrowsingContext.New(AngleSharp.Configuration.Default.WithDefaultLoader());
        // var dom = await context.OpenAsync(_url);
        //
        // // 省级列表
        // var itemList = dom.QuerySelectorAll("table.provincetable tr.provincetr td a");
        // if (itemList.Length == 0) throw Oops.Oh(ErrorCodeEnum.R2005);
        //
        // await _sysRegionRep.DeleteAsync(u => u.Id > 0);
        //
        // foreach (var element in itemList)
        // {
        //     var item = (IHtmlAnchorElement)element;
        //     var list = new List<SysRegion>();
        //
        //     var region = new SysRegion
        //     {
        //         Id = YitIdHelper.NextId(),
        //         Pid = 0,
        //         Name = item.TextContent,
        //         Remark = item.Href,
        //         Level = 1,
        //     };
        //     list.Add(region);
        //
        //     // 市级
        //     if (!string.IsNullOrEmpty(item.Href))
        //     {
        //         var dom1 = await context.OpenAsync(item.Href);
        //         var itemList1 = dom1.QuerySelectorAll("table.citytable tr.citytr td a");
        //         for (var i1 = 0; i1 < itemList1.Length; i1 += 2)
        //         {
        //             var item1 = (IHtmlAnchorElement)itemList1[i1 + 1];
        //             var region1 = new SysRegion
        //             {
        //                 Id = YitIdHelper.NextId(),
        //                 Pid = region.Id,
        //                 Name = item1.TextContent,
        //                 Code = itemList1[i1].TextContent,
        //                 Remark = item1.Href,
        //                 Level = 2,
        //             };
        //
        //             // 若URL中查询的一级行政区域缺少Code则通过二级区域填充
        //             if (list.Count == 1 && !string.IsNullOrEmpty(region1.Code))
        //                 region.Code = region1.Code.Substring(0, 2).PadRight(region1.Code.Length, '0');
        //
        //             // 同步层级为“1-省级”退出
        //             if (syncLevel < 2) break;
        //
        //             list.Add(region1);
        //
        //             // 区县级
        //             if (string.IsNullOrEmpty(item1.Href) || syncLevel <= 2) continue;
        //
        //             var dom2 = await context.OpenAsync(item1.Href);
        //             var itemList2 = dom2.QuerySelectorAll("table.countytable tr.countytr td a");
        //             for (var i2 = 0; i2 < itemList2.Length; i2 += 2)
        //             {
        //                 var item2 = (IHtmlAnchorElement)itemList2[i2 + 1];
        //                 var region2 = new SysRegion
        //                 {
        //                     Id = YitIdHelper.NextId(),
        //                     Pid = region1.Id,
        //                     Name = item2.TextContent,
        //                     Code = itemList2[i2].TextContent,
        //                     Remark = item2.Href,
        //                     Level = 3,
        //                 };
        //                 list.Add(region2);
        //
        //                 // 街道级
        //                 if (string.IsNullOrEmpty(item2.Href) || syncLevel <= 3) continue;
        //
        //                 var dom3 = await context.OpenAsync(item2.Href);
        //                 var itemList3 = dom3.QuerySelectorAll("table.towntable tr.towntr td a");
        //                 for (var i3 = 0; i3 < itemList3.Length; i3 += 2)
        //                 {
        //                     var item3 = (IHtmlAnchorElement)itemList3[i3 + 1];
        //                     var region3 = new SysRegion
        //                     {
        //                         Id = YitIdHelper.NextId(),
        //                         Pid = region2.Id,
        //                         Name = item3.TextContent,
        //                         Code = itemList3[i3].TextContent,
        //                         Remark = item3.Href,
        //                         Level = 4,
        //                     };
        //                     list.Add(region3);
        //
        //                     // 村级
        //                     if (string.IsNullOrEmpty(item3.Href) || syncLevel <= 4) continue;
        //
        //                     var dom4 = await context.OpenAsync(item3.Href);
        //                     var itemList4 = dom4.QuerySelectorAll("table.villagetable tr.villagetr td");
        //                     for (var i4 = 0; i4 < itemList4.Length; i4 += 3)
        //                     {
        //                         list.Add(new SysRegion
        //                         {
        //                             Id = YitIdHelper.NextId(),
        //                             Pid = region3.Id,
        //                             Name = itemList4[i4 + 2].TextContent,
        //                             Code = itemList4[i4].TextContent,
        //                             CityCode = itemList4[i4 + 1].TextContent,
        //                             Level = 5,
        //                         });
        //                     }
        //                 }
        //             }
        //         }
        //     }
        //
        //     //按省份同步快速写入提升同步效率，全部一次性写入容易出现从统计局获取数据失败
        //     await _sysRegionRep.Context.Fastest<SysRegion>().BulkCopyAsync(list);
        // }
    }

    /// <summary>
    /// 从统计局地图页面同步
    /// </summary>
    /// <param name="syncLevel"></param>
    private async Task SyncByMap(int syncLevel)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Referer", "http://xzqh.mca.gov.cn/map");
        var html = await client.GetStringAsync("http://xzqh.mca.gov.cn/map");

        var municipalityList = new List<string> { "北京", "天津", "上海", "重庆" };
        var provList = Regex.Match(html, @"(?<=var json = )(\[\{.*?\}\])(?=;)").Value.ToJsonEntity<List<Dictionary<string, string>>>();
        foreach (var dict1 in provList)
        {
            var list = new List<SysRegion>();
            var provName = dict1.GetValueOrDefault("shengji");
            var province = new SysRegion
            {
                Id = YitIdHelper.NextId(),
                Name = Regex.Replace(provName, "[(（].*?[）)]", ""),
                Code = dict1.GetValueOrDefault("quHuaDaiMa"),
                CityCode = dict1.GetValueOrDefault("quhao"),
                Level = 1,
                Pid = 0,
            };
            list.Add(province);

            if (syncLevel <= 1) continue;

            var prefList = await GetSelectList(provName);
            foreach (var dict2 in prefList)
            {
                var prefName = dict2.GetValueOrDefault("diji");
                var city = new SysRegion
                {
                    Id = YitIdHelper.NextId(),
                    Code = dict2.GetValueOrDefault("quHuaDaiMa"),
                    CityCode = dict2.GetValueOrDefault("quhao"),
                    Pid = province.Id,
                    Name = prefName,
                    Level = 2
                };
                if (municipalityList.Any(m => city.Name.StartsWith(m)))
                {
                    city.Name = "市辖区";
                    if (province.Code == city.Code) city.Code = province.Code.Substring(0, 2) + "0100";
                }
                list.Add(city);

                if (syncLevel <= 2) continue;

                var countyList = await GetSelectList(provName, prefName);
                foreach (var dict3 in countyList)
                {
                    var countyName = dict3.GetValueOrDefault("xianji");
                    var county = new SysRegion
                    {
                        Id = YitIdHelper.NextId(),
                        Code = dict3.GetValueOrDefault("quHuaDaiMa"),
                        CityCode = dict3.GetValueOrDefault("quhao"),
                        Name = countyName,
                        Pid = city.Id,
                        Level = 3
                    };
                    if (city.Code.IsNullOrEmpty())
                    {
                        // 省直辖县级行政单位 节点无Code编码处理
                        city.Code = county.Code.Substring(0, 3).PadRight(6, '0');
                    }
                    list.Add(county);
                }
            }

            // 按省份同步快速写入提升同步效率，全部一次性写入容易出现从统计局获取数据失败
            // 仅当数据量大于1000或非Oracle数据库时采用大数据量写入方式（SqlSugar官方已说明，数据量小于1000时，其性能不如普通插入, oracle此方法不支持事务）
            if (list.Count > 1000 && _sysRegionRep.Context.CurrentConnectionConfig.DbType != SqlSugar.DbType.Oracle)
            {
                // 执行大数据量写入
                try
                {
                    await _sysRegionRep.Context.Fastest<SysRegion>().BulkCopyAsync(list);
                }
                catch (SqlSugarException)
                {
                    // 若写入失败则尝试普通插入方式
                    await _sysRegionRep.InsertRangeAsync(list);
                }
            }
            else
            {
                await _sysRegionRep.InsertRangeAsync(list);
            }
        }

        // 获取选择数据
        async Task<List<Dictionary<string, string>>> GetSelectList(string prov, string prefecture = null)
        {
            var data = "";
            if (!string.IsNullOrWhiteSpace(prov)) data += $"shengji={prov}";
            if (!string.IsNullOrWhiteSpace(prefecture)) data += $"&diji={prefecture}";
            var json = await client.PostFormAsync("http://xzqh.mca.gov.cn/selectJson", data);
            return json.ToJsonEntity<List<Dictionary<string, string>>>();
        }
    }
}