using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;

namespace LiteMonitor.src.UI.SettingsPage
{
    public class MonitorPage : SettingsPageBase
    {
        private Panel _header;
        private Panel _container;
        private bool _isLoaded = false;

        // === 1. 布局常量定义 ===
        // 列 X 坐标
        private const int X_ID = 20;
        private const int X_NAME = 125;
        private const int X_SHORT = 245;
        private const int X_PANEL = 325;
        private const int X_TASKBAR = 415;
        private const int X_SORT = 500;

        // 排序按钮相对于 X_SORT 的偏移量 (确保上下对齐)
        private const int BTN_OFFSET_L = 0; // 左按钮偏移
        private const int BTN_OFFSET_R = 36;  // 右按钮偏移

        // 控件在行内的通用 Y 坐标
        private const int Y_ROW_CTRL = 8; 

        private List<GroupUI> _groupsUI = new List<GroupUI>();
        private List<RowUI> _rowsUI = new List<RowUI>();

        public MonitorPage()
        {
            this.BackColor = UIColors.MainBg;
            this.Dock = DockStyle.Fill;
            this.Padding = new Padding(0);

            _header = new Panel { Dock = DockStyle.Top, Height = 35, BackColor = UIColors.MainBg };
            _header.Padding = new Padding(20, 0, 20 + SystemInformation.VerticalScrollBarWidth, 0);

            // 表头文案简化，因为复选框旁边已经有文字了
            AddHead("监控项 (ID)", X_ID);
            AddHead("名称 (Name)", X_NAME);
            AddHead("简称 (Short)", X_SHORT);
            AddHead("显示/隐藏 (Display/Hide)", X_PANEL); 
            // AddHead("任务栏", X_TASKBAR); // 可以省略，或者保留作为列标题
            AddHead("排序 (Sort)", X_SORT); // 稍微居中一点

            this.Controls.Add(_header);

            _container = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(20, 35, 20, 20)
            };
            this.Controls.Add(_container);
            _header.BringToFront();
        }

