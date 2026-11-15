using System.Data;

namespace Server.Database
{
    public partial class SetBuffsInfoForm : Form
    {
        private SetBuffsSettings _settings;
        private int _editingIndex = -1;
        private bool _updatingList;
        private bool _changed;
        private string FilePath => Path.Combine(Settings.EnvirPath, "SetBuffs.txt");

        public sealed class SetBuffsSettings
        {
            public List<SetBuffInfo> Buffs { get; set; } = new List<SetBuffInfo>();

            public sealed class SetBuffInfo
            {
                public string Name { get; set; }
                public Dictionary<Stat, int> Stats { get; set; } = new Dictionary<Stat, int>();
            }
        }

        public SetBuffsInfoForm()
        {
            InitializeComponent();

            // 20261115 汉化（H8）：枚举下拉与属性列显示走本地化映射（写盘键保持枚举名不动）
            var statTable = BuildEnumTable<Stat>();
            cmbStat.DataSource = statTable;
            cmbStat.DisplayMember = "Display";
            cmbStat.ValueMember = "Value";

            var buffTable = BuildEnumTable<BuffType>();
            cmbBuffType.DataSource = buffTable;
            cmbBuffType.DisplayMember = "Display";
            cmbBuffType.ValueMember = "Value";

            cmbBuffType.DropDownStyle = ComboBoxStyle.DropDownList;

            // 20261115 汉化（H8）：属性列显示中文映射（CellFormatting 只改显示，Value 仍为枚举）
            dgvStats.CellFormatting += (s, args) =>
            {
                if (dgvStats.Columns[args.ColumnIndex]?.Name == "colStat" &&
                    args.Value is Stat stat)
                {
                    args.Value = stat.ToLocalizedServerString();
                    args.FormattingApplied = true;
                }
            };

            LoadSettings();
        }

        /// <summary>20261115 汉化（H8）：枚举→DataTable（Value=枚举本身保强转链，Display=本地化中文）</summary>
        private static DataTable BuildEnumTable<T>() where T : struct, Enum
        {
            var table = new DataTable();
            table.Columns.Add("Value", typeof(T));
            table.Columns.Add("Display", typeof(string));

            foreach (T value in Enum.GetValues(typeof(T)))
                table.Rows.Add(value, value.ToLocalizedServerString());

            return table;
        }

        private void LoadSettings()
        {
            _settings = new SetBuffsSettings();

            if (!File.Exists(FilePath))
            {
                File.WriteAllText(FilePath, string.Empty);
            }
            else
            {
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split(
                        new[] { ';' },
                        StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 0) continue;

                    var name = parts[0].Trim();

                    if (!Enum.TryParse<BuffType>(name, out _))
                        continue;

                    if (_settings.Buffs.Any(x => x.Name == name))
                        continue;

                    var buff = new SetBuffsSettings.SetBuffInfo
                    {
                        Name = name
                    };

                    for (int i = 1; i < parts.Length; i++)
                    {
                        var statParts = parts[i].Split('=');

                        if (statParts.Length != 2) continue;

                        if (!Enum.TryParse(
                            statParts[0].Trim(),
                            out Stat stat))
                            continue;

                        if (!int.TryParse(
                            statParts[1].Trim(),
                            out int value))
                            continue;

                        buff.Stats[stat] = value;
                    }

                    _settings.Buffs.Add(buff);
                }
            }

            lstBuffs.Items.Clear();

            foreach (var buff in _settings.Buffs)
                lstBuffs.Items.Add(buff.Name);
        }

