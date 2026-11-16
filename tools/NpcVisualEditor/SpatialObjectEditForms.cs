using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Server.MirDatabase;
using Server.MirEnvir;

namespace NpcVisualEditor
{
    /// <summary>刷怪点属性编辑（RespawnInfo 全字段）。</summary>
    public class RespawnEditForm : Form
    {
        private readonly RespawnInfo _r;
        private readonly ComboBox _cmbMonster;
        private readonly NumericUpDown _numCount, _numSpread, _numDelay, _numRandom,
            _numDirection, _numIndex, _numTicks;
        private readonly CheckBox _chkSaveTime;
        private readonly TextBox _txtRoute;

        public RespawnEditForm(RespawnInfo r, Envir envir)
        {
            _r = r;
            Text = "刷怪点属性";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(400, 360);

            int y = 16;
            Controls.Add(new Label { Text = "怪物:", Location = new Point(20, y), AutoSize = true });
            _cmbMonster = new ComboBox { Location = new Point(110, y - 3), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var m in envir.MonsterInfoList.OrderBy(d => d.Index))
                _cmbMonster.Items.Add(m);
            int sel = envir.MonsterInfoList.OrderBy(d => d.Index).ToList().FindIndex(d => d.Index == r.MonsterIndex);
            if (sel >= 0) _cmbMonster.SelectedIndex = sel;
            Controls.Add(_cmbMonster);
            y += 32;

            _numCount = AddNum("数量 Count", r.Count, ref y);
            _numSpread = AddNum("范围 Spread", r.Spread, ref y);
            _numDelay = AddNum("延迟 Delay(秒)", r.Delay, ref y);
            _numRandom = AddNum("随机延迟 RandomDelay", r.RandomDelay, ref y);
            _numDirection = AddNum("方向 Direction", r.Direction, 255, ref y);
            _numIndex = AddNum("重生组 RespawnIndex", r.RespawnIndex, int.MaxValue, ref y);
            _numTicks = AddNum("RespawnTicks(0=不用)", r.RespawnTicks, ref y);

            _chkSaveTime = new CheckBox { Text = "保存重生时间 (SaveRespawnTime)", Location = new Point(110, y), AutoSize = true, Checked = r.SaveRespawnTime };
            y += 30;
            Controls.Add(_chkSaveTime);

            Controls.Add(new Label { Text = "巡逻路线:", Location = new Point(20, y), AutoSize = true });
            _txtRoute = new TextBox { Location = new Point(110, y - 3), Width = 260, Text = r.RoutePath };
            y += 32;

            Controls.Add(new Label { Text = $"坐标: X={r.Location.X}  Y={r.Location.Y}（在画布上拖动改坐标）",
                Location = new Point(20, y), AutoSize = true, ForeColor = Color.Gray });
            y += 28;

            var ok = new Button { Text = "确定", Location = new Point(120, y), Width = 80, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "取消", Location = new Point(220, y), Width = 80, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) =>
            {
                if (_cmbMonster.SelectedItem is MonsterInfo m) _r.MonsterIndex = m.Index;
                _r.Count = (ushort)_numCount.Value;
                _r.Spread = (ushort)_numSpread.Value;
                _r.Delay = (ushort)_numDelay.Value;
                _r.RandomDelay = (ushort)_numRandom.Value;
                _r.Direction = (byte)_numDirection.Value;
                _r.RespawnIndex = (int)_numIndex.Value;
                _r.RespawnTicks = (ushort)_numTicks.Value;
                _r.SaveRespawnTime = _chkSaveTime.Checked;
                _r.RoutePath = _txtRoute.Text.Trim();
            };
            Controls.AddRange(new Control[] { ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private NumericUpDown AddNum(string label, decimal value, ref int y)
            => AddNum(label, value, ushort.MaxValue, ref y);

        private NumericUpDown AddNum(string label, decimal value, decimal max, ref int y)
        {
            Controls.Add(new Label { Text = label + ":", Location = new Point(20, y), AutoSize = true });
            var num = new NumericUpDown
            {
                Location = new Point(110, y - 3), Width = 120,
                Minimum = 0, Maximum = max, Value = Math.Min(max, Math.Max(0, value))
            };
            Controls.Add(num);
            y += 30;
            return num;
        }
    }

    /// <summary>安全区属性编辑（SafeZoneInfo 全字段）。</summary>
    public class SafeZoneEditForm : Form
    {
        public SafeZoneEditForm(SafeZoneInfo sz)
        {
            Text = "安全区属性";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(320, 170);

            int y = 18;
            Controls.Add(new Label { Text = "中心 X:", Location = new Point(20, y), AutoSize = true });
            var x = new NumericUpDown { Location = new Point(110, y - 3), Width = 150, Maximum = 10000, Value = sz.Location.X };
            y += 30;
            Controls.Add(new Label { Text = "中心 Y:", Location = new Point(20, y), AutoSize = true });
            var yy = new NumericUpDown { Location = new Point(110, y - 3), Width = 150, Maximum = 10000, Value = sz.Location.Y };
            y += 30;
            Controls.Add(new Label { Text = "半径 Size:", Location = new Point(20, y), AutoSize = true });
            var size = new NumericUpDown { Location = new Point(110, y - 3), Width = 150, Maximum = ushort.MaxValue, Value = sz.Size };
            y += 30;
            var start = new CheckBox { Text = "作为复活起始点 (StartPoint)", Location = new Point(20, y), AutoSize = true, Checked = sz.StartPoint };
            y += 30;

            var ok = new Button { Text = "确定", Location = new Point(70, y), Width = 80, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "取消", Location = new Point(170, y), Width = 80, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) =>
            {
                sz.Location = new Point((int)x.Value, (int)yy.Value);
                sz.Size = (ushort)size.Value;
                sz.StartPoint = start.Checked;
            };
            Controls.AddRange(new Control[] { x, yy, size, start, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    /// <summary>传送门（移动点）属性编辑（MovementInfo 全字段）。</summary>
    public class MovementEditForm : Form
    {
        public MovementEditForm(MovementInfo m, Envir envir)
        {
            Text = "传送门属性";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(380, 300);

            int y = 16;
            Controls.Add(new Label { Text = "目标地图:", Location = new Point(20, y), AutoSize = true });
            var cmbMap = new ComboBox { Location = new Point(110, y - 3), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
            var maps = envir.MapInfoList.OrderBy(d => d.Index).ToList();
            foreach (var map in maps) cmbMap.Items.Add(map);
            int sel = maps.FindIndex(d => d.Index == m.MapIndex);
            if (sel >= 0) cmbMap.SelectedIndex = sel;
            Controls.Add(cmbMap);
            y += 32;

            var sx = AddCoord("入口 Source X", m.Source.X, ref y);
            var sy = AddCoord("入口 Source Y", m.Source.Y, ref y);
            var dx = AddCoord("出口 Dest X", m.Destination.X, ref y);
            var dy = AddCoord("出口 Dest Y", m.Destination.Y, ref y);

            var hole = new CheckBox { Text = "需要镐 (NeedHole)", Location = new Point(20, y), AutoSize = true };
            var move = new CheckBox { Text = "需要移动 (NeedMove)", Location = new Point(160, y), AutoSize = true };
            y += 26;
            var big = new CheckBox { Text = "大地图显示", Location = new Point(20, y), AutoSize = true, Checked = m.ShowOnBigMap };
            y += 30;
            Controls.AddRange(new Control[] { hole, move, big });
            foreach (var c in new Control[] { hole, move, big }) { }
            // 原值回填
            hole.Checked = m.NeedHole; move.Checked = m.NeedMove; big.Checked = m.ShowOnBigMap;

            var ok = new Button { Text = "确定", Location = new Point(110, y), Width = 80, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "取消", Location = new Point(210, y), Width = 80, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) =>
            {
                if (cmbMap.SelectedItem is MapInfo map) m.MapIndex = map.Index;
                m.Source = new Point((int)sx.Value, (int)sy.Value);
                m.Destination = new Point((int)dx.Value, (int)dy.Value);
                m.NeedHole = hole.Checked;
                m.NeedMove = move.Checked;
                m.ShowOnBigMap = big.Checked;
            };
            Controls.AddRange(new Control[] { sx, sy, dx, dy, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private NumericUpDown AddCoord(string label, int value, ref int y)
        {
            Controls.Add(new Label { Text = label + ":", Location = new Point(20, y), AutoSize = true });
            var num = new NumericUpDown { Location = new Point(160, y - 3), Width = 120, Maximum = 10000, Value = Math.Min(10000, Math.Max(0, value)) };
            Controls.Add(num);
            y += 28;
            return num;
        }
    }
}
