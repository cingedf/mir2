# 20261117修复PixelDetect父容器按钮点击穿透

## 现象

用户反馈：窗口模式（1024×768）下，game 界面右下角的那些按钮（背包/角色/技能/任务/设置等）鼠标点击**不生效**，点下去反而让**游戏人物一直往下走**，相当于按钮无效。此问题在上一批 DPI 修复（commit a8f8682）后依旧存在。

## 排查过程

1. **先排除 DPI/坐标假设**：现场 ini `Mir2Config.ini` [Graphics] 为 `FullScreen=False / Resolution=1024`，无 ScreenWidth 项（逻辑分辨率默认 800×600，仅由 SetResolution 菜单改）；注册表 LogPixels=106（≈110.4% 缩放）。第一批加的 `Application.SetHighDpiMode(DpiUnaware)` 实测返回 true 但实际 HighDpiMode 仍 PerMonitor/DeviceDpi=106（.NET 8 下无效调用）。但窗口模式 `ClientSize == ScreenWidth == 1024×768`、PointToClient 与后缓冲同域，MPoint 本就 1:1 正确——**DPI 与本症状无关**。据此回退了本批一度尝试的方案A（CMain 坐标恒按比例换算，窗口模式为数学恒等、纯无效改动）。

2. **定位"人物往下走"机制**：角色持续向下走 = `MouseControl == MapControl` + `MouseDirection() == Down`（GameScene.cs:12365/12398）。即点击没有命中右下角按钮，事件穿透到了全屏 MapControl（GameScene.cs:2292 `InsertControl(0, ...)` 置底），`MirScene.OnMouseDown` 把点击派发给 MapControl → `MapButtons |= Left` → CheckInput 触发走路。

3. **命中链全量审计（子代理）**：13 处底板按钮的 `IsMouseOver` 判定统一使用 CMain.MPoint（逻辑坐标），按钮逻辑本身正确；问题在**父容器**——MainDialog / ChatDialog / MiniMapDialog 三个容器均为 `PixelDetect = true`（MainDialogs.cs:41/584/1777）。`MirImageControl.IsMouseOver`（MirImageControl.cs:195-198）为 `base.IsMouseOver(p) && (!_pixelDetect || Library.VisiblePixel(...) || Moving)`：PixelDetect 容器必须先靠 `Library.VisiblePixel` 通过，父容器不被命中，命中递归（MirControl.cs:890-896「父不中子不递归」）就整支跳过其所有子按钮。对照：ChatControlBar（MainDialogs.cs:1251，PixelDetect=false）的频道按钮正常——分界线正是父容器 PixelDetect true/false，与用户"那些按钮"（而非全部）失效的描述吻合。

4. **Smoking gun（Client\MirGraphics\MLibrary.cs）**：
   - `MImage.Data` 是裸指针（:935）。
   - commit 58af1ed（20261116 批2 渲染资源泄漏修复）在 `CreateTexture` 末尾 Unlock 后加 `Data = null;`（:979，防 use-after-unlock，本身正确）。
   - 但 `VisiblePixel`（:1022-1045）仍是 `bool result = false;`（:1029）→ `if (Data != null) { ...读 alpha... }`（:1032-1043）→ `return result;`（:1044）。Data 恒 null → **VisiblePixel 对任何已加载纹理永远返回 false**。
   - 58af1ed 注释声称"退化为整图可见"，但代码返回 false = **整图不可见**，退化策略写反。旧代码靠 Unlock 后的悬空指针读 alpha（未定义行为但碰巧可用），58af1ed 把它变成了确定性失败——这是批2 修复引入的回归（regression）。

## 修复内容

文件 `Client\MirGraphics\MLibrary.cs`（5 处，行为等价收敛、不改透明穿透语义）：

1. **字段区 :935 后**新增托管 alpha 缓存 `private byte[] _pickAlpha;`（附 20261117 注释）。
2. **CreateTexture**（GetTrueSize() 后、stream.Data.Dispose() 前）趁 Lock 窗口内 Data 裸指针仍有效，把 BGRA alpha 拷入托管数组保活：`_pickAlpha = new byte[w * h * 4]; System.Runtime.InteropServices.Marshal.Copy((IntPtr)Data, _pickAlpha, 0, _pickAlpha.Length);`（全限定名免加 using；w=Width/h=Height 与 VisiblePixel 索引口径一致，alpha 偏移 `(y<<2)+…+3`）。
3. **VisiblePixel** 整体改为读缓存：`if (_pickAlpha != null) return _pickAlpha[(p.Y * (Width << 2)) + (p.X << 2) + 3] != 0; return true;`——缓存未就绪时安全退化为**整图可见**（true，与 58af1ed 注释意图一致，修正原 result=false 写反的退化策略），恢复透明像素穿透判定的原始语义。
4. **保留 :979 `Data = null;`**（防御 use-after-unlock 是正确的，不回退）。
5. **DisposeTexture** :1019 `Data = null;` 后加 `_pickAlpha = null;`（随纹理一起回收缓存）。

附带：本批一度尝试的 CMain.cs 方案A（坐标恒按 ClientSize 比例换算，属窗口模式数学恒等无效改动）已**完整回退**，CMain.cs 恢复干净。

## 验证证据

- `dotnet build Client\Client.csproj -c Release`：**0 错误**，1 存量 MSB3277 警告（无害）。
- 逻辑复核：
  - 合法纹理：Lock 窗口内 alpha 被完整拷入 _pickAlpha，VisiblePixel 按 BGRA alpha 通道（偏移 +3）判定，与原设计逐像素命中语义等价；
  - 透明区域 alpha=0 → 命中穿透（父容器 PixelDetect 放行递归），不透明区域 alpha≠0 → 命中，子按钮恢复可点；
  - 纹理未就绪（_pickAlpha==null）→ 退化整图可见（true），不再恒 false，避免"整图不可见"回归；
  - Data=null 保留，杜绝 use-after-unlock；缓存为托管数组，随 DisposeTexture 释放，无新泄漏。
- 待用户实测：重启 Client.exe 后，各分辨率/全屏与窗口模式下右下角按钮（背包/角色/技能/任务/设置）、聊天滚动条、小地图按钮均可正常点击，点击不再穿透导致人物乱走；含透明区域的图标命中穿透行为如旧。

## 回滚方法

```bash
cd C:\1.40\mir2-angelk727
git revert <本提交哈希>
# 或
git checkout <本提交前哈希> -- Client/MirGraphics/MLibrary.cs
dotnet build Client\Client.csproj -c Release
```
