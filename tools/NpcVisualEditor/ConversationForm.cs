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

        public ConversationForm(NPCInfo npc, string scriptPath, Action onSaved)
        {
            _npc = npc;
            _scriptPath = scriptPath;
            _onSaved = onSaved;

            Text = $"脚本编辑 — {npc.GameName}（{_scriptPath}）";
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

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            var btnFlow = new Button { Text = "对话流程图", Location = new Point(140, 6), Width = 100 };
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
                    }
                    catch { }
                }
                using var flow = new DialogueFlowForm(_scriptPath, npc.GameName);
                flow.ShowDialog(this);
                // 流程图保存后重载文本
                if (File.Exists(_scriptPath))
                    _txt.Text = File.ReadAllText(_scriptPath);
                _onSaved?.Invoke();
            };
            var btnSave = new Button { Text = "保存", Location = new Point(340, 6), Width = 80 };
            var btnClose = new Button { Text = "关闭", Location = new Point(440, 6), Width = 80 };
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
            };
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                var dir = Path.GetDirectoryName(_scriptPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                // UTF-8 无 BOM（引擎 NPCScript 读取约定）
                File.WriteAllText(_scriptPath, _txt.Text, new UTF8Encoding(false));
                _onSaved?.Invoke();
                MessageBox.Show(this, "脚本已保存。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
