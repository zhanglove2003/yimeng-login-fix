# 一梦江湖登录修复

Windows 10/11 x64 的 WPF 桌面工具。程序会检查游戏目录和进程状态，备份目标文件，应用内置修复数据并验证 SHA-256；也可以一键回退最近一次修复。

## 构建

本机 .NET 10 SDK 安装在：

```text
C:\Users\Snow\.dotnet\sdk10\dotnet.exe
```

运行测试并生成便携版：

```powershell
pwsh -NoProfile -File .\Publish.ps1
```

输出位置：

```text
artifacts\portable-single\YiMengFix.exe
```

安装 Inno Setup 6 后生成安装包：

```powershell
pwsh -NoProfile -File .\Publish.ps1 -BuildInstaller
```

## 数据位置

- 设置：`%AppData%\YiMengFix\settings.json`
- 日志：`%LocalAppData%\YiMengFix\logs\`
- 游戏备份：`<游戏目录>\_tmsoft_fix_backup\`

## 安全行为

- 程序始终请求管理员权限；
- 每次替换前会自动备份两个目标文件；
- 界面只显示“回退上次修复”，避免误选旧备份；
- 临时文件通过哈希校验后才替换目标文件；
- 处理中途失败会尝试自动回滚；
- 正式 EXE 不调用或依赖 PowerShell 7。
