using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GoniPdfPrinter
{
    /// <summary>
    /// 보안 설정 창. 인쇄 작업 시(파일 저장 모드) 또는 트레이 메뉴의 기본 설정(설정 모드)으로 사용.
    /// </summary>
    sealed class SecurityForm : Form
    {
        readonly bool _jobMode;
        public AppSettings Result;
        public string OutputPath;

        CheckBox chkOpen, chkShowOpen, chkPerm, chkShowPerm, chkCopy, chkAccess, chkMeta, chkRemember, chkOpenAfter, chkSkip;
        TextBox txtOpen, txtOpen2, txtPerm, txtPerm2, txtFolder, txtName;
        ComboBox cboPrint, cboEdit, cboLevel;
        Label lblOpen, lblOpen2, lblPerm, lblPerm2, lblPrint, lblEdit;

        const int LabelX = 28, FieldX = 150, FieldW = 260;

        public SecurityForm(AppSettings s, bool jobMode, string suggestedName)
        {
            _jobMode = jobMode;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Malgun Gothic", 9F);
            Text = jobMode ? "Goni PDF - 보안 설정 후 저장" : "Goni PDF - 기본 보안 설정";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            Icon = AppIcon.Get();
            BackColor = Color.White;

            int y = 14;
            Header("암호 열기", ref y);
            chkOpen = Check("문서를 열 때 암호를 요구합니다.", 14, ref y);
            lblOpen = Lbl("암호:", y); txtOpen = Pw(y);
            chkShowOpen = new CheckBox { Text = "표시", Left = FieldX + FieldW + 10, Top = y + 2, AutoSize = true };
            Controls.Add(chkShowOpen);
            y += 32;
            lblOpen2 = Lbl("암호 확인:", y); txtOpen2 = Pw(y); y += 44;

            Header("권한", ref y);
            chkPerm = Check("문서 보안 설정 변경이나 특정 기능 사용 시 암호를 요구합니다.", 14, ref y);
            lblPerm = Lbl("암호:", y); txtPerm = Pw(y);
            chkShowPerm = new CheckBox { Text = "표시", Left = FieldX + FieldW + 10, Top = y + 2, AutoSize = true };
            Controls.Add(chkShowPerm);
            y += 32;
            lblPerm2 = Lbl("암호 확인:", y); txtPerm2 = Pw(y); y += 40;
            lblPrint = Lbl("인쇄 허용 범위:", y);
            cboPrint = Combo(y, "없음", "저해상도 (150 dpi)", "고해상도"); y += 34;
            lblEdit = Lbl("편집 허용 범위:", y);
            cboEdit = Combo(y, "없음", "페이지 삽입, 삭제 및 회전", "양식 필드 채우기 및 서명",
                "주석 작성, 양식 필드 채우기 및 서명", "페이지 추출을 제외한 모든 작업"); y += 38;
            chkCopy = Check("문구, 이미지를 포함한 내용물의 복사를 허용합니다.", 14, ref y);
            chkAccess = Check("화면 판독기 등 접근성 도구의 텍스트 접근을 허용합니다.", 14, ref y);
            y += 12;

            Header("기타 옵션", ref y);
            Lbl("암호화 방식:", y);
            cboLevel = Combo(y, "AES-256 (권장, Acrobat X 이상)", "AES-128 (Acrobat 7 이상 호환)"); y += 36;
            chkMeta = Check("문서 정보(제목, 작성자 등) 메타데이터도 암호화합니다.", 14, ref y);
            chkRemember = Check("암호를 기억합니다. (Windows 사용자 계정으로 보호하여 저장)", 14, ref y);
            chkOpenAfter = Check("저장 후 PDF 파일을 엽니다.", 14, ref y);
            chkSkip = Check("다음부터 이 창 없이 마지막 설정으로 바로 저장합니다.", 14, ref y);
            y += 12;

            Header("저장 위치", ref y);
            Lbl("폴더:", y);
            txtFolder = new TextBox { Left = FieldX, Top = y, Width = FieldW };
            Controls.Add(txtFolder);
            var btnBrowse = new Button { Text = "찾아보기...", Left = FieldX + FieldW + 8, Top = y - 1, Width = 90, Height = 25 };
            btnBrowse.Click += (o, e) => BrowseFolder();
            Controls.Add(btnBrowse);
            y += 32;
            if (jobMode)
            {
                Lbl("파일 이름:", y);
                txtName = new TextBox { Left = FieldX, Top = y, Width = FieldW, Text = suggestedName ?? "" };
                Controls.Add(txtName);
                Controls.Add(new Label { Text = ".pdf", Left = FieldX + FieldW + 6, Top = y + 3, AutoSize = true });
                y += 32;
            }

            y += 10;
            var btnOk = new Button { Text = jobMode ? "저장" : "확인", Width = 96, Height = 30, Left = 430 - 96 - 8 - 96, Top = y };
            var btnCancel = new Button { Text = "취소", Width = 96, Height = 30, Left = 430 - 96, Top = y, DialogResult = DialogResult.Cancel };
            btnOk.Click += (o, e) => OnOk();
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            ClientSize = new Size(530, y + 30 + 16);

            // 값 채우기
            chkOpen.Checked = s.RequireOpenPassword;
            txtOpen.Text = txtOpen2.Text = s.OpenPassword;
            chkPerm.Checked = s.RequirePermissionPassword;
            txtPerm.Text = txtPerm2.Text = s.PermissionPassword;
            cboPrint.SelectedIndex = (int)s.Print;
            cboEdit.SelectedIndex = (int)s.Edit;
            chkCopy.Checked = s.AllowCopy;
            chkAccess.Checked = s.AllowAccessibility;
            cboLevel.SelectedIndex = (int)s.Level;
            chkMeta.Checked = s.EncryptMetadata;
            chkRemember.Checked = s.RememberPasswords;
            chkOpenAfter.Checked = s.OpenAfterSave;
            chkSkip.Checked = s.SkipDialog;
            txtFolder.Text = s.OutputFolder;
            Result = s.Clone();

            chkOpen.CheckedChanged += (o, e) => UpdateEnabled();
            chkPerm.CheckedChanged += (o, e) => UpdateEnabled();
            chkShowOpen.CheckedChanged += (o, e) => { txtOpen.UseSystemPasswordChar = txtOpen2.UseSystemPasswordChar = !chkShowOpen.Checked; };
            chkShowPerm.CheckedChanged += (o, e) => { txtPerm.UseSystemPasswordChar = txtPerm2.UseSystemPasswordChar = !chkShowPerm.Checked; };
            UpdateEnabled();
            ResumeLayout(false);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 트레이 앱에서 띄우므로 강제로 전면에 표시
            TopMost = true;
            Activate();
            TopMost = false;
            if (_jobMode && txtName != null) { txtName.Focus(); txtName.SelectAll(); }
        }

        void Header(string text, ref int y)
        {
            Controls.Add(new Label
            {
                Text = text, Left = 14, Top = y, AutoSize = true,
                Font = new Font("Malgun Gothic", 12F, FontStyle.Bold)
            });
            y += 32;
        }

        CheckBox Check(string text, int x, ref int y)
        {
            var c = new CheckBox { Text = text, Left = x, Top = y, AutoSize = true };
            Controls.Add(c);
            y += 28;
            return c;
        }

        Label Lbl(string text, int y)
        {
            var l = new Label { Text = text, Left = LabelX, Top = y + 3, AutoSize = true };
            Controls.Add(l);
            return l;
        }

        TextBox Pw(int y)
        {
            var t = new TextBox { Left = FieldX, Top = y, Width = FieldW, UseSystemPasswordChar = true };
            Controls.Add(t);
            return t;
        }

        ComboBox Combo(int y, params string[] items)
        {
            var c = new ComboBox { Left = FieldX, Top = y, Width = FieldW, DropDownStyle = ComboBoxStyle.DropDownList };
            c.Items.AddRange(items);
            Controls.Add(c);
            return c;
        }

        void UpdateEnabled()
        {
            bool o = chkOpen.Checked;
            foreach (Control c in new Control[] { lblOpen, lblOpen2, txtOpen, txtOpen2, chkShowOpen }) c.Enabled = o;
            bool p = chkPerm.Checked;
            foreach (Control c in new Control[] { lblPerm, lblPerm2, txtPerm, txtPerm2, chkShowPerm, lblPrint, lblEdit, cboPrint, cboEdit, chkCopy, chkAccess })
                c.Enabled = p;
        }

        void BrowseFolder()
        {
            using (var d = new FolderBrowserDialog())
            {
                d.Description = "PDF를 저장할 폴더를 선택하세요.";
                d.ShowNewFolderButton = true;
                if (Directory.Exists(txtFolder.Text)) d.SelectedPath = txtFolder.Text;
                if (d.ShowDialog(this) == DialogResult.OK) txtFolder.Text = d.SelectedPath;
            }
        }

        void Fail(string msg, Control focus)
        {
            MessageBox.Show(this, msg, "Goni PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (focus != null) focus.Focus();
        }

        void OnOk()
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
                OutputFolder = txtFolder.Text.Trim()
            };
            string err = s.ToSecurityOptions().Validate();
            if (err != null) { Fail(err, null); return; }

            if (s.OutputFolder.Length == 0) { Fail("저장 폴더를 지정하세요.", txtFolder); return; }

            if (_jobMode)
            {
                string name = FileNames.Sanitize(txtName.Text.Trim());
                if (name.Length == 0) { Fail("파일 이름을 입력하세요.", txtName); return; }
                if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name += ".pdf";
                string path;
                try
                {
                    Directory.CreateDirectory(s.OutputFolder);
                    path = Path.Combine(s.OutputFolder, name);
                }
                catch (Exception ex) { Fail("저장 폴더를 사용할 수 없습니다.\n" + ex.Message, txtFolder); return; }
                if (File.Exists(path) &&
                    MessageBox.Show(this, "같은 이름의 파일이 이미 있습니다. 덮어쓰시겠습니까?\n\n" + path, "Goni PDF",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                OutputPath = path;
            }
            Result = s;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    static class FileNames
    {
        public static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim().TrimEnd('.');
        }

        /// <summary>문서 제목으로 기본 파일 이름 생성 ("보고서.docx" → "보고서")</summary>
        public static string FromTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "문서_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string t = title.Trim();
            // "문서1 - Word" 같은 앱 접미사는 그대로 두고, 파일 확장자만 제거
            int dot = t.LastIndexOf('.');
            if (dot > 0 && t.Length - dot - 1 >= 2 && t.Length - dot - 1 <= 5)
            {
                bool alnum = true;
                for (int i = dot + 1; i < t.Length; i++) if (!char.IsLetterOrDigit(t[i]) || t[i] > 127) alnum = false;
                if (alnum) t = t.Substring(0, dot);
            }
            // 경로가 포함된 제목이면 파일명만
            int slash = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
            if (slash >= 0 && slash < t.Length - 1) t = t.Substring(slash + 1);
            t = Sanitize(t);
            if (t.Length > 120) t = t.Substring(0, 120);
            return t.Length == 0 ? "문서_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") : t;
        }

        public static string Unique(string folder, string baseName)
        {
            string p = Path.Combine(folder, baseName + ".pdf");
            for (int i = 2; File.Exists(p); i++) p = Path.Combine(folder, baseName + " (" + i + ").pdf");
            return p;
        }
    }
}
