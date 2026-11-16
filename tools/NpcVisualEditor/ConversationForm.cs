using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Server.MirDatabase;

namespace NpcVisualEditor
{
    /// <summary>
    /// NPC 脚本（对话）编辑器。复刻原版 FormConversation：
    /// 大 Memo 编辑，保存为 UTF-8 无 BOM（引擎约定）。
    /// </summary>
    public class ConversationForm : Form
    {
        private readonly NPCInfo _npc;
        private readonly string _scriptPath;
        private readonly TextBox _txt;
        private readonly Action _onSaved;
        private string _originalText = ""; // 最近一次落盘/载入的文本，用于关闭时判断未保存改动

        public ConversationForm(NPCInfo npc, string scriptPath, Action onSaved)
        {
            _npc = npc;
            _scriptPath = scriptPath;
            _onSaved = onSaved;

            Text = $"脚本编辑 — {NpcUtil.SafeName(npc)}（{_scriptPath}）";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(860, 640);

            _txt = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 10F),
                AcceptsReturn = true,
                AcceptsTab = true
            };

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8, 6, 8, 0) };
            var btnFlow = new Button { Text = "对话流程图", Width = 100 };
            btnFlow.Click += (s, e) =>
            {
                // 先把当前文本落盘，流程图基于同一脚本文件解析
                if (File.Exists(_scriptPath) || !string.IsNullOrEmpty(_txt.Text))
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(_scriptPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllText(_scriptPath, _txt.Text, new UTF8Encoding(false));
                        _originalText = _txt.Text;
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine("流程图落盘失败: " + ex.Message); }
                }
                using var flow = new DialogueFlowForm(_scriptPath, NpcUtil.SafeName(npc));
                flow.ShowDialog(this);
                // 流程图保存后重载文本
                if (File.Exists(_scriptPath))
                {
                    _txt.Text = File.ReadAllText(_scriptPath);
                    _originalText = _txt.Text;
                }
                _onSaved?.Invoke();
            };
            var btnSave = new Button { Text = "保存", Width = 80 };
            var btnClose = new Button { Text = "关闭", Width = 80 };
            btnSave.Click += BtnSave_Click;
            btnClose.Click += (s, e) => Close();
            bottom.Controls.AddRange(new Control[] { btnFlow, btnSave, btnClose });

            Controls.Add(_txt);
            Controls.Add(bottom);

            Load += (s, e) =>
            {
                if (File.Exists(_scriptPath))
                    _txt.Text = File.ReadAllText(_scriptPath);
                else
                    _txt.Text = "; 新建脚本\r\n[@main]\r\n#IF\r\n#SAY\r\n你好。\r\n<关闭/@exit>\r\n";
                _originalText = _txt.Text;
            };

            // 关闭时若有未保存改动，提示选择「保存/丢弃/取消」
            FormClosing += (s, e) =>
            {
                if (_txt.Text == _originalText) return;
                var r = MessageBox.Show(this, "脚本有未保存的修改，是否保存？", "提示",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.Yes && !SaveSilent()) e.Cancel = true;
            };
        }

        private bool SaveSilent()
        {
            try
            {
                var dir = Path.GetDirectoryName(_scriptPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_scriptPath, _txt.Text, new UTF8Encoding(false));
                _originalText = _txt.Text;
                _onSaved?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (SaveSilent())
                MessageBox.Show(this, "脚本已保存。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
