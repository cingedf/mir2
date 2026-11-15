# 20261115接线PvpCanResistMagic配置生效

> 低优审计（死接线第四轮）维度③第 3 项：`Settings.PvpCanResistMagic` 有 INI Load/Save + 管理员 GUI 复选框，但服务端逻辑零消费——管理员在平衡配置里勾「PvP 可抵抗魔法」永远无效。姊妹配置 `PvpCanResistPoison` 有完整消费模式，本修复照其同构接线。用户授权自主决策逐项处置审计报告（@679@）。

## 现象

- `Server\Settings.cs:314` 声明、543/816 Load/Save（Items 段）、`Server.MirForms\BalanceConfigForm.cs:120-121/373` GUI 读写——四件套齐全，全仓逻辑零消费。
- 对照组：`PvpCanResistPoison` 在 HumanObject.ApplyPoison（8532）等 5 处以 `(施毒者.Race != ObjectType.Player) || Settings.PvpCanResistPoison` 模式门控毒物躲避生效。
- 魔法侧对应判定在 MapObject.GetArmour（533-580）的 MACAgility/MAC 分支：`Envir.Random.Next(Settings.MagicResistWeight) < Stats[Stat.魔法躲避]` 即 Miss——**无论攻击者是玩家还是怪物都生效**，PvP 时玩家魔法躲避恒生效、配置形同虚设。

## 排查过程

- grep `PvpCanResistMagic` 全仓 4 处（声明/Load/Save/GUI）零逻辑消费；grep `PvpCanResistPoison` 5 处消费模式（HumanObject.cs:6433/7866/8532、SepWarrior.cs:78、SepHighWarrior.cs:99）确立同构模板。
- grep `Stat.魔法躲避` 14 处消费点逐一分类：核心判定仅 MapObject.GetArmour 两处 MAC 分支；怪物 AI 侧 6 处（CrystalSpider/FlameScythe/GuardianRock/GreatFoxSpirit/Khazard/OmaKing）是怪物攻击判玩家躲避——攻击者是怪物，不属 PvP 门控范畴；PlayerObject 2 处为宝石合成消耗品；HumanObject 3 处为属性加成源。**接线点收敛为 GetArmour 单函数两分支**。
- 调用链确认：HumanObject.Attacked(HumanObject/MonsterObject) 全部经 GetArmour 单点生效 ⇒ 改两分支即覆盖全部 PvP/PvE 魔法命中判定；默认值 false = PvP 魔法躲避不生效（与 PvpCanResistPoison 默认一致），配置 GUI 勾选即开启，现状 PvE 行为零变化。

## 修复内容（`Server\MirObjects\MapObject.cs` GetArmour，两处对称）

```csharp
case DefenceType.MACAgility:
    // 体检④接线：PvpCanResistMagic 配置此前有 GUI/INI 无消费点——施法者为玩家时由该配置门控魔法躲避生效（与 PvpCanResistPoison 在 ApplyPoison 的门控模式同构；怪物施法不受限）
    if (((attacker.Race != ObjectType.Player) || Settings.PvpCanResistMagic) && Envir.Random.Next(Settings.MagicResistWeight) < Stats[Stat.魔法躲避])
    { ... Miss; hit = false; }
```

MAC 分支同构。语义：攻击者是怪物（PvE）⇒ 恒可躲避（现状不变）；攻击者是玩家（PvP）⇒ 受 `PvpCanResistMagic` 门控（默认关，GUI 可开）。

## 验证证据

- `dotnet build Server\Server.Library.csproj -c Release`：**0 警告 0 错误**（19.07s）。
- 逻辑复核：PvE 路径 attacker=MonsterObject ⇒ `Race != Player` 恒真 ⇒ 与原行为完全一致；PvP 路径受配置门控，默认 false 时魔法躲避不再 PvP 恒生效（**管理员可在平衡配置勾选恢复**，且这正是该配置的设计意图）。

## 回滚方法

```
git revert <本提交>
```

或手工还原 GetArmour 两处 if 条件为 `Envir.Random.Next(Settings.MagicResistWeight) < Stats[Stat.魔法躲避]`。
