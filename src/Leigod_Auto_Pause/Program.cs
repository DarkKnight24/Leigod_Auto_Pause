using AsarSharp;
using SettingManager;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

class Program
{
    private const string FileToReplace = "dist/main/main.js";
    private const string EmbeddedJsResourceName = "Leigod_Auto_Pause.Plugin.main.js";

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    static async Task Main(string[] args)
    {
        try
        {
            if (!IsRunningAsAdmin())
            {
                AllocConsole();
                Console.WriteLine("请以管理员身份运行此程序！");
                Console.WriteLine("请右键点击程序，选择“以管理员身份运行”。");
                Console.ReadKey();
                FreeConsole();
                return;
            }

            string currentDirectory = AppContext.BaseDirectory;
            string asarPath = Path.Combine(currentDirectory, "resources", "app.asar");
            byte[] embeddedJsBytes = GetEmbeddedJsBytes();

            if (NeedUpdate(asarPath, embeddedJsBytes))
            {
                AllocConsole();
                Console.WriteLine("检测到首次运行、雷神客户端更新或本地插件版本变化。");
                Console.WriteLine("正在使用 EXE 内置的 main.js 应用离线补丁...");

                bool patchSuccess = await ApplyPatchAsync(asarPath, embeddedJsBytes);
                if (patchSuccess)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("离线补丁应用成功，正在启动雷神加速器...");
                    Console.ResetColor();
                    LaunchLeigod(currentDirectory);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("补丁应用失败，按任意键退出。");
                    Console.ResetColor();
                    Console.ReadKey();
                }

                FreeConsole();
            }
            else
            {
                LaunchLeigod(currentDirectory);
            }
        }
        catch (Exception ex)
        {
            string errorMessage = $"程序运行时发生未知错误：\n\n{ex.Message}";
            MessageBox(IntPtr.Zero, errorMessage, "致命错误", 0x10);
        }
    }

    private static byte[] GetEmbeddedJsBytes()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        using Stream? stream = assembly.GetManifestResourceStream(EmbeddedJsResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"未找到内置插件资源 {EmbeddedJsResourceName}。请确认 main.js 已作为 EmbeddedResource 编译进程序。"
            );
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        // 不再把 account_token 的任何片段写入日志。
        // 这里在构建产物读取内置 JS 时做一次最小化安全处理，避免改变上游插件的其他行为。
        string script = Encoding.UTF8.GetString(memory.ToArray());
        script = Regex.Replace(
            script,
            @"writeLog\(\s*`\[Token\] Successfully obtained token\. The token is \$\{GLOBAL_USER_TOKEN\.substring\(\s*0,\s*10,\s*\)\}\.\.\.`,\s*\);",
            "writeLog(\"[Token] Successfully obtained token.\");",
            RegexOptions.CultureInvariant
        );

        return Encoding.UTF8.GetBytes(script);
    }

    private static bool NeedUpdate(string asarPath, byte[] embeddedJsBytes)
    {
        if (!File.Exists(asarPath))
        {
            throw new FileNotFoundException(
                "未找到 resources\\app.asar，请将本程序放到雷神加速器安装目录中运行。",
                asarPath
            );
        }

        AppSettings? settings = Manager.Load();
        if (settings is null || string.IsNullOrWhiteSpace(settings.PatchedAsarHash))
        {
            return true;
        }

        string currentAsarHash = GetFileSha256(asarPath);
        if (!string.Equals(
                currentAsarHash,
                settings.PatchedAsarHash,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            // app.asar 被雷神升级或被其他程序修改，需要重新打补丁。
            return true;
        }

        string embeddedJsHash = GetBytesSha256(embeddedJsBytes);
        return !string.Equals(
            embeddedJsHash,
            settings.AppliedJsHash,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static async Task<bool> ApplyPatchAsync(string asarPath, byte[] embeddedJsBytes)
    {
        string? tempDir = null;
        string asarDirectory = Path.GetDirectoryName(asarPath) ?? AppContext.BaseDirectory;
        string patchedAsarTempPath = Path.Combine(
            asarDirectory,
            $"{Path.GetFileNameWithoutExtension(asarPath)}.patched.tmp.asar"
        );

        try
        {
            Console.WriteLine("正在检查目标文件...");
            if (!File.Exists(asarPath))
            {
                throw new FileNotFoundException(
                    "未找到 resources\\app.asar，请将本程序放到雷神加速器安装目录中运行。",
                    asarPath
                );
            }

            string currentAsarHash = GetFileSha256(asarPath);
            AppSettings? previousSettings = Manager.Load();
            BackupCurrentAsarIfNeeded(asarPath, currentAsarHash, previousSettings);

            tempDir = Path.Combine(Path.GetTempPath(), "AsarPatcher_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            Console.WriteLine("正在解压 app.asar...");
            var extractor = new AsarExtractor(asarPath, tempDir);
            extractor.Extract();
            extractor.Dispose();

            string fileToReplacePath = Path.Combine(tempDir, FileToReplace);
            string? replaceDirectory = Path.GetDirectoryName(fileToReplacePath);
            if (!string.IsNullOrEmpty(replaceDirectory))
            {
                Directory.CreateDirectory(replaceDirectory);
            }

            Console.WriteLine("正在写入 EXE 内置插件...");
            await File.WriteAllBytesAsync(fileToReplacePath, embeddedJsBytes);

            // 先打包到临时 .asar，成功后再覆盖正式 app.asar，避免打包中途失败破坏客户端。
            if (File.Exists(patchedAsarTempPath))
            {
                File.Delete(patchedAsarTempPath);
            }

            Console.WriteLine("正在重新打包 app.asar...");
            var archiver = new AsarArchiver(tempDir, patchedAsarTempPath);
            archiver.Archive();
            archiver.Dispose();

            if (!File.Exists(patchedAsarTempPath))
            {
                throw new IOException("补丁打包完成后未找到临时 app.asar 文件。未覆盖原文件。");
            }

            File.Copy(patchedAsarTempPath, asarPath, true);

            string newAsarHash = GetFileSha256(asarPath);
            string newJsHash = GetBytesSha256(embeddedJsBytes);

            Manager.Save(new AppSettings
            {
                PatchedAsarHash = newAsarHash,
                AppliedJsHash = newJsHash
            });

            Console.WriteLine($"插件 SHA256: {newJsHash}");
            Console.WriteLine("状态信息更新完毕。");
            return true;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"处理过程中发生错误: {ex.Message}");
            Console.ResetColor();
            return false;
        }
        finally
        {
            if (tempDir is not null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // 临时目录清理失败不影响补丁结果。
                }
            }

            if (File.Exists(patchedAsarTempPath))
            {
                try
                {
                    File.Delete(patchedAsarTempPath);
                }
                catch
                {
                    // 临时文件清理失败不影响补丁结果。
                }
            }
        }
    }

    private static void BackupCurrentAsarIfNeeded(
        string asarPath,
        string currentAsarHash,
        AppSettings? previousSettings
    )
    {
        string backupPath = asarPath + ".bak";
        string previousBackupPath = asarPath + ".bak.previous";

        if (!File.Exists(backupPath))
        {
            File.Copy(asarPath, backupPath, false);
            Console.WriteLine("已创建原始 app.asar 备份: app.asar.bak");
            return;
        }

        if (previousSettings is null || string.IsNullOrWhiteSpace(previousSettings.PatchedAsarHash))
        {
            // 配置丢失时不贸然覆盖已有备份，优先保护现有可恢复副本。
            Console.WriteLine("已存在 app.asar.bak；由于缺少历史状态信息，本次保留现有备份不覆盖。");
            return;
        }

        bool currentFileIsPreviouslyPatched = string.Equals(
            currentAsarHash,
            previousSettings.PatchedAsarHash,
            StringComparison.OrdinalIgnoreCase
        );

        if (currentFileIsPreviouslyPatched)
        {
            // 仅插件版本变化，不刷新备份，避免把已经打过补丁的 app.asar 覆盖到备份里。
            return;
        }

        // 雷神升级/外部修改了 app.asar。先保留上一代备份，再把当前文件作为新的恢复点。
        File.Copy(backupPath, previousBackupPath, true);
        File.Copy(asarPath, backupPath, true);
        Console.WriteLine("检测到 app.asar 已更新：已刷新 app.asar.bak，并保留 app.asar.bak.previous。");
    }

    public static string GetFileSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static string GetBytesSha256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static bool IsRunningAsAdmin()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void LaunchLeigod(string installDirectory)
    {
        try
        {
            string leigodExePath = Path.Combine(installDirectory, "leigod_launcher.exe");
            if (!File.Exists(leigodExePath))
            {
                MessageBox(
                    IntPtr.Zero,
                    $"未找到雷神加速器启动程序：\n\n{leigodExePath}\n\n请确保本启动器位于雷神加速器安装目录。",
                    "启动失败",
                    0x10
                );
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = leigodExePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            string errorMessage = $"启动雷神加速器时发生未知错误：\n\n{ex.Message}";
            MessageBox(IntPtr.Zero, errorMessage, "致命错误", 0x10);
        }
    }
}