        private void lstBuffs_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_updatingList) return;

            SaveCurrentBuff();

            if (lstBuffs.SelectedIndex < 0) return;

            _editingIndex = lstBuffs.SelectedIndex;

            if (Enum.TryParse<BuffType>(
                _settings.Buffs[_editingIndex].Name,
                out var type))
            {
                // 20261115 汉化（H8）：DataSource 换 DataTable 后 SelectedItem 变 DataRowView，用 SelectedValue 定位
                cmbBuffType.SelectedValue = type;
            }
            else
            {
                cmbBuffType.SelectedIndex = -1;
            }

            dgvStats.Rows.Clear();

            foreach (var stat in _settings.Buffs[_editingIndex].Stats)
                dgvStats.Rows.Add(stat.Key, stat.Value);
        }

        private void btnAddStat_Click(object sender, EventArgs e)
        {
            // 20261115 汉化（H8）：DataSource 换 DataTable 后取 SelectedValue（枚举值本身）
            if (cmbStat.SelectedValue == null) return;

            dgvStats.Rows.Add((Stat)cmbStat.SelectedValue, 0);
            _changed = true;
        }

        private void btnDeleteStat_Click(object sender, EventArgs e)
        {
            if (dgvStats.CurrentRow == null) return;

            if (dgvStats.CurrentRow.IsNewRow) return;

            dgvStats.Rows.Remove(dgvStats.CurrentRow);
            _changed = true;
        }

        private void btnAddBuff_Click(object sender, EventArgs e)
        {
            // 20261115 汉化（H8）：DataSource 换 DataTable 后取 SelectedValue（枚举值本身）
            if (cmbBuffType.SelectedValue == null) return;

            var type = (BuffType)cmbBuffType.SelectedValue;
            var name = type.ToString();

            if (_settings.Buffs.Any(x => x.Name == name))
            {
                MessageBox.Show("这个 Buff 已经存在！");
                return;
            }

            var buff = new SetBuffsSettings.SetBuffInfo
            {
                Name = name
            };

            _settings.Buffs.Add(buff);

            _updatingList = true;
            lstBuffs.Items.Add(buff.Name);
            lstBuffs.SelectedIndex = lstBuffs.Items.Count - 1;
            _updatingList = false;

            _editingIndex = lstBuffs.SelectedIndex;
            // 20261115 汉化（H8）：DataSource 换 DataTable 后用 SelectedValue 定位
            cmbBuffType.SelectedValue = type;
            dgvStats.Rows.Clear();
            _changed = true;
        }

        private void btnDeleteBuff_Click(object sender, EventArgs e)
        {
            if (lstBuffs.SelectedIndex < 0) return;

            int index = lstBuffs.SelectedIndex;

            _settings.Buffs.RemoveAt(index);
            lstBuffs.Items.RemoveAt(index);

            _editingIndex = -1;
            cmbBuffType.SelectedIndex = -1;
            dgvStats.Rows.Clear();
            _changed = true;
        }

        private void SaveCurrentBuff()
        {
            if (_editingIndex < 0 || _editingIndex >= _settings.Buffs.Count) return;

            var buff = _settings.Buffs[_editingIndex];

            if (cmbBuffType.SelectedValue != null)
            {
                var newName = ((BuffType)cmbBuffType.SelectedValue).ToString();

                if (newName != buff.Name &&
                    _settings.Buffs.Any(x => x != buff && x.Name == newName))
                {
                    MessageBox.Show("这个 Buff 已经存在！");
                    // 20261115 汉化（H8）：回退选中用 SelectedValue（原 Enum.Parse 语义等价）
                    cmbBuffType.SelectedValue = Enum.Parse<BuffType>(buff.Name);
                    return;
                }

                buff.Name = newName;
            }

            buff.Stats.Clear();

            foreach (DataGridViewRow row in dgvStats.Rows)
            {
                if (row.IsNewRow) continue;

                if (row.Cells["colStat"].Value == null ||
                    row.Cells["colValue"].Value == null)
                    continue;

                if (!Enum.TryParse(
                    row.Cells["colStat"].Value.ToString(),
                    out Stat stat))
                    continue;

                if (!int.TryParse(
                    row.Cells["colValue"].Value.ToString(),
                    out int value))
                    continue;

                buff.Stats[stat] = value;
            }

            if (_editingIndex >= 0 &&
                _editingIndex < lstBuffs.Items.Count)
            {
                _updatingList = true;
                lstBuffs.Items[_editingIndex] = buff.Name;
                _updatingList = false;
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            SaveCurrentBuff();

            var lines = new List<string>();

            foreach (var buff in _settings.Buffs)
            {
                var line = buff.Name;

                foreach (var stat in buff.Stats)
                    line += ";" + stat.Key + "=" + stat.Value;

                line += ";;";
                lines.Add(line);
            }

            File.WriteAllLines(FilePath, lines);

            _changed = false;
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            if (_changed)
            {
                var result = MessageBox.Show(
                    "有未保存的修改，是否保存？",
                    "SetBuffs",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Cancel) return;

                if (result == DialogResult.Yes)
                    btnSave_Click(null, null);
            }

            Close();
        }

        private void dgvStats_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            _changed = true;
        }

        private void btnImport_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "导入 SetBuffs";
                dialog.Filter = "SetBuffs 文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                dialog.InitialDirectory = Path.Combine(
                    Settings.EnvirPath,
                    "Exports");

                if (dialog.ShowDialog() != DialogResult.OK) return;

                _settings = new SetBuffsSettings();

                foreach (var line in File.ReadAllLines(dialog.FileName))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split(
                        new[] { ';' },
                        StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 0) continue;

                    var name = parts[0].Trim();

                    if (!Enum.TryParse<BuffType>(name, out _))
                        continue;

                    if (_settings.Buffs.Any(x => x.Name == name))
                        continue;

                    var buff = new SetBuffsSettings.SetBuffInfo
                    {
                        Name = name
                    };

                    for (int i = 1; i < parts.Length; i++)
                    {
                        var statParts = parts[i].Split('=');

                        if (statParts.Length != 2) continue;

                        if (!Enum.TryParse(
                            statParts[0].Trim(),
                            out Stat stat))
                            continue;

                        if (!int.TryParse(
                            statParts[1].Trim(),
                            out int value))
                            continue;

                        buff.Stats[stat] = value;
                    }

                    _settings.Buffs.Add(buff);
                }

                lstBuffs.Items.Clear();

                foreach (var buff in _settings.Buffs)
                    lstBuffs.Items.Add(buff.Name);

                _editingIndex = -1;
                cmbBuffType.SelectedIndex = -1;
                dgvStats.Rows.Clear();
                _changed = true;

                MessageBox.Show("导入完成！");
            }
        }

        private void btnExport_Click(object sender, EventArgs e)
        {
            SaveCurrentBuff();

            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "导出 SetBuffs";
                dialog.Filter = "SetBuffs 文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                dialog.FileName = "SetBuffs.txt";
                dialog.InitialDirectory = Path.Combine(
                    Settings.EnvirPath,
                    "Exports");

                if (dialog.ShowDialog() != DialogResult.OK) return;

                var lines = new List<string>();

                foreach (var buff in _settings.Buffs)
                {
                    var line = buff.Name;

                    foreach (var stat in buff.Stats)
                        line += ";" + stat.Key + "=" + stat.Value;

                    line += ";;";
                    lines.Add(line);
                }

                File.WriteAllLines(dialog.FileName, lines);

                MessageBox.Show("导出完成！");
            }
        }
    }
}