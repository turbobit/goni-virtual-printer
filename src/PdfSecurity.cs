using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GoniPdfPrinter
{
    enum PrintPermission { None = 0, LowResolution = 1, HighResolution = 2 }

    enum EditPermission
    {
        None = 0,              // 없음
        PageAssembly = 1,      // 페이지 삽입, 삭제 및 회전
        FillForms = 2,         // 양식 필드 채우기 및 서명
        CommentAndForms = 3,   // 주석 작성, 양식 필드 채우기 및 서명
        AllExceptExtract = 4   // 페이지 추출을 제외한 모든 작업
    }

    enum EncryptionLevel { Aes256 = 0, Aes128 = 1 }

    sealed class SecurityOptions
    {
        public string OpenPassword = "";        // 사용자(열기) 암호 - 빈 값이면 암호 없이 열림
        public string PermissionPassword = "";  // 소유자(권한) 암호
        public bool RestrictPermissions;
        public PrintPermission Print = PrintPermission.None;
        public EditPermission Edit = EditPermission.None;
        public bool AllowCopy;
        public bool AllowAccessibility = true;
        public EncryptionLevel Level = EncryptionLevel.Aes256;
        public bool EncryptMetadata = true;

        public bool NeedsEncryption
        {
            get { return OpenPassword.Length > 0 || (RestrictPermissions && PermissionPassword.Length > 0); }
        }

        /// <summary>PDF 표준 보안 처리기의 /P 값 (ISO 32000 표 22)</summary>
        public int ComputeP()
        {
            if (!RestrictPermissions) return unchecked((int)0xFFFFFFFC);
            uint p = 0xFFFFF0C0; // 7,8, 13~32 비트는 1
            if (Print != PrintPermission.None) p |= 1u << 2;            // bit 3 인쇄
            if (Print == PrintPermission.HighResolution) p |= 1u << 11; // bit 12 고품질 인쇄
            switch (Edit)
            {
                case EditPermission.PageAssembly: p |= 1u << 10; break;                         // bit 11
                case EditPermission.FillForms: p |= 1u << 8; break;                             // bit 9
                case EditPermission.CommentAndForms: p |= (1u << 8) | (1u << 5); break;         // bit 9, 6
                case EditPermission.AllExceptExtract: p |= (1u << 3) | (1u << 5) | (1u << 8) | (1u << 10); break;
            }
            if (AllowCopy) p |= 1u << 4;            // bit 5 내용 복사
            if (AllowAccessibility) p |= 1u << 9;   // bit 10 접근성용 텍스트 추출
            return unchecked((int)p);
        }

        /// <summary>입력값 검증. 오류 메시지 또는 null.</summary>
        public string Validate()
        {
            if (Level == EncryptionLevel.Aes128)
            {
                foreach (string pw in new[] { OpenPassword, PermissionPassword })
                {
                    foreach (char c in pw)
                        if (c < 32 || c > 126)
                            return "AES-128 방식은 암호에 영문/숫자/기호만 사용할 수 있습니다.\n한글 암호는 AES-256 방식을 선택하세요.";
                    if (pw.Length > 32) return "AES-128 방식의 암호는 32자 이하여야 합니다.";
                }
            }
            else
            {
                if (Encoding.UTF8.GetByteCount(OpenPassword) > 127 || Encoding.UTF8.GetByteCount(PermissionPassword) > 127)
                    return "암호가 너무 깁니다. (최대 127바이트)";
            }
            if (RestrictPermissions && PermissionPassword.Length == 0)
                return "권한 암호를 입력하세요.";
            if (RestrictPermissions && OpenPassword.Length > 0 && OpenPassword == PermissionPassword)
                return "문서 열기 암호와 권한 암호는 서로 달라야 합니다.";
            return null;
        }
    }

    // ------------------------------------------------------------------
    // 보안 처리기
    // ------------------------------------------------------------------
    abstract class SecurityHandler
    {
        public abstract byte[] Encrypt(byte[] data, int num, int gen);
        public abstract PdfDict BuildEncryptDict();
        public bool EncryptMetadata = true;
        public abstract bool IsAes256 { get; }

        protected static readonly RandomNumberGenerator Rng = new RNGCryptoServiceProvider();

        public static byte[] RandomBytes(int n)
        {
            var b = new byte[n];
            Rng.GetBytes(b);
            return b;
        }

        protected static byte[] AesCbcEncrypt(byte[] key, byte[] data)
        {
            using (var aes = new AesCryptoServiceProvider())
            {
                aes.Key = key;
                aes.IV = RandomBytes(16);
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var enc = aes.CreateEncryptor())
                {
                    byte[] c = enc.TransformFinalBlock(data, 0, data.Length);
                    byte[] r = new byte[16 + c.Length];
                    Buffer.BlockCopy(aes.IV, 0, r, 0, 16);
                    Buffer.BlockCopy(c, 0, r, 16, c.Length);
                    return r;
                }
            }
        }

        protected static byte[] AesRaw(byte[] key, byte[] iv, byte[] data, CipherMode mode)
        {
            using (var aes = new AesCryptoServiceProvider())
            {
                aes.Key = key;
                aes.IV = iv ?? new byte[16];
                aes.Mode = mode;
                aes.Padding = PaddingMode.None;
                using (var enc = aes.CreateEncryptor())
                    return enc.TransformFinalBlock(data, 0, data.Length);
            }
        }

        protected static byte[] Concat(params byte[][] parts)
        {
            int len = 0;
            foreach (var p in parts) len += p.Length;
            var r = new byte[len];
            int o = 0;
            foreach (var p in parts) { Buffer.BlockCopy(p, 0, r, o, p.Length); o += p.Length; }
            return r;
        }

        protected static byte[] Sub(byte[] b, int off, int len)
        {
            var r = new byte[len];
            Buffer.BlockCopy(b, off, r, 0, len);
            return r;
        }

        protected static PdfDict CryptFilters(string cfm, int lengthBytes)
        {
            var std = new PdfDict();
            std.Set("AuthEvent", new PdfName("DocOpen"));
            std.Set("CFM", new PdfName(cfm));
            std.Set("Length", new PdfAtom(lengthBytes.ToString(CultureInfo.InvariantCulture)));
            var cf = new PdfDict();
            cf.Set("StdCF", std);
            return cf;
        }
    }

    /// <summary>AES-256 (PDF 2.0 / ISO 32000-2, V5 R6)</summary>
    sealed class Aes256Handler : SecurityHandler
    {
        readonly byte[] _fileKey;
        byte[] _u, _ue, _o, _oe, _perms;
        int _p;

        public override bool IsAes256 { get { return true; } }

        public Aes256Handler(SecurityOptions opt)
        {
            EncryptMetadata = opt.EncryptMetadata;
            _fileKey = RandomBytes(32);
            _p = opt.ComputeP();

            byte[] user = PreparePassword(opt.OpenPassword);
            string ownerStr = opt.RestrictPermissions && opt.PermissionPassword.Length > 0 ? opt.PermissionPassword : opt.OpenPassword;
            byte[] owner = PreparePassword(ownerStr);

            // 알고리즘 8: U, UE
            byte[] uvs = RandomBytes(8), uks = RandomBytes(8);
            _u = Concat(Hash2B(user, uvs, new byte[0]), uvs, uks);
            _ue = AesRaw(Hash2B(user, uks, new byte[0]), null, _fileKey, CipherMode.CBC);

            // 알고리즘 9: O, OE
            byte[] ovs = RandomBytes(8), oks = RandomBytes(8);
            _o = Concat(Hash2B(owner, ovs, _u), ovs, oks);
            _oe = AesRaw(Hash2B(owner, oks, _u), null, _fileKey, CipherMode.CBC);

            // 알고리즘 10: Perms
            byte[] perms = new byte[16];
            uint up = unchecked((uint)_p);
            perms[0] = (byte)up; perms[1] = (byte)(up >> 8); perms[2] = (byte)(up >> 16); perms[3] = (byte)(up >> 24);
            perms[4] = perms[5] = perms[6] = perms[7] = 0xFF;
            perms[8] = (byte)(EncryptMetadata ? 'T' : 'F');
            perms[9] = (byte)'a'; perms[10] = (byte)'d'; perms[11] = (byte)'b';
            Buffer.BlockCopy(RandomBytes(4), 0, perms, 12, 4);
            _perms = AesRaw(_fileKey, null, perms, CipherMode.ECB);
        }

        static byte[] PreparePassword(string pw)
        {
            // SASLprep 근사: NFKC 정규화 후 UTF-8, 127바이트 제한
            byte[] b = Encoding.UTF8.GetBytes((pw ?? "").Normalize(NormalizationForm.FormKC));
            if (b.Length > 127) b = Sub(b, 0, 127);
            return b;
        }

        /// <summary>ISO 32000-2 알고리즘 2.B</summary>
        static byte[] Hash2B(byte[] password, byte[] salt, byte[] udata)
        {
            byte[] k;
            using (var sha = SHA256.Create()) k = sha.ComputeHash(Concat(password, salt, udata));
            int round = 0;
            byte[] e;
            while (true)
            {
                byte[] seq = Concat(password, k, udata);
                byte[] k1 = new byte[seq.Length * 64];
                for (int i = 0; i < 64; i++) Buffer.BlockCopy(seq, 0, k1, i * seq.Length, seq.Length);
                e = AesRaw(Sub(k, 0, 16), Sub(k, 16, 16), k1, CipherMode.CBC);
                int sum = 0;
                for (int i = 0; i < 16; i++) sum += e[i];
                switch (sum % 3)
                {
                    case 0: using (var h = SHA256.Create()) k = h.ComputeHash(e); break;
                    case 1: using (var h = SHA384.Create()) k = h.ComputeHash(e); break;
                    default: using (var h = SHA512.Create()) k = h.ComputeHash(e); break;
                }
                round++;
                if (round >= 64 && e[e.Length - 1] <= round - 32) break;
            }
            return Sub(k, 0, 32);
        }

        public override byte[] Encrypt(byte[] data, int num, int gen)
        {
            return AesCbcEncrypt(_fileKey, data);
        }

        public override PdfDict BuildEncryptDict()
        {
            var d = new PdfDict();
            d.Set("Filter", new PdfName("Standard"));
            d.Set("V", new PdfAtom("5"));
            d.Set("R", new PdfAtom("6"));
            d.Set("Length", new PdfAtom("256"));
            d.Set("CF", CryptFilters("AESV3", 32));
            d.Set("StmF", new PdfName("StdCF"));
            d.Set("StrF", new PdfName("StdCF"));
            d.Set("O", new PdfString(_o));
            d.Set("U", new PdfString(_u));
            d.Set("OE", new PdfString(_oe));
            d.Set("UE", new PdfString(_ue));
            d.Set("P", new PdfAtom(_p.ToString(CultureInfo.InvariantCulture)));
            d.Set("Perms", new PdfString(_perms));
            d.Set("EncryptMetadata", new PdfAtom(EncryptMetadata ? "true" : "false"));
            return d;
        }
    }

    /// <summary>AES-128 (PDF 1.6, V4 R4, AESV2)</summary>
    sealed class Aes128Handler : SecurityHandler
    {
        static readonly byte[] Pad = {
            0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
            0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A };

        readonly byte[] _key;
        readonly byte[] _o, _u;
        readonly int _p;

        public override bool IsAes256 { get { return false; } }

        public Aes128Handler(SecurityOptions opt, byte[] id0)
        {
            EncryptMetadata = opt.EncryptMetadata;
            _p = opt.ComputeP();
            byte[] user = PadPw(opt.OpenPassword);
            string ownerStr = opt.RestrictPermissions && opt.PermissionPassword.Length > 0 ? opt.PermissionPassword : opt.OpenPassword;
            byte[] owner = PadPw(ownerStr);

            using (var md5 = MD5.Create())
            {
                // 알고리즘 3: O
                byte[] h = md5.ComputeHash(owner);
                for (int i = 0; i < 50; i++) h = md5.ComputeHash(h);
                byte[] okey = Sub(h, 0, 16);
                byte[] o = Rc4(okey, user);
                for (int i = 1; i <= 19; i++) o = Rc4(XorKey(okey, i), o);
                _o = o;

                // 알고리즘 2: 파일 키
                uint up = unchecked((uint)_p);
                byte[] pb = { (byte)up, (byte)(up >> 8), (byte)(up >> 16), (byte)(up >> 24) };
                byte[] input = Concat(user, _o, pb, id0);
                if (!EncryptMetadata) input = Concat(input, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
                byte[] k = md5.ComputeHash(input);
                for (int i = 0; i < 50; i++) k = md5.ComputeHash(Sub(k, 0, 16));
                _key = Sub(k, 0, 16);

                // 알고리즘 5: U
                byte[] u = Rc4(_key, md5.ComputeHash(Concat(Pad, id0)));
                for (int i = 1; i <= 19; i++) u = Rc4(XorKey(_key, i), u);
                _u = Concat(u, new byte[16]);
            }
        }

        static byte[] PadPw(string pw)
        {
            byte[] b = Encoding.GetEncoding(28591).GetBytes(pw ?? "");
            byte[] r = new byte[32];
            int n = Math.Min(32, b.Length);
            Buffer.BlockCopy(b, 0, r, 0, n);
            Buffer.BlockCopy(Pad, 0, r, n, 32 - n);
            return r;
        }

        static byte[] XorKey(byte[] key, int x)
        {
            var r = new byte[key.Length];
            for (int i = 0; i < key.Length; i++) r[i] = (byte)(key[i] ^ x);
            return r;
        }

        static byte[] Rc4(byte[] key, byte[] data)
        {
            var s = new byte[256];
            for (int i = 0; i < 256; i++) s[i] = (byte)i;
            int j = 0;
            for (int i = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) & 255;
                byte t = s[i]; s[i] = s[j]; s[j] = t;
            }
            var r = new byte[data.Length];
            int a = 0, bb = 0;
            for (int n = 0; n < data.Length; n++)
            {
                a = (a + 1) & 255;
                bb = (bb + s[a]) & 255;
                byte t = s[a]; s[a] = s[bb]; s[bb] = t;
                r[n] = (byte)(data[n] ^ s[(s[a] + s[bb]) & 255]);
            }
            return r;
        }

        public override byte[] Encrypt(byte[] data, int num, int gen)
        {
            byte[] ext = {
                (byte)num, (byte)(num >> 8), (byte)(num >> 16), (byte)gen, (byte)(gen >> 8),
                0x73, 0x41, 0x6C, 0x54 }; // "sAlT"
            byte[] objKey;
            using (var md5 = MD5.Create()) objKey = md5.ComputeHash(Concat(_key, ext));
            return AesCbcEncrypt(objKey, data);
        }

        public override PdfDict BuildEncryptDict()
        {
            var d = new PdfDict();
            d.Set("Filter", new PdfName("Standard"));
            d.Set("V", new PdfAtom("4"));
            d.Set("R", new PdfAtom("4"));
            d.Set("Length", new PdfAtom("128"));
            d.Set("CF", CryptFilters("AESV2", 16));
            d.Set("StmF", new PdfName("StdCF"));
            d.Set("StrF", new PdfName("StdCF"));
            d.Set("O", new PdfString(_o));
            d.Set("U", new PdfString(_u));
            d.Set("P", new PdfAtom(_p.ToString(CultureInfo.InvariantCulture)));
            if (!EncryptMetadata) d.Set("EncryptMetadata", new PdfAtom("false"));
            return d;
        }
    }

    // ------------------------------------------------------------------
    // 암호화된 PDF 재작성기
    // ------------------------------------------------------------------
    static class PdfEncryptor
    {
        public static byte[] Encrypt(byte[] input, SecurityOptions opt)
        {
            if (!opt.NeedsEncryption) return input;
            string err = opt.Validate();
            if (err != null) throw new PdfException(err);

            var doc = new PdfDocument(input);
            if (doc.Trailer.Get("Encrypt") != null)
                throw new PdfException("이미 암호화된 PDF입니다.");

            // 문서 ID
            byte[] id0, id1;
            var idArr = doc.Trailer.Get("ID") as PdfArray;
            if (idArr != null && idArr.Items.Count == 2 && idArr.Items[0] is PdfString && ((PdfString)idArr.Items[0]).Value.Length > 0)
            {
                id0 = ((PdfString)idArr.Items[0]).Value;
                id1 = SecurityHandler.RandomBytes(16);
            }
            else
            {
                id0 = SecurityHandler.RandomBytes(16);
                id1 = id0;
            }

            SecurityHandler handler = opt.Level == EncryptionLevel.Aes128
                ? (SecurityHandler)new Aes128Handler(opt, id0)
                : new Aes256Handler(opt);

            // 기록할 객체 수집 (교차참조/객체 스트림은 제외 - 내용은 개별 객체로 펼쳐짐)
            var nums = new List<int>();
            int maxNum = 0;
            foreach (int n in doc.LiveObjectNumbers())
            {
                PdfObj o;
                try { o = doc.GetObject(n); }
                catch (PdfException) { continue; }
                if (o == null) continue;
                var s = o as PdfStream;
                if (s != null && (s.Dict.IsType("XRef") || s.Dict.IsType("ObjStm"))) continue;
                nums.Add(n);
                if (n > maxNum) maxNum = n;
            }

            var rootRef = doc.Trailer.Get("Root") as PdfRef;
            if (rootRef == null) throw new PdfException("문서 카탈로그(/Root)가 없습니다.");

            string version = doc.Version;
            double v;
            if (!double.TryParse(version, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) v = 1.7;
            if (handler.IsAes256 && v < 2.0)
            {
                version = "1.7";
                // Acrobat 확장 수준 8 (AES-256 R6) 선언
                var cat = doc.GetObject(rootRef.Num) as PdfDict;
                if (cat != null && cat.Get("Extensions") == null)
                {
                    var adbe = new PdfDict();
                    adbe.Set("BaseVersion", new PdfName("1.7"));
                    adbe.Set("ExtensionLevel", new PdfAtom("8"));
                    var ext = new PdfDict();
                    ext.Set("ADBE", adbe);
                    cat.Set("Extensions", ext);
                }
            }
            else if (!handler.IsAes256 && v < 1.6) version = "1.6";

            int encNum = maxNum + 1;
            var w = new PdfWriter(handler);
            w.Raw("%PDF-" + version + "\n");
            w.Bytes(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });

            var offsets = new Dictionary<int, long>();
            var gens = new Dictionary<int, int>();
            foreach (int n in nums)
            {
                int gen = doc.GetGen(n);
                offsets[n] = w.Position;
                gens[n] = gen;
                w.Raw(n + " " + gen + " obj\n");
                w.WriteObject(doc.GetObject(n), n, gen, true);
                w.Raw("\nendobj\n");
            }

            offsets[encNum] = w.Position;
            gens[encNum] = 0;
            w.Raw(encNum + " 0 obj\n");
            w.WriteObject(handler.BuildEncryptDict(), encNum, 0, false);
            w.Raw("\nendobj\n");

            // 교차참조 테이블
            long xrefPos = w.Position;
            int size = encNum + 1;
            w.Raw("xref\n0 " + size + "\n");
            for (int i = 0; i < size; i++)
            {
                long off;
                if (offsets.TryGetValue(i, out off))
                    w.Raw(off.ToString("D10", CultureInfo.InvariantCulture) + " " + gens[i].ToString("D5", CultureInfo.InvariantCulture) + " n\r\n");
                else
                    w.Raw("0000000000 " + (i == 0 ? "65535" : "00001") + " f\r\n");
            }

            var trailer = new PdfDict();
            trailer.Set("Size", new PdfAtom(size.ToString(CultureInfo.InvariantCulture)));
            trailer.Set("Root", rootRef);
            var info = doc.Trailer.Get("Info") as PdfRef;
            if (info != null && offsets.ContainsKey(info.Num)) trailer.Set("Info", info);
            trailer.Set("Encrypt", new PdfRef(encNum, 0));
            var ids = new PdfArray();
            ids.Items.Add(new PdfString(id0));
            ids.Items.Add(new PdfString(id1));
            trailer.Set("ID", ids);
            w.Raw("trailer\n");
            w.WriteObject(trailer, 0, 0, false);
            w.Raw("\nstartxref\n" + xrefPos + "\n%%EOF\n");
            return w.ToArray();
        }
    }

    sealed class PdfWriter
    {
        readonly MemoryStream _ms = new MemoryStream();
        readonly SecurityHandler _h;
        static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        public PdfWriter(SecurityHandler h) { _h = h; }

        public long Position { get { return _ms.Position; } }
        public byte[] ToArray() { return _ms.ToArray(); }

        public void Raw(string s) { Bytes(Latin1.GetBytes(s)); }
        public void Bytes(byte[] b) { _ms.Write(b, 0, b.Length); }

        public void WriteObject(PdfObj o, int num, int gen, bool encrypt)
        {
            if (o == null) { Raw("null"); return; }
            if (o is PdfName) { Raw("/" + ((PdfName)o).Raw); return; }
            if (o is PdfAtom) { Raw(((PdfAtom)o).Raw); return; }
            if (o is PdfRef) { var r = (PdfRef)o; Raw(r.Num + " " + r.Gen + " R"); return; }
            if (o is PdfString)
            {
                byte[] v = ((PdfString)o).Value;
                if (encrypt) v = _h.Encrypt(v, num, gen);
                var sb = new StringBuilder(v.Length * 2 + 2);
                sb.Append('<');
                foreach (byte b in v) sb.Append(b.ToString("X2"));
                sb.Append('>');
                Raw(sb.ToString());
                return;
            }
            if (o is PdfArray)
            {
                Raw("[");
                bool first = true;
                foreach (var it in ((PdfArray)o).Items)
                {
                    if (!first) Raw(" ");
                    WriteObject(it, num, gen, encrypt);
                    first = false;
                }
                Raw("]");
                return;
            }
            if (o is PdfDict)
            {
                Raw("<<");
                foreach (var e in ((PdfDict)o).Entries)
                {
                    Raw("\n/" + e.Key + " ");
                    WriteObject(e.Value, num, gen, encrypt);
                }
                Raw("\n>>");
                return;
            }
            if (o is PdfStream)
            {
                var s = (PdfStream)o;
                byte[] data = s.Data;
                bool encStream = encrypt;
                if (s.Dict.IsType("Metadata") && !_h.EncryptMetadata) encStream = false;
                if (encStream) data = _h.Encrypt(data, num, gen);
                s.Dict.Set("Length", new PdfAtom(data.Length.ToString(CultureInfo.InvariantCulture)));
                WriteObject(s.Dict, num, gen, encrypt);
                Raw("\nstream\r\n");
                Bytes(data);
                Raw("\r\nendstream");
                return;
            }
            throw new PdfException("알 수 없는 객체 형식");
        }
    }
}
