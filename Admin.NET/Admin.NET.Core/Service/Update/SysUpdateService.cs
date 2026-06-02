// Admin.NET 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//
// 本项目主要遵循 MIT 许可证和 Apache 许可证（版本 2.0）进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 和 LICENSE-APACHE 文件。
//
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目二次开发而产生的一切法律纠纷和责任，我们不承担任何责任！

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

namespace Admin.NET.Core.Service;

/// <summary>
/// 系统更新管理服务 🧩
/// </summary>
[ApiDescriptionSettings(Order = 390)]
public class SysUpdateService : IDynamicApiController, ITransient
{
    private readonly SysCacheService _sysCacheService;
    private readonly CDConfigOptions _cdConfigOptions;

    public SysUpdateService(IOptions<CDConfigOptions> giteeOptions, SysCacheService sysCacheService)
    {
        _cdConfigOptions = giteeOptions.Value;
        _sysCacheService = sysCacheService;
    }

    /// <summary>
    /// 备份列表
    /// </summary>
    /// <returns></returns>
    [DisplayName("备份列表")]
    [ApiDescriptionSettings(Name = "List"), HttpPost]
    public Task<List<BackupOutput>> List()
    {
        const string backendDir = "Admin.NET";
        var rootPath = Path.GetFullPath(Path.Combine(_cdConfigOptions.BackendOutput, ".."));
        return Task.FromResult(Directory.GetFiles(rootPath, backendDir + "*.zip", SearchOption.TopDirectoryOnly)
            .Select(filePath =>
            {
                var file = new FileInfo(filePath);
                return new BackupOutput
                {
                    CreateTime = file.CreationTime,
                    FilePath = filePath,
                    FileName = file.Name
                };
            })
            .OrderByDescending(u => u.CreateTime)
            .ToList());
    }

    /// <summary>
    /// 还原
    /// </summary>
    /// <returns></returns>
    [DisplayName("还原")]
    [ApiDescriptionSettings(Name = "Restore"), HttpPost]
    public async Task Restore(RestoreInput input)
    {
        // 检查参数
        CheckConfig();
        try
        {
            var file = (await List()).FirstOrDefault(u => u.FileName.EqualIgnoreCase(input.FileName));
            if (file == null)
            {
                PrintfLog("文件不存在...");
                return;
            }

            PrintfLog("正在还原...");
            using ZipArchive archive = new(File.OpenRead(file.FilePath), ZipArchiveMode.Read, leaveOpen: false);
            archive.ExtractToDirectory(_cdConfigOptions.BackendOutput, true);
            PrintfLog("还原成功...");
        }
        catch (Exception ex)
        {
            PrintfLog("发生异常：" + ex.Message);
            throw;
        }
    }

    /// <summary>
    /// 从远端更新系统
    /// </summary>
    /// <returns></returns>
    [DisplayName("系统更新")]
    [ApiDescriptionSettings(Name = "Update"), HttpPost]
    public async Task Update()
    {
        var originColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"【{DateTime.Now}】从远端仓库部署项目");
        try
        {
            PrintfLog("----------------------------从远端仓库部署项目-开始----------------------------");

            // 检查参数
            CheckConfig();

            // 检查操作间隔
            if (_cdConfigOptions.UpdateInterval > 0)
            {
                if (_sysCacheService.Get<bool>(CacheConst.KeySysUpdateInterval)) throw Oops.Oh("请勿频繁操作");
                _sysCacheService.Set(CacheConst.KeySysUpdateInterval, true, TimeSpan.FromMinutes(_cdConfigOptions.UpdateInterval));
            }

            PrintfLog($"客户端host：{App.HttpContext.Request.Host}");
            PrintfLog($"客户端IP：{App.HttpContext.GetRemoteIpAddressToIPv4(true)}");
            PrintfLog($"仓库地址：https://gitee.com/{_cdConfigOptions.Owner}/{_cdConfigOptions.Repo}.git");
            PrintfLog($"仓库分支：{_cdConfigOptions.Branch}");

            // 获取解压后的根目录
            var rootPath = Path.GetFullPath(Path.Combine(_cdConfigOptions.BackendOutput, ".."));
            var tempDir = Path.Combine(rootPath, $"{_cdConfigOptions.Repo}-{_cdConfigOptions.Branch}");

            PrintfLog("清理旧文件...");
            FileHelper.TryDelete(tempDir);

            PrintfLog("拉取远端代码...");
            var stream = await GiteeHelper.DownloadRepoZip(_cdConfigOptions.Owner, _cdConfigOptions.Repo,
                _cdConfigOptions.AccessToken, _cdConfigOptions.Branch);

            PrintfLog("文件包解压...");
            using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: false);
            archive.ExtractToDirectory(rootPath);

