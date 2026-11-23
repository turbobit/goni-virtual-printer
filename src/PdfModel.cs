using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GoniPdfPrinter
{
    // ------------------------------------------------------------------
    // 최소 PDF 객체 모델
    // ------------------------------------------------------------------
    abstract class PdfObj { }

    sealed class PdfName : PdfObj
    {
        public string Raw; // '/' 제외
        public PdfName(string raw) { Raw = raw; }
    }

    /// <summary>숫자 및 true/false/null 등 원문 그대로 쓰는 토큰</summary>
    sealed class PdfAtom : PdfObj
    {
        public string Raw;
        public PdfAtom(string raw) { Raw = raw; }
        public bool TryInt(out int v)
        {
            return int.TryParse(Raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v);
        }
    }

    sealed class PdfString : PdfObj
    {
        public byte[] Value;
        public PdfString(byte[] v) { Value = v; }
    }

    sealed class PdfRef : PdfObj
    {
        public int Num, Gen;
        public PdfRef(int n, int g) { Num = n; Gen = g; }
    }

    sealed class PdfArray : PdfObj
    {
        public List<PdfObj> Items = new List<PdfObj>();
    }

    sealed class PdfDict : PdfObj
    {
        public List<KeyValuePair<string, PdfObj>> Entries = new List<KeyValuePair<string, PdfObj>>();

        public PdfObj Get(string key)
        {
            foreach (var e in Entries) if (e.Key == key) return e.Value;
            return null;
        }

        public void Set(string key, PdfObj value)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Key == key) { Entries[i] = new KeyValuePair<string, PdfObj>(key, value); return; }
            }
            Entries.Add(new KeyValuePair<string, PdfObj>(key, value));
        }

        public void Remove(string key)
        {
            Entries.RemoveAll(e => e.Key == key);
        }

        public bool IsType(string type)
        {
            var t = Get("Type") as PdfName;
            return t != null && t.Raw == type;
        }
    }

    sealed class PdfStream : PdfObj
    {
        public PdfDict Dict;
        public byte[] Data; // 원본(인코딩된) 스트림 바이트
        public PdfStream(PdfDict d, byte[] data) { Dict = d; Data = data; }
    }

    sealed class PdfException : Exception
    {
        public PdfException(string msg) : base(msg) { }
    }

    // ------------------------------------------------------------------
    // 렉서/파서
    // ------------------------------------------------------------------
    sealed class PdfParser
    {
        readonly byte[] _b;
        int _p;
        readonly Func<PdfRef, PdfObj> _resolver;

        public PdfParser(byte[] data, int pos, Func<PdfRef, PdfObj> resolver)
        {
            _b = data; _p = pos; _resolver = resolver;
        }

        public int Position { get { return _p; } set { _p = value; } }

        public static bool IsWs(byte c)
        {
            return c == 0 || c == 9 || c == 10 || c == 12 || c == 13 || c == 32;
        }

        public static bool IsDelim(byte c)
        {
            return c == '(' || c == ')' || c == '<' || c == '>' || c == '[' || c == ']' ||
                   c == '{' || c == '}' || c == '/' || c == '%';
        }

        public void SkipWs()
        {
            while (_p < _b.Length)
            {
                byte c = _b[_p];
                if (IsWs(c)) { _p++; continue; }
                if (c == '%')
                {
                    while (_p < _b.Length && _b[_p] != 10 && _b[_p] != 13) _p++;
                    continue;
                }
                break;
            }
        }

        string ReadRegular()
        {
            int s = _p;
            while (_p < _b.Length && !IsWs(_b[_p]) && !IsDelim(_b[_p])) _p++;
            return Encoding.GetEncoding(28591).GetString(_b, s, _p - s);
        }

        public string PeekKeyword()
        {
            int save = _p;
            SkipWs();
            string k = ReadRegular();
            _p = save;
            return k;
        }

        public string ReadKeyword()
        {
            SkipWs();
            return ReadRegular();
        }

        public int ReadInt()
        {
            SkipWs();
            string s = ReadRegular();
            int v;
            if (!int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v))
                throw new PdfException("정수 예상 위치에 '" + s + "' (오프셋 " + _p + ")");
            return v;
        }

        public PdfObj ParseObject()
        {
            SkipWs();
            if (_p >= _b.Length) throw new PdfException("예기치 않은 파일 끝");
            byte c = _b[_p];
            if (c == '/')
            {
                _p++;
                return new PdfName(ReadRegular());
            }
            if (c == '(') return ParseLiteralString();
            if (c == '<')
            {
                if (_p + 1 < _b.Length && _b[_p + 1] == '<') return ParseDict();
                return ParseHexString();
            }
            if (c == '[')
            {
                _p++;
                var arr = new PdfArray();
                while (true)
                {
                    SkipWs();
                    if (_p >= _b.Length) throw new PdfException("배열이 닫히지 않음");
                    if (_b[_p] == ']') { _p++; break; }
                    arr.Items.Add(ParseObject());
                }
                return arr;
            }
            if (c == '{' || c == '}' || c == ')' || c == '>' || c == ']')
            {
                // 일반적이지 않은 구분자 - 그대로 보존
                _p++;
                return new PdfAtom(((char)c).ToString());
            }

            string tok = ReadRegular();
            if (tok.Length == 0) throw new PdfException("알 수 없는 토큰 (오프셋 " + _p + ")");

            // "n g R" 참조 검사
            int n;
            if (IsUInt(tok) && int.TryParse(tok, NumberStyles.None, CultureInfo.InvariantCulture, out n))
            {
                int save = _p;
                SkipWs();
                string t2 = ReadRegular();
                if (IsUInt(t2))
                {
                    SkipWs();
                    string t3 = ReadRegular();
                    if (t3 == "R") return new PdfRef(n, int.Parse(t2, CultureInfo.InvariantCulture));
                }
                _p = save;
            }
            return new PdfAtom(tok);
        }

        static bool IsUInt(string s)
        {
            if (s.Length == 0 || s.Length > 10) return false;
            foreach (char ch in s) if (ch < '0' || ch > '9') return false;
            return true;
        }

        PdfDict ParseDict()
        {
            _p += 2;
            var d = new PdfDict();
            while (true)
            {
                SkipWs();
                if (_p >= _b.Length) throw new PdfException("사전이 닫히지 않음");
                if (_b[_p] == '>' && _p + 1 < _b.Length && _b[_p + 1] == '>') { _p += 2; break; }
                var key = ParseObject() as PdfName;
                if (key == null) throw new PdfException("사전 키가 이름이 아님 (오프셋 " + _p + ")");
                SkipWs();
                if (_b[_p] == '>' && _p + 1 < _b.Length && _b[_p + 1] == '>')
                {
                    d.Set(key.Raw, new PdfAtom("null"));
                    continue;
                }
                d.Set(key.Raw, ParseObject());
            }
            return d;
        }

        PdfString ParseHexString()
        {
            _p++;
            var ms = new MemoryStream();
            int hi = -1;
            while (_p < _b.Length && _b[_p] != '>')
            {
                int v = HexVal(_b[_p]);
                _p++;
                if (v < 0) continue;
                if (hi < 0) hi = v; else { ms.WriteByte((byte)(hi * 16 + v)); hi = -1; }
            }
            _p++;
            if (hi >= 0) ms.WriteByte((byte)(hi * 16));
            return new PdfString(ms.ToArray());
        }

        static int HexVal(byte c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        PdfString ParseLiteralString()
        {
            _p++;
            var ms = new MemoryStream();
            int depth = 1;
            while (_p < _b.Length)
            {
                byte c = _b[_p++];
                if (c == '\\')
                {
                    if (_p >= _b.Length) break;
                    byte e = _b[_p++];
                    switch (e)
                    {
                        case (byte)'n': ms.WriteByte(10); break;
                        case (byte)'r': ms.WriteByte(13); break;
                        case (byte)'t': ms.WriteByte(9); break;
                        case (byte)'b': ms.WriteByte(8); break;
                        case (byte)'f': ms.WriteByte(12); break;
                        case 13:
                            if (_p < _b.Length && _b[_p] == 10) _p++;
                            break;
                        case 10: break;
                        default:
                            if (e >= '0' && e <= '7')
                            {
                                int v = e - '0';
                                for (int i = 0; i < 2 && _p < _b.Length && _b[_p] >= '0' && _b[_p] <= '7'; i++)
                                    v = v * 8 + (_b[_p++] - '0');
                                ms.WriteByte((byte)v);
                            }
                            else ms.WriteByte(e); // \( \) \\ 및 기타
                            break;
                    }
                    continue;
                }
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0) break;
                }
                else if (c == 13)
                {
                    if (_p < _b.Length && _b[_p] == 10) _p++;
                    ms.WriteByte(10);
                    continue;
                }
                ms.WriteByte(c);
            }
            return new PdfString(ms.ToArray());
        }

        /// <summary>"n g obj ... endobj" 파싱. 스트림이면 PdfStream 반환.</summary>
        public PdfObj ParseIndirect(out int num, out int gen)
        {
            num = ReadInt();
            gen = ReadInt();
            string kw = ReadKeyword();
            if (kw != "obj") throw new PdfException("'obj' 키워드 누락 (객체 " + num + ")");
            PdfObj obj = ParseObject();
            if (PeekKeyword() == "stream")
            {
                var dict = obj as PdfDict;
                if (dict == null) throw new PdfException("스트림 사전이 아님 (객체 " + num + ")");
                ReadKeyword(); // stream
                if (_p < _b.Length && _b[_p] == 13) _p++;
                if (_p < _b.Length && _b[_p] == 10) _p++;
                int start = _p;
                int len = -1;
                var lenObj = dict.Get("Length");
                if (lenObj is PdfRef && _resolver != null) lenObj = _resolver((PdfRef)lenObj);
                var lenAtom = lenObj as PdfAtom;
                if (lenAtom != null) lenAtom.TryInt(out len);

                bool ok = false;
                if (len >= 0 && start + len <= _b.Length)
                {
                    _p = start + len;
                    if (PeekKeyword() == "endstream") ok = true;
                }
                if (!ok)
                {
                    int end = IndexOf(_b, "endstream", start);
                    if (end < 0) throw new PdfException("endstream 누락 (객체 " + num + ")");
                    int e = end;
                    if (e > start && _b[e - 1] == 10) e--;
                    if (e > start && _b[e - 1] == 13) e--;
                    len = e - start;
                    _p = end;
                }
                byte[] data = new byte[len];
                Buffer.BlockCopy(_b, start, data, 0, len);
                ReadKeyword(); // endstream
                obj = new PdfStream(dict, data);
            }
            return obj;
        }

        public static int IndexOf(byte[] b, string s, int from)
        {
            byte[] pat = Encoding.ASCII.GetBytes(s);
            for (int i = from; i <= b.Length - pat.Length; i++)
            {
                int j = 0;
                while (j < pat.Length && b[i + j] == pat[j]) j++;
                if (j == pat.Length) return i;
            }
            return -1;
        }

        public static int LastIndexOf(byte[] b, string s)
        {
            byte[] pat = Encoding.ASCII.GetBytes(s);
            for (int i = b.Length - pat.Length; i >= 0; i--)
            {
                int j = 0;
                while (j < pat.Length && b[i + j] == pat[j]) j++;
                if (j == pat.Length) return i;
            }
            return -1;
        }
    }

    // ------------------------------------------------------------------
    // 스트림 디코딩 (Flate + PNG predictor) - 교차참조/객체 스트림 해석용
    // ------------------------------------------------------------------
    static class PdfFilters
    {
        public static byte[] Decode(PdfStream s)
        {
            var f = s.Dict.Get("Filter");
            if (f == null) return s.Data;
            string name = null;
            if (f is PdfName) name = ((PdfName)f).Raw;
            else if (f is PdfArray && ((PdfArray)f).Items.Count == 1 && ((PdfArray)f).Items[0] is PdfName)
                name = ((PdfName)((PdfArray)f).Items[0]).Raw;
            if (name != "FlateDecode") throw new PdfException("지원하지 않는 필터: " + name);

            byte[] raw = Inflate(s.Data);
            var parms = s.Dict.Get("DecodeParms") as PdfDict;
            if (parms == null && s.Dict.Get("DecodeParms") is PdfArray)
            {
                var a = (PdfArray)s.Dict.Get("DecodeParms");
                if (a.Items.Count > 0) parms = a.Items[0] as PdfDict;
            }
            if (parms == null) return raw;
            int predictor = GetInt(parms, "Predictor", 1);
            if (predictor < 10) return raw;
            int columns = GetInt(parms, "Columns", 1);
            int colors = GetInt(parms, "Colors", 1);
            int bpc = GetInt(parms, "BitsPerComponent", 8);
            return PngUnpredict(raw, columns, colors, bpc);
        }

        static int GetInt(PdfDict d, string k, int def)
        {
            var a = d.Get(k) as PdfAtom;
            int v;
            if (a != null && a.TryInt(out v)) return v;
            return def;
        }

        static byte[] Inflate(byte[] data)
        {
            // zlib 헤더(2바이트) 건너뛰기
            int off = 0;
            if (data.Length >= 2 && (data[0] & 0x0F) == 8 && ((data[0] << 8) | data[1]) % 31 == 0) off = 2;
            using (var input = new MemoryStream(data, off, data.Length - off))
            using (var ds = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                try { ds.CopyTo(output); }
                catch (InvalidDataException) { /* adler32 등 끝부분 오류 무시 */ }
                return output.ToArray();
            }
        }

        static byte[] PngUnpredict(byte[] data, int columns, int colors, int bpc)
        {
            int bpp = Math.Max(1, colors * bpc / 8);
            int rowLen = (columns * colors * bpc + 7) / 8;
            var output = new MemoryStream();
            byte[] prev = new byte[rowLen];
            int p = 0;
            while (p + 1 + rowLen <= data.Length)
            {
                int type = data[p++];
                byte[] row = new byte[rowLen];
                Buffer.BlockCopy(data, p, row, 0, rowLen);
                p += rowLen;
                for (int i = 0; i < rowLen; i++)
                {
                    int left = i >= bpp ? row[i - bpp] : 0;
                    int up = prev[i];
                    int ul = i >= bpp ? prev[i - bpp] : 0;
                    switch (type)
                    {
                        case 1: row[i] = (byte)(row[i] + left); break;
                        case 2: row[i] = (byte)(row[i] + up); break;
                        case 3: row[i] = (byte)(row[i] + ((left + up) >> 1)); break;
                        case 4:
                            int pa = Math.Abs(up - ul), pb = Math.Abs(left - ul), pc = Math.Abs(left + up - 2 * ul);
                            int pr = (pa <= pb && pa <= pc) ? left : (pb <= pc ? up : ul);
                            row[i] = (byte)(row[i] + pr);
                            break;
                    }
                }
                output.Write(row, 0, rowLen);
                prev = row;
            }
            return output.ToArray();
        }
    }

    // ------------------------------------------------------------------
    // 문서 로더: 교차참조(테이블/스트림/객체 스트림) 해석
    // ------------------------------------------------------------------
    sealed class PdfDocument
    {
        struct XrefEntry
        {
            public int Type;    // 1 = 오프셋, 2 = 객체 스트림 내부
            public int Offset;  // Type1: 파일 오프셋, Type2: 객체 스트림 번호
            public int Gen;     // Type1: 세대, Type2: 인덱스
        }

        readonly byte[] _data;
        readonly Dictionary<int, XrefEntry> _xref = new Dictionary<int, XrefEntry>();
        readonly Dictionary<int, PdfObj> _cache = new Dictionary<int, PdfObj>();
        readonly Dictionary<int, int> _gens = new Dictionary<int, int>();
        readonly Dictionary<int, Dictionary<int, PdfObj>> _objStmCache = new Dictionary<int, Dictionary<int, PdfObj>>();

        public PdfDict Trailer;
        public string Version = "1.7";

        public PdfDocument(byte[] data)
        {
            _data = data;
            int h = PdfParser.IndexOf(data, "%PDF-", 0);
            if (h < 0 || h > 1024) throw new PdfException("PDF 파일이 아닙니다.");
            int e = h + 5;
            while (e < data.Length && e < h + 12 && (char.IsDigit((char)data[e]) || data[e] == '.')) e++;
            Version = Encoding.ASCII.GetString(data, h + 5, e - h - 5);

            try { ReadXrefChain(); }
            catch (PdfException) { Reconstruct(); }
            if (Trailer == null || Trailer.Get("Root") == null) Reconstruct();
        }

        public IEnumerable<int> ObjectNumbers { get { return _xref.Keys; } }

        public int GetGen(int num)
        {
            int g;
            return _gens.TryGetValue(num, out g) ? g : 0;
        }

        public PdfObj Resolve(PdfObj o)
        {
            var r = o as PdfRef;
            return r != null ? GetObject(r.Num) : o;
        }

        public PdfObj GetObject(int num)
        {
            PdfObj o;
            if (_cache.TryGetValue(num, out o)) return o;
            XrefEntry e;
            if (!_xref.TryGetValue(num, out e)) return null;
            _cache[num] = null; // 재귀 방지
            if (e.Type == 1)
            {
                var p = new PdfParser(_data, e.Offset, r => GetObject(r.Num));
                int n, g;
                o = p.ParseIndirect(out n, out g);
                _gens[num] = g;
            }
            else
            {
                Dictionary<int, PdfObj> objs = LoadObjStm(e.Offset);
                objs.TryGetValue(num, out o);
                _gens[num] = 0;
            }
            _cache[num] = o;
            return o;
        }

        Dictionary<int, PdfObj> LoadObjStm(int stmNum)
        {
            Dictionary<int, PdfObj> map;
            if (_objStmCache.TryGetValue(stmNum, out map)) return map;
            map = new Dictionary<int, PdfObj>();
            _objStmCache[stmNum] = map;
            var s = GetObject(stmNum) as PdfStream;
            if (s == null) throw new PdfException("객체 스트림 " + stmNum + " 없음");
            byte[] dec = PdfFilters.Decode(s);
            int n, first;
            ((PdfAtom)Resolve(s.Dict.Get("N"))).TryInt(out n);
            ((PdfAtom)Resolve(s.Dict.Get("First"))).TryInt(out first);
            var hp = new PdfParser(dec, 0, null);
            var nums = new int[n];
            var offs = new int[n];
            for (int i = 0; i < n; i++) { nums[i] = hp.ReadInt(); offs[i] = hp.ReadInt(); }
            for (int i = 0; i < n; i++)
            {
                var op = new PdfParser(dec, first + offs[i], null);
                map[nums[i]] = op.ParseObject();
            }
            return map;
        }

        void ReadXrefChain()
        {
            int sx = PdfParser.LastIndexOf(_data, "startxref");
            if (sx < 0) throw new PdfException("startxref 없음");
            var p = new PdfParser(_data, sx + 9, null);
            int offset = p.ReadInt();
            var visited = new HashSet<int>();
            while (offset > 0 && offset < _data.Length && visited.Add(offset))
            {
                PdfDict trailer = ReadXrefSection(offset);
                if (Trailer == null) Trailer = trailer;
                var xs = trailer.Get("XRefStm") as PdfAtom;
                int xsOff;
                if (xs != null && xs.TryInt(out xsOff) && visited.Add(xsOff)) ReadXrefSection(xsOff);
                var prev = trailer.Get("Prev") as PdfAtom;
                if (prev == null || !prev.TryInt(out offset)) break;
            }
        }

        PdfDict ReadXrefSection(int offset)
        {
            var p = new PdfParser(_data, offset, null);
            if (p.PeekKeyword() == "xref")
            {
                p.ReadKeyword();
                while (true)
                {
                    string k = p.PeekKeyword();
                    if (k == "trailer") { p.ReadKeyword(); break; }
                    int start = p.ReadInt();
                    int count = p.ReadInt();
                    for (int i = 0; i < count; i++)
                    {
                        int off = p.ReadInt();
                        int gen = p.ReadInt();
                        string t = p.ReadKeyword();
                        int num = start + i;
                        if (_xref.ContainsKey(num)) continue;
                        if (t == "n" && off > 0) _xref[num] = new XrefEntry { Type = 1, Offset = off, Gen = gen };
                        else _xref[num] = new XrefEntry { Type = 0 };
                    }
                }
                var d = p.ParseObject() as PdfDict;
                if (d == null) throw new PdfException("trailer 사전 오류");
                CleanFree();
                return d;
            }
            else
            {
                int n, g;
                var s = p.ParseIndirect(out n, out g) as PdfStream;
                if (s == null || !s.Dict.IsType("XRef")) throw new PdfException("교차참조 스트림 오류");
                byte[] dec = PdfFilters.Decode(s);
                var w = (PdfArray)s.Dict.Get("W");
                int[] ws = new int[3];
                for (int i = 0; i < 3; i++) ((PdfAtom)w.Items[i]).TryInt(out ws[i]);
                int size;
                ((PdfAtom)s.Dict.Get("Size")).TryInt(out size);
                var idx = new List<int>();
                var idxArr = s.Dict.Get("Index") as PdfArray;
                if (idxArr != null) foreach (var it in idxArr.Items) { int v; ((PdfAtom)it).TryInt(out v); idx.Add(v); }
                else { idx.Add(0); idx.Add(size); }
                int rowLen = ws[0] + ws[1] + ws[2];
                int pos = 0;
                for (int k = 0; k + 1 < idx.Count; k += 2)
                {
                    for (int i = 0; i < idx[k + 1]; i++)
                    {
                        if (pos + rowLen > dec.Length) break;
                        int type = ws[0] == 0 ? 1 : ReadBE(dec, pos, ws[0]);
                        int f2 = ReadBE(dec, pos + ws[0], ws[1]);
                        int f3 = ReadBE(dec, pos + ws[0] + ws[1], ws[2]);
                        pos += rowLen;
                        int num = idx[k] + i;
                        if (_xref.ContainsKey(num)) continue;
                        if (type == 1) _xref[num] = new XrefEntry { Type = 1, Offset = f2, Gen = f3 };
                        else if (type == 2) _xref[num] = new XrefEntry { Type = 2, Offset = f2, Gen = f3 };
                        else _xref[num] = new XrefEntry { Type = 0 };
                    }
                }
                CleanFree();
                return s.Dict;
            }
        }

        void CleanFree()
        {
            // Type 0(free)은 키로만 유지해 이전 섹션 값이 덮어쓰지 않게 함 - 열거 시 제외
        }

        public IEnumerable<int> LiveObjectNumbers()
        {
            var list = new List<int>();
            foreach (var kv in _xref) if (kv.Value.Type != 0 && kv.Key > 0) list.Add(kv.Key);
            list.Sort();
            return list;
        }

        static int ReadBE(byte[] b, int pos, int len)
        {
            int v = 0;
            for (int i = 0; i < len; i++) v = (v << 8) | b[pos + i];
            return v;
        }

        /// <summary>교차참조가 손상된 경우 "n g obj" 스캔으로 복구</summary>
        void Reconstruct()
        {
            _xref.Clear();
            _cache.Clear();
            for (int i = 0; i < _data.Length - 4; i++)
            {
                if (_data[i] != 'o' || _data[i + 1] != 'b' || _data[i + 2] != 'j') continue;
                if (i + 3 < _data.Length && !PdfParser.IsWs(_data[i + 3]) && !PdfParser.IsDelim(_data[i + 3])) continue;
                // 뒤로 "num gen " 찾기
                int j = i - 1;
                while (j >= 0 && PdfParser.IsWs(_data[j])) j--;
                int ge = j;
                while (j >= 0 && _data[j] >= '0' && _data[j] <= '9') j--;
                if (j == ge) continue;
                int gs = j + 1;
                while (j >= 0 && PdfParser.IsWs(_data[j])) j--;
                int ne = j;
                while (j >= 0 && _data[j] >= '0' && _data[j] <= '9') j--;
                if (j == ne) continue;
                int ns = j + 1;
                if (ns > 0 && !PdfParser.IsWs(_data[ns - 1]) && !PdfParser.IsDelim(_data[ns - 1])) continue;
                int num = int.Parse(Encoding.ASCII.GetString(_data, ns, ne - ns + 1), CultureInfo.InvariantCulture);
                int gen = int.Parse(Encoding.ASCII.GetString(_data, gs, ge - gs + 1), CultureInfo.InvariantCulture);
                _xref[num] = new XrefEntry { Type = 1, Offset = ns, Gen = gen };
            }
            int t = PdfParser.LastIndexOf(_data, "trailer");
            if (t >= 0)
            {
                var p = new PdfParser(_data, t + 7, null);
                Trailer = p.ParseObject() as PdfDict;
            }
            if (Trailer == null)
            {
                Trailer = new PdfDict();
                foreach (int num in LiveObjectNumbers())
                {
                    var d = GetObject(num) as PdfDict;
                    if (d != null && d.IsType("Catalog")) { Trailer.Set("Root", new PdfRef(num, GetGen(num))); break; }
                }
            }
            if (Trailer == null || Trailer.Get("Root") == null) throw new PdfException("PDF 구조를 해석할 수 없습니다.");
        }

        /// <summary>문서 정보의 제목(/Title)</summary>
        public string GetTitle()
        {
            try
            {
                var info = Resolve(Trailer.Get("Info")) as PdfDict;
                if (info == null) return null;
                var t = Resolve(info.Get("Title")) as PdfString;
                if (t == null) return null;
                return DecodeTextString(t.Value);
            }
            catch { return null; }
        }

        public static string DecodeTextString(byte[] v)
        {
            if (v.Length >= 2 && v[0] == 0xFE && v[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(v, 2, v.Length - 2);
            if (v.Length >= 3 && v[0] == 0xEF && v[1] == 0xBB && v[2] == 0xBF)
                return Encoding.UTF8.GetString(v, 3, v.Length - 3);
            return Encoding.GetEncoding(28591).GetString(v);
        }
    }
}
