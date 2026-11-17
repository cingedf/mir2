using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NpcVisualEditor
{
    /// <summary>
    /// 需求条件试算 + 标称伤害估算器（#11）。
    /// 说明：Crystal 无"属性反推 NeedLevel"公式，RequiredAmount 是人工填的 byte；
    /// 真实伤害依赖运行时（目标 AC/准确/暴击/buff/幸运随机），本工具只按引擎静态公式
    /// 给出标称区间与期望，UI 已显著标注，避免误当成真实伤害。
    /// 公式来源（只读复刻）：
    ///   MapObject.GetAttackPower/GetRangeAttackPower/GetArmour (MapObject.cs:268-582)
    ///   MagicInfo.GetDamage/GetPower/GetMultiplier (MagicInfo.cs:180-208)
    ///   HumanObject 佩戴判定 RequiredType/RequiredAmount (HumanObject.cs:1141-1227)
    /// </summary>
    public class DamageCalcForm : Form
    {
        private readonly NumericUpDown _numLevel, _numMinDC, _numMaxDC, _numMinMC, _numMaxMC;
        private readonly NumericUpDown _numMinSC, _numMaxSC, _numMinAC, _numMaxAC, _numMinMAC, _numMaxMAC;
        private readonly NumericUpDown _numLuck, _numArmour;
        private readonly NumericUpDown _numSkillBase, _numSkillPower, _numSkillLevel;
        private readonly NumericUpDown _numMultiBase, _numMultiBonus, _numReqAmount;
        private readonly ComboBox _cmbReqType;
        private readonly Label _result;

        private static readonly string[] ReqNames =
        {
            "等级(Level)", "防御上限(MaxAC)", "魔防上限(MaxMAC)", "攻击上限(MaxDC)",
            "魔法上限(MaxMC)", "道术上限(MaxSC)", "等级上限(MaxLevel)",
            "防御下限(MinAC)", "魔防下限(MinMAC)", "攻击下限(MinDC)",
            "魔法下限(MinMC)", "道术下限(MinSC)"
        };

        public DamageCalcForm()
        {
            Text = "需求试算 / 标称伤害估算";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(620, 660);

            var note = new Label
            {
                Text = "⚠ 本工具按引擎静态公式估算“标称伤害/需求条件”，非真实战斗结果。\n" +
                       "真实伤害还取决于目标防御/准确/敏捷、暴击、增益状态与幸运随机掷点。",
                Dock = DockStyle.Top, Height = 72, ForeColor = Color.DarkRed,
                AutoSize = false,  // 两行提示+Padding 实际高度>48，AutoSize 会撑高遮挡下方输入框
                BackColor = Color.FromArgb(255, 245, 220), Padding = new Padding(8, 6, 8, 4)
            };
            Controls.Add(note);

            var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
            int y = 8;

            _numLevel = AddRow(panel, "角色等级", ref y, 1, 255, 1);
            _numMinDC = AddRow(panel, "攻击下限 (MinDC)", ref y, 0, 9999, 0);
            _numMaxDC = AddRow(panel, "攻击上限 (MaxDC)", ref y, 0, 9999, 0);
            _numMinMC = AddRow(panel, "魔法下限 (MinMC)", ref y, 0, 9999, 0);
            _numMaxMC = AddRow(panel, "魔法上限 (MaxMC)", ref y, 0, 9999, 0);
            _numMinSC = AddRow(panel, "道术下限 (MinSC)", ref y, 0, 9999, 0);
            _numMaxSC = AddRow(panel, "道术上限 (MaxSC)", ref y, 0, 9999, 0);
            _numMinAC = AddRow(panel, "防御下限 (MinAC)", ref y, 0, 9999, 0);
            _numMaxAC = AddRow(panel, "防御上限 (MaxAC)", ref y, 0, 9999, 0);
            _numMinMAC = AddRow(panel, "魔防下限 (MinMAC)", ref y, 0, 9999, 0);
            _numMaxMAC = AddRow(panel, "魔防上限 (MaxMAC)", ref y, 0, 9999, 0);
            _numLuck = AddRow(panel, "幸运 (+7 以上必出上限)", ref y, 0, 20, 0);
            _numArmour = AddRow(panel, "目标防御 (AC, 仅参考)", ref y, 0, 9999, 0);

            y += 6;
            panel.Controls.Add(new Label { Text = "—— 技能（可选，物理攻击已含在上方 DC）——",
                Location = new Point(12, y), AutoSize = true, ForeColor = Color.SteelBlue }); y += 24;
            _numSkillBase = AddRow(panel, "技能伤害基数 DamageBase", ref y, 0, 9999, 0);
            _numSkillPower = AddRow(panel, "技能威力基数 MPower/Power", ref y, 0, 9999, 0);
            _numSkillLevel = AddRow(panel, "技能等级 (0-3)", ref y, 0, 3, 0);
            _numMultiBase = AddRow(panel, "倍率基数 MultiplierBase", ref y, 0, 1000, 100);
            _numMultiBonus = AddRow(panel, "倍率加成 MultiplierBonus", ref y, 0, 1000, 0);

            y += 6;
            panel.Controls.Add(new Label { Text = "—— 佩戴需求试算 (RequiredType / RequiredAmount) ——",
                Location = new Point(12, y), AutoSize = true, ForeColor = Color.SteelBlue }); y += 24;
            _cmbReqType = new ComboBox { Location = new Point(12, y), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbReqType.Items.AddRange(ReqNames);
            _cmbReqType.SelectedIndex = 0;
            panel.Controls.Add(_cmbReqType);
            _numReqAmount = new NumericUpDown { Location = new Point(285, y), Width = 100, Minimum = 0, Maximum = 255, Value = 0 };
            panel.Controls.Add(new Label { Text = "需求阈值", Location = new Point(395, y + 3), AutoSize = true });
            panel.Controls.Add(_numReqAmount); y += 34;

            var btn = new Button { Text = "计算", Location = new Point(12, y), Width = 100 };
            btn.Click += (s, e) => Calculate();
            panel.Controls.Add(btn);
            var btnReq = new Button { Text = "佩戴判定", Location = new Point(122, y), Width = 100 };
            btnReq.Click += (s, e) => CheckRequirement();
            panel.Controls.Add(btnReq);
            y += 40;

            _result = new Label
            {
                Location = new Point(12, y), Width = 580, Height = 160,
                AutoSize = false, BackColor = Color.FromArgb(245, 245, 250),
                BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(8)
            };
            panel.Controls.Add(_result);
            Controls.Add(panel);
        }

        private static NumericUpDown AddRow(Panel p, string label, ref int y, int min, int max, int val)
        {
            p.Controls.Add(new Label { Text = label, Location = new Point(12, y + 3), AutoSize = true });
            var n = new NumericUpDown { Location = new Point(285, y), Width = 160, Minimum = min, Maximum = max, Value = val };
            p.Controls.Add(n);
            y += 28;
            return n;
        }

        private int Val(NumericUpDown n) => (int)n.Value;

        private void Calculate()
        {
            int minDc = Val(_numMinDC), maxDc = Val(_numMaxDC);
            int minMc = Val(_numMinMC), maxMc = Val(_numMaxMC);
            int minSc = Val(_numMinSC), maxSc = Val(_numMaxSC);
            int luck = Val(_numLuck), armour = Val(_numArmour);

            // GetAttackPower: Random.Next(min, max+1)；幸运>0 直接取 max（MapObject.cs:268-285）
            int physMin = minDc, physMax = maxDc;
            if (luck > 0) physMin = physMax;
            int physAvg = luck > 0 ? physMax : (physMin + physMax) / 2;

            // 技能：GetPower=(int)Round((MPower/4)*(Level+1)+DefPower)，这里用威力基数代 MPower/DefPower
            // GetDamage=(int)((DamageBase+Power)*Multiplier)，Multiplier=Base+Level*Bonus
            int skillBase = Val(_numSkillBase), skillPower = Val(_numSkillPower), skillLevel = Val(_numSkillLevel);
            float multiplier = Val(_numMultiBase) / 100F + skillLevel * (Val(_numMultiBonus) / 100F);
            int power = (int)Math.Round((skillPower / 4F) * (skillLevel + 1) + skillPower);
            int skillDamage = (int)((skillBase + power) * multiplier);

            // 减防仅作参考（真实 Attacked 结算远复杂于此）
            int refAfterArmour = Math.Max(0, physAvg - armour / 2);

            _result.Text =
                $"【物理攻击（标称）】\r\n" +
                $"  区间: {physMin} ~ {physMax}" + (luck > 0 ? "（幸运>0，必出上限）" : "") + "\r\n" +
                $"  期望: {physAvg}\r\n\r\n" +
                $"【技能伤害（标称，倍率 {multiplier:0.##}）】\r\n" +
                $"  Power={power}, DamageBase={skillBase} → 约 {skillDamage}\r\n\r\n" +
                $"【参考】物理期望扣除目标防御/2 后约 {refAfterArmour}（真实结算依目标 AC/准确/暴击/buff）\r\n" +
                $"  魔法区间 {minMc}~{maxMc}，道术区间 {minSc}~{maxSc}（技能按对应属性代入上式）\r\n\r\n" +
                $"※ 所有结果为静态标称值，不代表实战伤害。";
        }

        private void CheckRequirement()
        {
            int reqType = _cmbReqType.SelectedIndex;
            int amount = Val(_numReqAmount);
            int level = Val(_numLevel);
            int minDc = Val(_numMinDC), maxDc = Val(_numMaxDC);
            int minMc = Val(_numMinMC), maxMc = Val(_numMaxMC);
            int minSc = Val(_numMinSC), maxSc = Val(_numMaxSC);
            int minAc = Val(_numMinAC), maxAc = Val(_numMaxAC);
            int minMac = Val(_numMinMAC), maxMac = Val(_numMaxMAC);
            // 佩戴判定复刻 HumanObject.cs:1141-1227：按 RequiredType 取对应属性与 Threshold 比较
            // ReqNames 顺序: 0 Level/1 MaxAC/2 MaxMAC/3 MaxDC/4 MaxMC/5 MaxSC/6 MaxLevel
            //                7 MinAC/8 MinMAC/9 MinDC/10 MinMC/11 MinSC
            int actual = reqType switch
            {
                0 => level,                            // Level
                1 => maxAc,                            // MaxAC
                2 => maxMac,                           // MaxMAC
                3 => maxDc,                            // MaxDC
                4 => maxMc,                            // MaxMC
                5 => maxSc,                            // MaxSC
                6 => level,                            // MaxLevel（简化按等级）
                7 => minAc,                            // MinAC
                8 => minMac,                           // MinMAC
                9 => minDc, 10 => minMc, 11 => minSc,
                _ => -1
            };
            string attr = ReqNames[Math.Max(0, Math.Min(ReqNames.Length - 1, reqType))];
            bool ok = actual >= amount;
            _result.Text = $"【佩戴需求试算】\r\n" +
                           $"  需求: {attr} ≥ {amount}\r\n" +
                           $"  当前: {actual}（等级{level}, DC {minDc}-{maxDc}, MC {minMc}-{maxMc}, SC {minSc}-{maxSc}）\r\n" +
                           $"  判定: {(ok ? "✔ 满足佩戴条件" : "✘ 不满足，需提升 " + attr)}\r\n\r\n" +
                           $"※ RequiredAmount 是装备上人工填的 byte(0-255)，本工具不做属性反推。\r\n" +
                           $"  实际判定以服务端 HumanObject 为准（含变体装备 GetRealItem）。";
        }
    }
}
