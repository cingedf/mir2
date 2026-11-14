# 20261115实装英雄双倍经验链路与GIVEHEROBUFF

> 素材③（HeroUseItems 英雄物品链 A-完整版）：「经验丰富」哑弹脚本的完整复活——补齐 BuffType 枚举、英雄物品触发缺口、英雄侧授予命令三处代码层缺失。数据侧（物品播种/脚本改写）由后续工具与数据 commit 完成。

## 现象

- 部署区 `HeroUseItems.txt` 的 `[@_UseItem(500)]` 页（高级通行证，24h 英雄双倍经验）是静默哑弹：`GIVEBUFF 经验丰富` 的「经验丰富」不是 BuffType 枚举成员（TryParse 失败静默 return）；且 MirDB 无 Shape=500 触发物品；英雄用 Type=21 物品在 HeroObject.UseItem 落 default 直接 return（不消耗、不触发脚本）。
- 考证确认（子代理全链取证）：触发链四环全断 + 第5重语义断裂（GIVEBUFF 只加主人，英雄经验读英雄自己 Stats）。

## 排查过程

- ItemInfo 二进制格式（Shared\Data\ItemData.cs:74-209 ctor / 213-263 Save，Version=118 走 ushort StackSize 分支）；ItemType.特殊消耗品=21（Enums.cs:1027）；Shape 为 short 直写无枚举约束，全库 4320 件物品 Shape 最大 306，**500 空闲**。
- 触发链：玩家用 Type=21 → PlayerObject.UseItem:6470 `CallDefaultNPC(DefaultNPCType.UseItem, item.Info.Shape)` → `[@_UseItem(Shape)]` 页 → NPCSegment 玩家侧执行端（4090-4175，SetBuffs.txt 预设 + param[5..] Key=Val 只许覆盖预设非零键）。
- 英雄链断点确证：MirConnection.UseItem 2483 → Player.HeroUseItem:6719 → Hero.UseItem（322-634）case 全集无特殊消耗品 → default return。
- 脚本上下文实参链确证：**player 恒为英雄主人**（CallDefaultNPC 的 DelayedAction 挂主人 ActionList → CompleteNPC → script.Call(this=PlayerObject)）⇒ 给英雄加 buff 必须新命令显式取 `player.Hero`。
- BuffType 空闲值复核：**805 已被「防御型绝技」占用**（⑤ 报告此点有误），800 段（ItemsEffect）实际占满至 831 天龙攻击，832-999 空闲 ⇒ 新值定 **832 双倍经验**。
- Stat.经验收益=100（Shared\Data\Stat.cs:132）为经验加成消费键（HeroObject.GainExp:1216 与玩家经验链同源）。

## 修复内容（4 文件，全部源码侧）

1. **`Shared\Enums.cs`**：BuffType 枚举 ItemsEffect 段尾新增 `双倍经验 = 832`（注释注明段界）。
2. **`Server\MirDatabase\BuffInfo.cs`**：注册表 749 行（天龙攻击）后、Battlefield 段前插入 `双倍经验` 注册（StackType=ResetStatAndDuration、Visible=true、无 Properties——死亡/换图不掉，通行证语义）。
3. **`Server\MirObjects\HeroObject.cs`**：UseItem switch 补 `case ItemType.特殊消耗品:` → `Owner.CallDefaultNPC(DefaultNPCType.UseItem, item.Info.Shape);`——英雄用特殊消耗品与玩家同路触发 `[@_UseItem(Shape)]` 页（物品照常扣减，p.Success=true 路径不变）。
4. **`Server\MirObjects\NPC\NPCActions.cs` + `NPCSegment.cs`**：
   - ActionType 新增 `GiveHeroBuff`；
   - ParseAct 新增 `case "GIVEHEROBUFF"`（参数布局与 GIVEBUFF 完全一致：名/秒/bool×3/Key=Val，parts.Length<4 早退）；
   - 玩家侧执行端新增 `case ActionType.GiveHeroBuff`：`player.Hero` 在场校验 → SetBuffs.txt 预设行解析（与 GiveBuff 同构）→ param[5..] Key=Val **直填**（无 GiveBuff 的"只许覆盖预设非零键"限制，因英雄侧无需预设保护语义）→ `player.Hero.AddBuff(heroType, player.Hero, Settings.Second * heroDuration, heroBuffStats, true)`（第 5 参显式 true，避开 visible 旗标错接的历史坑）。

## 验证证据

- `dotnet build Server\Server.Library.csproj -c Release`：**0 警告 0 错误**（19.28s）。
- 逻辑复核：英雄用 Shape=500 物品 → Owner.CallDefaultNPC → 500 页 → `GIVEHEROBUFF 双倍经验 86400 ... 经验收益=200` → 英雄 Stats[经验收益]=200（GainExp 消费）；玩家侧同名页用 GIVEBUFF 仍加主人，两命令互不干扰。
- 后续配套（另 commit）：ItemSpawnSeed 播种「高级通行证」（Shape=500）+ SetBuffs.txt 预设行 + HeroUseItems.txt 哑弹段改写。

## 回滚方法

```
git revert <本提交>
```

或手工回退：Enums.cs 删 `双倍经验 = 832`；BuffInfo.cs 删注册块；HeroObject.cs 删特殊消耗品 case；NPCActions.cs 删 GiveHeroBuff；NPCSegment.cs 删 ParseAct GIVEHEROBUFF case 与执行端 GiveHeroBuff case。
