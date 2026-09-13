# 本地安全编译说明

这个 fork 的启动器不会在运行时从 GitHub、Gitee 或其他地址下载 `main.js`。

仓库根目录的 `main.js` 会在 **编译阶段**作为 Embedded Resource 写入 `Leigod_Auto_Pause.exe`。如果你修改或同步了 `main.js`，必须重新编译启动器后才会生效。

## 1. 环境要求

- Windows 10 / 11 x64
- .NET 8 SDK
- Git

确认 SDK：

```powershell
dotnet --version
```

## 2. Clone

```powershell
git clone https://github.com/DarkKnight24/Leigod_Auto_Pause.git
cd Leigod_Auto_Pause
```

## 3. 普通编译

```powershell
dotnet restore .\src\Leigod_Auto_Pause\Leigod_Auto_Pause.csproj
dotnet build .\src\Leigod_Auto_Pause\Leigod_Auto_Pause.csproj -c Release
```

输出通常位于：

```text
src\Leigod_Auto_Pause\bin\Release\net8.0\
```

## 4. 推荐：生成单文件、自包含的 Windows x64 版本

直接运行仓库根目录的：

```powershell
.\build.ps1
```

或者手动执行：

```powershell
dotnet publish .\src\Leigod_Auto_Pause\Leigod_Auto_Pause.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o .\publish
```

生成文件位于：

```text
publish\
```

## 5. 使用

把发布后的 `Leigod_Auto_Pause.exe` 放到雷神加速器安装目录，也就是能够看到：

```text
leigod_launcher.exe
resources\app.asar
```

的目录中，然后以管理员权限运行。

首次运行时启动器会：

1. 读取 EXE 内置的 `main.js`；
2. 备份当前 `resources\app.asar` 为 `app.asar.bak`；
3. 在临时目录解包并注入插件；
4. 先生成 `app.patched.tmp.asar`；
5. 只有打包成功后才覆盖正式 `app.asar`；
6. 保存已注入文件与插件的 SHA-256，用于后续判断是否需要重新打补丁。

## 6. 备份策略

- 首次安装：创建 `app.asar.bak`。
- 仅本 fork 的 `main.js` 发生变化：不会用已打补丁的文件覆盖备份。
- 雷神自身升级、导致 `app.asar` 改变：旧的 `app.asar.bak` 会先保存成 `app.asar.bak.previous`，然后当前版本成为新的 `app.asar.bak`。
- 如果本地状态配置丢失但已经存在 `.bak`：默认保留现有备份，不贸然覆盖。

## 7. 与上游版本的关键安全差异

上游启动器运行时会从 Gitee/GitHub `main` 分支拉取最新 `main.js`。本 fork 删除了该行为：

```text
运行 EXE -> 读取 EXE 内置 main.js -> 注入 app.asar
```

因此运行时不会因为上游仓库内容突然变化而自动执行新的 JS。

编译产物在读取内置 JS 时还会移除原插件日志中 `account_token` 前缀片段的输出；token 本身仍只用于原有的雷神官方暂停接口逻辑。

> 注意：插件自身为了实现功能，仍会访问雷神官方 API；这里移除的是“启动器从第三方代码仓库动态下载并执行插件代码”的供应链风险。
