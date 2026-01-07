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
        private Panel _container;
        private bool _isLoaded = false;
        private bool _isTaskbarTab = false;

        private Panel _tabPanel;    
        private Panel _headerPanel; 

        private Button _btnTabMain;
        private Button _btnTabBar;
        private LiteCheck _chkLinkHorizontal;
        
        private Label _lblCol1; 
        private Label _lblCol2; 
        private Label _lblCol3; 
        private Label _lblCol4; 

        public MonitorPage()
        {
            this.BackColor = UIColors.MainBg;
            this.Dock = DockStyle.Fill;
            this.Padding = new Padding(0);

            _container = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = UIUtils.S(new Padding(20, 5, 20, 0)) // 这里的 Bottom Padding 在 AutoScroll 下可能失效，依靠 Spacer 解决
            };
            this.Controls.Add(_container);

            InitHeader();
            
            this.Controls.SetChildIndex(_container, 0); 
        }

        private void InitHeader()
        {
            // === A. 选项卡面板 ===
            _tabPanel = new Panel 
            { 
                Dock = DockStyle.Top, 
                Height = UIUtils.S(42), 
                BackColor = UIColors.MainBg,
                Padding = UIUtils.S(new Padding(20, 0, 20, 0))
            };

            _tabPanel.Paint += (s, e) => {
                using (var p = new Pen(UIColors.Border))
                    e.Graphics.DrawLine(p, 0, _tabPanel.Height - 1, _tabPanel.Width, _tabPanel.Height - 1);
            };

            _btnTabMain = CreateTabButton("🖥️ " + LanguageManager.T("Menu.MainForm"), true);
            _btnTabBar = CreateTabButton("➖ " + LanguageManager.T("Menu.Taskbar") + " / " + LanguageManager.T("Menu.Horizontal"), false);

            _btnTabMain.Click += (s, e) => SwitchTab(false);
            _btnTabBar.Click += (s, e) => SwitchTab(true);

            _btnTabMain.Location = new Point(UIUtils.S(20), UIUtils.S(8));
            _btnTabBar.Location = new Point(_btnTabMain.Right + UIUtils.S(10), UIUtils.S(8));

            _chkLinkHorizontal = new LiteCheck(false, LanguageManager.T("Menu.HorizontalFollowsTaskbar")) 
            {
                AutoSize = true,
                Visible = false,
                ForeColor = UIColors.TextSub,
                Font = UIFonts.Bold(9F)
            };
            
            _tabPanel.Resize += (s, e) => {
                _chkLinkHorizontal.Location = new Point(
                    _tabPanel.Width - _chkLinkHorizontal.Width - UIUtils.S(20), 
                    UIUtils.S(10));
            };

            _tabPanel.Controls.AddRange(new Control[] { _btnTabMain, _btnTabBar, _chkLinkHorizontal });


            // === B. 表头面板 ===
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = UIUtils.S(34), 
                BackColor = UIColors.MainBg, 
                Padding = UIUtils.S(new Padding(20, 0, 20, 0))
            };

            _lblCol1 = CreateHeaderLabel();
            _lblCol2 = CreateHeaderLabel();
            _lblCol3 = CreateHeaderLabel();
            _lblCol4 = CreateHeaderLabel();
            
            _headerPanel.Controls.AddRange(new Control[] { _lblCol1, _lblCol2, _lblCol3, _lblCol4 });

            this.Controls.Add(_headerPanel);
            this.Controls.Add(_tabPanel);
        }

        private Label CreateHeaderLabel()
        {
            return new Label {
                AutoSize = true,
                ForeColor = UIColors.TextSub, 
                Font = UIFonts.Bold(8F),
                Visible = true
            };
        }

        private Button CreateTabButton(string text, bool active)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = UIFonts.Bold(9F),
                Padding = new Padding(5, 0, 5, 0)
            };
            btn.FlatAppearance.BorderSize = 0;
            UpdateBtnStyle(btn, active);
            return btn;
        }

        private void UpdateBtnStyle(Button btn, bool active)
        {
            if (active)
            {
                btn.BackColor = UIColors.Primary;
                btn.ForeColor = Color.White;
            }
            else
            {
                btn.BackColor = Color.Transparent;
                btn.ForeColor = UIColors.TextSub;
            }
        }

        private void SwitchTab(bool toTaskbarMode)
        {
            if (_isTaskbarTab == toTaskbarMode && _isLoaded) return;
            if (_isLoaded) Save(); 

            _isTaskbarTab = toTaskbarMode;

            UpdateBtnStyle(_btnTabMain, !_isTaskbarTab);
            UpdateBtnStyle(_btnTabBar, _isTaskbarTab);
            
            _chkLinkHorizontal.Visible = _isTaskbarTab;
            if (_isTaskbarTab && Config != null)
                _chkLinkHorizontal.Checked = Config.HorizontalFollowsTaskbar;

            _tabPanel.PerformLayout(); 
            _chkLinkHorizontal.Location = new Point(
                    _tabPanel.Width - _chkLinkHorizontal.Width - UIUtils.S(20), 
                    UIUtils.S(10));

            ReloadList();
        }

        public override void OnShow()
        {
            base.OnShow();
            if (Config == null) return;
            if (!_isLoaded) SwitchTab(false);
        }

        private void ReloadList()
        {
            _container.SuspendLayout();
            while (_container.Controls.Count > 0) _container.Controls[0].Dispose();

            UpdateHeaderLayout();

            // [需求3] 强制底部留白 Spacer
            // 因为我们是倒序添加 (Dock=Top)，最先添加的控件会被挤到最底部
            // 所以先添加这个 Spacer，它就会呆在列表的最下面
            var spacer = new Panel { Dock = DockStyle.Top, Height = UIUtils.S(30), BackColor = Color.Transparent };
            _container.Controls.Add(spacer);

            if (_isTaskbarTab)
            {
                var items = Config.MonitorItems.OrderBy(x => x.TaskbarSortIndex).ToList();
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    var row = new MonitorItemRow(items[i]);
                    row.SetMode(true); 
                    row.MoveUp += (s, e) => MoveControl(row, -1);
                    row.MoveDown += (s, e) => MoveControl(row, 1);
                    _container.Controls.Add(row);
                }
            }
            else
            {
                var items = Config.MonitorItems.OrderBy(x => x.SortIndex).ToList();
                var groups = items.GroupBy(x => x.Key.Split('.')[0]);
                foreach (var g in groups.Reverse())
                {
                    var block = CreateGroupBlock(g.Key, g.ToList());
                    _container.Controls.Add(block);
                }
            }

            _container.ResumeLayout();
            _isLoaded = true;
        }

        private void UpdateHeaderLayout()
        {
            int y = UIUtils.S(10); 
            // [需求1] 还原 20px 偏移
            int offset = UIUtils.S(20); 

            _lblCol1.Text = LanguageManager.T("Menu.MonitorItem");
            _lblCol1.Location = new Point(MonitorLayout.X_COL1 + offset, y);

            if (_isTaskbarTab)
                _lblCol2.Text = LanguageManager.T("Menu.short"); 
            else
                _lblCol2.Text = LanguageManager.T("Menu.name");  
            _lblCol2.Location = new Point(MonitorLayout.X_COL2 + offset, y);

            _lblCol3.Text = LanguageManager.T("Menu.showHide"); 
            _lblCol3.Location = new Point(MonitorLayout.X_COL3 + offset, y);

            _lblCol4.Text = LanguageManager.T("Menu.sort");
            _lblCol4.Location = new Point(MonitorLayout.X_COL4 + offset, y);
        }

        private GroupBlock CreateGroupBlock(string groupKey, List<MonitorItemConfig> items)
        {
            string alias = Config.GroupAliases.ContainsKey(groupKey) ? Config.GroupAliases[groupKey] : "";
            var header = new MonitorGroupHeader(groupKey, alias);
            var rowsPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.White };
            var block = new GroupBlock(header, rowsPanel);

            header.MoveUp += (s, e) => MoveControl(block, -1);
            header.MoveDown += (s, e) => MoveControl(block, 1);

            for (int i = items.Count - 1; i >= 0; i--)
            {
                var row = new MonitorItemRow(items[i]);
                row.SetMode(false); 
                row.MoveUp += (s, e) => MoveControl(row, -1);
                row.MoveDown += (s, e) => MoveControl(row, 1);
                rowsPanel.Controls.Add(row);
            }
            return block;
        }

        private void MoveControl(Control c, int dir)
        {
            var p = c.Parent;
            if (p == null) return;
            int idx = p.Controls.GetChildIndex(c);
            int newIdx = idx - dir; 
            if (newIdx >= 0 && newIdx < p.Controls.Count)
                p.Controls.SetChildIndex(c, newIdx);
        }

        public override void Save()
        {
            if (!_isLoaded || Config == null) return;

            Config.HorizontalFollowsTaskbar = _chkLinkHorizontal.Checked;
            var flatList = new List<MonitorItemConfig>();
            
            // 注意：因为增加了 Spacer，且 Spacer 是最先添加的(Index最大)
            // Reverse后 Spacer 会变成第一个，所以我们要过滤掉它
            var controls = _container.Controls.Cast<Control>().Reverse().Where(c => c is MonitorItemRow || c is GroupBlock).ToList();
            
            int indexCounter = 0;

            if (_isTaskbarTab)
            {
                foreach (Control c in controls)
                {
                    if (c is MonitorItemRow row)
                    {
                        row.SyncToConfig();
                        row.Config.TaskbarSortIndex = indexCounter++; 
                        flatList.Add(row.Config);
                    }
                }
            }
            else
            {
                foreach (Control c in controls)
                {
                    if (c is GroupBlock block)
                    {
                        string alias = block.Header.InputAlias.Inner.Text.Trim();
                        string defName = LanguageManager.T("Groups." + block.Header.GroupKey);
                        if (!string.IsNullOrEmpty(alias) && alias != defName) 
                            Config.GroupAliases[block.Header.GroupKey] = alias;
                        else 
                            Config.GroupAliases.Remove(block.Header.GroupKey);

                        var rows = block.RowsPanel.Controls.Cast<Control>().Reverse();
                        foreach (Control rc in rows)
                        {
                            if (rc is MonitorItemRow row)
                            {
                                row.SyncToConfig();
                                row.Config.SortIndex = indexCounter++; 
                                flatList.Add(row.Config);
                            }
                        }
                    }
                }
            }
            Config.SyncToLanguage();
        }

        private class GroupBlock : Panel
        {
            public MonitorGroupHeader Header { get; }
            public Panel RowsPanel { get; }
            public GroupBlock(MonitorGroupHeader header, Panel rowsPanel)
            {
                Header = header; RowsPanel = rowsPanel;
                Dock = DockStyle.Top; AutoSize = true;
                Padding = UIUtils.S(new Padding(0, 0, 0, 20));
                var card = new LiteCard { Dock = DockStyle.Top };
                card.Controls.Add(rowsPanel);
                card.Controls.Add(header);
                Controls.Add(card);
            }
        }
    }
}