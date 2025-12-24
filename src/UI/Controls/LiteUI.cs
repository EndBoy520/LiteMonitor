using System;
using System.Drawing;
using System.Windows.Forms;

namespace LiteMonitor.src.UI.Controls
{
    public static class UIColors
    {
        public static Color MainBg = Color.FromArgb(243, 243, 243);    // 窗体背景
        public static Color SidebarBg = Color.FromArgb(240, 240, 240); // 侧边栏背景
        public static Color CardBg = Color.White;
        public static Color Border = Color.FromArgb(220, 220, 220);
        public static Color Primary = Color.FromArgb(0, 120, 215);
        public static Color TextMain = Color.FromArgb(32, 32, 32);
        public static Color TextSub = Color.FromArgb(120, 120, 120);
        public static Color GroupHeader = Color.FromArgb(248, 249, 250); 
        
        // Win11 风格导航栏颜色
        public static Color NavSelected = Color.FromArgb(230, 230, 230); 
        public static Color NavHover = Color.FromArgb(235, 235, 235);
    }

    // =======================================================================
    // 1. 设置页面核心组件 (Settings Components)
    // =======================================================================

    // [容器] 双列设置卡片组
    public class LiteSettingsGroup : Panel
    {
        private TableLayoutPanel _layout;
        private Panel _inner;

        public LiteSettingsGroup(string title)
        {
            this.AutoSize = true;
            this.Dock = DockStyle.Top;
            this.Padding = new Padding(1); // 外边框粗细
            this.BackColor = UIColors.Border; // 外边框颜色
            this.Margin = new Padding(0, 0, 0, 15); // 组与组之间的下间距

            // 内部容器（白色背景）
            _inner = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, AutoSize = true };
            
