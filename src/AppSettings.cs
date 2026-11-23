using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GoniPdfPrinter
{
    /// <summary>
    /// 마지막 사용 설정 저장/복원. %APPDATA%\GoniPdfPrinter\settings.ini
    /// 암호는 Windows DPAPI(현재 사용자 범위)로 암호화하여 저장한다.
    /// </summary>
    sealed class AppSettings
    {
        public bool RequireOpenPassword = false;
        public string OpenPassword = "";
        public bool RequirePermissionPassword = true;
        public string PermissionPassword = "";
        public PrintPermission Print = PrintPermission.None;
        public EditPermission Edit = EditPermission.None;
        public bool AllowCopy = false;
        public bool AllowAccessibility = true;
        public EncryptionLevel Level = EncryptionLevel.Aes256;
        public bool EncryptMetadata = true;
        public bool RememberPasswords = true;
        public bool OpenAfterSave = true;
        public bool SkipDialog = false;
        public bool SkipEncryptionOnce = false; // [암호 없이 저장] - 이번 작업에만 적용, 파일에 기록하지 않음

        // 저장 위치 / 파일 이름
        public FolderMode FolderMode = FolderMode.LastUsed;
        public string FixedFolder = FileNaming.DefaultFolder;
        public string LastFolder = FileNaming.DefaultFolder;
        public string FileNamePattern = FileNaming.DefaultPattern;
        public ConflictMode Conflict = ConflictMode.AutoNumber;
        public bool StripAppName = true;

        /// <summary>기본 저장 폴더(해석된 실제 경로). 해석 실패 시 문서 폴더.</summary>
        public string ResolveDefaultFolder(DateTime now)
        {
            string raw = FolderMode == FolderMode.Fixed ? FixedFolder : LastFolder;
            return FileNaming.ResolveFolder(raw, now) ?? FileNaming.DefaultFolder;
        }

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GoniPdfPrinter"); }
        }

        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public SecurityOptions ToSecurityOptions()
        {
            if (SkipEncryptionOnce) return new SecurityOptions();
            return new SecurityOptions
            {
                OpenPassword = RequireOpenPassword ? OpenPassword : "",
                PermissionPassword = RequirePermissionPassword ? PermissionPassword : "",
                RestrictPermissions = RequirePermissionPassword,
                Print = Print,
                Edit = Edit,
                AllowCopy = AllowCopy,
                AllowAccessibility = AllowAccessibility,
                Level = Level,
                EncryptMetadata = EncryptMetadata
            };
        }

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0 || line.StartsWith("#")) continue;
                    map[line.Substring(0, i).Trim()] = line.Substring(i + 1);
                }
                s.RequireOpenPassword = GetBool(map, "RequireOpenPassword", s.RequireOpenPassword);
                s.RequirePermissionPassword = GetBool(map, "RequirePermissionPassword", s.RequirePermissionPassword);
                s.Print = (PrintPermission)GetInt(map, "Print", (int)s.Print, 0, 2);
                s.Edit = (EditPermission)GetInt(map, "Edit", (int)s.Edit, 0, 4);
                s.AllowCopy = GetBool(map, "AllowCopy", s.AllowCopy);
                s.AllowAccessibility = GetBool(map, "AllowAccessibility", s.AllowAccessibility);
                s.Level = (EncryptionLevel)GetInt(map, "Level", (int)s.Level, 0, 1);
                s.EncryptMetadata = GetBool(map, "EncryptMetadata", s.EncryptMetadata);
                s.RememberPasswords = GetBool(map, "RememberPasswords", s.RememberPasswords);
                s.OpenAfterSave = GetBool(map, "OpenAfterSave", s.OpenAfterSave);
                s.SkipDialog = GetBool(map, "SkipDialog", s.SkipDialog);
                string v;
                // 1.0.0 호환: OutputFolder → 마지막/지정 폴더
                if (map.TryGetValue("OutputFolder", out v) && v.Trim().Length > 0) s.LastFolder = s.FixedFolder = v.Trim();
                s.FolderMode = (FolderMode)GetInt(map, "FolderMode", (int)s.FolderMode, 0, 1);
                if (map.TryGetValue("FixedFolder", out v) && v.Trim().Length > 0) s.FixedFolder = v.Trim();
                if (map.TryGetValue("LastFolder", out v) && v.Trim().Length > 0) s.LastFolder = v.Trim();
                if (map.TryGetValue("FileNamePattern", out v) && v.Trim().Length > 0) s.FileNamePattern = v.Trim();
                s.Conflict = (ConflictMode)GetInt(map, "Conflict", (int)s.Conflict, 0, 2);
                s.StripAppName = GetBool(map, "StripAppName", s.StripAppName);
                if (s.RememberPasswords)
                {
                    s.OpenPassword = Unprotect(map, "OpenPassword");
                    s.PermissionPassword = Unprotect(map, "PermissionPassword");
                }
            }
            catch { /* 손상된 설정은 기본값 사용 */ }
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            var sb = new StringBuilder();
            sb.AppendLine("# Goni PDF 가상 프린터 - 마지막 사용 설정");
            Add(sb, "RequireOpenPassword", RequireOpenPassword);
            Add(sb, "RequirePermissionPassword", RequirePermissionPassword);
            sb.AppendLine("Print=" + (int)Print);
            sb.AppendLine("Edit=" + (int)Edit);
            Add(sb, "AllowCopy", AllowCopy);
            Add(sb, "AllowAccessibility", AllowAccessibility);
            sb.AppendLine("Level=" + (int)Level);
            Add(sb, "EncryptMetadata", EncryptMetadata);
            Add(sb, "RememberPasswords", RememberPasswords);
            Add(sb, "OpenAfterSave", OpenAfterSave);
            Add(sb, "SkipDialog", SkipDialog);
            sb.AppendLine("FolderMode=" + (int)FolderMode);
            sb.AppendLine("FixedFolder=" + OneLine(FixedFolder));
            sb.AppendLine("LastFolder=" + OneLine(LastFolder));
            sb.AppendLine("FileNamePattern=" + OneLine(FileNamePattern));
            sb.AppendLine("Conflict=" + (int)Conflict);
            Add(sb, "StripAppName", StripAppName);
            if (RememberPasswords)
            {
                sb.AppendLine("OpenPassword=" + Protect(OpenPassword));
                sb.AppendLine("PermissionPassword=" + Protect(PermissionPassword));
            }
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(true));
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }

        static readonly char[] LineBreaks = { (char)13, (char)10 };

        static string OneLine(string v)
        {
            foreach (char c in LineBreaks) v = (v ?? "").Replace(c, ' ');
            return (v ?? "").Trim();
        }


        static void Add(StringBuilder sb, string k, bool v) { sb.AppendLine(k + "=" + (v ? "1" : "0")); }

        static bool GetBool(Dictionary<string, string> m, string k, bool def)
        {
            string v;
            if (!m.TryGetValue(k, out v)) return def;
            return v.Trim() == "1";
        }

        static int GetInt(Dictionary<string, string> m, string k, int def, int min, int max)
        {
            string v;
            int r;
            if (!m.TryGetValue(k, out v) || !int.TryParse(v.Trim(), out r) || r < min || r > max) return def;
            return r;
        }

        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GoniPdfPrinter.v1");

        static string Protect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            byte[] p = ProtectedData.Protect(Encoding.UTF8.GetBytes(s), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(p);
        }

        static string Unprotect(Dictionary<string, string> m, string k)
        {
            string v;
            if (!m.TryGetValue(k, out v) || v.Trim().Length == 0) return "";
            try
            {
                byte[] p = ProtectedData.Unprotect(Convert.FromBase64String(v.Trim()), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(p);
            }
            catch { return ""; }
        }
    }
}
