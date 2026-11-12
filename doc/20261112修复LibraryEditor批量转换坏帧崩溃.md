# 20261112 修复 LibraryEditor 批量转换 .wil 坏帧崩溃

## 一、现象

用户使用 `Build\Server Tools\LibraryEditor\Release\LibraryEditor.exe` 批量转换（转换→选多个 .wil）时弹出 JIT 调试异常对话框：

```
System.IO.EndOfStreamException: Unable to read beyond the end of the stream.
   at System.IO.BinaryReader.ReadInt16()
   at LibraryEditor.WeMadeLibrary.WeMadeImage..ctor(BinaryReader reader, Byte nType) — WeMadeLibrary.cs:line 322
   at LibraryEditor.WeMadeLibrary.CheckImage(Int32 index) — WeMadeLibrary.cs:line 164
   at LibraryEditor.WeMadeLibrary.Initialize() — WeMadeLibrary.cs:line 71
   at LibraryEditor.LMain.<convertToolStripMenuItem_Click>b__32_1(Int32 i) — LMain.cs:line 522
   at System.Threading.Tasks.Parallel...
```

批量转换中断，已选文件一个都没转出。

## 二、排查过程

1. **读崩溃栈三处代码**：
   - `LMain.cs@505`：批量转换 `Parallel.For` 对每个文件 `new WeMadeLibrary(fileName)` → `ToMLibrary()`，全程无 try-catch，任一文件抛异常即炸整批；
   - `WeMadeLibrary.cs@59 Initialize()`：构造时对全部帧逐个 `CheckImage(i)`（即「全帧预检」，这是与游戏内按需读帧不同的地方）；
   - `WeMadeLibrary.cs@157 CheckImage()`：`_fStream.Position = _indexList[index]` 后直接 `new WeMadeImage(_bReader, _nType)`，@322 `Width = reader.ReadInt16()` 读帧头——**索引偏移越过文件尾时直接抛 EndOfStreamException，零容错**。

2. **PowerShell 探针全量扫描 41 个 .wil**（`C:\Users\Administrator\Desktop\Data`，复刻 LibraryEditor 解析逻辑：帧头需读 8 字节宽/高/X/Y）：
   - **37 个完好**；**4 个文件各 1 个坏帧，全是最后一帧**：

| 文件 | 帧数 | 末帧偏移 | 文件长度 | 越界量 |
|---|---|---|---|---|
| DnItems.wil | 566 | 117328 | 116330 | **-998 字节**（偏移超出文件尾） |
| Items.wil | 571 | 292422 | 291424 | **-998 字节** |
| stateitem.wil | 561 | 520652 | 519693 | **-959 字节** |
| mmap.wil | 190 | 17396799 | 17396799 | 正好 EOF（帧头 0 字节可读） |

3. **根因定性**：供应商老资源（2003 年 Wil 文件）的 WIX 索引末条为死索引——声明的帧偏移超出 WIL 实际文件尾。这些帧本无实际像素数据（死条目）。LibraryEditor 的全帧预检对这些帧零容错，`ReadInt16` 越过文件尾抛异常；批量转换无 per-file 容错，一个坏文件炸掉整批。

## 三、修复内容

改动 2 个文件：

### 1. `LibraryEditor\Graphics\WeMadeLibrary.cs`

- **CheckImage 加坏帧防御**（@157）：索引偏移 `<=0` 或 `> 文件长度 - ImageStructureSize[nType]`（帧头实际尺寸表，nType=1/2/4/5 需 16~17 字节）时，不再定位读取，改为放入无参构造的空白占位帧：
  ```csharp
  long offset = _indexList[index];
  if (offset <= 0 || offset > _fStream.Length - ImageStructureSize[_nType])
  {
      Images[index] = new WeMadeImage();
      return;
  }
  ```
- **WeMadeImage 加无参构造**：全零尺寸（Width=Height=0），使 `CreateTexture` 直接跳过（Image 保持 null），转换时 `MLibraryV2.MImage(null)` 走既有 `FBytes=空数组` 分支——与正常空白帧完全同一路径，末帧转为 .Lib 空图，不产生任何副作用。

### 2. `LibraryEditor\LMain.cs`

- **批量转换 per-file try-catch**（@505 `convertToolStripMenuItem_Click`）：单个文件意外损坏（坏索引/截断/压缩数据错等他类异常）只弹窗提示该文件名+错误信息，不再中断整批；其余文件继续转换。

## 四、验证证据

1. **编译**：`dotnet build LibraryEditor\LibraryEditor.csproj -c Release` → 0 错误（警告仍为已知的 6 个，含用户定保留的 MSB3277 与 ListViewNF CS8622）。
2. **IL 验证**：反射读取 Release 产物 `LibraryEditor.dll`，`CheckImage` IL 长度 252 字节（含防御逻辑），`WeMadeImage` 构造函数 2 个（无参构造已编入）。
3. **端到端探针**（Temp 临时工程，ProjectReference 直连修复后源码）：
   - 4 个坏文件 + 1 个好文件直接 `new WeMadeLibrary()`（= 原崩溃路径全帧预检）：
     ```
     DnItems.wil: 构造成功 总帧=566 空图帧=187
     Items.wil:   构造成功 总帧=571 空图帧=189
     mmap.wil:    构造成功 总帧=190 空图帧=48
     stateitem.wil: 构造成功 总帧=561 空图帧=310
     Prguse.wil:  构造成功 总帧=510 空图帧=259（对照，完好文件不受影响）
     ```
   - `ToMLibrary()` 全链路：DnItems.wil → DnItems.Lib 转换成功（输出 115110 字节），坏尾帧转为空图，无异常。
4. **产物就位**：新 dll/exe 已输出到 `Build\Server Tools\LibraryEditor\Release\`，用户可直接复测。

## 五、回滚方法

```
git revert <本次 commit>
dotnet build LibraryEditor\LibraryEditor.csproj -c Release
```

或手动还原 `LibraryEditor\Graphics\WeMadeLibrary.cs` 的 CheckImage（去掉偏移越界判断，恢复 `_fStream.Position = _indexList[index]` 直读）与 `WeMadeImage` 无参构造，及 `LibraryEditor\LMain.cs` 批量转换的 try-catch 包裹，重新编译即可。