            // 标题栏
            var header = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = UIColors.GroupHeader };
            var lbl = new Label { 
                Text = title, Location = new Point(15, 10), AutoSize = true, 
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), ForeColor = UIColors.TextMain 
            };
            header.Controls.Add(lbl);
            // 标题栏底部分隔线
            header.Paint += (s, e) => e.Graphics.DrawLine(new Pen(UIColors.Border), 0, 39, header.Width, 39);

            // 双列网格布局
            _layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                // Padding: 左右留白 25px
                Padding = new Padding(25, 10, 25, 15), 
                BackColor = Color.White
            };
            // 设置两列各占 50%
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            _inner.Controls.Add(_layout);
            _inner.Controls.Add(header);
            this.Controls.Add(_inner);
        }

        public void AddItem(Control item)
        {
            _layout.Controls.Add(item);
        }

        public void AddFullItem(Control item)
        {
            _layout.Controls.Add(item);
            _layout.SetColumnSpan(item, 2);
        }
    }

    // [单元格] 设置项：左侧文字 + 右侧控件
    public class LiteSettingsItem : Panel
    {
        public LiteSettingsItem(string text, Control ctrl)
        {
            this.Height = 40;
            this.Dock = DockStyle.Fill;
            // Margin: 右侧间距 40px，让左右两列分得更开
            this.Margin = new Padding(0, 2, 40, 2); 

            // 1. 标签 (左侧)
            var lbl = new Label { 
                Text = text, 
                AutoSize = true, 
                Font = new Font("Microsoft YaHei UI", 9F), 
                ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.MiddleLeft 
            };
            
            // 2. 控件预处理
            if (ctrl is LiteCheck) ctrl.Height = 22; 

            this.Controls.Add(lbl);
            this.Controls.Add(ctrl);

            // 动态垂直居中对齐
            this.Layout += (s, e) => {
                int mid = this.Height / 2;
                lbl.Location = new Point(0, mid - lbl.Height / 2);
                ctrl.Location = new Point(this.Width - ctrl.Width, mid - ctrl.Height / 2);
            };
            
            // ★ 修改点：底部绘制分隔线，颜色加深为 225, 225, 225
            this.Paint += (s, e) => {
                using(var p = new Pen(Color.FromArgb(225, 225, 225))) 
                    e.Graphics.DrawLine(p, 0, Height-1, Width, Height-1);
            };
        }
    }

    // [控件] 带边框的精致下拉框
    public class LiteComboBox : Panel
    {
        public ComboBox Inner;

        public LiteComboBox()
        {
            this.Size = new Size(110, 26);
            this.BackColor = Color.White;
            this.Padding = new Padding(1);

            Inner = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F),
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Margin = new Padding(0)
            };
            
            this.Controls.Add(Inner);
            
            this.Paint += (s, e) => 
            {
                using (var p = new Pen(UIColors.Border)) 
                {
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                }
            };
        }

        public object SelectedItem { get => Inner.SelectedItem; set => Inner.SelectedItem = value; }
        public int SelectedIndex { get => Inner.SelectedIndex; set => Inner.SelectedIndex = value; }
        public ComboBox.ObjectCollection Items => Inner.Items;
        public override string Text { get => Inner.Text; set => Inner.Text = value; }
    }

    // [控件] CheckBox (支持可选文案)
    public class LiteCheck : CheckBox 
    { 
        public LiteCheck(bool val, string text = "") 
        { 
            Checked = val; 
            AutoSize = true; 
            Cursor = Cursors.Hand; 
            Text = text; // 设置文案
            Padding = new Padding(2); 
            ForeColor = UIColors.TextMain;
            Font = new Font("Microsoft YaHei UI", 9F);
        } 
    }

    // =======================================================================
    // 2. 通用基础组件 (General Components)
    // =======================================================================

    public class LiteButton : Button 
    { 
        public LiteButton(string t, bool p) 
        { 
            Text = t; Size = new Size(80, 32); FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand; Font = new Font("Segoe UI", 9F); 
            if (p) { BackColor = UIColors.Primary; ForeColor = Color.White; FlatAppearance.BorderSize = 0; } 
            else { BackColor = Color.White; ForeColor = UIColors.TextMain; FlatAppearance.BorderColor = UIColors.Border; } 
        } 
    }

    public class LiteNavBtn : Button
    {
        private bool _isActive;
        public bool IsActive 
        {
            get => _isActive;
            set { _isActive = value; Invalidate(); }
        }

        public LiteNavBtn(string text)
        {
            Text = "  " + text; Size = new Size(150, 40); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            TextAlign = ContentAlignment.MiddleLeft; Font = new Font("Microsoft YaHei UI", 10F);
            Cursor = Cursors.Hand; Margin = new Padding(5, 2, 5, 2); 
            BackColor = UIColors.SidebarBg; ForeColor = UIColors.TextMain;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color bg = _isActive ? UIColors.NavSelected : 
                       (ClientRectangle.Contains(PointToClient(Cursor.Position)) ? UIColors.NavHover : UIColors.SidebarBg);
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, ClientRectangle);

            if (_isActive) {
                using (var b = new SolidBrush(UIColors.Primary)) e.Graphics.FillRectangle(b, 0, 8, 3, Height - 16);
                Font = new Font(Font, FontStyle.Bold);
            } else {
                Font = new Font(Font, FontStyle.Regular);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Point(12, 9), UIColors.TextMain);
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
    }

    public class LiteUnderlineInput : Panel
    {
        public TextBox Inner;
        public LiteUnderlineInput(string text)
        {
            this.Size = new Size(110, 26); this.BackColor = Color.Transparent; this.Padding = new Padding(0, 5, 0, 2); 
            Inner = new TextBox { Text = text, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, BackColor = Color.White, Font = new Font("Microsoft YaHei UI", 9F), ForeColor = UIColors.TextMain };
            Inner.Enter += (s, e) => this.Invalidate(); Inner.Leave += (s, e) => this.Invalidate();
            this.Controls.Add(Inner); this.Click += (s, e) => Inner.Focus();
        }
        public void SetBg(Color c) { Inner.BackColor = c; }
        protected override void OnPaint(PaintEventArgs e) {
            var c = Inner.Focused ? UIColors.Primary : Color.LightGray; int h = Inner.Focused ? 2 : 1;
            using (var b = new SolidBrush(c)) e.Graphics.FillRectangle(b, 0, Height - h, Width, h);
        }
    }

    public class LiteSortBtn : Button
    {
        public LiteSortBtn(string txt)
        {
            Text = txt; Size = new Size(24, 24); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; 
            BackColor = Color.FromArgb(245, 245, 245); ForeColor = Color.DimGray; Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 7F, FontStyle.Bold); Margin = new Padding(0);
        }
    }

    public class LiteCard : Panel
    {
        public LiteCard() { BackColor = UIColors.CardBg; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Top; Padding = new Padding(1); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var p = new Pen(UIColors.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }
    }
}