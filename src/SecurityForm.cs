using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GoniPdfPrinter
{
    /// <summary>
    /// 보안·저장 설정 창.
    ///  - 작업 모드(jobMode=true): 인쇄 작업 저장. 하단에 이번 저장 폴더/파일 이름, [암호 없이 저장] 버튼.
    ///  - 설정 모드: 트레이 메뉴의 기본 설정 편집.
    /// </summary>
    sealed class SecurityForm : Form
    {
        readonly bool _jobMode;
        readonly string _docTitle;
        readonly AppSettings _orig;
        readonly DateTime _now = DateTime.Now;
        public AppSettings Result;
        public string OutputPath;

        // 보안 탭
        CheckBox chkOpen, chkShowOpen, chkPerm, chkShowPerm, chkCopy, chkAccess;
        TextBox txtOpen, txtOpen2, txtPerm, txtPerm2;
        ComboBox cboPrint, cboEdit;
        Label lblOpen, lblOpen2, lblPerm, lblPerm2, lblPrint, lblEdit;
        // 저장 설정 탭
        RadioButton rdoLast, rdoFixed;
        TextBox txtFixed, txtPattern;
        Button btnFixedBrowse, btnToken;
        Label lblPreview;
        CheckBox chkStrip;
        ComboBox cboConflict;
        // 기타 옵션 탭
        ComboBox cboLevel;
        CheckBox chkMeta, chkRemember, chkOpenAfter, chkSkip;
        // 하단 (작업 모드)
        TextBox txtFolder, txtName;
        bool _folderEdited, _nameEdited, _updating;

        const int LabelX = 16, FieldX = 150, FieldW = 250;
        const string SampleTitle = "분기 보고서.docx - Word";

        public SecurityForm(AppSettings s, bool jobMode, string docTitle)
        {
            _jobMode = jobMode;
            _docTitle = docTitle;
            _orig = s.Clone();
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Malgun Gothic", 9F);
            Text = jobMode ? "Goni PDF - 보안 설정 후 저장" : "Goni PDF - 기본 설정";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            Icon = AppIcon.Get();

            var tabs = new TabControl { Left = 10, Top = 10, Width = 530, Height = 482 };
            tabs.TabPages.Add(BuildSecurityTab());
            tabs.TabPages.Add(BuildSaveTab());
            tabs.TabPages.Add(BuildOptionsTab());
            Controls.Add(tabs);

            int y = tabs.Bottom + 12;
            if (jobMode)
            {
                AddLabel(this, "저장 폴더:", 18, y);
                txtFolder = new TextBox { Left = FieldX - 10, Top = y, Width = 290 };
                Controls.Add(txtFolder);
                var b = new Button { Text = "찾아보기...", Left = txtFolder.Right + 6, Top = y - 1, Width = 90, Height = 25 };
                b.Click += (o, e) => { string f = PickFolder(txtFolder.Text); if (f != null) txtFolder.Text = f; };
                Controls.Add(b);
                y += 32;
                AddLabel(this, "파일 이름:", 18, y);
                txtName = new TextBox { Left = FieldX - 10, Top = y, Width = 290 };
                Controls.Add(txtName);
                Controls.Add(new Label { Text = ".pdf", Left = txtName.Right + 4, Top = y + 3, AutoSize = true });
                y += 40;
            }

            var btnOk = new Button { Text = jobMode ? "저장" : "확인", Width = 96, Height = 30, Top = y };
            var btnCancel = new Button { Text = "취소", Width = 96, Height = 30, Top = y, DialogResult = DialogResult.Cancel };
            btnCancel.Left = tabs.Right - btnCancel.Width;
            btnOk.Left = btnCancel.Left - 8 - btnOk.Width;
            btnOk.Click += (o, e) => OnOk(false);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            if (jobMode)
            {
                var btnPlain = new Button { Text = "암호 없이 저장", Width = 120, Height = 30, Left = tabs.Left, Top = y };
                btnPlain.Click += (o, e) => OnOk(true);
                new ToolTip().SetToolTip(btnPlain, "이번 문서만 암호·권한 없이 일반 PDF로 저장합니다.\n기억된 보안 설정은 바뀌지 않습니다.");
                Controls.Add(btnPlain);
            }
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            ClientSize = new Size(tabs.Right + 10, y + 30 + 14);

            LoadValues(s);
            ResumeLayout(false);
        }

        // ------------------------------------------------------------------
        // 탭 구성
        // ------------------------------------------------------------------

        TabPage BuildSecurityTab()
        {
            var p = NewPage("보안");
            int y = 10;
            Header(p, "암호 열기", ref y);
            chkOpen = Check(p, "문서를 열 때 암호를 요구합니다.", ref y);
            lblOpen = AddLabel(p, "암호:", LabelX + 8, y); txtOpen = Pw(p, y);
            chkShowOpen = new CheckBox { Text = "표시", Left = FieldX + FieldW + 10, Top = y + 2, AutoSize = true };
            p.Controls.Add(chkShowOpen);
            y += 32;
            lblOpen2 = AddLabel(p, "암호 확인:", LabelX + 8, y); txtOpen2 = Pw(p, y); y += 42;

            Header(p, "권한", ref y);
            chkPerm = Check(p, "문서 보안 설정 변경이나 특정 기능 사용 시 암호를 요구합니다.", ref y);
            lblPerm = AddLabel(p, "암호:", LabelX + 8, y); txtPerm = Pw(p, y);
            chkShowPerm = new CheckBox { Text = "표시", Left = FieldX + FieldW + 10, Top = y + 2, AutoSize = true };
            p.Controls.Add(chkShowPerm);
            y += 32;
            lblPerm2 = AddLabel(p, "암호 확인:", LabelX + 8, y); txtPerm2 = Pw(p, y); y += 38;
            lblPrint = AddLabel(p, "인쇄 허용 범위:", LabelX + 8, y);
            cboPrint = Combo(p, y, FieldW, "없음", "저해상도 (150 dpi)", "고해상도"); y += 34;
            lblEdit = AddLabel(p, "편집 허용 범위:", LabelX + 8, y);
            cboEdit = Combo(p, y, FieldW, "없음", "페이지 삽입, 삭제 및 회전", "양식 필드 채우기 및 서명",
                "주석 작성, 양식 필드 채우기 및 서명", "페이지 추출을 제외한 모든 작업"); y += 38;
            chkCopy = Check(p, "문구, 이미지를 포함한 내용물의 복사를 허용합니다.", ref y);
            chkAccess = Check(p, "화면 판독기 등 접근성 도구의 텍스트 접근을 허용합니다.", ref y);
            y += 4;
            Hint(p, "두 항목을 모두 끄면 암호 없는 일반 PDF로 저장됩니다.", y);
            return p;
        }

        TabPage BuildSaveTab()
        {
            var p = NewPage("저장 설정");
            int y = 10;
            Header(p, "기본 저장 위치", ref y);
            rdoLast = new RadioButton { Text = "마지막으로 저장한 폴더", Left = LabelX, Top = y, AutoSize = true };
            p.Controls.Add(rdoLast);
            y += 28;
            rdoFixed = new RadioButton { Text = "항상 이 폴더:", Left = LabelX, Top = y, AutoSize = true };
            p.Controls.Add(rdoFixed);
            y += 28;
            txtFixed = new TextBox { Left = LabelX + 20, Top = y, Width = 384 };
            p.Controls.Add(txtFixed);
            btnFixedBrowse = new Button { Text = "찾아보기...", Left = txtFixed.Right + 6, Top = y - 1, Width = 90, Height = 25 };
            btnFixedBrowse.Click += (o, e) => { string f = PickFolder(txtFixed.Text); if (f != null) txtFixed.Text = f; };
            p.Controls.Add(btnFixedBrowse);
            y += 28;
            Hint(p, "환경 변수와 날짜 항목을 쓸 수 있습니다. 없는 폴더는 자동으로 만듭니다.\r\n예) %USERPROFILE%\\Documents\\PDF\\{yyyy}-{MM}", y, LabelX + 20);
            y += 44;

            Header(p, "기본 파일 이름", ref y);
            AddLabel(p, "형식:", LabelX + 8, y);
            txtPattern = new TextBox { Left = FieldX - 40, Top = y, Width = 290 };
            p.Controls.Add(txtPattern);
            btnToken = new Button { Text = "항목 추가 ▾", Left = txtPattern.Right + 6, Top = y - 1, Width = 90, Height = 25 };
            btnToken.Click += (o, e) => TokenMenu().Show(btnToken, new Point(0, btnToken.Height));
            p.Controls.Add(btnToken);
            y += 32;
            AddLabel(p, "미리보기:", LabelX + 8, y);
            lblPreview = new Label { Left = FieldX - 40, Top = y + 3, Width = 386, Height = 36, ForeColor = Color.FromArgb(0, 90, 180) };
            p.Controls.Add(lblPreview);
            y += 40;
            chkStrip = Check(p, "문서 제목에서 프로그램 이름과 확장자를 제거합니다.", ref y);
            Hint(p, "예) \"보고서.docx - Word\" → \"보고서\",  \"Microsoft Excel - 매출.xlsx\" → \"매출\"", y - 4, LabelX + 20);
            y += 26;
            AddLabel(p, "같은 이름이 있으면:", LabelX + 8, y);
            cboConflict = Combo(p, y, 250, "번호 붙이기 (예: 이름 (2).pdf)", "덮어쓰기", "저장할 때 묻기");
            y += 40;
            Hint(p,
                "{title} 문서 제목   {date} 2026-11-23   {time} 093015   {datetime}\r\n" +
                "{yyyy} {MM} {dd} {HH} {mm} {ss}   {user} 사용자   {computer} 컴퓨터\r\n" +
                "{counter} 일련번호 (빈 번호 자동),  서식 지정: {date:yyyyMMdd}  {counter:000}", y, LabelX + 8);

            rdoLast.CheckedChanged += (o, e) => UpdateSaveEnabled();
            rdoFixed.CheckedChanged += (o, e) => UpdateSaveEnabled();
            txtFixed.TextChanged += (o, e) => SyncJobFields();
            txtPattern.TextChanged += (o, e) => UpdatePreview();
            chkStrip.CheckedChanged += (o, e) => UpdatePreview();
            return p;
        }

        TabPage BuildOptionsTab()
        {
            var p = NewPage("기타 옵션");
            int y = 10;
            Header(p, "암호화", ref y);
            AddLabel(p, "암호화 방식:", LabelX + 8, y);
            cboLevel = Combo(p, y, FieldW, "AES-256 (권장, Acrobat X 이상)", "AES-128 (Acrobat 7 이상 호환)");
            y += 36;
            chkMeta = Check(p, "문서 정보(제목, 작성자 등) 메타데이터도 암호화합니다.", ref y);
            chkRemember = Check(p, "암호를 기억합니다. (Windows 사용자 계정으로 보호하여 저장)", ref y);
            y += 12;
            Header(p, "저장 후 동작", ref y);
            chkOpenAfter = Check(p, "저장 후 PDF 파일을 엽니다.", ref y);
            chkSkip = Check(p, "다음부터 이 창 없이 마지막 설정으로 바로 저장합니다.", ref y);
            Hint(p, "트레이 아이콘을 더블클릭하면 이 설정을 다시 바꿀 수 있습니다.", y - 2, LabelX + 20);
            return p;
        }

        ContextMenuStrip TokenMenu()
        {
            var m = new ContextMenuStrip();
            string[,] items =
            {
                { "문서 제목", "{title}" }, { "날짜 (2026-11-23)", "{date}" }, { "시간 (093015)", "{time}" },
                { "날짜_시간", "{datetime}" }, { "연 (yyyy)", "{yyyy}" }, { "월 (MM)", "{MM}" }, { "일 (dd)", "{dd}" },
                { "사용자 이름", "{user}" }, { "컴퓨터 이름", "{computer}" }, { "일련번호 (001)", "{counter:000}" }
            };
            for (int i = 0; i < items.GetLength(0); i++)
            {
                string token = items[i, 1];
                m.Items.Add(items[i, 0] + "   " + token, null, (o, e) => InsertToken(token));
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("기본값으로 (문서 제목)", null, (o, e) => { txtPattern.Text = FileNaming.DefaultPattern; });
            return m;
        }

        void InsertToken(string token)
        {
            int pos = txtPattern.SelectionStart;
            string t = txtPattern.Text.Remove(pos, txtPattern.SelectionLength);
            txtPattern.Text = t.Insert(pos, token);
            txtPattern.SelectionStart = pos + token.Length;
            txtPattern.Focus();
        }

        // ------------------------------------------------------------------
        // 값 채우기 / 동기화
        // ------------------------------------------------------------------

        void LoadValues(AppSettings s)
        {
            _updating = true;
            chkOpen.Checked = s.RequireOpenPassword;
            txtOpen.Text = txtOpen2.Text = s.OpenPassword;
            chkPerm.Checked = s.RequirePermissionPassword;
            txtPerm.Text = txtPerm2.Text = s.PermissionPassword;
            cboPrint.SelectedIndex = (int)s.Print;
            cboEdit.SelectedIndex = (int)s.Edit;
            chkCopy.Checked = s.AllowCopy;
            chkAccess.Checked = s.AllowAccessibility;

            rdoFixed.Checked = s.FolderMode == FolderMode.Fixed;
            rdoLast.Checked = s.FolderMode == FolderMode.LastUsed;
            txtFixed.Text = s.FixedFolder;
            txtPattern.Text = s.FileNamePattern;
            chkStrip.Checked = s.StripAppName;
            cboConflict.SelectedIndex = (int)s.Conflict;

            cboLevel.SelectedIndex = (int)s.Level;
            chkMeta.Checked = s.EncryptMetadata;
            chkRemember.Checked = s.RememberPasswords;
            chkOpenAfter.Checked = s.OpenAfterSave;
            chkSkip.Checked = s.SkipDialog;
            _updating = false;

            chkOpen.CheckedChanged += (o, e) => UpdateEnabled();
            chkPerm.CheckedChanged += (o, e) => UpdateEnabled();
            chkShowOpen.CheckedChanged += (o, e) => { txtOpen.UseSystemPasswordChar = txtOpen2.UseSystemPasswordChar = !chkShowOpen.Checked; };
            chkShowPerm.CheckedChanged += (o, e) => { txtPerm.UseSystemPasswordChar = txtPerm2.UseSystemPasswordChar = !chkShowPerm.Checked; };
            if (_jobMode)
            {
                txtFolder.TextChanged += (o, e) => { if (!_updating) _folderEdited = true; };
                txtName.TextChanged += (o, e) => { if (!_updating) _nameEdited = true; };
            }
            UpdateEnabled();
            UpdateSaveEnabled();
            UpdatePreview();
        }

        void UpdateEnabled()
        {
            bool o = chkOpen.Checked;
            foreach (Control c in new Control[] { lblOpen, lblOpen2, txtOpen, txtOpen2, chkShowOpen }) c.Enabled = o;
            bool p = chkPerm.Checked;
            foreach (Control c in new Control[] { lblPerm, lblPerm2, txtPerm, txtPerm2, chkShowPerm, lblPrint, lblEdit, cboPrint, cboEdit, chkCopy, chkAccess })
                c.Enabled = p;
        }

        void UpdateSaveEnabled()
        {
            txtFixed.Enabled = btnFixedBrowse.Enabled = rdoFixed.Checked;
            SyncJobFields();
        }

        string PreviewTitle { get { return _jobMode ? _docTitle : SampleTitle; } }

        void UpdatePreview()
        {
            string name = CurrentNameFromPattern();
            lblPreview.Text = name + ".pdf" + (_jobMode ? "" : "\r\n(제목 \"" + SampleTitle + "\" 인 경우)");
            SyncJobFields();
        }

        string CurrentNameFromPattern()
        {
            return FileNaming.Expand(txtPattern.Text, PreviewTitle, _now, chkStrip.Checked, 1);
        }

        /// <summary>사용자가 직접 고치지 않았다면 하단 폴더/파일명을 설정에 맞춰 갱신</summary>
        void SyncJobFields()
        {
            if (!_jobMode || txtFolder == null) return;
            _updating = true;
            if (!_folderEdited)
            {
                var tmp = _orig.Clone();
                tmp.FolderMode = rdoFixed.Checked ? FolderMode.Fixed : FolderMode.LastUsed;
                tmp.FixedFolder = txtFixed.Text;
                txtFolder.Text = tmp.ResolveDefaultFolder(_now);
            }
            if (!_nameEdited)
            {
                string folder = FileNaming.ResolveFolder(txtFolder.Text, _now);
                string name = folder != null && FileNaming.HasCounter(txtPattern.Text)
                    ? FileNaming.ExpandWithCounter(folder, txtPattern.Text, _docTitle, _now, chkStrip.Checked)
                    : CurrentNameFromPattern();
                txtName.Text = name;
            }
            _updating = false;
        }

        /// <summary>스크린샷 생성 등에서 창이 포커스를 가져가지 않게 함</summary>
        internal static bool SuppressActivation = false;

        protected override bool ShowWithoutActivation { get { return SuppressActivation; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (SuppressActivation) return;
            // 트레이 앱에서 띄우므로 강제로 전면에 표시
            TopMost = true;
            Activate();
            TopMost = false;
            if (_jobMode) { txtName.Focus(); txtName.SelectAll(); }
        }

        // ------------------------------------------------------------------
        // 확인
        // ------------------------------------------------------------------

        void Fail(string msg, Control focus)
        {
            MessageBox.Show(this, msg, "Goni PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (focus != null) { SelectTabOf(focus); focus.Focus(); }
        }

        void SelectTabOf(Control c)
        {
            for (Control p = c.Parent; p != null; p = p.Parent)
            {
                var page = p as TabPage;
                if (page != null) { ((TabControl)page.Parent).SelectedTab = page; return; }
            }
        }

        void OnOk(bool noEncryption)
        {
            if (!noEncryption)
            {
                if (chkOpen.Checked)
                {
                    if (txtOpen.Text.Length == 0) { Fail("문서 열기 암호를 입력하세요.", txtOpen); return; }
                    if (txtOpen.Text != txtOpen2.Text) { Fail("문서 열기 암호와 암호 확인이 일치하지 않습니다.", txtOpen2); return; }
                }
                if (chkPerm.Checked)
                {
                    if (txtPerm.Text.Length == 0) { Fail("권한 암호를 입력하세요.", txtPerm); return; }
                    if (txtPerm.Text != txtPerm2.Text) { Fail("권한 암호와 암호 확인이 일치하지 않습니다.", txtPerm2); return; }
                }
            }

            var s = new AppSettings
            {
                RequireOpenPassword = chkOpen.Checked,
                OpenPassword = txtOpen.Text,
                RequirePermissionPassword = chkPerm.Checked,
                PermissionPassword = txtPerm.Text,
                Print = (PrintPermission)cboPrint.SelectedIndex,
                Edit = (EditPermission)cboEdit.SelectedIndex,
                AllowCopy = chkCopy.Checked,
                AllowAccessibility = chkAccess.Checked,
                Level = (EncryptionLevel)cboLevel.SelectedIndex,
                EncryptMetadata = chkMeta.Checked,
                RememberPasswords = chkRemember.Checked,
                OpenAfterSave = chkOpenAfter.Checked,
                SkipDialog = chkSkip.Checked,
                FolderMode = rdoFixed.Checked ? FolderMode.Fixed : FolderMode.LastUsed,
                FixedFolder = txtFixed.Text.Trim(),
                LastFolder = _orig.LastFolder,
                FileNamePattern = txtPattern.Text.Trim().Length == 0 ? FileNaming.DefaultPattern : txtPattern.Text.Trim(),
                StripAppName = chkStrip.Checked,
                Conflict = (ConflictMode)cboConflict.SelectedIndex
            };

            if (noEncryption)
            {
                // 이번 작업만 암호 없이: 기억된 보안 설정은 유지
                foreach (var f in new[] { "RequireOpenPassword", "OpenPassword", "RequirePermissionPassword", "PermissionPassword",
                                          "Print", "Edit", "AllowCopy", "AllowAccessibility" })
                {
                    var fi = typeof(AppSettings).GetField(f);
                    fi.SetValue(s, fi.GetValue(_orig));
                }
                s.SkipEncryptionOnce = true;
            }
            else
            {
                string err = s.ToSecurityOptions().Validate();
                if (err != null) { Fail(err, null); return; }
            }

            if (s.FolderMode == FolderMode.Fixed)
            {
                if (s.FixedFolder.Length == 0) { Fail("기본 저장 폴더를 지정하세요.", txtFixed); return; }
                if (FileNaming.ResolveFolder(s.FixedFolder, _now) == null) { Fail("기본 저장 폴더 경로가 올바르지 않습니다.", txtFixed); return; }
            }

            if (_jobMode)
            {
                string folder = FileNaming.ResolveFolder(txtFolder.Text, _now);
                if (folder == null) { Fail("저장 폴더 경로가 올바르지 않습니다.", txtFolder); return; }
                string werr = FileNaming.EnsureWritable(folder);
                if (werr != null) { Fail("저장 폴더를 사용할 수 없습니다.\n" + folder + "\n\n" + werr, txtFolder); return; }

                // 파일 이름 칸에 {date} 같은 항목을 직접 써도 전개
                string typed = txtName.Text;
                string name = typed.IndexOf('{') >= 0
                    ? (FileNaming.HasCounter(typed)
                        ? FileNaming.ExpandWithCounter(folder, typed, _docTitle, _now, s.StripAppName)
                        : FileNaming.Expand(typed, _docTitle, _now, s.StripAppName, 1))
                    : FileNaming.SanitizeFileName(typed);
                if (typed.Trim().Length == 0) { Fail("파일 이름을 입력하세요.", txtName); return; }

                string path;
                bool exists;
                try { path = FileNaming.BuildPath(folder, name, s.Conflict, out exists); }
                catch (Exception ex) { Fail(ex.Message, txtFolder); return; }
                if (exists)
                {
                    var r = MessageBox.Show(this,
                        "같은 이름의 파일이 이미 있습니다.\n" + path + "\n\n예: 덮어쓰기\n아니요: 번호를 붙여 저장\n취소: 돌아가기",
                        "Goni PDF", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Cancel) return;
                    if (r == DialogResult.No) path = FileNaming.NumberedPath(folder, Path.GetFileNameWithoutExtension(path));
                }
                OutputPath = path;
                s.LastFolder = folder;
            }
            Result = s;
            DialogResult = DialogResult.OK;
            Close();
        }

        // ------------------------------------------------------------------
        // 컨트롤 도우미
        // ------------------------------------------------------------------

        static TabPage NewPage(string text)
        {
            return new TabPage(text) { BackColor = Color.White, UseVisualStyleBackColor = false };
        }

        static void Header(Control parent, string text, ref int y)
        {
            parent.Controls.Add(new Label
            {
                Text = text, Left = 10, Top = y, AutoSize = true,
                Font = new Font("Malgun Gothic", 11F, FontStyle.Bold)
            });
            y += 30;
        }

        static void Hint(Control parent, string text, int y, int x = LabelX)
        {
            parent.Controls.Add(new Label { Text = text, Left = x, Top = y, AutoSize = true, ForeColor = Color.DimGray });
        }

        static CheckBox Check(Control parent, string text, ref int y)
        {
            var c = new CheckBox { Text = text, Left = LabelX, Top = y, AutoSize = true };
            parent.Controls.Add(c);
            y += 28;
            return c;
        }

        static Label AddLabel(Control parent, string text, int x, int y)
        {
            var l = new Label { Text = text, Left = x, Top = y + 3, AutoSize = true };
            parent.Controls.Add(l);
            return l;
        }

        static TextBox Pw(Control parent, int y)
        {
            var t = new TextBox { Left = FieldX, Top = y, Width = FieldW, UseSystemPasswordChar = true };
            parent.Controls.Add(t);
            return t;
        }

        static ComboBox Combo(Control parent, int y, int width, params string[] items)
        {
            var c = new ComboBox { Left = FieldX, Top = y, Width = width, DropDownStyle = ComboBoxStyle.DropDownList };
            c.Items.AddRange(items);
            parent.Controls.Add(c);
            return c;
        }

        string PickFolder(string current)
        {
            using (var d = new FolderBrowserDialog())
            {
                d.Description = "PDF를 저장할 폴더를 선택하세요.";
                d.ShowNewFolderButton = true;
                string resolved = FileNaming.ResolveFolder(current, _now);
                if (resolved != null && Directory.Exists(resolved)) d.SelectedPath = resolved;
                return d.ShowDialog(this) == DialogResult.OK ? d.SelectedPath : null;
            }
        }
    }
}