            // 项目目录
            var backendDir = "Admin.NET"; // 后端根目录
            var entryProjectName = "Admin.NET.Web.Entry"; // 启动项目目录
            var tempOutput = Path.Combine(rootPath, $"{_cdConfigOptions.Repo}_temp");

            PrintfLog("编译项目...");
            PrintfLog($"发布版本：{_cdConfigOptions.Publish.Configuration}");
            PrintfLog($"目标框架：{_cdConfigOptions.Publish.TargetFramework}");
            PrintfLog($"运行环境：{_cdConfigOptions.Publish.RuntimeIdentifier}");
            var option = _cdConfigOptions.Publish;
            var adminNetDir = Path.Combine(tempDir, backendDir);
            var args = $"publish \"{entryProjectName}\" -c {option.Configuration} -f {option.TargetFramework} -r {option.RuntimeIdentifier} --output \"{tempOutput}\"";
            await RunCommandAsync("dotnet", args, adminNetDir);

            PrintfLog("复制 wwwroot 目录...");
            var wwwrootDir = Path.Combine(adminNetDir, entryProjectName, "wwwroot");
            FileHelper.CopyDirectory(wwwrootDir, Path.Combine(tempOutput, "wwwroot"), true);

            // 删除排除文件
            foreach (var filePath in (_cdConfigOptions.ExcludeFiles ?? new()).SelectMany(file => Directory.GetFiles(tempOutput, file, SearchOption.TopDirectoryOnly)))
            {
                PrintfLog($"排除文件：{filePath}");
                FileHelper.TryDelete(filePath);
            }

            PrintfLog("备份原项目文件...");
            string backupPath = Path.Combine(rootPath, $"{_cdConfigOptions.Repo}_{DateTime.Now:yyyy_MM_dd}.zip");
            if (File.Exists(backupPath)) File.Delete(backupPath);
            ZipFile.CreateFromDirectory(_cdConfigOptions.BackendOutput, backupPath);

            // 将临时文件移动到正式目录
            FileHelper.CopyDirectory(tempOutput, _cdConfigOptions.BackendOutput, true);

            PrintfLog("清理文件...");
            FileHelper.TryDelete(tempOutput);
            FileHelper.TryDelete(tempDir);

            if (_cdConfigOptions.BackupCount > 0)
            {
                var fileList = await List();
                if (fileList.Count > _cdConfigOptions.BackupCount)
                    PrintfLog("清除多余的备份文件...");
                while (fileList.Count > _cdConfigOptions.BackupCount)
                {
                    var last = fileList.Last();
                    FileHelper.TryDelete(last.FilePath);
                    fileList.Remove(last);
                }
            }

