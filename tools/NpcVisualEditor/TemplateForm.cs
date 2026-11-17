using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Server.MirDatabase;

namespace NpcVisualEditor
{
    /// <summary>
    /// 玩法模板生成器：一键生成常见 NPC 玩法脚本（UTF-8 无 BOM）。
    /// 使用引擎已知命令（MOVE / CHECKITEM / TAKEITEM / GIVEITEM / GIVEGOLD /
    /// GIVEEXP / GIVEBUFF / CHECKGOLD / INSTANCEMOVE 等），不产生未知 # 标签。
    /// </summary>
    public class TemplateForm : Form
    {
        private readonly string _serverDir;
        private readonly ComboBox _cmbTemplate;
        private readonly ListBox _lstMaps;
        private readonly List<MapInfo> _maps;
        private readonly Panel _paramPanel;
        private readonly TextBox _txtPreview;

        private class Tpl
        {
            public string Key, Name, Desc;
            public Func<TemplateForm, string> Build;
            public override string ToString() => Name; // 下拉框显示中文名而非类型名
        }

        private readonly Dictionary<string, Tpl> _templates;

        // 参数控件
        private TextBox _pItem, _pCount, _pGold, _pExp, _pQuestion, _pAnswer;
        private TextBox _pMapX, _pMapY, _pBuff, _pNpcName;

        public TemplateForm(string serverDir, IEnumerable<MapInfo> maps)
        {
            _serverDir = serverDir;
            _maps = maps.OrderBy(d => d.Index).ToList();
            _templates = BuildTemplates();

            Text = "玩法模板生成器";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(960, 720);

            // 左：模板列表
            var left = new Panel { Dock = DockStyle.Left, Width = 230 };
            left.Controls.Add(new Label { Text = "选择玩法模板:", Location = new Point(12, 12), AutoSize = true });
            _cmbTemplate = new ComboBox
            {
                Location = new Point(12, 36), Width = 205, DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var t in _templates.Values) _cmbTemplate.Items.Add(t);
            if (_cmbTemplate.Items.Count > 0) _cmbTemplate.SelectedIndex = 0;
            left.Controls.Add(_cmbTemplate);

            left.Controls.Add(new Label { Text = "传送目标地图:", Location = new Point(12, 76), AutoSize = true });
            _lstMaps = new ListBox { Location = new Point(12, 98), Size = new Size(205, 300) };
            foreach (var m in _maps) _lstMaps.Items.Add(m);
            if (_lstMaps.Items.Count > 0) _lstMaps.SelectedIndex = 0;
            left.Controls.Add(_lstMaps);
            // Splitter 须在目标 Left 控件之前添加，FindTarget 才指向 left
            Controls.Add(new Splitter { Dock = DockStyle.Left, Width = 4, MinSize = 160 });
            Controls.Add(left);

            // 中：参数
            _paramPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            Controls.Add(_paramPanel);

            // 右：预览
            var right = new Panel { Dock = DockStyle.Right, Width = 430 };
            right.Controls.Add(new Label { Text = "生成脚本预览:", Location = new Point(10, 10), AutoSize = true });
            _txtPreview = new TextBox
            {
                Location = new Point(10, 34), Size = new Size(410, 560), Multiline = true,
                ScrollBars = ScrollBars.Both, ReadOnly = true, Font = new Font("Consolas", 9F)
            };
            var btnSave = new Button { Text = "保存脚本", Location = new Point(10, 610), Width = 110 };
            btnSave.Click += BtnSave_Click;
            right.Controls.AddRange(new Control[] { _txtPreview, btnSave });
            // Splitter 须在目标 Right 控件之前添加，FindTarget 才指向 right
            Controls.Add(new Splitter { Dock = DockStyle.Right, Width = 4, MinSize = 220 });
            Controls.Add(right);

            _cmbTemplate.SelectedIndexChanged += (s, e) => { BuildParams(); UpdatePreview(); };
            _lstMaps.SelectedIndexChanged += (s, e) => UpdatePreview(); // 传送模板换地图时刷新
            BuildParams();
            UpdatePreview();
        }

