using System;
using System.Drawing;
using System.IO;
using System.Linq; // 引入 Linq
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;
using LiteMonitor.src.System;

namespace LiteMonitor.src.UI.SettingsPage
{
    public class GeneralPage : SettingsPageBase
    {
        private Panel _container;
        private bool _isLoaded = false;

        private LiteComboBox _cmbLang;
        private LiteCheck _chkAutoStart;
        private LiteCheck _chkTopMost;
        private LiteComboBox _cmbRefresh;

        private LiteCheck _chkAutoHide;
        private LiteCheck _chkClickThrough;
        private LiteCheck _chkClamp;
        private LiteCheck _chkHideTray;
        private LiteCheck _chkHideMain;

        private LiteComboBox _cmbNet;
        private LiteComboBox _cmbDisk;

        public GeneralPage()
        {
            this.BackColor = UIColors.MainBg;
            this.Dock = DockStyle.Fill;
            this.Padding = new Padding(0);
            _container = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
            this.Controls.Add(_container);
        }

        public override void OnShow()
        {
            if (Config == null || _isLoaded) return;

            _container.SuspendLayout();
            _container.Controls.Clear();

           
            CreateBehaviorCard(); 
            CreateSystemCard();   
            CreateSourceCard();   

            _container.ResumeLayout();
            _isLoaded = true;
        }

        private void CreateSystemCard()
        {
            var group = new LiteSettingsGroup("System Settings");

            // 1. Language
            _cmbLang = new LiteComboBox();
            string langDir = Path.Combine(AppContext.BaseDirectory, "resources/lang");
            if (Directory.Exists(langDir))
            {
                foreach (var file in Directory.EnumerateFiles(langDir, "*.json"))
                {
                    string code = Path.GetFileNameWithoutExtension(file);
                    _cmbLang.Items.Add(code.ToUpper());
                }
            }
            string curLang = string.IsNullOrEmpty(Config.Language) ? "en" : Config.Language;
            foreach (var item in _cmbLang.Items)
            {
                if (item.ToString().Contains(curLang.ToUpper())) _cmbLang.SelectedItem = item;
            }
            group.AddItem(new LiteSettingsItem(LanguageManager.T("Menu.Language"), _cmbLang));

            // 2. AutoStart (★ 传入 "Enable")
            _chkAutoStart = new LiteCheck(Config.AutoStart, "Enable");
            group.AddItem(new LiteSettingsItem(LanguageManager.T("Menu.AutoStart"), _chkAutoStart));

            // 4. Refresh Rate
            _cmbRefresh = new LiteComboBox();
            int[] rates = { 100, 200, 500, 1000, 2000, 3000 };
            foreach (var r in rates) _cmbRefresh.Items.Add(r + " ms");
            SetComboVal(_cmbRefresh, Config.RefreshMs + " ms");
            group.AddItem(new LiteSettingsItem(LanguageManager.T("Menu.Refresh"), _cmbRefresh));

            AddGroupToPage(group);
        }

        private void CreateBehaviorCard()
        {
            var group = new LiteSettingsGroup("Behavior");

            // ★ 全部传入 "Enable"
            // 3. TopMost (★ 传入 "Enable")
            _chkTopMost = new LiteCheck(Config.TopMost, "Enable");
            group.AddItem(new LiteSettingsItem(LanguageManager.T("Menu.TopMost"), _chkTopMost));

             _chkClamp = new LiteCheck(Config.ClampToScreen, "Enable");
            group.AddItem(new LiteSettingsItem(LanguageManager.T("Menu.ClampToScreen"), _chkClamp));

            _chkAutoHide = new LiteCheck(Config.AutoHide, "Enable");
            group.AddItem(new LiteSettingsItem(LanguageManager.T("Menu.AutoHide"), _chkAutoHide));

            _chkClickThrough = new LiteCheck(Config.ClickThrough, "Enable");
            group.AddItem(new LiteSettingsItem(LanguageManager.T("Menu.ClickThrough"), _chkClickThrough));

            _chkHideTray = new LiteCheck(Config.HideTrayIcon, "Enable");
            group.AddItem(new LiteSettingsItem(LanguageManager.T("Menu.HideTrayIcon"), _chkHideTray));

            _chkHideMain = new LiteCheck(Config.HideMainForm, "Enable");
            group.AddItem(new LiteSettingsItem(LanguageManager.T("Menu.HideMainForm"), _chkHideMain));

            _chkHideTray.CheckedChanged += (s, e) => CheckVisibilitySafe();
            _chkHideMain.CheckedChanged += (s, e) => CheckVisibilitySafe();

            AddGroupToPage(group);
        }

