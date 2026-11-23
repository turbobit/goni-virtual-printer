using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GoniPdfPrinter
{
    /// <summary>기본 저장 폴더 결정 방식</summary>
    enum FolderMode { LastUsed = 0, Fixed = 1 }

    /// <summary>같은 이름의 파일이 있을 때</summary>
    enum ConflictMode { AutoNumber = 0, Overwrite = 1, Ask = 2 }

    /// <summary>
    /// 저장 파일명·폴더 규칙.
    /// 파일명 형식 토큰: {title} {date} {time} {datetime} {yyyy} {yy} {MM} {dd} {HH} {mm} {ss}
    ///                  {user} {computer} {counter}  + 서식 지정 {date:yyyyMMdd} {counter:000}
    /// 폴더: 환경 변수(%USERPROFILE%)와 {yyyy} {MM} {dd} {date} {user} {computer} 사용 가능.
    /// </summary>
    static class FileNaming
    {
        public const string DefaultPattern = "{title}";
        public const string FallbackTitle = "문서";
        public const int MaxNameLength = 150;     // 확장자 제외 파일명 최대 길이(문자)
        public const int MaxPathLength = 259;     // MAX_PATH - 1

        static readonly Regex TokenRx = new Regex(@"\{(\w+)(?::([^{}]*))?\}", RegexOptions.Compiled);

        static readonly HashSet<string> KnownExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "doc", "docx", "docm", "dot", "dotx", "rtf", "odt", "txt", "text", "log", "md", "csv", "tsv",
            "xls", "xlsx", "xlsm", "xlsb", "ods", "ppt", "pptx", "pptm", "pps", "ppsx", "odp",
            "hwp", "hwpx", "hwt", "cell", "show", "pdf", "xps", "oxps",
            "htm", "html", "mht", "mhtml", "xml", "json",
            "jpg", "jpeg", "png", "gif", "bmp", "tif", "tiff", "webp", "heic", "svg", "emf", "wmf",
            "vsd", "vsdx", "pub", "one", "msg", "eml", "cs", "py", "js", "ts", "java", "c", "cpp", "h", "sql", "ini", "cfg"
        };

        // 창 제목에 붙는 프로그램 이름 (앞: "Microsoft Word - 문서", 뒤: "문서 - Word")
        static readonly string[] AppNames =
        {
            "Microsoft Word", "Word", "Microsoft Excel", "Excel", "Microsoft PowerPoint", "PowerPoint",
            "Microsoft Outlook", "Outlook", "OneNote", "Microsoft Visio", "Visio", "Microsoft Publisher",
            "메모장", "Notepad", "Notepad++", "워드패드", "WordPad", "그림판", "Paint", "사진", "Photos",
            "한글", "한컴오피스 한글", "Hwp", "한셀", "한쇼", "한컴오피스",
            "Google Chrome", "Chrome", "Microsoft Edge", "Edge", "Mozilla Firefox", "Firefox",
            "Whale", "Naver Whale", "Opera", "Brave",
            "Adobe Acrobat", "Adobe Acrobat Reader", "Adobe Acrobat Reader DC", "Acrobat Reader",
            "Visual Studio Code", "Visual Studio", "LibreOffice Writer", "LibreOffice Calc", "LibreOffice Impress",
        };

        static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
            "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
            "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³"
        };

        // ------------------------------------------------------------------
        // 문서 제목 정리
        // ------------------------------------------------------------------

        /// <summary>인쇄 작업의 문서 제목에서 파일명에 쓸 부분만 추출. 쓸 수 없으면 빈 문자열.</summary>
        public static string CleanTitle(string title, bool stripAppName)
        {
            if (title == null) return "";
            string t = NormalizeText(title);
            if (t.Length == 0) return "";

            if (stripAppName)
            {
                t = t.TrimStart('*', '•', '●').Trim(); // 메모장 등의 "*수정됨" 표시
                t = StripAppNames(t);
            }

            // URL → 마지막 경로 조각 또는 호스트
            Uri uri;
            if (Regex.IsMatch(t, @"^[a-zA-Z][a-zA-Z0-9+.-]*://") && Uri.TryCreate(t, UriKind.Absolute, out uri))
            {
                string seg = "";
                string[] parts = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0) seg = Uri.UnescapeDataString(parts[parts.Length - 1]);
                t = seg.Length > 0 ? seg : uri.Host;
            }
            // 전체 경로 → 파일명 (C:\..., \\server\..., /home/...)
            else if (Regex.IsMatch(t, @"^([a-zA-Z]:[\\/]|\\\\|/)"))
            {
                string[] parts = t.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0) t = parts[parts.Length - 1];
            }

            if (stripAppName) t = StripKnownExtension(t);
            return t.Trim();
        }

        static string StripAppNames(string t)
        {
            for (int guard = 0; guard < 3; guard++)
            {
                string before = t;
                foreach (string app in AppNames)
                {
                    string a = Regex.Escape(app);
                    // 뒤: "보고서.docx - Word", "페이지 - 개인 - Microsoft Edge" 의 마지막 조각
                    var m = Regex.Match(t, @"^(?<rest>.+?)\s+[-–—|]\s+" + a + @"$", RegexOptions.IgnoreCase);
                    if (m.Success) { t = m.Groups["rest"].Value.Trim(); break; }
                    // 앞: "Microsoft Word - 보고서.docx"
                    m = Regex.Match(t, @"^" + a + @"\s+[-–—|]\s+(?<rest>.+)$", RegexOptions.IgnoreCase);
                    if (m.Success) { t = m.Groups["rest"].Value.Trim(); break; }
                }
                if (t == before) break;
            }
            // 호환 모드/읽기 전용 표시: "보고서 [호환 모드]", "보고서 (읽기 전용)"
            t = Regex.Replace(t, @"\s*[\[\(](호환 모드|호환성 모드|읽기 전용|Compatibility Mode|Read-Only|Protected View|제한된 보기)[\]\)]\s*", " ",
                RegexOptions.IgnoreCase).Trim();
            return t;
        }

        static string StripKnownExtension(string t)
        {
            int dot = t.LastIndexOf('.');
            if (dot > 0 && dot < t.Length - 1 && KnownExtensions.Contains(t.Substring(dot + 1).Trim()))
                return t.Substring(0, dot).TrimEnd();
            return t;
        }

        static string NormalizeText(string s)
        {
            try { s = s.Normalize(NormalizationForm.FormC); } catch (ArgumentException) { }
            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char c in s)
            {
                // 제어 문자, 방향 제어 문자(RLO 등), 0폭 문자, BOM 제거
                if (char.IsControl(c) || (c >= '\u200B' && c <= '\u200F') || (c >= '\u202A' && c <= '\u202E') ||
                    (c >= '\u2066' && c <= '\u2069') || c == '\uFEFF')
                {
                    if (c == '\t' || c == '\r' || c == '\n') { if (!space) { sb.Append(' '); space = true; } }
                    continue;
                }
                if (char.IsWhiteSpace(c))
                {
                    if (!space) { sb.Append(' '); space = true; }
                    continue;
                }
                sb.Append(c);
                space = false;
            }
            return sb.ToString().Trim();
        }

        // ------------------------------------------------------------------
        // 파일명 형식 전개
        // ------------------------------------------------------------------

        /// <summary>형식 문자열을 전개해 확장자(.pdf) 없는 안전한 파일명을 만든다.</summary>
        public static string Expand(string pattern, string title, DateTime now, bool stripAppName, int counter)
        {
            if (string.IsNullOrWhiteSpace(pattern)) pattern = DefaultPattern;
            string cleanTitle = CleanTitle(title, stripAppName);
            if (cleanTitle.Length == 0 || IsOnlyJunk(SanitizeComponent(cleanTitle))) cleanTitle = FallbackTitle;

            string result = TokenRx.Replace(pattern, m =>
            {
                string v = TokenValue(m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null, cleanTitle, now, counter);
                return v ?? m.Value; // 알 수 없는 토큰은 그대로
            });
            return SanitizeFileName(result);
        }

        public static bool HasCounter(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            foreach (Match m in TokenRx.Matches(pattern))
                if (m.Groups[1].Value.Equals("counter", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string TokenValue(string name, string fmt, string title, DateTime now, int counter)
        {
            switch (name.ToLowerInvariant())
            {
                case "title": return title;
                case "date": return SafeFormat(now, fmt, "yyyy-MM-dd");
                case "time": return SafeFormat(now, fmt, "HHmmss");
                case "datetime": return SafeFormat(now, fmt, "yyyy-MM-dd_HHmmss");
                case "yyyy": return now.ToString("yyyy", CultureInfo.InvariantCulture);
                case "yy": return now.ToString("yy", CultureInfo.InvariantCulture);
                case "mm": return name == "MM" ? now.ToString("MM", CultureInfo.InvariantCulture) : now.ToString("mm", CultureInfo.InvariantCulture);
                case "dd": return now.ToString("dd", CultureInfo.InvariantCulture);
                case "hh": return now.ToString("HH", CultureInfo.InvariantCulture);
                case "ss": return now.ToString("ss", CultureInfo.InvariantCulture);
                case "user": return SanitizeComponent(Environment.UserName);
                case "computer": return SanitizeComponent(Environment.MachineName);
                case "counter":
                    try { return counter.ToString(string.IsNullOrEmpty(fmt) ? "0" : fmt, CultureInfo.InvariantCulture); }
                    catch (FormatException) { return counter.ToString(CultureInfo.InvariantCulture); }
                default: return null;
            }
        }

        static string SafeFormat(DateTime now, string fmt, string def)
        {
            if (string.IsNullOrEmpty(fmt)) fmt = def;
            try { return SanitizeComponent(now.ToString(fmt, CultureInfo.InvariantCulture)); }
            catch (FormatException) { return now.ToString(def, CultureInfo.InvariantCulture); }
        }

        // ------------------------------------------------------------------
        // 파일명 정리
        // ------------------------------------------------------------------

        static readonly HashSet<char> InvalidChars = new HashSet<char>(Path.GetInvalidFileNameChars());

        /// <summary>파일명에 쓸 수 없는 문자를 '_'로 바꾸고 연속된 '_'는 하나로.</summary>
        static string SanitizeComponent(string s)
        {
            s = NormalizeText(s ?? "");
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                bool bad = InvalidChars.Contains(c) || c < 32;
                char o = bad ? '_' : c;
                if (o == '_' && bad && sb.Length > 0 && sb[sb.Length - 1] == '_') continue;
                sb.Append(o);
            }
            return sb.ToString();
        }

        static bool IsOnlyJunk(string s)
        {
            foreach (char c in s) if (c != '_' && c != '.' && c != ' ' && c != '-') return false;
            return true;
        }

        /// <summary>확장자 없는 최종 파일명. 비면 기본 이름.</summary>
        public static string SanitizeFileName(string name)
        {
            string s = SanitizeComponent(name);
            // 사용자가 .pdf를 직접 붙였으면 제거 (호출 측에서 붙임)
            while (s.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4).TrimEnd();
            s = s.Trim().TrimEnd('.', ' ').TrimStart(' ');
            s = TruncateTextElements(s, MaxNameLength).TrimEnd('.', ' ');
            if (s.Length == 0 || IsOnlyJunk(s)) s = FallbackTitle;

            // 예약된 장치 이름 (CON, NUL, COM1, "aux.txt" 포함)
            string stem = s;
            int dot = stem.IndexOf('.');
            if (dot >= 0) stem = stem.Substring(0, dot);
            if (ReservedNames.Contains(stem.TrimEnd())) s = "_" + s;
            return s;
        }

        /// <summary>서로게이트 쌍/결합 문자를 자르지 않고 최대 길이로 자른다.</summary>
        public static string TruncateTextElements(string s, int maxChars)
        {
            if (s.Length <= maxChars) return s;
            var e = StringInfo.GetTextElementEnumerator(s);
            int end = 0;
            while (e.MoveNext())
            {
                int next = e.ElementIndex + ((string)e.Current).Length;
                if (next > maxChars) break;
                end = next;
            }
            return s.Substring(0, end);
        }

        // ------------------------------------------------------------------
        // 폴더
        // ------------------------------------------------------------------

        public static string DefaultFolder
        {
            get
            {
                string d = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return string.IsNullOrEmpty(d) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : d;
            }
        }

        /// <summary>폴더 설정값(환경 변수·날짜 토큰 포함)을 실제 경로로. 해석할 수 없으면 null.</summary>
        public static string ResolveFolder(string folder, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(folder)) return null;
            string f = folder.Trim().Trim('"').Trim();
            f = Environment.ExpandEnvironmentVariables(f);
            f = TokenRx.Replace(f, m =>
            {
                string n = m.Groups[1].Value;
                if (n.Equals("title", StringComparison.OrdinalIgnoreCase) || n.Equals("counter", StringComparison.OrdinalIgnoreCase))
                    return m.Value;
                return TokenValue(n, m.Groups[2].Success ? m.Groups[2].Value : null, "", now, 1) ?? m.Value;
            });
            if (f.StartsWith("~")) f = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + f.Substring(1);
            try
            {
                if (!Path.IsPathRooted(f)) f = Path.Combine(DefaultFolder, f);
                if (f.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
                // 드라이브 문자 뒤 외의 ':' 금지 (C:\a:b)
                if (f.IndexOf(':', 2) >= 0) return null;
                f = Path.GetFullPath(f);
                if (f.Length > 3) f = f.TrimEnd('\\', '/');
                return f;
            }
            catch { return null; }
        }

        /// <summary>폴더를 만들고 쓰기 가능한지 확인. 실패 시 오류 메시지 반환.</summary>
        public static string EnsureWritable(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string probe = Path.Combine(folder, ".goni_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
                using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
                return null;
            }
            catch (Exception ex)
            {
                return ex is UnauthorizedAccessException ? "폴더에 쓸 권한이 없습니다." : ex.Message;
            }
        }

        // ------------------------------------------------------------------
        // 최종 경로
        // ------------------------------------------------------------------

        /// <summary>
        /// 폴더 + 파일명(확장자 없음)으로 저장 경로 생성.
        /// 경로 길이 제한에 맞게 이름을 줄이고, conflict 규칙에 따라 번호를 붙인다.
        /// Ask 모드에서 같은 이름이 있으면 exists=true 와 함께 원래 경로를 돌려준다.
        /// </summary>
        public static string BuildPath(string folder, string baseName, ConflictMode conflict, out bool exists)
        {
            exists = false;
            string name = FitName(folder, baseName, "");
            string path = Path.Combine(folder, name + ".pdf");
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            if (conflict == ConflictMode.Overwrite && !Directory.Exists(path)) return path;
            if (conflict == ConflictMode.Ask && !Directory.Exists(path)) { exists = true; return path; }
            return NumberedPath(folder, baseName);
        }

        public static string NumberedPath(string folder, string baseName)
        {
            for (int i = 2; i < 100000; i++)
            {
                string suffix = " (" + i + ")";
                string p = Path.Combine(folder, FitName(folder, baseName, suffix) + suffix + ".pdf");
                if (!File.Exists(p) && !Directory.Exists(p)) return p;
            }
            throw new IOException("사용 가능한 파일 이름을 찾을 수 없습니다.");
        }

        /// <summary>{counter} 형식이 있을 때 존재하지 않는 첫 번호로 전개.</summary>
        public static string ExpandWithCounter(string folder, string pattern, string title, DateTime now, bool strip)
        {
            for (int n = 1; n < 100000; n++)
            {
                string name = Expand(pattern, title, now, strip, n);
                string p = Path.Combine(folder, FitName(folder, name, "") + ".pdf");
                if (!File.Exists(p) && !Directory.Exists(p)) return name;
            }
            return Expand(pattern, title, now, strip, 1);
        }

        static string FitName(string folder, string name, string suffix)
        {
            int room = MaxPathLength - folder.TrimEnd('\\').Length - 1 - suffix.Length - 4; // "\" + ".pdf"
            if (room < 1) throw new PathTooLongException("저장 폴더 경로가 너무 깁니다.");
            if (name.Length <= room) return name;
            string t = TruncateTextElements(name, room).TrimEnd('.', ' ');
            return t.Length == 0 ? "_" : t;
        }

        /// <summary>임시 파일에 쓴 뒤 교체 (쓰기 도중 실패 시 기존 파일 보존)</summary>
        public static void WriteAtomic(string path, byte[] data)
        {
            string tmp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp";
            if (tmp.Length > MaxPathLength) tmp = Path.Combine(Path.GetDirectoryName(path), Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(tmp, data);
                if (File.Exists(path))
                {
                    try { File.Replace(tmp, path, null, true); }
                    catch (IOException ex)
                    {
                        throw new IOException("기존 파일을 바꿀 수 없습니다. 다른 프로그램에서 열려 있는지 확인하세요.\n(" + ex.Message + ")");
                    }
                }
                else File.Move(tmp, path);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
    }
}