        private Dictionary<string, Tpl> BuildTemplates()
        {
            var d = new Dictionary<string, Tpl>();
            void Add(string key, string name, string desc, Func<TemplateForm, string> build) =>
                d[key] = new Tpl { Key = key, Name = name, Desc = desc, Build = build };

            Add("exchange", "物品兑换", "检查物品数量→扣除→给予奖励（兑换商店）", f =>
                "[@MAIN]\r\n#SAY\r\n欢迎来到兑换处。\r\n<兑换/@EXCHANGE> <关闭/@Exit>\r\n\r\n" +
                "[@EXCHANGE]\r\n#IF\r\nCHECKITEM " + f._pItem.Text + " " + f._pCount.Text + "\r\n" +
                "#ACT\r\nTAKEITEM " + f._pItem.Text + " " + f._pCount.Text + "\r\n" +
                "GIVEGOLD " + f._pGold.Text + "\r\nGIVEEXP " + f._pExp.Text + "\r\n" +
                "LOCALMESSAGE \"兑换成功！\" Hint\r\nBREAK\r\n\r\n" +
                "#ELSESAY\r\n你没有足够的「" + f._pItem.Text + "」。\r\n");

            Add("teleport", "传送NPC", "点击传送到指定地图坐标", f =>
            {
                var m = (f._lstMaps.SelectedItem as MapInfo);
                string map = m?.FileName?.Replace(".map", "") ?? "3";
                return "[@MAIN]\r\n#SAY\r\n要前往「" + (m?.Title ?? map) + "」吗？\r\n" +
                    "<<确认传送/@GO/cornflowerblue>> <关闭/@Exit>\r\n\r\n" +
                    "[@GO]\r\n#IF\r\n#ACT\r\nMOVE " + map + " " + f._pMapX.Text + " " + f._pMapY.Text + "\r\n";
            });

            Add("answer", "答题玩法", "随机问答，答对给奖", f =>
                "[@MAIN]\r\n#SAY\r\n" + f._pQuestion.Text + "\r\n" +
                "<<我知道答案/@ASK/cornflowerblue>> <放弃/@Exit>\r\n\r\n" +
                "[@ASK]\r\n#IF\r\n#SAY\r\n请输入你的答案，答对有奖！\r\n" +
                "<<答案是" + f._pAnswer.Text + "/@RIGHT/sandybrown>> <答错了/@WRONG>\r\n\r\n" +
                "[@RIGHT]\r\n#IF\r\n#ACT\r\nGIVEGOLD " + f._pGold.Text + "\r\n" +
                "LOCALMESSAGE \"回答正确，奖励已发放！\" Hint\r\nBREAK\r\n\r\n" +
                "[@WRONG]\r\n#IF\r\n#SAY\r\n很遗憾，答错了，再来一次？\r\n<再试一次/@MAIN>\r\n");

            Add("signin", "每日签到", "每天签到领取奖励（基于 CheckDay/变量标志）", f =>
                "[@MAIN]\r\n#IF\r\nCHECK [200] 0\r\n#SAY\r\n你今天还没有签到。\r\n<<签到领奖/@SIGN/cornflowerblue>> <关闭/@Exit>\r\n" +
                "#ELSESAY\r\n你今天已经签到过了，明天再来吧。\r\n\r\n" +
                "[@SIGN]\r\n#IF\r\nCHECK [200] 0\r\n#ACT\r\nMOV [200] 1\r\n" +
                "GIVEGOLD " + f._pGold.Text + "\r\nGIVEEXP " + f._pExp.Text + "\r\n" +
                "LOCALMESSAGE \"签到成功！\" Hint\r\nBREAK\r\n");

            Add("boss", "BOSS召唤", "检查物品→召唤BOSS到当前地图", f =>
                "[@MAIN]\r\n#SAY\r\n想召唤BOSS吗？需要「" + f._pItem.Text + "」×" + f._pCount.Text + "。\r\n" +
                "<<召唤BOSS/@SUMMON/sandybrown>> <关闭/@Exit>\r\n\r\n" +
                "[@SUMMON]\r\n#IF\r\nCHECKITEM " + f._pItem.Text + " " + f._pCount.Text + "\r\n" +
                "#ACT\r\nTAKEITEM " + f._pItem.Text + " " + f._pCount.Text + "\r\n" +
                "MONGEN " + f._pBuff.Text + " 1\r\nLOCALMESSAGE \"BOSS已召唤！\" Hint\r\nBREAK\r\n\r\n" +
                "#ELSESAY\r\n召唤材料不足。\r\n");

            Add("upgrade", "装备升级", "双装备合成升级（检查→扣除→给予）", f =>
                "[@MAIN]\r\n#SAY\r\n我可以帮你升级装备。\r\n<升级/@UP> <关闭/@Exit>\r\n\r\n" +
                "[@UP]\r\n#IF\r\nCHECKITEM " + f._pItem.Text + " 2\r\nCHECKGOLD >= " + f._pGold.Text + "\r\n" +
                "#ACT\r\nTAKEITEM " + f._pItem.Text + " 2\r\n" +
                "GIVEITEM " + f._pItem.Text + " 1\r\nLOCALMESSAGE \"升级成功！\" Hint\r\nBREAK\r\n\r\n" +
                "#ELSESAY\r\n需要 2 件装备和 " + f._pGold.Text + " 金币。\r\n");

            Add("buff", "增益BUFF赋予", "给予玩家限时增益效果", f =>
                "[@MAIN]\r\n#SAY\r\n需要增益效果吗？\r\n<<接受BUFF/@BUFF/cornflowerblue>> <关闭/@Exit>\r\n\r\n" +
                "[@BUFF]\r\n#IF\r\nCHECKGOLD >= " + f._pGold.Text + "\r\n#ACT\r\n" +
                "GIVEBUFF " + f._pBuff.Text + " 1800 TRUE FALSE FALSE\r\n" +
                "LOCALMESSAGE \"增益已附加！\" Hint\r\nBREAK\r\n\r\n" +
                "#ELSESAY\r\n金币不足（需要 " + f._pGold.Text + "）。\r\n");

            Add("reward", "等级奖励", "达到等级领取奖励", f =>
                "[@MAIN]\r\n#IF\r\nLEVEL >= " + f._pCount.Text + "\r\n#SAY\r\n你已达到领取等级。\r\n" +
                "<<领取奖励/@GET/cornflowerblue>> <关闭/@Exit>\r\n#ELSESAY\r\n等级不足 " + f._pCount.Text + " 级。\r\n\r\n" +
                "[@GET]\r\n#IF\r\nCHECK [201] 0\r\n#ACT\r\nMOV [201] 1\r\n" +
                "GIVEITEM " + f._pItem.Text + " 1\r\nGIVEGOLD " + f._pGold.Text + "\r\n" +
                "LOCALMESSAGE \"奖励已发放！\" Hint\r\nBREAK\r\n#ELSESAY\r\n奖励已领取过。\r\n");

            return d;
        }