        private void AddHead(string text, int x)
        {
            var lbl = new Label
            {
                Text = text,
                Location = new Point(x + 20, 10), // 左对齐，不再 +20
                AutoSize = true,
                ForeColor = UIColors.TextSub,
                Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold)
            };
            _header.Controls.Add(lbl);
        }

        public override void OnShow()
        {
            if (Config == null) return;
            if (_isLoaded) return; 

            _container.SuspendLayout();
            _container.Controls.Clear();
            _groupsUI.Clear();
            _rowsUI.Clear();

            var allItems = Config.MonitorItems.OrderBy(x => x.SortIndex).ToList();
            var groups = allItems.GroupBy(x => x.Key.Split('.')[0]);

            foreach (var g in groups.Reverse())
            {
                CreateGroupCard(g.Key, g.ToList());
            }
            _container.ResumeLayout();
            _isLoaded = true;
        }

        private void CreateGroupCard(string groupKey, List<MonitorItemConfig> items)
        {
            var wrapper = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 20) };
            var card = new LiteCard { Dock = DockStyle.Top };

            var rowsPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.White };
            var headerPanel = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = UIColors.GroupHeader };
            
            // 使用 UIColors.Border 保持统一
            headerPanel.Paint += (s, e) => e.Graphics.DrawLine(new Pen(UIColors.Border), 0, 44, headerPanel.Width, 44);

            // --- Group Header Controls ---
            var lblId = new Label { 
                Text = groupKey, 
                Location = new Point(X_ID, 12), 
                AutoSize = true, 
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), 
                ForeColor = Color.Gray 
            };

            string defGName = LanguageManager.T("Groups." + groupKey);
            if (defGName.StartsWith("Groups.")) defGName = groupKey;
            string alias = (Config.GroupAliases != null && Config.GroupAliases.ContainsKey(groupKey)) ? Config.GroupAliases[groupKey] : "";
            
            // 使用工厂方法创建输入框
            var inputGroup = CreateInput(string.IsNullOrEmpty(alias) ? defGName : alias, X_NAME, 100,UIColors.GroupHeader , FontStyle.Bold);
            
            // 使用工厂方法创建排序按钮，位置严格对齐
            var btnUp = CreateSortBtn("▲", X_SORT + BTN_OFFSET_L, (s, e) => MoveGroup(wrapper, -1));
            var btnDown = CreateSortBtn("▼", X_SORT + BTN_OFFSET_R, (s, e) => MoveGroup(wrapper, 1));

            headerPanel.Controls.AddRange(new Control[] { lblId, inputGroup, btnUp, btnDown });

            for (int i = items.Count - 1; i >= 0; i--)
            {
                var row = CreateRow(items[i], rowsPanel);
                rowsPanel.Controls.Add(row);
            }

            card.Controls.Add(rowsPanel);
            card.Controls.Add(headerPanel);

            wrapper.Controls.Add(card);
            _container.Controls.Add(wrapper);

            _groupsUI.Add(new GroupUI { Key = groupKey, Input = inputGroup.Inner });
        }

        private Control CreateRow(MonitorItemConfig item, Panel parentContainer)
        {
            var row = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.White };

            // ID
            var lblId = new Label
            {
                Text = item.Key,
                Location = new Point(X_ID, 14),
                Size = new Size(90, 20),
                AutoEllipsis = true,
                ForeColor = UIColors.TextSub,
                Font = new Font("Microsoft YaHei UI", 8F)
            };

            // Name & Short Inputs (使用工厂方法)
            string defName = LanguageManager.T("Items." + item.Key);
            string valName = string.IsNullOrEmpty(item.UserLabel) ? defName : item.UserLabel;
            var inputName = CreateInput(valName, X_NAME, 100, Color.White);

            string defShortKey = "Short." + item.Key;
            string defShort = LanguageManager.T(defShortKey);
            if (defShort.StartsWith("Short.")) defShort = item.Key.Split('.').Last();
            string valShort = string.IsNullOrEmpty(item.TaskbarLabel) ? defShort : item.TaskbarLabel;
            var inputShort = CreateInput(valShort, X_SHORT, 60, Color.White);

            // === 2. 优化：复选框带文字，左对齐，移除魔数 ===
            // 假设 LiteCheck 支持 (bool checked, string text) 构造函数
            // 如果不支持，你需要修改 LiteCheck.cs 或手动设置 Text 属性
            var chk1 = new LiteCheck(item.VisibleInPanel, "主界面") { Location = new Point(X_PANEL, 10) };
            var chk2 = new LiteCheck(item.VisibleInTaskbar, "任务栏") { Location = new Point(X_TASKBAR, 10) };

            // === 1. 优化：按钮位置使用常量，与 Group 严格对齐 ===
            var btnUp = CreateSortBtn("▲", X_SORT + BTN_OFFSET_L, (s, e) => MoveRow(row, -1, parentContainer));
            var btnDown = CreateSortBtn("▼", X_SORT + BTN_OFFSET_R, (s, e) => MoveRow(row, 1, parentContainer));

            row.Controls.AddRange(new Control[] { lblId, inputName, inputShort, chk1, chk2, btnUp, btnDown });
            
            // 优化：使用 UIColors.Border，且稍微让线短一点
            row.Paint += (s, e) => e.Graphics.DrawLine(new Pen(UIColors.Border), X_ID, 43, row.Width - 20, 43);

            _rowsUI.Add(new RowUI { Config = item, RowControl = row, InputName = inputName.Inner, InputShort = inputShort.Inner, ChkPanel = chk1, ChkTaskbar = chk2 });
            return row;
        }

        // === 3. 提取通用 Helper 方法 ===

        private LiteUnderlineInput CreateInput(string text, int x, int width, Color bg, FontStyle fontStyle = FontStyle.Regular)
        {
            var input = new LiteUnderlineInput(text);
            input.Location = new Point(x, Y_ROW_CTRL);
            input.Size = new Size(width, 28);
            input.SetBg(bg);
            // 统一字体颜色
            input.Inner.ForeColor = UIColors.TextMain;
            // 统一字体大小 (如果需要的话)
            input.Inner.Font = new Font("Microsoft YaHei UI", 9F, fontStyle); 
            return input;
        }

        private LiteSortBtn CreateSortBtn(string text, int x, EventHandler onClick)
        {
            var btn = new LiteSortBtn(text);
            btn.Location = new Point(x, 10); // 统一 Y 坐标
            btn.Click += onClick;
            return btn;
        }

        // === 业务逻辑保持不变 ===

        private void MoveRow(Control row, int dir, Panel container)
        {
            int idx = container.Controls.GetChildIndex(row);
            int newIdx = idx - dir;
            if (newIdx >= 0 && newIdx < container.Controls.Count)
                container.Controls.SetChildIndex(row, newIdx);
        }

        private void MoveGroup(Control wrapper, int dir)
        {
            var p = wrapper.Parent;
            int idx = p.Controls.GetChildIndex(wrapper);
            int newIdx = idx - dir;
            if (newIdx >= 0 && newIdx < p.Controls.Count)
                p.Controls.SetChildIndex(wrapper, newIdx);
        }

        public override void Save()
        {
            // 1. 安全检查：如果页面从未加载，控件对象都不存在，绝对不能保存
            if (!_isLoaded) return;
            
            // 初始化字典防止空引用
            if (Config.GroupAliases == null) Config.GroupAliases = new Dictionary<string, string>();
            
            var flatList = new List<MonitorItemConfig>();
            int sortIdx = 0;

            // 遍历容器中的所有分组卡片
            // 注意：保持原有的倒序遍历 (Count-1 -> 0)，确保排序逻辑与界面显示一致
            for (int i = _container.Controls.Count - 1; i >= 0; i--)
            {
                if (_container.Controls[i] is Panel wrapper && wrapper.Controls.Count > 0)
                {
                    var card = wrapper.Controls[0] as LiteCard;
                    if (card == null) continue;

                    var headerPanel = card.Controls.Count > 1 ? card.Controls[1] as Panel : null;
                    var rowsPanel = card.Controls.Count > 0 ? card.Controls[0] as Panel : null;

                    // ====== 1. 保存分组别名 (Group Aliases) ======
                    var gInput = headerPanel?.Controls.OfType<LiteUnderlineInput>().FirstOrDefault()?.Inner;
                    var gUI = _groupsUI.FirstOrDefault(u => u.Input == gInput);
                    
                    if (gUI != null)
                    {
                        string val = gUI.Input.Text.Trim();
                        if (!string.IsNullOrEmpty(val))
                        {
                            Config.GroupAliases[gUI.Key] = val;
                        }
                        else
                        {
                            if (Config.GroupAliases.ContainsKey(gUI.Key))
                                Config.GroupAliases.Remove(gUI.Key);
                        }
                    }

                    // ====== 2. 保存子项配置 (Rows) ======
                    if (rowsPanel != null)
                    {
                        for (int j = rowsPanel.Controls.Count - 1; j >= 0; j--)
                        {
                            var rowCtrl = rowsPanel.Controls[j];
                            var rUI = _rowsUI.FirstOrDefault(r => r.RowControl == rowCtrl);
                            
                            if (rUI != null)
                            {
                                var item = rUI.Config;

                                // --- [优化] 名称 (UserLabel) ---
                                string valName = rUI.InputName.Text.Trim();
                                // 获取原始 key，例如 "Items.CPU.Load"
                                string keyName = "Items." + item.Key; 
                                // 获取原始翻译（忽略用户当前的自定义覆盖）
                                string originalName = LanguageManager.GetOriginal(keyName);

                                // ★★★ 核心逻辑：如果输入内容 == 原始翻译，则保存为空 ★★★
                                if (string.Equals(valName, originalName, StringComparison.OrdinalIgnoreCase))
                                {
                                    item.UserLabel = ""; 
                                }
                                else
                                {
                                    item.UserLabel = valName; 
                                }

                                // --- [优化] 简称 (TaskbarLabel) ---
                                string valShort = rUI.InputShort.Text.Trim();
                                string keyShort = "Short." + item.Key;
                                string originalShort = LanguageManager.GetOriginal(keyShort);

                                // ★★★ 核心逻辑：同上 ★★★
                                if (string.Equals(valShort, originalShort, StringComparison.OrdinalIgnoreCase))
                                {
                                    item.TaskbarLabel = "";
                                }
                                else
                                {
                                    item.TaskbarLabel = valShort;
                                }

                                // --- 开关状态 ---
                                item.VisibleInPanel = rUI.ChkPanel.Checked;
                                item.VisibleInTaskbar = rUI.ChkTaskbar.Checked;
                                
                                // --- 排序索引 ---
                                item.SortIndex = sortIdx++;
                                
                                // ★★★★★ 绝对不能漏掉这一行！ ★★★★★
                                flatList.Add(item); 
                            }
                        }
                    }
                }
            }
            
            // 更新配置列表
            Config.MonitorItems = flatList;
        }
        private class GroupUI { public string Key; public TextBox Input; }
        private class RowUI { public MonitorItemConfig Config; public Control RowControl; public TextBox InputName; public TextBox InputShort; public CheckBox ChkPanel; public CheckBox ChkTaskbar; }
    }
}