            PrintfLog("重启项目后生效...");
        }
        catch (Exception ex)
        {
            PrintfLog("发生异常：" + ex.Message);
            throw;
        }
        finally
        {
            PrintfLog("----------------------------从远端仓库部署项目-结束----------------------------");
            Console.ForegroundColor = originColor;
        }
    }

    /// <summary>
    /// 仓库WebHook接口
    /// </summary>
    /// <returns></returns>
    [AllowAnonymous]
    [DisplayName("仓库WebHook接口")]
    [ApiDescriptionSettings(Name = "WebHook"), HttpPost]
    public async Task WebHook(Dictionary<string, object> input)
    {
        if (!_cdConfigOptions.Enabled) throw Oops.Oh("未启用持续部署功能");
        PrintfLog("----------------------------收到WebHook请求-开始----------------------------");

        try
        {
            // 获取请求头信息
            var even = App.HttpContext.Request.Headers.FirstOrDefault(u => u.Key == "X-Gitee-Event").Value
                .FirstOrDefault();
            var ua = App.HttpContext.Request.Headers.FirstOrDefault(u => u.Key == "User-Agent").Value.FirstOrDefault();

            var timestamp = input.GetValueOrDefault("timestamp")?.ToString();
            var token = input.GetValueOrDefault("sign")?.ToString();
            PrintfLog("User-Agent：" + ua);
            PrintfLog("Gitee-Event：" + even);
            PrintfLog("Gitee-Token：" + token);
            PrintfLog("Gitee-Timestamp：" + timestamp);

            PrintfLog("开始验签...");
            var secret = GetWebHookKey();
            var stringToSign = $"{timestamp}\n{secret}";
            using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var signData = mac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
            var encodedSignData = Convert.ToBase64String(signData);
            var calculatedSignature = WebUtility.UrlEncode(encodedSignData);

            if (calculatedSignature != token) throw Oops.Oh("非法签名");
            PrintfLog("验签成功...");

            var hookName = input.GetValueOrDefault("hook_name") as string;
            PrintfLog("Hook-Name：" + hookName);

            switch (hookName)
            {
                // 提交修改
                case "push_hooks":
                    {
                        var commitList = input.GetValueOrDefault("commits")?.Adapt<List<Dictionary<string, object>>>() ?? new();
                        foreach (var commit in commitList)
                        {
                            var author = commit.GetValueOrDefault("author")?.Adapt<Dictionary<string, object>>();
                            PrintfLog("Commit-Message：" + commit.GetValueOrDefault("message"));
                            PrintfLog("Commit-Time：" + commit.GetValueOrDefault("timestamp"));
                            PrintfLog("Commit-Author：" + author?.GetValueOrDefault("username"));
                            PrintfLog("Modified-List：" + author?.GetValueOrDefault("modified")?.Adapt<List<string>>().Join());
                            PrintfLog("----------------------------------------------------------");
                        }

                        break;
                    }
                // 合并 Pull Request
                case "merge_request_hooks":
                    {
                        var pull = input.GetValueOrDefault("pull_request")?.Adapt<Dictionary<string, object>>();
                        var user = pull?.GetValueOrDefault("user")?.Adapt<Dictionary<string, object>>();
                        PrintfLog("Pull-Request-Title：" + pull?.GetValueOrDefault("message"));
                        PrintfLog("Pull-Request-Time：" + pull?.GetValueOrDefault("created_at"));
                        PrintfLog("Pull-Request-Author：" + user?.GetValueOrDefault("username"));
                        PrintfLog("Pull-Request-Body：" + pull?.GetValueOrDefault("body"));
                        break;
                    }
                // 新的issue
                case "issue_hooks":
                    {
                        var issue = input.GetValueOrDefault("issue")?.Adapt<Dictionary<string, object>>();
                        var user = issue?.GetValueOrDefault("user")?.Adapt<Dictionary<string, object>>();
                        var labelList = issue?.GetValueOrDefault("labels")?.Adapt<List<Dictionary<string, object>>>();
                        PrintfLog("Issue-UserName：" + user?.GetValueOrDefault("username"));
                        PrintfLog("Issue-Labels：" + labelList?.Select(u => u.GetValueOrDefault("name")).Join());
                        PrintfLog("Issue-Title：" + issue?.GetValueOrDefault("title"));
                        PrintfLog("Issue-Time：" + issue?.GetValueOrDefault("created_at"));
                        PrintfLog("Issue-Body：" + issue?.GetValueOrDefault("body"));
                        return;
                    }
                // 评论
                case "note_hooks":
                    {
                        var comment = input.GetValueOrDefault("comment")?.Adapt<Dictionary<string, object>>();
                        var user = input.GetValueOrDefault("user")?.Adapt<Dictionary<string, object>>();
                        PrintfLog("comment-UserName：" + user?.GetValueOrDefault("username"));
                        PrintfLog("comment-Time：" + comment?.GetValueOrDefault("created_at"));
                        PrintfLog("comment-Content：" + comment?.GetValueOrDefault("body"));
                        return;
                    }
                default:
                    return;
            }

            var updateInterval = _cdConfigOptions.UpdateInterval;
            try
            {
                _cdConfigOptions.UpdateInterval = 0;
                await Update();
            }
            finally
            {
                _cdConfigOptions.UpdateInterval = updateInterval;
            }
        }
        finally
        {
            PrintfLog("----------------------------收到WebHook请求-结束----------------------------");
        }
    }

    /// <summary>
    /// 获取WebHook接口密钥
    /// </summary>
    /// <returns></returns>
    [DisplayName("获取WebHook接口密钥")]
    [ApiDescriptionSettings(Name = "WebHookKey"), HttpGet]
    public string GetWebHookKey()
    {
        return CryptogramUtil.Encrypt(_cdConfigOptions.AccessToken);
    }

    /// <summary>
    /// 获取日志列表
    /// </summary>
    /// <returns></returns>
    [DisplayName("获取日志列表")]
    [ApiDescriptionSettings(Name = "Logs"), HttpGet]
    public List<string> LogList()
    {
        return _sysCacheService.Get<List<string>>(CacheConst.KeySysUpdateLog) ?? new();
    }

    /// <summary>
    /// 清空日志
    /// </summary>
    /// <returns></returns>
    [DisplayName("清空日志")]
    [ApiDescriptionSettings(Name = "Clear"), HttpGet]
    public void ClearLog()
    {
        _sysCacheService.Remove(CacheConst.KeySysUpdateLog);
    }

    /// <summary>
    /// 检查参数
    /// </summary>
    /// <returns></returns>
    private void CheckConfig()
    {
        PrintfLog("检查CD配置参数...");

        if (_cdConfigOptions == null) throw Oops.Oh("CDConfig配置不能为空");

        if (string.IsNullOrWhiteSpace(_cdConfigOptions.Owner)) throw Oops.Oh("仓库用户名不能为空");

        if (string.IsNullOrWhiteSpace(_cdConfigOptions.Repo)) throw Oops.Oh("仓库名不能为空");

        // if (string.IsNullOrWhiteSpace(_cdConfigOptions.Branch)) throw Oops.Oh("分支名不能为空");

        if (string.IsNullOrWhiteSpace(_cdConfigOptions.AccessToken)) throw Oops.Oh("授权信息不能为空");

        if (string.IsNullOrWhiteSpace(_cdConfigOptions.BackendOutput)) throw Oops.Oh("部署目录不能为空");

        if (_cdConfigOptions.Publish == null) throw Oops.Oh("编译配置不能为空");

        if (string.IsNullOrWhiteSpace(_cdConfigOptions.Publish.Configuration)) throw Oops.Oh("运行环境编译配置不能为空");

        if (string.IsNullOrWhiteSpace(_cdConfigOptions.Publish.TargetFramework)) throw Oops.Oh(".NET版本编译配置不能为空");

        if (string.IsNullOrWhiteSpace(_cdConfigOptions.Publish.RuntimeIdentifier)) throw Oops.Oh("运行平台配置不能为空");
    }

    /// <summary>
    /// 打印日志
    /// </summary>
    /// <param name="message"></param>
    private void PrintfLog(string message)
    {
        var logList = _sysCacheService.Get<List<string>>(CacheConst.KeySysUpdateLog) ?? new();

        var content = $"【{DateTime.Now}】 {message}";

        Console.WriteLine(content);

        logList.Add(content);

        _sysCacheService.Set(CacheConst.KeySysUpdateLog, logList);
    }

    /// <summary>
    /// 执行命令
    /// </summary>
    /// <param name="command">命令</param>
    /// <param name="arguments">参数</param>
    /// <param name="workingDirectory">工作目录</param>
    private async Task RunCommandAsync(string command, string arguments, string workingDirectory)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process();
        process.StartInfo = processStartInfo;
        process.Start();

        while (!process.StandardOutput.EndOfStream)
        {
            string line = await process.StandardOutput.ReadLineAsync();
            if (string.IsNullOrEmpty(line)) continue;
            PrintfLog(line.Trim());
        }
        await process.WaitForExitAsync();
    }
}