        private void BuildParams()
        {
            _paramPanel.Controls.Clear();
            var t = (Tpl)_cmbTemplate.SelectedItem;
            if (t == null) return;

            int y = 10;
            void AddField(string label, ref TextBox textBox, string def)
            {
                _paramPanel.Controls.Add(new Label { Text = label, Location = new Point(10, y), AutoSize = true });
                textBox = new TextBox { Location = new Point(150, y - 3), Width = 200, Text = def };
                textBox.TextChanged += (s, e) => UpdatePreview(); // 参数改动实时刷新预览，避免保存过期脚本
                _paramPanel.Controls.Add(textBox);
                y += 34;
            }

            _paramPanel.Controls.Add(new Label
            {
                Text = t.Name + "\r\n" + t.Desc, Location = new Point(10, y), AutoSize = true,
                ForeColor = Color.SteelBlue
            });
            y += 56;

            _pItem = new TextBox(); _pCount = new TextBox(); _pGold = new TextBox();
            _pExp = new TextBox(); _pQuestion = new TextBox(); _pAnswer = new TextBox();
            _pMapX = new TextBox(); _pMapY = new TextBox(); _pBuff = new TextBox();
            _pNpcName = new TextBox();

            if (t.Key == "exchange" || t.Key == "boss" || t.Key == "upgrade" || t.Key == "reward")
                AddField("物品名称:", ref _pItem, "玄天魂石");
            if (t.Key == "exchange" || t.Key == "boss" || t.Key == "upgrade" || t.Key == "reward")
                AddField("数量/等级:", ref _pCount, t.Key == "reward" ? "35" : "10");
            AddField("金币数量:", ref _pGold, "10000");
            if (t.Key == "exchange" || t.Key == "signin")
                AddField("经验值:", ref _pExp, "1000");
            if (t.Key == "answer")
            {
                AddField("问题:", ref _pQuestion, "传奇世界的创建者是谁？");
                AddField("答案:", ref _pAnswer, "盛大");
            }
            if (t.Key == "teleport")
            {
                AddField("目标 X:", ref _pMapX, "300");
                AddField("目标 Y:", ref _pMapY, "300");
            }
            if (t.Key == "boss" || t.Key == "buff")
                AddField("怪物/BUFF名:", ref _pBuff, t.Key == "buff" ? "龙之特效" : "沃玛教主");
        }

        private void UpdatePreview()
        {
            var t = (Tpl)_cmbTemplate.SelectedItem;
            if (t == null) return;
            try { _txtPreview.Text = t.Build(this); }
            catch (Exception ex) { _txtPreview.Text = "生成失败: " + ex.Message; }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            var t = (Tpl)_cmbTemplate.SelectedItem;
            if (t == null) return;
            var dlg = new SaveFileDialog
            {
                Title = "保存 NPC 脚本",
                Filter = "脚本文件 (*.txt)|*.txt",
                FileName = t.Key + "-NPC.txt",
                InitialDirectory = Path.Combine(_serverDir, "Envir", "NPCs")
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var dir = Path.GetDirectoryName(dlg.FileName);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(dlg.FileName, _txtPreview.Text, new UTF8Encoding(false));
                MessageBox.Show(this, "脚本已保存: " + dlg.FileName, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
