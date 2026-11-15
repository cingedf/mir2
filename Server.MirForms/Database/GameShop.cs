using Server.MirEnvir;

namespace Server
{
    public partial class GameShop : Form
    {

        private List<GameShopItem> SelectedItems;

        public Envir Envir => SMain.EditEnvir;

        public GameShop()
        {
            InitializeComponent();

            LoadGameShopItems();

            GameShopSearchBox_TextChanged(this, EventArgs.Empty);
        }

        private void GameShop_Load(object sender, EventArgs e)
        {
            UpdateInterface();
            LoadItemsIntoComboBox();
            ItemComboBox.SelectedIndex = -1;
        }

        private void GameShop_FormClosed(object sender, FormClosedEventArgs e)
        {
            Envir.SaveDB();
        }

        public class ListBoxItem
        {
            public string DisplayMember { get; set; }
            public object ValueMember { get; set; }

            public override string ToString()
            {
                return DisplayMember;
            }
        }

        private void LoadGameShopItems()
        {


            ClassFilter_lb.Items.Clear();
            CategoryFilter_lb.Items.Clear();
            GameShopListBox.Items.Clear();

            // 20261115 修复（十问⑥）：职业筛选列表原样列数据英文 Class 值（Warrior/Archer...），改 ListBoxItem 显示中文、值保英文原值
            // 20261115 修复（三轮⑥）：职业筛选固定列出全六项（原数据驱动方式在商城数据只有 All 时只剩「全部职业」一项）；
            // 数据里出现固定集之外的 Class 值仍追加兜底，显示走 ClassDisplay、值保英文原值
            ClassFilter_lb.Items.Add(new ListBoxItem { DisplayMember = "全部职业", ValueMember = null });
            CategoryFilter_lb.Items.Add("全部分类");

            HashSet<string> seenClasses = new HashSet<string>(StringComparer.Ordinal) { "All", "Warrior", "Assassin", "Taoist", "Wizard", "Archer" };
            foreach (string fixedClass in new[] { "Warrior", "Assassin", "Taoist", "Wizard", "Archer" })
                ClassFilter_lb.Items.Add(new ListBoxItem { DisplayMember = ClassDisplay(fixedClass), ValueMember = fixedClass });

            for (int i = 0; i < SMain.EditEnvir.GameShopList.Count; i++)
            {
                string cls = SMain.EditEnvir.GameShopList[i].Class;
                if (seenClasses.Add(cls)) ClassFilter_lb.Items.Add(new ListBoxItem { DisplayMember = ClassDisplay(cls), ValueMember = cls });
                if (!CategoryFilter_lb.Items.Contains(SMain.EditEnvir.GameShopList[i].Category)) CategoryFilter_lb.Items.Add(SMain.EditEnvir.GameShopList[i].Category);

                GameShopListBox.Items.Add(SMain.EditEnvir.GameShopList[i]);
            }

            ClassFilter_lb.Text = "全部职业";
            CategoryFilter_lb.Text = "全部分类";
            SectionFilter_lb.Text = "全部物品";
        }

        // 20261115 修复（十问⑥）：英文 Class 原值→中文显示名映射（未知值回退原样）
        private static string ClassDisplay(string value)
        {
            switch (value)
            {
                case "All": return "全部";
                case "Warrior": return "战士";
                case "Assassin": return "刺客";
                case "Taoist": return "道士";
                case "Wizard": return "法师";
                case "Archer": return "弓箭手";
                default: return value;
            }
        }

        private void UpdateGameShopList()
        {

            GameShopListBox.Items.Clear();
            for (int i = 0; i < SMain.EditEnvir.GameShopList.Count; i++)
            {
                // 20261115 修复（十问⑥）：职业筛选改按选中项 ValueMember（英文原值）比对数据；哨兵 ValueMember=null 即全选
                // 20261115 汉化（H10）：过滤哨兵标签与显示标签整体一致替换为中文（designer 预置同步），比较逻辑等价
                ListBoxItem selectedClassItem = ClassFilter_lb.SelectedItem as ListBoxItem;
                string selectedClass = selectedClassItem?.ValueMember as string;
                if (selectedClass == null || SMain.EditEnvir.GameShopList[i].Class == selectedClass)
                    if (SectionFilter_lb.Text == "全部物品" || SMain.EditEnvir.GameShopList[i].TopItem && SectionFilter_lb.Text == "推荐物品" || SMain.EditEnvir.GameShopList[i].Deal && SectionFilter_lb.Text == "特价物品" || SMain.EditEnvir.GameShopList[i].Date > Envir.Now.AddDays(-7) && SectionFilter_lb.Text == "新品物品")
                        if (CategoryFilter_lb.Text == "全部分类" || SMain.EditEnvir.GameShopList[i].Category == CategoryFilter_lb.Text)
                            GameShopListBox.Items.Add(SMain.EditEnvir.GameShopList[i]);
            }
        }

