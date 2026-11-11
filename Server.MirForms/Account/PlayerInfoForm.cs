using Server.MirDatabase;
using Server.MirObjects;
using System.Diagnostics;

namespace Server
{
    public partial class PlayerInfoForm : Form
    {
        CharacterInfo Character = null;

        public PlayerInfoForm()
        {
            InitializeComponent();
        }

        public PlayerInfoForm(uint playerId)
        {
            InitializeComponent();

            PlayerObject player = SMain.Envir.GetPlayer(playerId);

            if (player == null)
            {
                Close();
                return;
            }

            Character = SMain.Envir.GetCharacterInfo(player.Name);

            UpdateTabs();
        }

        #region PlayerInfo
        private void UpdatePlayerInfo()
        {
            IndexTextBox.Text = Character.Index.ToString();
            NameTextBox.Text = Character.Name;
            LevelTextBox.Text = Character.Level.ToString();
            PKPointsTextBox.Text = Character.PKPoints.ToString();
            GoldTextBox.Text = $"{Character.AccountInfo.Gold:n0}";
            GameGoldTextBox.Text = String.Format("{0:n0}", Character.AccountInfo.Credit);


            if (Character?.Player != null)
            {
                CurrentMapLabel.Text = $"{Character.Player.CurrentMap.Info.Title} / {Character.Player.CurrentMap.Info.FileName}";
                CurrentXY.Text = $"X:{Character.CurrentLocation.X}: Y:{Character.CurrentLocation.Y}";

                ExpTextBox.Text = $"{string.Format("{0:#0.##%}", Character.Player.Experience / (double)Character.Player.MaxExperience)}";
                ACBox.Text = $"{Character.Player.Stats[Stat.MinAC]}-{Character.Player.Stats[Stat.MaxAC]}";
                AMCBox.Text = $"{Character.Player.Stats[Stat.MinMAC]}-{Character.Player.Stats[Stat.MaxMAC]}";
                DCBox.Text = $"{Character.Player.Stats[Stat.MinDC]}-{Character.Player.Stats[Stat.MaxDC]}";
                MCBox.Text = $"{Character.Player.Stats[Stat.MinMC]}-{Character.Player.Stats[Stat.MaxMC]}";
                SCBox.Text = $"{Character.Player.Stats[Stat.MinSC]}-{Character.Player.Stats[Stat.MaxSC]}";
                ACCBox.Text = $"{Character.Player.Stats[Stat.准确]}";
                AGILBox.Text = $"{Character.Player.Stats[Stat.敏捷]}";
                ATKSPDBox.Text = $"{Character.Player.Stats[Stat.攻击速度]}";
            }
            else
            {
                CurrentMapLabel.Text = "离线";
                CurrentXY.Text = "离线";
            }

            CurrentIPLabel.Text = Character.AccountInfo.LastIP;
            OnlineTimeLabel.Text = Character.LastLoginDate > Character.LastLogoutDate ? (SMain.Envir.Now - Character.LastLoginDate).TotalMinutes.ToString("##") + " 分钟" : "Offline";

            ChatBanExpiryTextBox.Text = Character.ChatBanExpiryDate.ToString();
        }
        #endregion

        #region PlayerPets
        private void UpdatePetInfo()
        {
            ClearPetInfo();

            if (Character?.Player == null) return;

            foreach (MonsterObject Pet in Character.Player.Pets)
            {
                var listItem = new ListViewItem(Pet.Name) { Tag = Pet };
                listItem.SubItems.Add(Pet.PetLevel.ToString());
                listItem.SubItems.Add($"{Pet.Health}/{Pet.MaxHealth}");
                listItem.SubItems.Add($"地图: {Pet.CurrentMap.Info.Title}, X: {Pet.CurrentLocation.X}, Y: {Pet.CurrentLocation.Y}");

                PetView.Items.Add(listItem);
            }
        }

        private void ClearPetInfo()
        {
            PetView.Items.Clear();
        }
        #endregion

        #region PlayerMagics
        private void UpdatePlayerMagics()
        {
            MagicListViewNF.Items.Clear();

            for (int i = 0; i < Character.Magics.Count; i++)
            {
                UserMagic magic = Character.Magics[i];
                if (magic == null) continue;

                ListViewItem ListItem = new ListViewItem(magic.Info.Name.ToString()) { Tag = this };

                ListItem.SubItems.Add(magic.Level.ToString());

                switch (magic.Level)
                {
                    case 0:
                        ListItem.SubItems.Add($"{magic.Experience}/{magic.Info.Need1}");
                        break;
                    case 1:
                        ListItem.SubItems.Add($"{magic.Experience}/{magic.Info.Need2}");
                        break;
                    case 2:
                        ListItem.SubItems.Add($"{magic.Experience}/{magic.Info.Need3}");
                        break;
                    case 3:
                        ListItem.SubItems.Add($"-");
                        break;
                }

                if (magic.Key > 8)
                {
                    var key = magic.Key % 8;

                    ListItem.SubItems.Add(string.Format("CTRL+F{0}", key != 0 ? key : 8));
                }
                else if (magic.Key > 0)
                {
                    ListItem.SubItems.Add(string.Format("F{0}", magic.Key));
                }
                else if (magic.Key == 0)
                {
                    ListItem.SubItems.Add(string.Format("未设置", magic.Key));
                }

                ListItem.SubItems.Add(magic.Key.ToString());
                MagicListViewNF.Items.Add(ListItem);
            }
        }
        #endregion

        #region PlayerQuests
        private void UpdatePlayerQuests()
        {
            QuestInfoListViewNF.Items.Clear();

            foreach (int completedQuestID in Character.CompletedQuests)
            {
                QuestInfo completedQuest = SMain.Envir.GetQuestInfo(completedQuestID);

                ListViewItem item = new ListViewItem(completedQuestID.ToString());
                item.SubItems.Add("已完成");
                item.SubItems.Add(completedQuest.Name.ToString());
                QuestInfoListViewNF.Items.Add(item);
            }

            foreach (QuestProgressInfo currentQuest in Character.CurrentQuests)
            {
                ListViewItem item = new ListViewItem(currentQuest.Index.ToString());
                item.SubItems.Add("进行中");
                item.SubItems.Add(currentQuest.Info.Name.ToString());
                QuestInfoListViewNF.Items.Add(item);
            }
        }
        #endregion

        #region PlayerItems
        private void UpdatePlayerItems()
        {
            PlayerItemInfoListViewNF.Items.Clear();

            if (Character == null) return;

            for (int i = 0; i < Character.Inventory.Length; i++)
            {
                UserItem inventoryItem = Character.Inventory[i];

                if (inventoryItem == null) continue;

                ListViewItem inventoryItemListItem = new ListViewItem($"{inventoryItem.UniqueID}");

                if (i < 6)
                {
                    inventoryItemListItem.SubItems.Add($"物品栏 | 位置: [{i + 1}]");
                }
                else if (i >= 6 && i < 46)
                {
                    inventoryItemListItem.SubItems.Add($"背包 | 位置: [{i - 5}]");
                }
                else
                {
                    inventoryItemListItem.SubItems.Add($"扩展背包 | 位置: [{i - 45}]");
                }

                inventoryItemListItem.SubItems.Add($"{inventoryItem.FriendlyName}");
                inventoryItemListItem.SubItems.Add($"{inventoryItem.Count}/{inventoryItem.Info.StackSize}");
                inventoryItemListItem.SubItems.Add($"{inventoryItem.CurrentDura}/{inventoryItem.MaxDura}");

                PlayerItemInfoListViewNF.Items.Add(inventoryItemListItem);
            }


            for (int i = 0; i < Character.QuestInventory.Length; i++)
            {
                UserItem questItem = Character.QuestInventory[i];

                if (questItem == null) continue;

                ListViewItem questItemListItem = new ListViewItem($"{questItem.UniqueID}");
                questItemListItem.SubItems.Add($"任务物品 | 位置: [{i + 1}]");

                questItemListItem.SubItems.Add($"{questItem.FriendlyName}");
                questItemListItem.SubItems.Add($"{questItem.Count}/{questItem.Info.StackSize}");
                questItemListItem.SubItems.Add($"{questItem.CurrentDura}/{questItem.MaxDura}");

                PlayerItemInfoListViewNF.Items.Add(questItemListItem);
            }

            for (int i = 0; i < Character.AccountInfo.Storage.Length; i++)
            {
                UserItem storeItem = Character.AccountInfo.Storage[i];

                if (storeItem == null) continue;

                ListViewItem storeItemListItem = new ListViewItem($"{storeItem.UniqueID}");

                if (i < 80)
                {
                    storeItemListItem.SubItems.Add($"仓库 | 位置: [{i + 1}]");
                }
                else
                {
                    storeItemListItem.SubItems.Add($"扩展仓库 | 位置: [{i - 79}]");
                }

                storeItemListItem.SubItems.Add($"{storeItem.FriendlyName}");
                storeItemListItem.SubItems.Add($"{storeItem.Count}/{storeItem.Info.StackSize}");
                storeItemListItem.SubItems.Add($"{storeItem.CurrentDura}/{storeItem.MaxDura}");

                PlayerItemInfoListViewNF.Items.Add(storeItemListItem);
            }

            for (int i = 0; i < Character.Equipment.Length; i++)
            {
                UserItem equipItem = Character.Equipment[i];

                if (equipItem == null) continue;

                ListViewItem equipItemListItem = new ListViewItem($"{equipItem.UniqueID}");

                equipItemListItem.SubItems.Add($"装备栏 | 位置: [{i + 1}]");

                equipItemListItem.SubItems.Add($"{equipItem.FriendlyName}");
                equipItemListItem.SubItems.Add($"{equipItem.Count}/{equipItem.Info.StackSize}");
                equipItemListItem.SubItems.Add($"{equipItem.CurrentDura}/{equipItem.MaxDura}");

                PlayerItemInfoListViewNF.Items.Add(equipItemListItem);
            }
        }
        #endregion

        #region Buttons
        private void UpdateButton_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("是否确定要更新", "更新", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            SaveChanges();
        }

        /// <summary>
        /// 解析 min-max 双值属性框（支持「min-max」「min-max」全角连接符或单值写法）。
        /// 单值写法时 min=max=该值；解析失败返回 null。
        /// </summary>
        private static (int min, int max)? ParseMinMax(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            text = text.Trim().Replace("－", "-").Replace("—", "-").Replace("–", "-");

            int idx = text.IndexOf('-');
            if (idx < 0)
            {
                if (!int.TryParse(text, out int single)) return null;
                return (single, single);
            }

            string minPart = text.Substring(0, idx).Trim();
            string maxPart = text.Substring(idx + 1).Trim();

            if (!int.TryParse(minPart, out int min)) return null;
            if (!int.TryParse(maxPart, out int max)) return null;

            return (min, max);
        }

        /// <summary>
        /// 方案B临时属性：把管理员填写的属性值以无限时长 buff（RemoveOnDeath|RemoveOnExit）
        /// 加到在线玩家/英雄身上，立即生效、死亡或下线自动失效，不写入存档。
        /// </summary>
        private void ApplyStatsBuff(MapObject target, string targetName)
        {
            if (target == null) return;

            var pairs = new (string text, BuffType type, Stat minStat, Stat maxStat, string label)[]
            {
                (ACBox.Text,     BuffType.防御提升,     Stat.MinAC,  Stat.MaxAC,  "物理防御"),
                (AMCBox.Text,    BuffType.魔法防御提升, Stat.MinMAC, Stat.MaxMAC, "魔法防御"),
                (DCBox.Text,     BuffType.攻击力提升,   Stat.MinDC,  Stat.MaxDC,  "物理攻击"),
                (MCBox.Text,     BuffType.魔法力提升,   Stat.MinMC,  Stat.MaxMC,  "魔法攻击"),
                (SCBox.Text,     BuffType.道术力提升,   Stat.MinSC,  Stat.MaxSC,  "道术攻击"),
            };

            var stats = new Stats();
            int applied = 0;

            foreach (var p in pairs)
            {
                var v = ParseMinMax(p.text);
                if (v == null) continue;

                stats[p.minStat] = v.Value.min;
                stats[p.maxStat] = v.Value.max;
                applied++;
            }

            var singlePairs = new (string text, BuffType type, Stat stat, string label)[]
            {
                (ACCBox.Text,    BuffType.准确提升,     Stat.准确,     "准确"),
                (AGILBox.Text,   BuffType.敏捷提升,     Stat.敏捷,     "敏捷"),
                (ATKSPDBox.Text, BuffType.攻击速度提升, Stat.攻击速度, "攻击速度"),
            };

            foreach (var p in singlePairs)
            {
                if (!int.TryParse(p.text?.Trim(), out int v)) continue;

                stats[p.stat] = v;
                applied++;
            }

            if (applied == 0)
            {
                // 全部留空 = 清除之前的临时属性
                target.RemoveBuff(BuffType.防御提升);
                target.RemoveBuff(BuffType.魔法防御提升);
                target.RemoveBuff(BuffType.攻击力提升);
                target.RemoveBuff(BuffType.魔法力提升);
                target.RemoveBuff(BuffType.道术力提升);
                target.RemoveBuff(BuffType.准确提升);
                target.RemoveBuff(BuffType.敏捷提升);
                target.RemoveBuff(BuffType.攻击速度提升);

                MessageBox.Show($"已清除{(target is HeroObject ? "英雄" : "玩家")}「{targetName}」的临时属性（未填写任何数值）。", "临时属性", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 注意：不能先 RemoveBuff 再 AddBuff 同类型——RemoveBuff 只是标记删除（FlagForRemoval），
            // 1 秒 tick 才真正移除；随后 AddBuff 会命中旧 buff 重置数值但清除不了删除标记，buff 仍会被 tick 收走。
            // 直接 AddBuff：StackType=ResetStatAndDuration 会自动覆盖旧时长与数值（无旧 buff 时新建）。
            // duration 必须给大值：duration=0 + 非 Infinite 堆叠会被 ProcessBuffs 首个 tick 判过期移除。
            target.AddBuff(BuffType.防御提升, target, int.MaxValue, stats, true, false);

                MessageBox.Show($"已对{(target is HeroObject ? "英雄" : "玩家")}「{targetName}」应用 {applied} 项临时属性（死亡或下线后失效）。", "临时属性", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SaveChanges()
        {
            CharacterInfo info = Character;

            string tempGold = GoldTextBox.Text.Replace(",", "");
            string tempCredit = GameGoldTextBox.Text.Replace(",", "");

            info.Name = NameTextBox.Text;

            if (byte.TryParse(LevelTextBox.Text, out byte level))
            {
                info.Level = level;
            }
            else
            {
                MessageBox.Show("等级输入错误");
                return;
            }

            if (int.TryParse(PKPointsTextBox.Text, out int pkPoints))
            {
                info.PKPoints = pkPoints;
            }
            else
            {
                MessageBox.Show("PK点输入错误");
                return;
            }

            if (uint.TryParse(tempGold, out uint gold))
            {
                info.AccountInfo.Gold = gold;
            }
            else
            {
                MessageBox.Show("金币输入错误");
                return;
            }

            if (uint.TryParse(tempCredit, out uint credit))
            {
                info.AccountInfo.Credit = credit;
            }
            else
            {
                MessageBox.Show("信用币输入错误");
                return;
            }

            // 属性框（方案B临时生效）：仅在线玩家可用，buff 死亡/下线自动失效
            if (Character.Player != null)
            {
                ApplyStatsBuff(Character.Player, Character.Name);
            }
            else if (ACBox.Text.Trim().Length > 0)
            {
                MessageBox.Show("玩家当前离线，临时属性仅对在线玩家生效。", "临时属性", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            UpdateTabs();
        }

        private void SendMessageButton_Click(object sender, EventArgs e)
        {
            if (Character?.Player == null) return;

            if (SendMessageTextBox.Text.Length < 1) return;

            Character.Player.ReceiveChat(SendMessageTextBox.Text, ChatType.Announcement);
        }

        private void KickButton_Click(object sender, EventArgs e)
        {
            if (Character?.Player == null) return;

            Character.Player.Connection.SendDisconnect(4);
            //also update account so player can't log back in for x minutes?
        }

        private void KillButton_Click(object sender, EventArgs e)
        {
            if (Character?.Player == null) return;

            Character.Player.Die();
        }

        private void KillPetsButton_Click(object sender, EventArgs e)
        {
            if (Character?.Player == null) return;

            for (int i = Character.Player.Pets.Count - 1; i >= 0; i--)
                Character.Player.Pets[i].Die();

            ClearPetInfo();
        }
        private void SafeZoneButton_Click(object sender, EventArgs e)
        {
            if (Character?.Player == null) return;

            Character.Player.Teleport(SMain.Envir.GetMap(Character.BindMapIndex), Character.BindLocation);
        }

        private void ChatBanButton_Click(object sender, EventArgs e)
        {
            if (Character?.Player == null) return;
            if (Character.AccountInfo.AdminAccount) return;

            Character.ChatBanned = true;

            DateTime date;

            DateTime.TryParse(ChatBanExpiryTextBox.Text, out date);

            Character.ChatBanExpiryDate = date;
        }

        private void ChatBanExpiryTextBox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            DateTime temp;

            if (!DateTime.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }
            ActiveControl.BackColor = SystemColors.Window;
        }

        private void OpenAccountButton_Click(object sender, EventArgs e)
        {
            string accountId = Character.AccountInfo.AccountID;

            AccountInfoForm form = new AccountInfoForm(accountId, true);

            form.ShowDialog();
        }

        private void CurrentIPLabel_Click(object sender, EventArgs e)
        {
            string ipAddress = CurrentIPLabel.Text;

            string url = $"https://127.0.0.1/ip/{ipAddress}";//默认 whatismyipaddress.com

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });

                CurrentIPLabel.ForeColor = Color.Blue;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开统一资源定位符时出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AccountBanButton_Click(object sender, EventArgs e)
        {
            if (Character.AccountInfo.AdminAccount) return;

            Character.AccountInfo.Banned = true;

            DateTime date;

            DateTime.TryParse(ChatBanExpiryTextBox.Text, out date);

            Character.AccountInfo.ExpiryDate = date;

            if (Character?.Player != null)
            {
                Character.Player.Connection.SendDisconnect(6);
            }
        }
        #endregion

        #region PlayerFlagSearch
        private List<ListViewItem> allFlagItems = new List<ListViewItem>();
        private void PopulatePlayerFlagsListView()
        {
            PlayerFlagsListView.Items.Clear();
            allFlagItems.Clear();

            for (int flagNumber = 1; flagNumber <= 1000; flagNumber++)
            {
                ListViewItem listItem = new ListViewItem(flagNumber.ToString());

                bool isFlagActive = flagNumber >= 0 && flagNumber < Character.Flags.Length && Character.Flags[flagNumber];

                listItem.SubItems.Add(isFlagActive ? "已激活" : "未激活");

                listItem.ForeColor = isFlagActive ? Color.Green : Color.Red;

                allFlagItems.Add(listItem);
            }

            FilterFlags();
        }
        private void FilterFlags()
        {
            PlayerFlagsListView.Items.Clear();

            foreach (var item in allFlagItems)
            {
                bool showItem = true;

                if (ActiveFlagsCheckBox.Checked && item.SubItems[1].Text != "已激活")
                {
                    showItem = false;
                }

                if (!string.IsNullOrEmpty(FlagSearchBox.Text))
                {
                    if (int.TryParse(FlagSearchBox.Text, out int searchFlagNumber))
                    {
                        if (int.Parse(item.SubItems[0].Text) != searchFlagNumber)
                        {
                            showItem = false;
                        }
                    }
                    else
                    {
                        showItem = false;
                    }
                }

                if (showItem)
                {
                    PlayerFlagsListView.Items.Add(item);
                }
            }
        }

        private void ActiveFlagsCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            FilterFlags();
        }
        private void FlagSearchBox_TextChanged(object sender, EventArgs e)
        {
            FilterFlags();
        }
        private void OpenFlagsButton_Click(object sender, EventArgs e)
        {
            string filePath = Path.Combine("Envir", "SET [].txt");

            if (!File.Exists(filePath))
            {
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    for (int i = 1; i <= 1999; i++)
                    {
                        writer.WriteLine($"[{i:D3}] -");
                    }
                }
            }

            Process.Start("notepad.exe", filePath);
        }
        private void EnableSelectedFlag_Click(object sender, EventArgs e)
        {
            if (PlayerFlagsListView.SelectedItems.Count > 0)
            {
                ListViewItem selectedItem = PlayerFlagsListView.SelectedItems[0];
                int flagIndex = int.Parse(selectedItem.Text);

                var result = MessageBox.Show("确定要启用该标志吗？", "操作确认", MessageBoxButtons.YesNo);

                if (result == DialogResult.Yes)
                {
                    if (flagIndex >= 0 && flagIndex < Character.Flags.Length)
                    {
                        Character.Flags[flagIndex] = true;

                        selectedItem.SubItems[1].Text = "已激活";
                        selectedItem.SubItems[1].ForeColor = Color.Green;
                    }
                    else
                    {
                        MessageBox.Show("标志编号无效。");
                    }
                }
            }
            else
            {
                MessageBox.Show("请先选中要启用的标志。");
            }
        }
        private void DisableSelectedFlag_Click(object sender, EventArgs e)
        {
            if (PlayerFlagsListView.SelectedItems.Count > 0)
            {
                ListViewItem selectedItem = PlayerFlagsListView.SelectedItems[0];
                int flagIndex = int.Parse(selectedItem.Text);

                var result = MessageBox.Show("确定要禁用该标志吗？", "操作确认", MessageBoxButtons.YesNo);

                if (result == DialogResult.Yes)
                {
                    if (flagIndex >= 0 && flagIndex < Character.Flags.Length)
                    {
                        Character.Flags[flagIndex] = false;

                        selectedItem.SubItems[1].Text = "未激活";
                        selectedItem.SubItems[1].ForeColor = Color.Red;
                    }
                    else
                    {
                        MessageBox.Show("标志编号无效。");
                    }
                }
            }
            else
            {
                MessageBox.Show("请先选中要禁用的标志。");
            }
        }
        #endregion

        #region UpdateTabs
        private void UpdateTabs()
        {
            if (Character == null)
            {
                Close();
                return;
            }

            UpdatePlayerInfo();
            PopulatePlayerFlagsListView();
            UpdatePetInfo();
            UpdatePlayerItems();
            UpdatePlayerMagics();
            UpdatePlayerQuests();
            UpdateHeroInfo();
        }
        #endregion

        #region Tab Resize
        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (tabControl1.SelectedIndex)
            {
                case 0: //Player
                    Size = new Size(725, 510);
                    break;
                case 1: //Quest
                    Size = new Size(423, 510);
                    break;
                case 2: //Item
                    Size = new Size(597, 510);
                    break;
                case 3: //Magic
                    Size = new Size(458, 510);
                    break;
                case 4: //Pet
                    Size = new Size(533, 510);
                    break;
                case 5: //Hero
                    Size = new Size(802, 510);
                    break;
            }

            UpdateTabs();
        }
        #endregion

        #region Hero List
        private static void ShowError(string text)
        {
            MessageBox.Show(text, "数据错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        private void UpdateHeroInfo()
        {
            if (Character?.Player?.Hero != null)
            {
                HeroObject hero = Character.Player.Hero;

                HeroNameTextBox.Text = hero.Name;
                HeroLevelTextBox.Text = hero.Level.ToString();
                HeroClassTextBox.Text = $"{hero.Class}";

                if (hero.CurrentMap?.Info != null)
                {
                    HeroCurrentMapLabel.Text = $"{hero.CurrentMap.Info.Title} / {hero.CurrentMap.Info.FileName}";
                    HeroCurrentXY.Text = $"X:{hero.CurrentLocation.X} Y:{hero.CurrentLocation.Y}";
                }
                else
                {
                    HeroCurrentMapLabel.Text = "离线";
                    HeroCurrentXY.Text = "离线";
                }

                if (hero.MaxExperience > 0)
                {
                    HeroExpTextBox.Text = $"{hero.Experience / (double)hero.MaxExperience:#0.##%}";
                }
                else
                {
                    HeroExpTextBox.Text = "0%";
                }

                HeroACBox.Text = $"{hero.Stats[Stat.MinAC]}-{hero.Stats[Stat.MaxAC]}";
                HeroAMCBox.Text = $"{hero.Stats[Stat.MinMAC]}-{hero.Stats[Stat.MaxMAC]}";
                HeroDCBox.Text = $"{hero.Stats[Stat.MinDC]}-{hero.Stats[Stat.MaxDC]}";
                HeroMCBox.Text = $"{hero.Stats[Stat.MinMC]}-{hero.Stats[Stat.MaxMC]}";
                HeroSCBox.Text = $"{hero.Stats[Stat.MinSC]}-{hero.Stats[Stat.MaxSC]}";

                HeroACCBox.Text = $"{hero.Stats[Stat.准确]}";
                HeroAGILBox.Text = $"{hero.Stats[Stat.敏捷]}";
                HeroATKSPDBox.Text = $"{hero.Stats[Stat.攻击速度]}";

                UpdateHeroMagic();
                UpdateHeroItems();
            }
            else
            {
                HeroCurrentMapLabel.Text = "离线";
                HeroCurrentXY.Text = "离线";
            }
        }

        private void UpdateHeroMagic()
        {
            HeroMagicList.Items.Clear();

            if (Character?.Heroes == null) return;


            foreach (HeroInfo hero in Character.Heroes)
            {
                if (hero?.Magics == null) continue;
                foreach (UserMagic magic in hero.Magics)
                {
                    if (magic?.Info == null) continue;

                    ListViewItem listItem = new ListViewItem(magic.Info.Name.ToString()) { Tag = this };
                    listItem.SubItems.Add(magic.Level.ToString());

                    switch (magic.Level)
                    {
                        case 0:
                            listItem.SubItems.Add($"{magic.Experience}/{magic.Info.Need1}");
                            break;
                        case 1:
                            listItem.SubItems.Add($"{magic.Experience}/{magic.Info.Need2}");
                            break;
                        case 2:
                            listItem.SubItems.Add($"{magic.Experience}/{magic.Info.Need3}");
                            break;
                        default:
                            listItem.SubItems.Add("-");
                            break;
                    }

                    if (magic.Key > 8)
                    {
                        int key = magic.Key % 8;
                        listItem.SubItems.Add( $"CTRL+F{(key != 0 ? key : 8)}");
                    }
                    else if (magic.Key > 0)
                    {
                        listItem.SubItems.Add($"F{magic.Key}");
                    }
                    else
                    {
                        listItem.SubItems.Add("无快捷键");
                    }
                    listItem.SubItems.Add(magic.Key.ToString());
                    HeroMagicList.Items.Add(listItem);
                }
            }
        }
        private void UpdateHeroItems()
        {
            HeroItemInfoListViewNF.Items.Clear();

            if (Character?.Heroes == null) return;

            HeroInfo selectedHero = Character.Heroes.FirstOrDefault();

            if (selectedHero == null) return;

            if (selectedHero.Inventory != null)
            {
                for (int i = 0; i < selectedHero.Inventory.Length; i++)
                {
                    UserItem inventoryItem = selectedHero.Inventory[i];

                    if (inventoryItem?.Info == null) continue;

                    ListViewItem item = new ListViewItem($"{inventoryItem.UniqueID}");

                    if (i < 6)
                    {
                        item.SubItems.Add($"腰带 | 格子: [{i + 1}]");
                    }
                    else if (i < 46)
                    {
                        item.SubItems.Add($"背包一 | 格子: [{i - 5}]");
                    }
                    else
                    {
                        item.SubItems.Add($"背包二 | 格子: [{i - 45}]");
                    }


                    item.SubItems.Add(inventoryItem.FriendlyName);
                    item.SubItems.Add($"{inventoryItem.Count}/{inventoryItem.Info.StackSize}");
                    item.SubItems.Add($"{inventoryItem.CurrentDura}/{inventoryItem.MaxDura}");

                    HeroItemInfoListViewNF.Items.Add(item);
                }
            }

            if (selectedHero.Equipment != null)
            {
                for (int i = 0; i < selectedHero.Equipment.Length; i++)
                {
                    UserItem equipItem = selectedHero.Equipment[i];

                    if (equipItem?.Info == null) continue;

                    ListViewItem item = new ListViewItem($"{equipItem.UniqueID}");

                    item.SubItems.Add($"装备 | 格子: [{i + 1}]");
                    item.SubItems.Add(equipItem.FriendlyName);
                    item.SubItems.Add($"{equipItem.Count}/{equipItem.Info.StackSize}");
                    item.SubItems.Add($"{equipItem.CurrentDura}/{equipItem.MaxDura}");

                    HeroItemInfoListViewNF.Items.Add(item);
                }
            }
        }

        private void HeroUpdateButton_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定要保存英雄修改吗？", "保存确认", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            HeroSaveChanges();
        }

        private void HeroSaveChanges()
        {
            if (Character?.Heroes == null) return;

            HeroInfo selectedHero = Character.Heroes.FirstOrDefault();

            if (selectedHero == null) return;

            if (string.IsNullOrWhiteSpace(HeroNameTextBox.Text))
            {
                ShowError("英雄名称不能为空"); return;
            }

            if (!byte.TryParse(HeroLevelTextBox.Text, out byte level))
            {
                ShowError("英雄等级输入错误，请检查后重新输入"); return;
            }

            selectedHero.Name = HeroNameTextBox.Text;
            selectedHero.Level = level;

            // 英雄属性框（方案B临时生效）：仅在线英雄可用，buff 死亡/下线自动失效
            if (Character.Player?.Hero != null)
            {
                ApplyStatsBuff(Character.Player.Hero, HeroNameTextBox.Text);
            }
            else if (HeroACBox.Text.Trim().Length > 0)
            {
                MessageBox.Show("英雄当前不在线，临时属性仅对在线英雄生效。", "临时属性", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            UpdateTabs();
        }
        #endregion
    }
}