using Client.MirGraphics;
using SlimDX;
using SlimDX.Direct3D9;
using System.Drawing.Imaging;

namespace Client.MirControls
{
    public sealed class MirTextBox : MirControl
    {
        #region Back Color

        protected override void OnBackColourChanged()
        {
            base.OnBackColourChanged();
            if (TextBox != null && !TextBox.IsDisposed)
                TextBox.BackColor = BackColour;
        }

        #endregion

        #region Enabled

        protected override void OnEnabledChanged()
        {
            base.OnEnabledChanged();
            if (TextBox != null && !TextBox.IsDisposed)
                TextBox.Enabled = Enabled;
        }

        #endregion

        #region Fore Color

        protected override void OnForeColourChanged()
        {
            base.OnForeColourChanged();
            if (TextBox != null && !TextBox.IsDisposed)
                TextBox.ForeColor = ForeColour;
        }

        #endregion

        #region Location

        protected override void OnLocationChanged()
        {
            base.OnLocationChanged();
            UpdateNativeBounds();

            TextureValid = false;
            Redraw();
        }

        #endregion

        #region Max Length

        public int MaxLength
        {
            get
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    return TextBox.MaxLength;
                return -1;
            }
            set
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    TextBox.MaxLength = value;
            }
        }

        #endregion

        #region Parent

        protected override void OnParentChanged()
        {
            base.OnParentChanged();
            if (TextBox != null && !TextBox.IsDisposed)
                OnVisibleChanged();
        }

        #endregion

        #region Password

        public bool Password
        {
            get
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    return TextBox.UseSystemPasswordChar;
                return false;
            }
            set
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    TextBox.UseSystemPasswordChar = value;
            }
        }

        #endregion

        #region Font

        public System.Drawing.Font Font
        {
            get
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    return TextBox.Font;
                return null;
            }
            set
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    TextBox.Font = ScaleFont(value);
            }
        }

        #endregion

        #region Size

        protected override void OnSizeChanged()
        {
            UpdateNativeBounds();

            DisposeTexture();

            _size = Size;

            if (TextBox != null && !TextBox.IsDisposed)
                base.OnSizeChanged();
        }

        // 20261116 全屏拉伸适配：原生 WinForms TextBox 挂在窗体上，坐标是客户区物理像素；
        // 全屏拉伸后窗体=物理屏尺寸而游戏画面按逻辑分辨率渲染再拉伸，原生控件若仍用逻辑坐标会与画面错位
        //（登录框跑偏现象）。此处按拉伸比例换算 TextBox 的物理 bounds；窗口模式比例=1 行为不变。
        public void UpdateNativeBounds()
        {
            if (TextBox == null || TextBox.IsDisposed) return;

            var client = Program.Form != null ? Program.Form.ClientSize : System.Drawing.Size.Empty;
            if (Settings.FullScreen && client.Width > 0 && client.Height > 0 &&
                Settings.ScreenWidth > 0 && Settings.ScreenHeight > 0)
            {
                TextBox.Location = new Point(
                    DisplayLocation.X * client.Width / Settings.ScreenWidth,
                    DisplayLocation.Y * client.Height / Settings.ScreenHeight);
                TextBox.Size = new System.Drawing.Size(
                    Size.Width * client.Width / Settings.ScreenWidth,
                    Size.Height * client.Height / Settings.ScreenHeight);
            }
            else
            {
                TextBox.Location = DisplayLocation;
                TextBox.Size = Size;
            }
        }

        // 20261116 全屏/分辨率切换后刷新所有已挂窗体的原生 TextBox bounds（ToggleFullScreen/SetResolution 调用）
        public static void RefreshAllNativeBounds()
        {
            if (Program.Form == null) return;

            foreach (System.Windows.Forms.Control c in Program.Form.Controls)
            {
                if (c is System.Windows.Forms.TextBox tb && tb.Tag is MirTextBox mir)
                    mir.UpdateNativeBounds();
            }
        }

        #endregion
        
        #region TextBox

        public bool CanLoseFocus;
        public readonly TextBox TextBox;
        private Pen CaretPen;

        #endregion

        #region Label

        public string Text
        {
            get
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    return TextBox.Text;
                return null;
            }
            set
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    TextBox.Text = value;
            }
        }
        public string[] MultiText
        {
            get
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    return TextBox.Lines;
                return null;
            }
            set
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    TextBox.Lines = value;
            }
        }

        #endregion

        #region Visible

        public override bool Visible
        {
            get
            {
                return base.Visible;
            }
            set
            {
                base.Visible = value;
                OnVisibleChanged();
            }
        }

        protected override void OnVisibleChanged()
        {
            base.OnVisibleChanged();

            if (TextBox != null && !TextBox.IsDisposed)
                TextBox.Visible = Visible;
        }
        private void TextBox_VisibleChanged(object sender, EventArgs e)
        {
            DialogChanged();

            if (TextBox.Visible && TextBox.CanFocus)
                if (Program.Form.ActiveControl == null || Program.Form.ActiveControl == Program.Form)
                    Program.Form.ActiveControl = TextBox;

            if (!TextBox.Visible)
                if (Program.Form.ActiveControl == TextBox)
                    Program.Form.Focus();
        }
        private void SetFocus(object sender, EventArgs e)
        {
            if (TextBox.Visible)
                TextBox.VisibleChanged -= SetFocus;
            if (TextBox.Parent != null)
                TextBox.ParentChanged -= SetFocus;

            if (TextBox.CanFocus) TextBox.Focus();
            else if (TextBox.Visible && TextBox.Parent != null)
                Program.Form.ActiveControl = TextBox;


        }

        #endregion

        #region MultiLine

        public override void MultiLine()
        {
            TextBox.Multiline = true;
            UpdateNativeBounds();

            DisposeTexture();
            Redraw();
        }

        #endregion

        public MirTextBox()
        {
            BackColour = Color.Black;

            // 20261116 全屏输入框重影修复：D3D 自绘纹理（CreateTexture 抓控件位图画进游戏画面）是旧版
            // 独占全屏下「原生子控件被 D3D 表面盖住」的补救；现全屏=无边框窗口模式，原生 TextBox 直接
            // 合成显示在画面上，自绘再画一遍=重影。关闭自绘，由原生控件独占显示。
            DrawControlTexture = false;
            TextureValid = false;

            TextBox = new TextBox
            {
                BackColor = BackColour,
                BorderStyle = BorderStyle.None,
                Font = new System.Drawing.Font(Settings.FontName, 10F * 96f / CMain.Graphics.DpiX),
                ForeColor = ForeColour,
                Location = DisplayLocation,
                Size = Size,
                Visible = Visible,
                Tag = this,
                // 20261116 输入框光标修复：自定义 Cursor_TextPrompt.CUR 是 32×32 大光标，在 20px 高输入框上比例失调
                //（拉伸全屏下更显巨大）；改用系统标准 I 形文本光标，与输入框尺寸协调
                Cursor = System.Windows.Forms.Cursors.IBeam
            };

            CaretPen = new Pen(ForeColour, 1);

            TextBox.VisibleChanged += TextBox_VisibleChanged;
            TextBox.ParentChanged += TextBox_VisibleChanged;
            TextBox.KeyUp += TextBoxOnKeyUp;  
            TextBox.KeyPress += TextBox_KeyPress;

            TextBox.KeyPress += TextBox_NeedRedraw;
            TextBox.KeyUp += TextBox_NeedRedraw;
            // 20261116 编辑手感修复：Back/Delete 等控制键不触发 KeyPress，KeyUp 时 TextBox 布局可能未同步，
            // 删字后自绘光标错位；TextChanged 在文本实际变化后触发（含 Back/Delete/粘贴/IME 提交），补齐盲区
            TextBox.TextChanged += TextBox_NeedRedraw;
            TextBox.MouseDown += TextBox_NeedRedraw;
            TextBox.MouseUp += TextBox_NeedRedraw;
            TextBox.LostFocus += TextBox_NeedRedraw;
            TextBox.GotFocus += TextBox_NeedRedraw;
            TextBox.MouseWheel += TextBox_NeedRedraw;

            Shown += MirTextBox_Shown;
            TextBox.MouseMove += CMain.CMain_MouseMove;
        }

        private void TextBox_NeedRedraw(object sender, EventArgs e)
        {
            TextureValid = false;
            Redraw();
        }

        protected unsafe override void CreateTexture()
        {
            // 20261116 全屏输入框重影修复：全屏下不再自绘（原生控件直接显示，见 ctor 注释）
            if (!Settings.FullScreen) return;
            if (!DrawControlTexture) return;

            if (Size.IsEmpty)
                return;

            if (TextureSize != Size)
                DisposeTexture();

            if (ControlTexture == null || ControlTexture.Disposed)
            {
                DXManager.ControlList.Add(this);

                ControlTexture = new Texture(DXManager.Device, Size.Width, Size.Height, 1, Usage.None, Format.A8R8G8B8, Pool.Managed);
                TextureSize = Size;
            }

            Point caret = GetCaretPosition();

            DataRectangle stream = ControlTexture.LockRectangle(0, LockFlags.Discard);
            using (Bitmap bm = new Bitmap(Size.Width, Size.Height, Size.Width * 4, PixelFormat.Format32bppArgb, stream.Data.DataPointer))
            {
                TextBox.DrawToBitmap(bm, new Rectangle(0, 0, Size.Width, Size.Height));
                using (Graphics graphics = Graphics.FromImage(bm))
                {
                    graphics.DrawImage(bm, Point.Empty);
                    if (TextBox.Focused)
                        graphics.DrawLine(CaretPen, new Point(caret.X, caret.Y), new Point(caret.X, caret.Y + TextBox.Font.Height));
                }

            }
            ControlTexture.UnlockRectangle(0);
            DXManager.Sprite.Flush();
            TextureValid = true;
        }

        private Point GetCaretPosition()
        {
            Point result = TextBox.GetPositionFromCharIndex(TextBox.SelectionStart);

            if (result.X == 0 && TextBox.Text.Length > 0)
            {
                result = TextBox.GetPositionFromCharIndex(TextBox.Text.Length - 1);
                int s = result.X / TextBox.Text.Length;
                result.X = (int)(result.X + (s * 1.46));
                result.Y = TextBox.GetLineFromCharIndex(TextBox.SelectionStart) * TextBox.Font.Height;
            }

            return result;
        }

        private void TextBoxOnKeyUp(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.PrintScreen:
                    CMain.CMain_KeyUp(sender, e);
                    break;

            }
        }

        void TextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            base.OnKeyPress(e);

            if (e.KeyChar == (char)Keys.Escape)
            {
                Program.Form.ActiveControl = null;
                e.Handled = true;
            }
        }


        void MirTextBox_Shown(object sender, EventArgs e)
        {
            TextBox.Parent = Program.Form;
            UpdateNativeBounds(); // 20261116：挂上窗体后按当前全屏/窗口态刷新物理 bounds
            CMain.Ctrl = false;
            CMain.Shift = false;
            CMain.Alt = false;
            CMain.Tilde = false;

            TextureValid = false;
            SetFocus();
        }

        public void SetFocus()
        {
            if (!TextBox.Visible)
                TextBox.VisibleChanged += SetFocus;
            else if (TextBox.Parent == null)
                TextBox.ParentChanged += SetFocus;
            else
                TextBox.Focus();
        }

        public void DialogChanged()
        {
            MirMessageBox box1 = null;
            MirInputBox box2 = null;
            MirAmountBox box3 = null;

            if (MirScene.ActiveScene != null && MirScene.ActiveScene.Controls.Count > 0)
            {
                box1 = (MirMessageBox) MirScene.ActiveScene.Controls.FirstOrDefault(ob => ob is MirMessageBox);
                box2 = (MirInputBox) MirScene.ActiveScene.Controls.FirstOrDefault(O => O is MirInputBox);
                box3 = (MirAmountBox) MirScene.ActiveScene.Controls.FirstOrDefault(ob => ob is MirAmountBox);
            }


            if ((box1 != null && box1 != Parent) || (box2 != null && box2 != Parent)  || (box3 != null && box3 != Parent))
                TextBox.Visible = false;
            else
                TextBox.Visible = Visible && TextBox.Parent != null;
        }


        #region Disposable

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing) return;

            if (!TextBox.IsDisposed)
                TextBox.Dispose();
        }


        #endregion
    }
}