        private void GameShopListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateInterface();
        }

        public void UpdateInterface(bool refreshList = false)
        {
            SelectedItems = GameShopListBox.SelectedItems.Cast<GameShopItem>().ToList();

            if (SelectedItems.Count == 0)
            {
                GoldPrice_textbox.Text = String.Empty;
                GPPrice_textbox.Text = String.Empty;
                Stock_textbox.Text = String.Empty;
                Individual_checkbox.Checked = false;
                Class_combo.SelectedValue = "All";
                Category_textbox.Text = "";
                TopItem_checkbox.Checked = false;
                DealofDay_checkbox.Checked = false;
                CredxGold_textbox.Text = Settings.CredxGold.ToString();
                ItemDetails_gb.Visible = false;
                TotalSold_label.Text = "0";
                LeftinStock_label.Text = "";
                Count_textbox.Text = String.Empty;
                CreditOnlyBox.Checked = false;
                GoldOnlyBox.Checked = false;

                // Reset ComboBox
                ItemComboBox.SelectedIndex = -1;

                return;
            }

            ItemDetails_gb.Visible = true;

            GoldPrice_textbox.Text = SelectedItems[0].GoldPrice.ToString();
            GPPrice_textbox.Text = SelectedItems[0].CreditPrice.ToString();
            Stock_textbox.Text = SelectedItems[0].Stock.ToString();
            Individual_checkbox.Checked = SelectedItems[0].iStock;
            // 20261115 汉化（十问⑥）：DataSource 绑定后按 SelectedValue（英文原值）回显
            Class_combo.SelectedValue = SelectedItems[0].Class;
            if (Class_combo.SelectedIndex < 0) Class_combo.SelectedIndex = 0; // 非"全部"职业的存盘值（如 None）回退到"全部"显示，不误写回
            Category_textbox.Text = SelectedItems[0].Category;
            TopItem_checkbox.Checked = SelectedItems[0].TopItem;
            DealofDay_checkbox.Checked = SelectedItems[0].Deal;
            Count_textbox.Text = SelectedItems[0].Count.ToString();
            CreditOnlyBox.Checked = SelectedItems[0].CanBuyCredit;
            GoldOnlyBox.Checked = SelectedItems[0].CanBuyGold;

            // Set the ItemComboBox selection to match the ItemIndex
            if (SelectedItems[0].Info != null && !string.IsNullOrEmpty(SelectedItems[0].Info.Name))
            {
                var itemName = SelectedItems[0].Info.Name;

                // Select the corresponding item in the ComboBox
                if (ItemComboBox.Items.Contains(itemName))
                {
                    ItemComboBox.SelectedItem = itemName;
                }
                else
                {
                    ItemComboBox.SelectedIndex = -1; // Reset if no match found
                }
            }
            else
            {
                ItemComboBox.SelectedIndex = -1; // Reset if no valid Info or Name
            }

            GetStats();
        }

        private void GetStats()
        {
            int purchased;

            SMain.Envir.GameshopLog.TryGetValue(SelectedItems[0].GIndex, out purchased);
            TotalSold_label.Text = purchased.ToString();

            if (!Individual_checkbox.Checked && SelectedItems[0].Stock != 0)
            {
                if (SelectedItems[0].Stock - purchased >= 0)
                    LeftinStock_label.Text = (SelectedItems[0].Stock - purchased).ToString();
                else
                    LeftinStock_label.Text = "";
            }
            else if (SelectedItems[0].Stock == 0)
            {
                LeftinStock_label.Text = "无限的";
            }
            else if (Individual_checkbox.Checked)
            {
                LeftinStock_label.Text = "不能单独结算";
            }
        }

        private void GoldPrice_textbox_TextChanged(object sender, EventArgs e)
        {

            uint temp;

            if (!uint.TryParse(GoldPrice_textbox.Text, out temp))
            {
                GoldPrice_textbox.BackColor = Color.Red;
                return;
            }

            GoldPrice_textbox.BackColor = SystemColors.Window;

            for (int i = 0; i < SelectedItems.Count; i++)
                SelectedItems[i].GoldPrice = temp;
        }

