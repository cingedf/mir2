# 20261114 全局字体升级 Microsoft YaHei

## 现象

- 客户端全部 UI 文字（标签/按钮/聊天/背包等约 100 处 `new Font(...)`）统一走 `Settings.FontName`（Client\Settings.cs L74），默认值为 `"Arial"`，可被 Mir2Config.ini `[Game] FontName` 覆盖。
- 部署配置 deploy\Client\Mir2Config.ini L37 `FontName=Arial`。
- Arial 无 CJK 字形，GDI 按"字体回退链"逐字符回退到宋体点阵渲染中文 → 中文显示为发虚点阵小字，是界面"廉价感"的直接来源（观感问题与 P1 UI 换皮同源，故作为 P1.1 先行处理）。

## 排查过程

- 全仓 grep `new Font(`：100 处全部传 `Settings.FontName`（另有 StuntDialog L35 已硬编码 `"微软雅黑"` 11F 的先例），无散落的硬编码字体。
- `Settings.FontName` 定义于 Client\Settings.cs L74（namespace Client，internal class Settings）；全仓共三个 Settings 类（Client/Server/AutoPatcherAdmin），互不引用，改 Client 侧不影响服务端。
- 配置链：`Mir2Config.ini [Game] FontName` → Settings.Load 时覆盖默认值；FontSize 默认 8F。
- 结论：改默认值 + 改部署 ini 两处即可全局生效；字号/布局不动，规避所有布局回归风险。

## 修复内容

1. `Client\Settings.cs` L74：`FontName = "Arial"` → `FontName = "Microsoft YaHei"`（选用双语族名而非"微软雅黑"，英文系统与中文系统均可解析）。
2. `C:\1.40\mir2-deploy\Client\Mir2Config.ini` L37：`FontName=Arial` → `FontName=Microsoft YaHei`（存量环境立即生效）。

构建部署：`dotnet build Client.csproj -c Release`（BaseOutputPath=..\Build\Client\，部署区跑 Release 产物，构建后手动 Copy-Item 同步 Client.exe/Client.dll/Client.pdb/Shared.dll/Shared.pdb 到 deploy\Client\，不覆盖第三方 dll）。

## 验证证据

- `dotnet build Client.csproj -c Release`：0 错误。
- 二进制取证（System.Reflection.Metadata 枚举 #US 堆，临时工具 usdump）：
  - Release Client.dll 含 `"Microsoft YaHei"` 字面量，堆内顺序 QuestTracking.ini → Microsoft YaHei → 127.0.0.1 与 Settings.cs 字段声明序吻合；
  - 同步后 deploy\Client\Client.dll 同样含该字面量（1461 条 user strings 中确认）。
- 教训记录：PowerShell 内嵌 `[Text.Encoding]::Unicode.GetString(bytes).Contains('Microsoft YaHei')` 出现假阴性（命令文本经 shell 转码破坏匹配串），二进制字面量验证应以专用取证工具为准。
- 实机验证：启动 deploy\Client\Client.exe 登录后观察 UI 中文/英文文字渲染（雅黑平滑字形、无点阵发虚）。

## 回滚方法

- `Client\Settings.cs` L74 改回 `"Arial"`、`deploy\Client\Mir2Config.ini` 改回 `FontName=Arial`，重新构建 Release 并同步部署区即可；或 `git revert` 本次提交（源码侧）。

## 备注

- deploy\Client\Error.txt（当晚 19:44）为用户运行日志，与本次改动无关，后续排查 UI 阶段如需可查。
