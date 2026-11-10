# Library 工具说明

本目录包含仓库自带的两个 **WIL 图库（.wil/.wzl 资源包）GUI 工具**，均为解决方案（Legend of Mir.sln）成员项目，属于官方源码的一部分：

| 工具 | 用途 | 说明 |
| --- | --- | --- |
| **LibraryEditor** | WIL/WZL 图库**编辑器** | 新建/打开/保存/另存图库，V1↔V2 格式转换、复制到其他图库、添加图片（依赖 CustomFormControl、ManagedSquish、Ionic.Zlib） |
| **LibraryViewer** | WIL 图库**浏览器** | 打开图库后以缩略图列表预览、统计图片数量（只读查看） |

## 编译与输出

- 编译单个工具：`dotnet build LibraryEditor/LibraryEditor.csproj -c Release`（产物在 `Build\Server Tools\LibraryEditor\`，Viewer 同理）。
- 两者历史上未随服务端日常构建编译（`Build\` 已被 .gitignore 忽略），需要修改游戏贴图资源时才用到。

## 归置说明

- **保持原位**：两个 csproj 使用 `..\Components\`、`..\Controls\`、`..\Shared\` 相对路径引用，且在 sln 中注册——移动目录需同步修改相对引用与 sln，收益低、风险高，故不迁移到 `tools\`。
- `tools\` 下的四个工具（GameShopMigrate / NPCFlagFix / RespawnFix / SpawnProbe）是本仓库运维期新增的**无头命令行工具**，与这两个 GUI 工具定位不同，勿混淆。