        private void GPPrice_textbox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            uint temp;

            if (!uint.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }

            ActiveControl.BackColor = SystemColors.Window;

            for (int i = 0; i < SelectedItems.Count; i++)
                SelectedItems[i].CreditPrice = temp;

            if (ActiveControl.Text != "") GoldPrice_textbox.Text = (temp * Settings.CredxGold).ToString();
        }

        private void Class_combo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            // 20261115 汉化（十问⑥）：DataSource 绑定后取 SelectedValue（英文原值）写回 item.Class，显示中文但存盘英文不变
            string temp = Class_combo.SelectedValue as string;

            for (int i = 0; i < SelectedItems.Count; i++)
                SelectedItems[i].Class = temp;
        }

        private void TopItem_checkbox_CheckedChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            for (int i = 0; i < SelectedItems.Count; i++)
                SelectedItems[i].TopItem = TopItem_checkbox.Checked;
        }

        private void Remove_button_Click(object sender, EventArgs e)
        {
            if (SelectedItems.Count == 0) return;

            if (MessageBox.Show("确定要删除选定物品？", "删除商城物品", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            for (int i = 0; i < SelectedItems.Count; i++) Envir.Remove(SelectedItems[i]);

            LoadGameShopItems();
            UpdateInterface();
        }

        private void DealofDay_checkbox_CheckedChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            for (int i = 0; i < SelectedItems.Count; i++)
                SelectedItems[i].Deal = DealofDay_checkbox.Checked;
        }

        private void Category_textbox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;
            string temp = ActiveControl.Text;

            for (int i = 0; i < SelectedItems.Count; i++)
                SelectedItems[i].Category = temp;
        }

        private void Stock_textbox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            int temp;

            if (!int.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }

            ActiveControl.BackColor = SystemColors.Window;

            for (int i = 0; i < SelectedItems.Count; i++)
                SelectedItems[i].Stock = temp;

            GetStats();
        }

        private void Individual_checkbox_CheckedChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            for (int i = 0; i < SelectedItems.Count; i++)
                SelectedItems[i].iStock = Individual_checkbox.Checked;

        }

        private void CredxGold_textbox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            short temp;

            if (!short.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }

            ActiveControl.BackColor = SystemColors.Window;
            Settings.CredxGold = temp;
        }

        private void Count_textbox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            ushort temp;

            if (!ushort.TryParse(ActiveControl.Text, out temp) || temp > 999)
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }

            if (temp < 1)
            {
                temp = 1;
                ActiveControl.Text = "1";
            }
            else if (temp > SelectedItems[0].Info.StackSize)
            {
                temp = SelectedItems[0].Info.StackSize;
                ActiveControl.Text = SelectedItems[0].Info.StackSize.ToString();
            }

            ActiveControl.BackColor = SystemColors.Window;
            SelectedItems[0].Count = temp;
        }

        private void ClassFilter_lb_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateGameShopList();
        }

        private void SectionFilter_lb_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateGameShopList();
        }

        private void CategoryFilter_lb_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateGameShopList();
        }

        private void ResetFilter_button_Click(object sender, EventArgs e)
        {
            ClassFilter_lb.Text = "全部职业";
            CategoryFilter_lb.Text = "全部分类";
            SectionFilter_lb.Text = "全部物品";
            UpdateGameShopList();

        }

        private void ServerLog_button_Click(object sender, EventArgs e)
        {
            if (SMain.Envir.Running)
            {
                if (MessageBox.Show("重置购买日志无法恢复，库存将设置为默认值，操作立即生效。", "删除日志", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                SMain.Envir.ClearGameshopLog();
            }
            else
            {
                if (MessageBox.Show("重置购买日志无法恢复，库存将设置为默认值，启动服务器时生效", "删除日志", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                SMain.Envir.ResetGS = true;
            }
        }
        private void GoldOnlyBox_CheckedChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender)
                return;

            for (int i = 0; i < SelectedItems.Count; i++)
                SelectedItems[i].CanBuyGold = GoldOnlyBox.Checked;
        }

        private void CreditOnly_CheckedChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender)
                return;

            for (int i = 0; i < SelectedItems.Count; i++)
                SelectedItems[i].CanBuyCredit = CreditOnlyBox.Checked;
        }

        #region Load Items
        private void LoadItemsIntoComboBox()
        {
            ItemComboBox.Items.Clear();

            // Add "None" as a default option
            ItemComboBox.Items.Add("None");

            // Add all items from ItemInfoList
            foreach (var item in SMain.EditEnvir.ItemInfoList)
            {
                if (!string.IsNullOrEmpty(item.Name))
                {
                    ItemComboBox.Items.Add($"{item.Name}");
                }
            }

            ItemComboBox.SelectedIndex = 0; // Default to "None"
        }
        #endregion

        private void Add_Button_Click(object sender, EventArgs e)
        {
            if (SMain.EditEnvir.ItemInfoList == null || SMain.EditEnvir.ItemInfoList.Count == 0)
            {
                MessageBox.Show("没有物品可添加", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Get the first item's index as default
            var defaultItem = SMain.EditEnvir.ItemInfoList.First();
            int firstItemIndex = defaultItem.Index;

            // Find the next available GIndex
            int nextGIndex = SMain.EditEnvir.GameShopList.Count > 0
                ? SMain.EditEnvir.GameShopList.Max(item => item.GIndex) + 1
                : 1;

            // Create the new GameShopItem
            var newItem = new GameShopItem
            {
                GIndex = nextGIndex,
                GoldPrice = 0,
                CreditPrice = 0,
                ItemIndex = firstItemIndex,
                Info = defaultItem,
                Date = DateTime.Now,
                Class = "None",
                Category = "None"
            };

            // Add to GameShopList (main data source)
            SMain.EditEnvir.GameShopList.Add(newItem);

            // Add to GameShopListBox for UI display
            GameShopListBox.Items.Add(newItem);

            // Set ComboBox to the first item's name
            ItemComboBox.SelectedItem = $"{defaultItem.Name}";

            // Save the database
            Envir.SaveDB();
        }

        private void ItemComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Ensure we have a selected GameShopItem
            if (SelectedItems == null || SelectedItems.Count == 0)
                return;

            // Get the selected item name
            var selectedName = ItemComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedName) || selectedName == "None")
                return;

            // Find the corresponding ItemInfo object by name
            var newItemInfo = SMain.EditEnvir.ItemInfoList
                .FirstOrDefault(x => x.Name == selectedName);

            if (newItemInfo == null)
                return;

            // Update the selected GameShopItem
            var selectedGameShopItem = SelectedItems[0];
            selectedGameShopItem.ItemIndex = newItemInfo.Index;
            selectedGameShopItem.Info = newItemInfo;

            // Refresh the GameShopListBox to reflect changes
            int selectedIndex = GameShopListBox.SelectedIndex;
            GameShopListBox.Items[selectedIndex] = selectedGameShopItem;
        }

        #region Search Box
        private void GameShopSearchBox_TextChanged(object sender, EventArgs e)
        {
            string searchText = GameShopSearchBox.Text.Trim().ToLower();

            GameShopListBox.Items.Clear();

            foreach (var item in SMain.EditEnvir.GameShopList)
            {
                // Add to list if search text is empty or the item matches the search criteria
                if (string.IsNullOrEmpty(searchText) ||
                    (!string.IsNullOrEmpty(item.Info?.Name) && item.Info.Name.ToLower().Contains(searchText)))
                {
                    GameShopListBox.Items.Add(item);
                }
            }
        }

        #endregion

        private void ExportButton_Click(object sender, EventArgs e)
        {
            if (GameShopListBox.Items.Count == 0)
            {
                MessageBox.Show("没有物品可导出", "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string exportDir = Path.Combine(Application.StartupPath, "Exports");
            if (!Directory.Exists(exportDir))
                Directory.CreateDirectory(exportDir);

            SaveFileDialog saveDialog = new SaveFileDialog
            {
                Title = "GameShop Info",
                Filter = "Text Files (*.txt)|*.txt",
                FileName = "7_商城数据.txt",
                InitialDirectory = exportDir
            };

            if (saveDialog.ShowDialog() != DialogResult.OK)
                return;

            List<string> lines = new List<string>
            {
                "GameShop Info",
                ""
            };

            foreach (var obj in GameShopListBox.Items)
            {
                if (obj is GameShopItem item && item.Info != null)
                {
                    string name = item.Info.Name;
                    uint gp = item.CreditPrice;
                    uint gold = item.GoldPrice;

                    lines.Add($"{name}: GameGold: {gp:n0} Gold: {gold:n0}");
                }
            }

            File.WriteAllLines(saveDialog.FileName, lines);

            MessageBox.Show("导出完成", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