        private void CreateSourceCard()
        {
            var group = new LiteSettingsGroup("Hardware Source");

            // Disk
            _cmbDisk = new LiteComboBox();
            foreach (var d in HardwareMonitor.ListAllDisks()) _cmbDisk.Items.Add(d);
            SetComboVal(_cmbDisk, string.IsNullOrEmpty(Config.PreferredDisk) ? LanguageManager.T("Menu.Auto") : Config.PreferredDisk);
            group.AddItem(new LiteSettingsItem(LanguageManager.T("Menu.DiskSource"), _cmbDisk));

            // Network
            _cmbNet = new LiteComboBox();
            foreach (var n in HardwareMonitor.ListAllNetworks()) _cmbNet.Items.Add(n);
            SetComboVal(_cmbNet, string.IsNullOrEmpty(Config.PreferredNetwork) ? LanguageManager.T("Menu.Auto") : Config.PreferredNetwork);
            group.AddItem(new LiteSettingsItem(LanguageManager.T("Menu.NetworkSource"), _cmbNet));

            AddGroupToPage(group);
        }

        private void AddGroupToPage(LiteSettingsGroup group)
        {
            var wrapper = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 20) };
            wrapper.Controls.Add(group);
            _container.Controls.Add(wrapper);
            _container.Controls.SetChildIndex(wrapper, 0);
        }

        private void SetComboVal(LiteComboBox cmb, string val)
        {
            if (!cmb.Items.Contains(val)) cmb.Items.Insert(0, val);
            cmb.SelectedItem = val;
        }
        
        private void CheckVisibilitySafe()
        {
            if (!Config.ShowTaskbar && _chkHideMain.Checked && _chkHideTray.Checked)
            {
                if (_chkHideMain.Focused) _chkHideMain.Checked = false;
                else _chkHideTray.Checked = false;
            }
        }

        public override void Save()
        {
            if (!_isLoaded) return;

            Config.AutoStart = _chkAutoStart.Checked;
            Config.TopMost = _chkTopMost.Checked;
            Config.RefreshMs = ParseInt(_cmbRefresh.Text);
            if (Config.RefreshMs < 50) Config.RefreshMs = 1000;

            if (_cmbLang.SelectedItem != null)
            {
                string s = _cmbLang.SelectedItem.ToString();
                if (s.Contains("(") && s.Contains(")")) {
                    int start = s.LastIndexOf("(") + 1;
                    int len = s.LastIndexOf(")") - start;
                    if (len > 0) Config.Language = s.Substring(start, len).ToLower();
                } else if (s == "Auto") Config.Language = "";
            }

            Config.AutoHide = _chkAutoHide.Checked;
            Config.ClickThrough = _chkClickThrough.Checked;
            Config.ClampToScreen = _chkClamp.Checked;
            Config.HideTrayIcon = _chkHideTray.Checked;
            Config.HideMainForm = _chkHideMain.Checked;

            if (_cmbDisk.SelectedItem != null) {
                string d = _cmbDisk.SelectedItem.ToString();
                Config.PreferredDisk = (d == "Auto") ? "" : d;
            }
            if (_cmbNet.SelectedItem != null) {
                string n = _cmbNet.SelectedItem.ToString();
                Config.PreferredNetwork = (n == "Auto") ? "" : n;
            }
            AutoStart.Set(Config.AutoStart);
        }

        private int ParseInt(string s)
        {
            string clean = new string(s.Where(char.IsDigit).ToArray()); 
            return int.TryParse(clean, out int v) ? v : 0;
        }
    }
}