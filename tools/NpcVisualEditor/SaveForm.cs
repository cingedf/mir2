using System;
using System.Windows.Forms;

namespace NpcVisualEditor
{
    /// <summary>
    /// 保存确认。复刻原版 FormSave 勾选模式（原版三勾对应
    /// Merchant.txt/Npcs.txt/MapInfo.txt → 克隆中合并为一次 MirDB 原子保存）。
    /// </summary>
    public class SaveForm : Form
    {
        public bool Backup { get; private set; } = true;

        private readonly CheckBox _chkBackup;

        public SaveForm(string serverDir)
        {
            Text = "保存数据库";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 160);

            var lbl = new Label
            {
                Text = "将保存 NPC / 地图 / 安全区 / 刷怪等全部数据到\r\n" + serverDir + "\\Server.MirDB",
                Location = new Point(20, 16), AutoSize = true
            };
            _chkBackup = new CheckBox
            {
                Text = "保存前自动备份（Server.MirDB.bak-时间戳）",
                Location = new Point(20, 60), AutoSize = true, Checked = true
            };
            var btnOk = new Button { Text = "保存", Location = new Point(140, 100), Width = 80, DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "取消", Location = new Point(240, 100), Width = 80, DialogResult = DialogResult.Cancel };

            Controls.AddRange(new Control[] { lbl, _chkBackup, btnOk, btnCancel });
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (DialogResult == DialogResult.OK) Backup = _chkBackup.Checked;
        }
    }
}
