using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Server.MirDatabase;
using Server.MirEnvir;

namespace NpcVisualEditor
{
    /// <summary>
    /// NPC 属性编辑。复刻原版 FormNpcInfoEdit：
    /// 脚本名/地图/X/Y/显示名/外观/间隔时间/变色 + 外观预览。
    /// </summary>
    public class NpcEditForm : Form
    {
        private readonly NPCInfo _npc;
        private readonly Envir _envir;
        private readonly string _serverDir;
        private readonly bool _isNew;

        private TextBox _txtFileName, _txtName, _txtX, _txtY, _txtImage;
        private ComboBox _cmbMap;
        private NumericUpDown _numRate;
        private CheckBox _chkBigMap, _chkTeleport;
        private PictureBox _picPreview;

        public NpcEditForm(NPCInfo npc, Envir envir, string serverDir, bool isNew)
        {
            _npc = npc;
            _envir = envir;
            _serverDir = serverDir;
            _isNew = isNew;

            Text = isNew ? "新建 NPC" : $"编辑 NPC [{npc.Index}]";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 330);

            int lx = 20, tx = 130, w = 300, y = 20, dy = 34;

            AddLabel("脚本文件名:", lx, y);
            _txtFileName = new TextBox { Location = new Point(tx, y - 3), Width = w, Text = _npc.FileName };
            y += dy;

            AddLabel("显示名称:", lx, y);
            _txtName = new TextBox { Location = new Point(tx, y - 3), Width = w, Text = _npc.Name };
            y += dy;

            AddLabel("所在地图:", lx, y);
            _cmbMap = new ComboBox { Location = new Point(tx, y - 3), Width = w, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var m in _envir.MapInfoList.OrderBy(d => d.Index))
                _cmbMap.Items.Add(m);
            if (_npc.MapIndex >= 0)
            {
                int i = _cmbMap.Items.Cast<MapInfo>().ToList()
                    .FindIndex(d => d.Index == _npc.MapIndex);
                if (i >= 0) _cmbMap.SelectedIndex = i;
            }
            y += dy;

            AddLabel("坐标 X / Y:", lx, y);
            _txtX = new TextBox { Location = new Point(tx, y - 3), Width = 80, Text = _npc.Location.X.ToString() };
            _txtY = new TextBox { Location = new Point(tx + 95, y - 3), Width = 80, Text = _npc.Location.Y.ToString() };
            y += dy;

            AddLabel("外观编号 Image:", lx, y);
            _txtImage = new TextBox { Location = new Point(tx, y - 3), Width = 80, Text = _npc.Image.ToString() };
            _picPreview = new PictureBox { Location = new Point(tx + 120, y - 8), Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
            _txtImage.TextChanged += (s, e) => UpdatePreview();
            var btnPickImg = new Button { Text = "选择...", Location = new Point(tx + 200, y - 3), Width = 62 };
            btnPickImg.Click += (s, e) =>
            {
                int cur = ushort.TryParse(_txtImage.Text, out ushort v) ? v : -1;
                using var picker = new ImagePickerForm(_serverDir, "NPC", cur);
                if (picker.ShowDialog(this) == DialogResult.OK)
                    _txtImage.Text = picker.SelectedId.ToString();
            };
            Controls.Add(btnPickImg);
            y += dy;

            AddLabel("刷新间隔 Rate:", lx, y);
            _numRate = new NumericUpDown { Location = new Point(tx, y - 3), Width = 80, Minimum = 0, Maximum = 65535, Value = _npc.Rate };
            y += dy;

            AddLabel("大地图显示:", lx, y);
            _chkBigMap = new CheckBox { Location = new Point(tx, y - 2), Text = "显示在大地图", Checked = _npc.ShowOnBigMap };
            _chkTeleport = new CheckBox { Location = new Point(tx + 140, y - 2), Text = "可传送至", Checked = _npc.CanTeleportTo };
            y += dy + 16;

            var btnScript = new Button { Text = "编辑脚本...", Location = new Point(60, y), Width = 100 };
            btnScript.Click += (s, e) =>
            {
                // 引擎约定：NPCScript.LoadInfo = Path.Combine(Settings.NPCPath, FileName + ".txt")
                string path = System.IO.Path.Combine(_serverDir, "Envir", "NPCs", _npc.FileName + ".txt");
                using var conv = new ConversationForm(_npc, path, null);
                conv.ShowDialog(this);
            };
            var btnOk = new Button { Text = "确定", Location = new Point(180, y), Width = 80, DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "取消", Location = new Point(280, y), Width = 80, DialogResult = DialogResult.Cancel };
            btnOk.Click += BtnOk_Click;
            Controls.AddRange(new Control[] {
                _txtFileName, _txtName, _cmbMap, _txtX, _txtY, _txtImage, _picPreview,
                _numRate, _chkBigMap, _chkTeleport, btnScript, btnOk, btnCancel });
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            UpdatePreview();
        }

        private void AddLabel(string text, int x, int y)
        {
            Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true });
        }

        private void UpdatePreview()
        {
            // 先释放旧图再加载，避免反复输入ID造成 GDI+ 句柄泄漏
            var oldImg = _picPreview.Image;
            _picPreview.Image = null;
            oldImg?.Dispose();
            if (!ushort.TryParse(_txtImage.Text, out ushort img)) return;
            string dir = Path.Combine(_serverDir, "Envir", "Previews", "NPC");
            if (!Directory.Exists(dir)) return;
            string path = Path.Combine(dir, img + ".bmp");
            if (!File.Exists(path))
            {
                var hit = Directory.GetFiles(dir, "*.bmp")
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == img.ToString());
                if (hit == null) return;
                path = hit;
            }
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                using var tmp = Image.FromStream(fs);
                _picPreview.Image = new Bitmap(tmp); // 深拷贝，脱离流生命周期
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NPC 预览图加载失败 {path}: {ex.Message}");
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (_txtFileName.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "脚本文件名不能为空。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            if (!int.TryParse(_txtX.Text, out int x) || !int.TryParse(_txtY.Text, out int y))
            {
                MessageBox.Show(this, "坐标必须是数字。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            if (_cmbMap.SelectedItem is not MapInfo map)
            {
                MessageBox.Show(this, "请选择地图。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            _npc.FileName = _txtFileName.Text.Trim();
            _npc.Name = _txtName.Text.Trim();
            _npc.MapIndex = map.Index;
            _npc.Location = new Point(x, y);
            if (ushort.TryParse(_txtImage.Text, out ushort img)) _npc.Image = img;
            _npc.Rate = (ushort)_numRate.Value;
            _npc.ShowOnBigMap = _chkBigMap.Checked;
            _npc.CanTeleportTo = _chkTeleport.Checked;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var img = _picPreview.Image;
                _picPreview.Image = null;
                img?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
