using System;
using System.IO;
using System.Windows.Forms;
using Server.MirEnvir;

namespace NpcVisualEditor
{
    /// <summary>
    /// 启动器：选择服务端/客户端目录，加载 Server.MirDB 后进入主编辑器。
    /// 复刻原版 Form1（移除注册面板）。
    /// </summary>
    public class LauncherForm : Form
    {
        private ComboBox _cmbServer, _cmbClient;
        private Button _btnEnter, _btnExit;
        private Label _lblStatus;

        private static readonly string ConfigFile =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "editor.ini");

        public LauncherForm()
        {
            Text = "NPC 可视化编辑器 — Crystal 版 V1.0";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new System.Drawing.Size(640, 220);

            var lblServer = new Label { Text = "服务端目录:", Location = new System.Drawing.Point(20, 20), AutoSize = true };
            _cmbServer = new ComboBox { Location = new System.Drawing.Point(110, 16), Width = 440 };
            var btnBrowseServer = new Button { Text = "浏览...", Location = new System.Drawing.Point(560, 14), Width = 60 };

            var lblClient = new Label { Text = "客户端目录:", Location = new System.Drawing.Point(20, 56), AutoSize = true };
            _cmbClient = new ComboBox { Location = new System.Drawing.Point(110, 52), Width = 440 };
            var btnBrowseClient = new Button { Text = "浏览...", Location = new System.Drawing.Point(560, 50), Width = 60 };
            var lblClientHint = new Label { Text = "（用于读取小地图 Client\\Map\\MiniMaps_Export）", Location = new System.Drawing.Point(110, 80), AutoSize = true, ForeColor = System.Drawing.Color.Gray };

            _btnEnter = new Button { Text = "进入编辑器", Location = new System.Drawing.Point(200, 115), Width = 110, Height = 32 };
            _btnExit = new Button { Text = "退出", Location = new System.Drawing.Point(330, 115), Width = 80, Height = 32 };
            _lblStatus = new Label { Text = "", Location = new System.Drawing.Point(20, 160), AutoSize = true, ForeColor = System.Drawing.Color.Firebrick };

            Controls.AddRange(new Control[] {
                lblServer, _cmbServer, btnBrowseServer,
                lblClient, _cmbClient, btnBrowseClient, lblClientHint,
                _btnEnter, _btnExit, _lblStatus });

            btnBrowseServer.Click += (s, e) => BrowseFolder(_cmbServer);
            btnBrowseClient.Click += (s, e) => BrowseFolder(_cmbClient);
            _btnEnter.Click += BtnEnter_Click;
            _btnExit.Click += (s, e) => Close();
            AcceptButton = _btnEnter;
            CancelButton = _btnExit;   // Esc 退出

            LoadHistory();
        }

        private void BrowseFolder(ComboBox cmb)
        {
            using var dlg = new FolderBrowserDialog { ShowNewFolderButton = false };
            if (Directory.Exists(cmb.Text)) dlg.SelectedPath = cmb.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK) cmb.Text = dlg.SelectedPath;
        }

        private void LoadHistory()
        {
            if (!File.Exists(ConfigFile)) return;
            foreach (var line in File.ReadAllLines(ConfigFile))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim(), val = line[(eq + 1)..].Trim();
                if (key == "server" && val.Length > 0) _cmbServer.Items.Add(val);
                if (key == "client" && val.Length > 0) _cmbClient.Items.Add(val);
            }
            if (_cmbServer.Items.Count > 0) _cmbServer.SelectedIndex = 0;
            if (_cmbClient.Items.Count > 0) _cmbClient.SelectedIndex = 0;
        }

        private void SaveHistory()
        {
            using var w = new StreamWriter(ConfigFile);
            if (_cmbServer.Text.Length > 0) w.WriteLine("server=" + _cmbServer.Text);
            if (_cmbClient.Text.Length > 0) w.WriteLine("client=" + _cmbClient.Text);
        }

        private void BtnEnter_Click(object sender, EventArgs e)
        {
            string serverDir = _cmbServer.Text.Trim();
            if (!Directory.Exists(serverDir))
            {
                MessageBox.Show(this, "服务端目录不存在。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string dbPath = Path.Combine(serverDir, "Server.MirDB");
            if (!File.Exists(dbPath))
            {
                MessageBox.Show(this, $"未找到 {dbPath}\r\n请选择包含 Server.MirDB 的服务端目录。", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _lblStatus.ForeColor = System.Drawing.Color.DarkGreen;
            _lblStatus.Text = "正在加载数据库，请稍候...";
            Enabled = false;
            Cursor = Cursors.WaitCursor;

            try
            {
                // 引擎全部路径（EnvirPath/NPCPath/MapPath...）相对工作目录解析
                Environment.CurrentDirectory = serverDir;

                var envir = Envir.Edit;
                if (!envir.LoadDB())
                {
                    _lblStatus.ForeColor = System.Drawing.Color.Firebrick;
                    _lblStatus.Text = "数据库加载失败（版本不兼容或文件损坏）。";
                    Enabled = true;
                    Cursor = Cursors.Default;
                    return;
                }

                SaveHistory();

                string clientDir = _cmbClient.Text.Trim();
                Hide();
                using (var main = new MainForm(envir, serverDir, clientDir))
                {
                    main.ShowDialog();
                }
                Close();
            }
            catch (Exception ex)
            {
                _lblStatus.ForeColor = System.Drawing.Color.Firebrick;
                _lblStatus.Text = "加载失败: " + ex.Message;
                Enabled = true;
                Cursor = Cursors.Default;
            }
        }
    }
}
