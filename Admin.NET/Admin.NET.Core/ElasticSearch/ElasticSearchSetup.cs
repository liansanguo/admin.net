// Admin.NET 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//
// 本项目主要遵循 MIT 许可证和 Apache 许可证（版本 2.0）进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 和 LICENSE-APACHE 文件。
//
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目二次开发而产生的一切法律纠纷和责任，我们不承担任何责任！

using Elastic.Clients.Elasticsearch;

namespace Admin.NET.Core.ElasticSearch;

/// <summary>
/// ES服务注册
/// </summary>
public static class ElasticSearchSetup
{
    /// <summary>
    /// 注册所有ES客户端（日志+业务）
    /// </summary>
    public static void AddElasticSearchClients(this IServiceCollection services)
    {
        // 1. 创建客户端字典（枚举→客户端实例）
        var clients = new Dictionary<EsClientTypeEnum, ElasticsearchClient>();

        // 2. 注册日志客户端
        var loggingClient = ElasticSearchClientFactory.CreateClient<ElasticSearchOptions>(configPath: "ElasticSearch:Logging");
        if (loggingClient != null)
        {
            clients[EsClientTypeEnum.Logging] = loggingClient;
        }

        // 3. 注册业务客户端
        var businessClient = ElasticSearchClientFactory.CreateClient<ElasticSearchOptions>(configPath: "ElasticSearch:Business");
        if (businessClient != null)
        {
            clients[EsClientTypeEnum.Business] = businessClient;
        }

        // 4. 将客户端容器注册为单例（全局唯一）
        services.AddSingleton(new ElasticSearchClientContainer(clients));
    }
}