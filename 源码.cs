// -*- coding: utf-8 -*-
// ARG 快贴板 (QuickPad) —— WinForms 版
// 目标：把“频繁复制粘贴”做到最少操作。
// 编译（.NET Framework 4，Windows 自带 csc）：
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /codepage:65001 /out:ARG快贴板.exe arg_pasteboard.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ArgPad
{
    // ---------------- 数据模型 ----------------
    public class ClipItem
    {
        public string id { get; set; }
        public string text { get; set; }
        public bool pinned { get; set; }
        public string created { get; set; }
        public string updated { get; set; }
    }

    // ---------------- 主题 ----------------
    public static class Theme
    {
        // 纯黑白极简配色
        public static readonly Color BG = Color.FromArgb(0, 0, 0);
        public static readonly Color PANEL = Color.FromArgb(10, 10, 10);
        public static readonly Color CARD = Color.FromArgb(20, 20, 20);
        public static readonly Color INPUT = Color.FromArgb(30, 30, 30);
        public static readonly Color HOVER = Color.FromArgb(42, 42, 42);
        public static readonly Color SELECT = Color.FromArgb(51, 51, 51);
        public static readonly Color FG = Color.FromArgb(255, 255, 255);
        public static readonly Color DIM = Color.FromArgb(158, 158, 158);
        public static readonly Color ACCENT = Color.FromArgb(255, 255, 255);
        public static readonly Color BORDER = Color.FromArgb(42, 42, 42);
        public static readonly Color ON_ACCENT = Color.FromArgb(0, 0, 0);

        public static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = PANEL;
            b.ForeColor = FG;
            b.Cursor = Cursors.Hand;
            b.Font = new Font("Microsoft YaHei UI", 9f);
            b.Padding = new Padding(6, 2, 6, 2);
            b.AutoSize = true;
            if (onClick != null) b.Click += onClick;
            return b;
        }
    }

    // ---------------- 深色工具栏渲染 ----------------
    public class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin { get { return Theme.CARD; } }
        public override Color ToolStripGradientMiddle { get { return Theme.CARD; } }
        public override Color ToolStripGradientEnd { get { return Theme.CARD; } }
        public override Color ToolStripDropDownBackground { get { return Theme.CARD; } }
        public override Color MenuItemSelected { get { return Theme.HOVER; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.HOVER; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.HOVER; } }
        public override Color MenuItemBorder { get { return Theme.BORDER; } }
        public override Color MenuBorder { get { return Theme.BORDER; } }
        public override Color ImageMarginGradientBegin { get { return Theme.CARD; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.CARD; } }
        public override Color ImageMarginGradientEnd { get { return Theme.CARD; } }
        public override Color SeparatorDark { get { return Theme.BORDER; } }
        public override Color SeparatorLight { get { return Theme.BORDER; } }
    }

    public class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColorTable()) { }
    }

    // ---------------- 密码 / 编码工具箱 ----------------
    public static class Cipher
    {
        static readonly Dictionary<char, string> Morse = new Dictionary<char, string>
        {
            {'A',".-"}, {'B',"-..."}, {'C',"-.-."}, {'D',"-.."}, {'E',"."}, {'F',"..-."},
            {'G',"--."}, {'H',"...."}, {'I',".."}, {'J',".---"}, {'K',"-.-"}, {'L',".-.."},
            {'M',"--"}, {'N',"-."}, {'O',"---"}, {'P',".--."}, {'Q',"--.-"}, {'R',".-."},
            {'S',"..."}, {'T',"-"}, {'U',"..-"}, {'V',"...-"}, {'W',".--"}, {'X',"-..-"},
            {'Y',"-.--"}, {'Z',"--.."},
            {'0',"-----"}, {'1',".----"}, {'2',"..---"}, {'3',"...--"}, {'4',"....-"},
            {'5',"....."}, {'6',"-...."}, {'7',"--..."}, {'8',"---.."}, {'9',"----."},
            {'.',".-.-.-"}, {',',"--..--"}, {'?',"..--.."}, {'\'',".----."}, {'!',"-.-.--"},
            {'/',"-..-."}, {'(',"-.--."}, {')',"-.--.-"}, {'&',".-..."}, {':',"---..."},
            {';',"-.-.-."}, {'=',"-...-"}, {'+',".-.-."}, {'-',"-....-"}, {'_',"..--.-"},
            {'"',".-..-."}, {'$',"...-..-"}, {'@',".--.-."}, {' ',"/"},
        };
        static readonly Dictionary<string, char> MorseRev = Morse.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);

        public static string B64E(string t) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(t)); }
        public static string B64D(string t)
        {
            string s = Regex.Replace(t, @"\s+", "");
            while (s.Length % 4 != 0) s += "=";
            return Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }
        public static string HexE(string t) { var b = Encoding.UTF8.GetBytes(t); var sb = new StringBuilder(); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
        public static string HexD(string t)
        {
            string s = Regex.Replace(t, "[^0-9a-fA-F]", "");
            if (s.Length % 2 != 0) s = "0" + s;
            var bytes = new byte[s.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return Encoding.UTF8.GetString(bytes);
        }
        public static string BinE(string t) { var b = Encoding.UTF8.GetBytes(t); var sb = new StringBuilder(); foreach (var x in b) sb.Append(Convert.ToString(x, 2).PadLeft(8, '0')).Append(' '); return sb.ToString().Trim(); }
        public static string BinD(string t)
        {
            string s = Regex.Replace(t, "[^01]", "");
            while (s.Length % 8 != 0 && s.Length > 0) s = s.Substring(0, s.Length - 1);
            var sb = new StringBuilder();
            for (int i = 0; i + 8 <= s.Length; i += 8) sb.Append((char)Convert.ToByte(s.Substring(i, 8), 2));
            return sb.ToString();
        }
        public static string Caesar(string t, int shift)
        {
            var sb = new StringBuilder();
            foreach (char ch in t)
            {
                if (ch >= 'a' && ch <= 'z') sb.Append((char)((((ch - 'a' + shift) % 26) + 26) % 26 + 'a'));
                else if (ch >= 'A' && ch <= 'Z') sb.Append((char)((((ch - 'A' + shift) % 26) + 26) % 26 + 'A'));
                else sb.Append(ch);
            }
            return sb.ToString();
        }
        public static string Rot13(string t) { return Caesar(t, 13); }
        public static string Atabash(string t)
        {
            var sb = new StringBuilder();
            foreach (char ch in t)
            {
                if (ch >= 'a' && ch <= 'z') sb.Append((char)('z' - (ch - 'a')));
                else if (ch >= 'A' && ch <= 'Z') sb.Append((char)('Z' - (ch - 'A')));
                else sb.Append(ch);
            }
            return sb.ToString();
        }
        public static string Vigenere(string t, string key, bool decrypt = false)
        {
            if (string.IsNullOrEmpty(key)) return t;
            string k = new string(key.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
            if (k.Length == 0) return t;
            var sb = new StringBuilder(); int ki = 0;
            foreach (char ch in t)
            {
                if (char.IsLetter(ch))
                {
                    int shift = k[ki % k.Length] - 'a';
                    int b = char.IsUpper(ch) ? 'A' : 'a';
                    int c = ((ch - b + (decrypt ? -shift : shift)) % 26 + 26) % 26;
                    sb.Append((char)(b + c)); ki++;
                }
                else sb.Append(ch);
            }
            return sb.ToString();
        }
        public static string Reverse(string t) { char[] a = t.ToCharArray(); Array.Reverse(a); return new string(a); }
        public static string MorseE(string t)
        {
            var sb = new StringBuilder();
            foreach (char ch in t.ToUpperInvariant())
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Morse.ContainsKey(ch) ? Morse[ch] : ch.ToString());
            }
            return sb.ToString();
        }
        public static string MorseD(string t)
        {
            var sb = new StringBuilder();
            foreach (string w in Regex.Split(t.Trim(), @"\s+")) sb.Append(MorseRev.ContainsKey(w) ? MorseRev[w].ToString() : w);
            return sb.ToString();
        }
        public static string UrlE(string t) { return Uri.EscapeDataString(t); }
        public static string UrlD(string t) { return Uri.UnescapeDataString(t); }
    }

    // ---------------- 持久化（JSON） ----------------
    public static class Json
    {
        static string Esc(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Items(List<ClipItem> items)
        {
            var sb = new StringBuilder(); sb.Append("[");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var it = items[i];
                sb.Append("{\"id\":\"").Append(Esc(it.id ?? "")).Append("\",\"text\":\"")
                  .Append(Esc(it.text ?? "")).Append("\",\"pinned\":").Append(it.pinned ? "true" : "false")
                  .Append(",\"created\":\"").Append(Esc(it.created ?? "")).Append("\",\"updated\":\"")
                  .Append(Esc(it.updated ?? "")).Append("\"}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        // 极简 JSON 解析：仅支持本程序写出的 items 数组
        public static List<ClipItem> ParseItems(string json)
        {
            var result = new List<ClipItem>();
            int i = 0;
            while (i < json.Length && json[i] != '[') i++;
            if (i >= json.Length) return result;
            i++;
            while (i < json.Length)
            {
                while (i < json.Length && json[i] != '{' && json[i] != ']') i++;
                if (i >= json.Length || json[i] == ']') break;
                var obj = ParseObject(json, ref i);
                if (obj != null) result.Add(obj);
            }
            return result;
        }

        static ClipItem ParseObject(string s, ref int i)
        {
            var it = new ClipItem();
            i++; // skip {
            while (i < s.Length && s[i] != '}')
            {
                while (i < s.Length && s[i] != '"' && s[i] != '}') i++;
                if (i >= s.Length || s[i] == '}') break;
                string key = ParseString(s, ref i);
                while (i < s.Length && s[i] != ':') i++;
                i++; // skip :
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length) break;
                if (s[i] == '"') { string val = ParseString(s, ref i); Set(it, key, val); }
                else if (s[i] == 't') { i += 4; Set(it, key, "true"); }
                else if (s[i] == 'f') { i += 5; Set(it, key, "false"); }
                else { while (i < s.Length && s[i] != ',' && s[i] != '}') i++; }
                while (i < s.Length && s[i] != ',' && s[i] != '}') i++;
                if (i < s.Length && s[i] == ',') i++;
            }
            if (i < s.Length && s[i] == '}') i++;
            return it;
        }

        static string ParseString(string s, ref int i)
        {
            i++; // skip opening quote
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\\')
                {
                    i++;
                    if (i >= s.Length) break;
                    char e = s[i];
                    if (e == 'n') sb.Append('\n');
                    else if (e == 'r') sb.Append('\r');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'u' && i + 4 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                    else sb.Append(e);
                    i++;
                }
                else if (c == '"') { i++; break; }
                else { sb.Append(c); i++; }
            }
            return sb.ToString();
        }

        static void Set(ClipItem it, string key, string val)
        {
            switch (key)
            {
                case "id": it.id = val; break;
                case "text": it.text = val; break;
                case "created": it.created = val; break;
                case "updated": it.updated = val; break;
                case "pinned": it.pinned = val == "true"; break;
            }
        }

        public static string Settings(Dictionary<string, object> d)
        {
            var sb = new StringBuilder(); sb.Append("{");
            bool first = true;
            foreach (var kv in d)
            {
                if (!first) sb.Append(","); first = false;
                sb.Append("\"").Append(Esc(kv.Key)).Append("\":");
                if (kv.Value is bool) sb.Append((bool)kv.Value ? "true" : "false");
                else if (kv.Value is double || kv.Value is float) sb.Append(((double)kv.Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
                else sb.Append("\"").Append(Esc(kv.Value == null ? "" : kv.Value.ToString())).Append("\"");
            }
            sb.Append("}");
            return sb.ToString();
        }

        // 解析扁平的设置对象：{"auto_record":true,"opacity":0.97}
        public static Dictionary<string, string> ParseFlat(string json)
        {
            var d = new Dictionary<string, string>();
            int i = 0;
            while (i < json.Length && json[i] != '{') i++;
            if (i >= json.Length) return d;
            i++;
            while (i < json.Length && json[i] != '}')
            {
                while (i < json.Length && json[i] != '"' && json[i] != '}') i++;
                if (i >= json.Length || json[i] == '}') break;
                string key = ParseString(json, ref i);
                while (i < json.Length && json[i] != ':') i++;
                i++;
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i >= json.Length) break;
                string val;
                if (json[i] == '"') val = ParseString(json, ref i);
                else
                {
                    int start = i;
                    while (i < json.Length && json[i] != ',' && json[i] != '}') i++;
                    val = json.Substring(start, i - start).Trim();
                }
                d[key] = val;
                while (i < json.Length && json[i] != ',' && json[i] != '}') i++;
                if (i < json.Length && json[i] == ',') i++;
            }
            return d;
        }
    }

    // ---------------- 存储 ----------------
    public class ItemStore
    {
        public List<ClipItem> items = new List<ClipItem>();
        readonly string path;

        // 数据统一存放在用户目录，避免在程序所在目录（例如桌面）生成 data 文件夹
        // 最终位置：C:\Users\<用户>\AppData\Local\ARG快贴板\data\items.json
        // 可用环境变量 ARGPAD_DATA_DIR 覆盖（自测/便携用途）
        public static string DefaultRoot()
        {
            string env = Environment.GetEnvironmentVariable("ARGPAD_DATA_DIR");
            if (!string.IsNullOrEmpty(env)) return env;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ARG快贴板");
        }

        public string FilePath { get { return path; } }
        public string BackupPath { get { return path + ".bak"; } }

        public ItemStore(string baseDir) { path = Path.Combine(baseDir, "data", "items.json"); Load(); }

        public void Load()
        {
            // 主文件读取失败（缺失/损坏）时，回退到自动备份
            items = TryLoad(path);
            if (items == null) items = TryLoad(BackupPath);
            if (items == null) items = new List<ClipItem>();
        }

        static List<ClipItem> TryLoad(string p)
        {
            try { if (File.Exists(p)) return Json.ParseItems(File.ReadAllText(p, Encoding.UTF8)); }
            catch { }
            return null;
        }

        // 原子写入：先写临时文件，再用 File.Replace 覆盖，旧内容自动留到 .bak
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, Json.Items(items), Encoding.UTF8);
                if (File.Exists(path))
                {
                    try { File.Replace(tmp, path, BackupPath); }
                    catch { File.Copy(tmp, path, true); File.Delete(tmp); }
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
            catch { }
        }

        public ClipItem Add(string text, bool pinned = false, bool fromClipboard = false)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            if (fromClipboard && items.Count > 0 && items[items.Count - 1].text == text) return null;
            var it = new ClipItem
            {
                id = Guid.NewGuid().ToString("N"),
                text = text,
                pinned = pinned,
                created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            };
            items.Add(it);
            Save();
            return it;
        }

        public void Update(string id, string text, bool pinned)
        {
            var it = items.FirstOrDefault(x => x.id == id);
            if (it == null) return;
            it.text = (text ?? "").Trim();
            it.pinned = pinned;
            it.updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Save();
        }

        public void Delete(string id)
        {
            items.RemoveAll(x => x.id == id);
            Save();
        }

        public void Clear()
        {
            items.Clear();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "[]", Encoding.UTF8);
                if (File.Exists(BackupPath)) File.Delete(BackupPath);
            }
            catch { }
        }

        public List<ClipItem> Ordered(string search)
        {
            var result = new List<ClipItem>(items.Where(x => !x.pinned));
            var pinned = new List<ClipItem>(items.Where(x => x.pinned));
            pinned.Sort((a, b) => string.Compare(b.created, a.created));
            result.Sort((a, b) => string.Compare(b.created, a.created));
            var all = new List<ClipItem>(); all.AddRange(pinned); all.AddRange(result);
            string q = (search ?? "").Trim().ToLowerInvariant();
            if (q.Length > 0) all = all.Where(x => (x.text ?? "").ToLowerInvariant().Contains(q)).ToList();
            return all;
        }
    }

    // ---------------- 编辑对话框 ----------------
    public class EditForm : Form
    {
        TextBox txt; CheckBox chk;
        public string ResultText { get; private set; }
        public bool ResultPinned { get; private set; }

        public EditForm(string title, string initialText, bool initialPinned)
        {
            Text = title;
            BackColor = Theme.PANEL;
            Size = new Size(480, 360);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.PANEL,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12),
                Margin = new Padding(0),
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            Controls.Add(root);

            chk = new CheckBox { Text = "置顶", Checked = initialPinned, ForeColor = Theme.DIM, BackColor = Theme.PANEL, AutoSize = true, Font = new Font("Microsoft YaHei UI", 9f), Margin = new Padding(0, 8, 0, 0) };
            root.Controls.Add(chk, 0, 0);

            txt = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Theme.INPUT,
                ForeColor = Theme.FG,
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 11f),
                Text = initialText,
                Margin = new Padding(0, 0, 0, 8),
            };
            root.Controls.Add(txt, 0, 1);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.PANEL, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0) };
            var ok = Theme.MakeButton("保存", (s, e) => { ResultText = txt.Text.Trim(); if (ResultText.Length == 0) { MessageBox.Show("内容不能为空"); return; } ResultPinned = chk.Checked; DialogResult = DialogResult.OK; Close(); });
            ok.BackColor = Theme.ACCENT; ok.ForeColor = Theme.ON_ACCENT; ok.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold); ok.Margin = new Padding(6, 6, 0, 0);
            var cancel = Theme.MakeButton("取消", (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
            cancel.BackColor = Theme.INPUT; cancel.Margin = new Padding(0, 6, 0, 0);
            bottom.Controls.Add(ok);
            bottom.Controls.Add(cancel);
            root.Controls.Add(bottom, 0, 2);

            AcceptButton = ok; CancelButton = cancel;
        }
    }

    // ---------------- 密码工具箱窗口 ----------------
    public class CipherForm : Form
    {
        TextBox input; TextBox output; TextBox vkey;
        public CipherForm()
        {
            Text = "密码 / 编码工具箱";
            BackColor = Theme.PANEL;
            Size = new Size(700, 720);
            MinimumSize = new Size(560, 520);
            StartPosition = FormStartPosition.CenterParent;

            // 用 TableLayoutPanel 分行，保证所有工具都完整显示、并能随窗口缩放
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.PANEL,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 6,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 输入 标题
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));      // 输入框
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));     // 工具按钮（3 列 × 5 行）
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));      // Vigenère 行
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 输出 标题
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));     // 输出区
            Controls.Add(root);

            Func<string, Label> mkLabel = (txt) => new Label
            {
                Text = txt,
                ForeColor = Theme.DIM,
                BackColor = Theme.PANEL,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9f),
                Margin = new Padding(0, 4, 0, 2),
            };

            root.Controls.Add(mkLabel("输入"), 0, 0);
            input = new TextBox { Multiline = true, Dock = DockStyle.Fill, BackColor = Theme.INPUT, ForeColor = Theme.FG, BorderStyle = BorderStyle.None, Font = new Font("Microsoft YaHei UI", 11f), ScrollBars = ScrollBars.Vertical, Margin = new Padding(0, 0, 0, 6) };
            root.Controls.Add(input, 0, 1);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.PANEL, ColumnCount = 3, RowCount = 5, Margin = new Padding(0) };
            for (int c = 0; c < 3; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            for (int r = 0; r < 5; r++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));
            var ops = new List<Tuple<string, Func<string, string>>>
            {
                Tuple.Create<string, Func<string,string>>("Base64 编码", Cipher.B64E),
                Tuple.Create<string, Func<string,string>>("Base64 解码", Cipher.B64D),
                Tuple.Create<string, Func<string,string>>("Hex 编码", Cipher.HexE),
                Tuple.Create<string, Func<string,string>>("Hex 解码", Cipher.HexD),
                Tuple.Create<string, Func<string,string>>("二进制编码", Cipher.BinE),
                Tuple.Create<string, Func<string,string>>("二进制解码", Cipher.BinD),
                Tuple.Create<string, Func<string,string>>("ROT13", Cipher.Rot13),
                Tuple.Create<string, Func<string,string>>("凯撒+3", t => Cipher.Caesar(t, 3)),
                Tuple.Create<string, Func<string,string>>("凯撒-3", t => Cipher.Caesar(t, -3)),
                Tuple.Create<string, Func<string,string>>("Atbash", Cipher.Atabash),
                Tuple.Create<string, Func<string,string>>("Morse 编码", Cipher.MorseE),
                Tuple.Create<string, Func<string,string>>("Morse 解码", Cipher.MorseD),
                Tuple.Create<string, Func<string,string>>("倒序", Cipher.Reverse),
                Tuple.Create<string, Func<string,string>>("URL 编码", Cipher.UrlE),
                Tuple.Create<string, Func<string,string>>("URL 解码", Cipher.UrlD),
            };
            foreach (var op in ops)
            {
                var fn = op.Item2;
                var b = new Button
                {
                    Text = op.Item1,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Theme.INPUT,
                    ForeColor = Theme.FG,
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    Margin = new Padding(2),
                    Cursor = Cursors.Hand,
                    Font = new Font("Microsoft YaHei UI", 9f),
                };
                b.FlatAppearance.BorderSize = 0;
                b.Click += (s, e) => Apply(fn);
                grid.Controls.Add(b);
            }
            root.Controls.Add(grid, 0, 2);

            var vrow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.PANEL, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
            vrow.Controls.Add(new Label { Text = "Vigenère 密钥", ForeColor = Theme.DIM, BackColor = Theme.PANEL, AutoSize = true, Font = new Font("Microsoft YaHei UI", 9f), Margin = new Padding(0, 8, 8, 0) });
            vkey = new TextBox { BackColor = Theme.INPUT, ForeColor = Theme.FG, BorderStyle = BorderStyle.None, Width = 140, Font = new Font("Microsoft YaHei UI", 10f), Margin = new Padding(0, 6, 8, 0) };
            vrow.Controls.Add(vkey);
            var enc = Theme.MakeButton("加密", (s, e) => Apply(t => Cipher.Vigenere(t, vkey.Text)));
            enc.BackColor = Theme.INPUT; enc.Margin = new Padding(0, 4, 6, 0);
            var dec = Theme.MakeButton("解密", (s, e) => Apply(t => Cipher.Vigenere(t, vkey.Text, true)));
            dec.BackColor = Theme.INPUT; dec.Margin = new Padding(0, 4, 0, 0);
            vrow.Controls.Add(enc); vrow.Controls.Add(dec);
            root.Controls.Add(vrow, 0, 3);

            root.Controls.Add(mkLabel("输出"), 0, 4);
            var ow = new Panel { Dock = DockStyle.Fill, BackColor = Theme.PANEL, Margin = new Padding(0) };
            output = new TextBox { Multiline = true, Dock = DockStyle.Fill, BackColor = Theme.INPUT, ForeColor = Theme.FG, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 11f), ScrollBars = ScrollBars.Vertical, WordWrap = true };
            var copy = Theme.MakeButton("复制", (s, e) => { try { Clipboard.SetText(output.Text); } catch { } });
            copy.BackColor = Theme.ACCENT; copy.ForeColor = Theme.ON_ACCENT;
            copy.AutoSize = false; copy.Width = 64; copy.Dock = DockStyle.Right;
            ow.Controls.Add(output); ow.Controls.Add(copy);
            root.Controls.Add(ow, 0, 5);
        }

        void Apply(Func<string, string> fn)
        {
            string t = input.Text.TrimEnd('\r', '\n');
            if (t.Length == 0) return;
            try { output.Text = fn(t); }
            catch (Exception ex) { output.Text = "[解码失败] " + ex.Message; }
        }
    }

    // ---------------- 抓取快捷键 ----------------
    public class HotKey
    {
        public uint Mods;
        public uint Vk;
        public string Text;

        public const uint MOD_ALT = 0x1;
        public const uint MOD_CONTROL = 0x2;
        public const uint MOD_SHIFT = 0x4;
        public const uint MOD_WIN = 0x8;
        public const uint MOD_NOREPEAT = 0x4000;

        public static uint KeyToVk(string name)
        {
            name = (name ?? "").Trim().ToUpperInvariant();
            if (name.Length >= 2 && name[0] == 'F')
            {
                int n;
                if (int.TryParse(name.Substring(1), out n) && n >= 1 && n <= 24) return (uint)(0x70 + (n - 1));
            }
            if (name.Length == 1)
            {
                char c = name[0];
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return (uint)c;
            }
            return 0;
        }

        public static string VkToName(uint vk)
        {
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);
            if ((vk >= 'A' && vk <= 'Z') || (vk >= '0' && vk <= '9')) return ((char)vk).ToString();
            return "VK" + vk;
        }

        public static string Format(uint mods, uint vk)
        {
            var sb = new StringBuilder();
            if ((mods & MOD_CONTROL) != 0) sb.Append("Ctrl+");
            if ((mods & MOD_ALT) != 0) sb.Append("Alt+");
            if ((mods & MOD_SHIFT) != 0) sb.Append("Shift+");
            if ((mods & MOD_WIN) != 0) sb.Append("Win+");
            sb.Append(VkToName(vk));
            return sb.ToString();
        }

        public static HotKey Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split('+');
            uint mods = 0;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control": mods |= MOD_CONTROL; break;
                    case "alt": mods |= MOD_ALT; break;
                    case "shift": mods |= MOD_SHIFT; break;
                    case "win": mods |= MOD_WIN; break;
                }
            }
            uint vk = KeyToVk(parts[parts.Length - 1]);
            if (vk == 0) return null;
            return new HotKey { Mods = mods, Vk = vk, Text = Format(mods, vk) };
        }
    }

    // 让用户按键设置抓取快捷键
    public class HotkeyForm : Form
    {
        Label lbl;
        public uint Mods;
        public uint Vk;
        public string HotkeyText;
        const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4;

        public HotkeyForm(string current)
        {
            Text = "设置抓取快捷键";
            BackColor = Theme.PANEL;
            Size = new Size(420, 200);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;

            lbl = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.FG,
                BackColor = Theme.PANEL,
                Font = new Font("Microsoft YaHei UI", 11f),
                Text = "请直接按下你想要的快捷键\n\n建议用单个按键，比如 F9（更顺手）\n可加 Ctrl / Alt / Shift\n\n当前：" + current,
            };
            Controls.Add(lbl);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = Theme.PANEL };
            var cancel = Theme.MakeButton("取消", (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
            cancel.BackColor = Theme.INPUT;
            cancel.Location = new Point(336, 8);
            bottom.Controls.Add(cancel);
            Controls.Add(bottom);

            KeyDown += OnKey;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;

            Keys k = e.KeyCode;
            uint mods = 0;
            if ((e.Modifiers & Keys.Control) != 0) mods |= MOD_CONTROL;
            if ((e.Modifiers & Keys.Alt) != 0) mods |= MOD_ALT;
            if ((e.Modifiers & Keys.Shift) != 0) mods |= MOD_SHIFT;

            if (k == Keys.Escape && mods == 0) { DialogResult = DialogResult.Cancel; Close(); return; }
            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin) return;

            uint vk = (uint)k;
            bool ok = (vk >= 0x70 && vk <= 0x87) || (vk >= 'A' && vk <= 'Z') || (vk >= '0' && vk <= '9');
            if (!ok)
            {
                lbl.Text = "这个键不支持，请用 F1~F24、字母或数字\n（可以再加 Ctrl / Alt / Shift）";
                return;
            }

            Mods = mods;
            Vk = vk;
            HotkeyText = HotKey.Format(mods, vk);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // ---------------- 主窗口 ----------------
    public class QuickPadForm : Form
    {
        ItemStore store;
        List<ClipItem> visible = new List<ClipItem>();
        ListBox list;
        TextBox quickInput, search;
        Button hotkeyBtn;
        HotKey captureHotkey;
        string settingsPath;
        Label status;
        TableLayoutPanel body;
        ToolStripButton collapseBtn, topmostBtn;
        ToolStrip toolbar;
        const int EditZoneWidth = 30;
        bool collapsed = false;
        Size prevSize;
        string lastSet = "";
        Timer statusTimer;
        CipherForm cipherForm;
        bool testMode;

        const int EM_SETCUEBANNER = 0x1501;
        const int WM_HOTKEY = 0x0312;
        const int HOTKEY_ID = 0xA17;
        const byte VK_CONTROL = 0x11;
        const byte VK_C = 0x43;
        const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        Timer captureTimer;
        string clipBefore = null;
        int captureTries = 0;

        public QuickPadForm(bool testMode = false)
        {
            this.testMode = testMode;
            captureHotkey = HotKey.Parse("F9");
            // 自测模式使用临时目录，绝不触碰用户的真实数据
            string root = testMode ? Path.Combine(Path.GetTempPath(), "argpad_selftest") : ItemStore.DefaultRoot();
            store = new ItemStore(root);
            settingsPath = Path.Combine(Path.GetDirectoryName(store.FilePath), "settings.json");
            if (!testMode) MigrateLegacyData();

            Text = "ARG 快贴板";
            BackColor = Theme.BG;
            Size = new Size(410, 560);
            MinimumSize = new Size(240, 60);
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Opacity = 0.97;
            MaximizeBox = false;
            ApplyDefaultPosition();

            // 头部（ToolStrip，窄窗口时自动折叠到“>>”）
            toolbar = new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                Renderer = new DarkRenderer(),
                BackColor = Theme.CARD,
                ForeColor = Theme.FG,
                Padding = new Padding(4, 0, 4, 0),
                CanOverflow = true,
                LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
            };
            toolbar.Items.Add(new ToolStripLabel("ARG 快贴板")
            {
                ForeColor = Theme.FG,
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                Padding = new Padding(2, 0, 10, 0),
            });
            Func<string, EventHandler, ToolStripButton> mk = (txt, h) =>
            {
                var b = new ToolStripButton(txt) { ForeColor = Theme.FG, DisplayStyle = ToolStripItemDisplayStyle.Text };
                b.Click += h;
                return b;
            };
            toolbar.Items.Add(mk("工具箱", (s, e) => ShowCipher()));
            toolbar.Items.Add(mk("◐", (s, e) => ChangeOpacity(-0.08)));
            toolbar.Items.Add(mk("◑", (s, e) => ChangeOpacity(0.08)));
            topmostBtn = mk("总在最前", (s, e) => ToggleTopmost());
            toolbar.Items.Add(topmostBtn);
            collapseBtn = mk("▁", (s, e) => ToggleCollapse());
            toolbar.Items.Add(collapseBtn);
            Func<string, EventHandler, ToolStripMenuItem> mki = (txt, h) =>
            {
                var mi = new ToolStripMenuItem(txt) { ForeColor = Theme.FG };
                mi.Click += h;
                return mi;
            };
            var menuBtn = new ToolStripDropDownButton("☰") { ForeColor = Theme.FG, DisplayStyle = ToolStripItemDisplayStyle.Text };
            menuBtn.DropDownItems.Add(mki("设置抓取快捷键…", (s, e) => ChooseHotkey()));
            menuBtn.DropDownItems.Add(mki("记录当前剪贴板", (s, e) => CaptureClipboardNow()));
            menuBtn.DropDownItems.Add(new ToolStripSeparator());
            menuBtn.DropDownItems.Add(mki("导出", (s, e) => Export()));
            menuBtn.DropDownItems.Add(mki("打开数据目录", (s, e) => OpenDataDir()));
            menuBtn.DropDownItems.Add(mki("窗口归位", (s, e) => ResetPosition()));
            menuBtn.DropDownItems.Add(new ToolStripSeparator());
            menuBtn.DropDownItems.Add(mki("清空", (s, e) => ClearAll()));
            menuBtn.DropDownItems.Add(mki("退出", (s, e) => Quit()));
            toolbar.Items.Add(menuBtn);

            // 主体：固定行顺序 —— 快速新增 / 搜索+抓取键 / 列表 / 状态栏
            body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.BG,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(8, 8, 8, 0),
                Margin = new Padding(0),
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));   // 快速新增
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));   // 搜索 + 抓取键
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 列表
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));   // 状态栏

            var inrow = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.BG, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            inrow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            inrow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46f));
            quickInput = new TextBox { Dock = DockStyle.Fill, BackColor = Theme.INPUT, ForeColor = Theme.FG, BorderStyle = BorderStyle.None, Font = new Font("Microsoft YaHei UI", 10f), Margin = new Padding(0, 8, 6, 8) };
            quickInput.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; QuickAdd(); } };
            quickInput.HandleCreated += (s, e) => SendMessage(quickInput.Handle, EM_SETCUEBANNER, (IntPtr)1, "输入后回车，新增一条");
            var addBtn = new Button { Text = "＋", FlatStyle = FlatStyle.Flat, BackColor = Theme.ACCENT, ForeColor = Theme.ON_ACCENT, Dock = DockStyle.Fill, AutoSize = false, Margin = new Padding(0, 8, 0, 8), Cursor = Cursors.Hand, Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold) };
            addBtn.FlatAppearance.BorderSize = 0;
            addBtn.Click += (s, e) => QuickAdd();
            inrow.Controls.Add(quickInput, 0, 0);
            inrow.Controls.Add(addBtn, 1, 0);
            body.Controls.Add(inrow, 0, 0);

            var bar = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.BG, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128f));
            search = new TextBox { Dock = DockStyle.Fill, BackColor = Theme.INPUT, ForeColor = Theme.FG, BorderStyle = BorderStyle.None, Font = new Font("Microsoft YaHei UI", 9f), Margin = new Padding(0, 7, 6, 7) };
            search.TextChanged += (s, e) => RefreshList();
            search.HandleCreated += (s, e) => SendMessage(search.Handle, EM_SETCUEBANNER, (IntPtr)1, "搜索…");
            hotkeyBtn = new Button { FlatStyle = FlatStyle.Flat, Dock = DockStyle.Fill, AutoSize = false, Margin = new Padding(0, 7, 0, 7), Cursor = Cursors.Hand, Font = new Font("Microsoft YaHei UI", 9f), BackColor = Theme.INPUT, ForeColor = Theme.FG, Text = "抓取键：F9" };
            hotkeyBtn.FlatAppearance.BorderSize = 0;
            hotkeyBtn.Click += (s, e) => ChooseHotkey();
            bar.Controls.Add(search, 0, 0);
            bar.Controls.Add(hotkeyBtn, 1, 0);
            body.Controls.Add(bar, 0, 1);

            list = new ListBox { Dock = DockStyle.Fill, BackColor = Theme.CARD, ForeColor = Theme.FG, BorderStyle = BorderStyle.None, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 34, Font = new Font("Microsoft YaHei UI", 10f), Margin = new Padding(0) };
            list.DrawItem += DrawListItem;
            list.MouseClick += (s, e) =>
            {
                int idx = list.IndexFromPoint(e.Location);
                if (idx < 0 || idx >= visible.Count) return;
                list.SelectedIndex = idx;
                if (e.X <= EditZoneWidth) OpenEdit(visible[idx]); // 左侧编辑按钮
            };
            list.DoubleClick += (s, e) =>
            {
                var p = list.PointToClient(Cursor.Position);
                int idx = list.IndexFromPoint(p);
                if (idx < 0 || idx >= visible.Count) return;
                if (p.X > EditZoneWidth) CopyItem(visible[idx]); else OpenEdit(visible[idx]);
            };
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) DeleteSelected(); else if (e.KeyCode == Keys.Enter) CopySelected(); };
            list.ContextMenuStrip = BuildMenu();

            status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.DIM, BackColor = Theme.BG, Font = new Font("Microsoft YaHei UI", 8f), Padding = new Padding(2, 0, 0, 0), Margin = new Padding(0) };
            body.Controls.Add(list, 0, 2);
            body.Controls.Add(status, 0, 3);

            // 用表格布局把「工具栏 / 主体」分成上下两行，避免 Dock 顺序导致互相遮挡
            var rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.BG,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            rootLayout.Controls.Add(toolbar, 0, 0);
            rootLayout.Controls.Add(body, 0, 1);
            Controls.Add(rootLayout);

            FormClosing += (s, e) =>
            {
                if (testMode) return;
                store.Save();
                SaveSettings();
                try { UnregisterHotKey(Handle, HOTKEY_ID); } catch { }
            };

            if (!testMode) LoadSettings();
            if (!testMode) RegisterCaptureHotkey();
            ApplyHotkeyButton();
            topmostBtn.Text = TopMost ? "总在最前" : "取消置顶";
            RefreshList();
            if (!testMode)
            {
                statusTimer = new Timer { Interval = 2200 };
                statusTimer.Tick += (s, e) => { statusTimer.Stop(); UpdateStatus(); };
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterCaptureHotkey();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID) CaptureSelection();
            base.WndProc(ref m);
        }

        ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("复制", null, (s, e) => CopySelected());
            menu.Items.Add("编辑", null, (s, e) => { var it = Selected(); if (it != null) OpenEdit(it); });
            menu.Items.Add("置顶/取消置顶", null, (s, e) => { var it = Selected(); if (it != null) { store.Update(it.id, it.text, !it.pinned); RefreshList(); } });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("删除", null, (s, e) => DeleteSelected());
            menu.Opening += (s, e) => { };
            return menu;
        }

        ClipItem Selected()
        {
            int idx = list.SelectedIndex;
            if (idx >= 0 && idx < visible.Count) return visible[idx];
            return null;
        }

        void DrawListItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(sel ? Theme.SELECT : Theme.CARD))
                e.Graphics.FillRectangle(bg, e.Bounds);
            // 左侧“编辑”按钮
            var btnRect = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 5, EditZoneWidth - 4, e.Bounds.Height - 10);
            using (var bbg = new SolidBrush(sel ? Color.FromArgb(70, 70, 70) : Theme.INPUT))
                e.Graphics.FillRectangle(bbg, btnRect);
            TextRenderer.DrawText(e.Graphics, "✎", new Font(list.Font.FontFamily, 9f), btnRect, Theme.FG,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            // 文本
            string txt = list.Items[e.Index] as string ?? "";
            TextRenderer.DrawText(e.Graphics, txt, list.Font,
                new Rectangle(e.Bounds.X + EditZoneWidth + 6, e.Bounds.Y, e.Bounds.Width - EditZoneWidth - 12, e.Bounds.Height),
                Theme.FG,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        static string Preview(string text, int n = 62)
        {
            string t = (text ?? "").Replace("\r", "").Replace("\n", " ").Trim();
            if (t.Length == 0) return "（空）";
            return t.Length <= n ? t : t.Substring(0, n) + "…";
        }

        void RefreshList()
        {
            visible = store.Ordered(search.Text);
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var it in visible) list.Items.Add((it.pinned ? "📌 " : "") + Preview(it.text));
            list.EndUpdate();
            UpdateStatus();
        }

        void UpdateStatus()
        {
            string hk = captureHotkey != null ? captureHotkey.Text : "F9";
            status.Text = store.items.Count + " 条 · 选中文字后按 " + hk + " 记录 · 双击条目复制";
        }

        void Flash(string msg)
        {
            status.Text = msg;
            if (!testMode && statusTimer != null) { statusTimer.Stop(); statusTimer.Start(); }
        }

        void QuickAdd()
        {
            string t = quickInput.Text.Trim();
            if (t.Length == 0) { quickInput.Focus(); return; }
            store.Add(t);
            quickInput.Text = "";
            RefreshList();
        }

        void CopyItem(ClipItem it)
        {
            try { Clipboard.SetText(it.text); } catch { }
            lastSet = it.text;
            Flash("已复制：" + Preview(it.text, 40));
        }

        void CopySelected() { var it = Selected(); if (it != null) CopyItem(it); }
        void DeleteSelected() { var it = Selected(); if (it != null) { store.Delete(it.id); RefreshList(); } }

        void OpenEdit(ClipItem it)
        {
            using (var ef = new EditForm("编辑条目", it.text, it.pinned))
            {
                if (ef.ShowDialog(this) == DialogResult.OK)
                {
                    store.Update(it.id, ef.ResultText, ef.ResultPinned);
                    RefreshList();
                }
            }
        }

        // 按下抓取快捷键：模拟 Ctrl+C 复制当前选中的文字，然后记录下来
        void CaptureSelection()
        {
            try { clipBefore = Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch { clipBefore = null; }

            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            captureTries = 0;
            if (captureTimer == null)
            {
                captureTimer = new Timer { Interval = 80 };
                captureTimer.Tick += (s, e) => CaptureTick();
            }
            captureTimer.Start();
        }

        void CaptureTick()
        {
            captureTries++;
            try
            {
                if (Clipboard.ContainsText())
                {
                    string clip = Clipboard.GetText();
                    if (clip != null && clip != clipBefore && clip.Trim().Length > 0)
                    {
                        captureTimer.Stop();
                        store.Add(clip, fromClipboard: true);
                        RefreshList();
                        Flash("已记录：" + Preview(clip, 30));
                        return;
                    }
                }
            }
            catch { }
            if (captureTries >= 10)
            {
                captureTimer.Stop();
                Flash("没有检测到选中的文字");
            }
        }

        void CaptureClipboardNow()
        {
            try
            {
                string clip = Clipboard.GetText();
                if (clip != null && clip != lastSet && clip.Trim().Length > 0)
                {
                    store.Add(clip, fromClipboard: true);
                    RefreshList();
                    Flash("已快存剪贴板内容");
                }
                else Flash("剪贴板为空或未变化");
            }
            catch { }
        }

        void ToggleCollapse()
        {
            if (collapsed)
            {
                body.Visible = true;
                collapseBtn.Text = "▁";
                Size = prevSize;
                collapsed = false;
            }
            else
            {
                prevSize = Size;
                body.Visible = false;
                collapseBtn.Text = "▔";
                Height = Math.Max(28 + SystemInformation.CaptionHeight + 2, 60);
                collapsed = true;
            }
        }

        void ToggleTopmost()
        {
            TopMost = !TopMost;
            topmostBtn.Text = TopMost ? "总在最前" : "取消置顶";
            if (!testMode) SaveSettings();
        }

        void ShowCipher()
        {
            if (cipherForm == null || cipherForm.IsDisposed) cipherForm = new CipherForm();
            cipherForm.TopMost = TopMost;
            cipherForm.Show();
            cipherForm.Activate();
        }

        void ClearAll()
        {
            if (store.items.Count == 0) return;
            if (MessageBox.Show(this, "确定清空所有记录吗？此操作不可撤销。", "清空", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                store.Clear();
                RefreshList();
            }
        }

        void Export()
        {
            if (store.items.Count == 0) { MessageBox.Show(this, "还没有内容可导出"); return; }
            using (var dlg = new SaveFileDialog { Title = "导出", Filter = "文本文件|*.txt|Markdown|*.md|所有文件|*.*", FileName = "arg_记录.txt" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var sb = new StringBuilder();
                foreach (var it in store.Ordered("")) sb.Append(it.text.TrimEnd('\r', '\n')).Append("\r\n\r\n");
                try { File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8); MessageBox.Show(this, "已导出到：\n" + dlg.FileName, "导出成功"); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败"); }
            }
        }

        void Quit()
        {
            store.Save();
            Application.Exit();
        }

        // 旧版本把 data 放在程序目录下；首次运行时把数据迁到用户目录并清掉旧目录
        void MigrateLegacyData()
        {
            try
            {
                string legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "items.json");
                if (File.Exists(store.FilePath) || !File.Exists(legacy)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath));
                File.Copy(legacy, store.FilePath, false);
                store.Load();
                // 数据已复制到新位置，删除旧的 data 文件夹，避免继续留在桌面/程序目录
                try
                {
                    string legacyDir = Path.GetDirectoryName(legacy);
                    File.Delete(legacy);
                    if (Directory.Exists(legacyDir) && Directory.GetFileSystemEntries(legacyDir).Length == 0)
                        Directory.Delete(legacyDir);
                }
                catch { }
            }
            catch { }
        }

        void OpenDataDir()
        {
            try
            {
                string dir = Path.GetDirectoryName(store.FilePath);
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch { }
        }

        // ---------------- 设置持久化 ----------------
        void LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsPath)) return;
                var d = Json.ParseFlat(File.ReadAllText(settingsPath, Encoding.UTF8));
                string v;
                if (d.TryGetValue("capture_hotkey", out v)) captureHotkey = HotKey.Parse(v);
                if (d.TryGetValue("topmost", out v)) TopMost = (v == "true");
                double o;
                if (d.TryGetValue("opacity", out v) &&
                    double.TryParse(v, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out o))
                    Opacity = Math.Max(0.4, Math.Min(1.0, o));
                int x, y;
                if (d.TryGetValue("win_x", out v) && int.TryParse(v, out x) &&
                    d.TryGetValue("win_y", out v) && int.TryParse(v, out y))
                    Location = ClampToArea(new Point(x, y));
            }
            catch { }
        }

        void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                var d = new Dictionary<string, object>();
                if (captureHotkey != null) d["capture_hotkey"] = captureHotkey.Text;
                d["topmost"] = TopMost;
                d["opacity"] = (double)Opacity;
                d["win_x"] = Location.X;
                d["win_y"] = Location.Y;
                File.WriteAllText(settingsPath, Json.Settings(d), Encoding.UTF8);
            }
            catch { }
        }

        void ApplyHotkeyButton()
        {
            if (hotkeyBtn == null) return;
            string t = captureHotkey != null ? captureHotkey.Text : "F9";
            hotkeyBtn.Text = "抓取键：" + t;
            UpdateStatus();
        }

        void ChooseHotkey()
        {
            string cur = captureHotkey != null ? captureHotkey.Text : "F9";
            using (var hf = new HotkeyForm(cur))
            {
                if (hf.ShowDialog(this) != DialogResult.OK) return;
                captureHotkey = new HotKey { Mods = hf.Mods, Vk = hf.Vk, Text = hf.HotkeyText };
                RegisterCaptureHotkey();
                ApplyHotkeyButton();
                if (!testMode) SaveSettings();
            }
        }

        void RegisterCaptureHotkey()
        {
            if (testMode) return;
            try { UnregisterHotKey(Handle, HOTKEY_ID); } catch { }
            if (captureHotkey == null) return;
            bool ok = RegisterHotKey(Handle, HOTKEY_ID, captureHotkey.Mods | HotKey.MOD_NOREPEAT, captureHotkey.Vk);
            if (!ok) Flash("快捷键 " + captureHotkey.Text + " 注册失败，可能被别的程序占用了");
        }

        // ---------------- 窗口位置 ----------------
        Rectangle WorkArea()
        {
            return Screen.PrimaryScreen.WorkingArea;
        }

        Point DefaultPosition()
        {
            var wa = WorkArea();
            return new Point(wa.Right - Width - 24, wa.Bottom - Height - 24);
        }

        Point ClampToArea(Point p)
        {
            var wa = WorkArea();
            int x = Math.Max(wa.Left, Math.Min(p.X, wa.Right - Width));
            int y = Math.Max(wa.Top, Math.Min(p.Y, wa.Bottom - Height));
            return new Point(x, y);
        }

        void ApplyDefaultPosition()
        {
            Location = ClampToArea(DefaultPosition());
        }

        void ResetPosition()
        {
            ApplyDefaultPosition();
            if (!testMode) SaveSettings();
            Flash("窗口已回到右下角");
        }

        void ChangeOpacity(double delta)
        {
            Opacity = Math.Max(0.4, Math.Min(1.0, Opacity + delta));
            if (!testMode) SaveSettings();
        }
    }

    // ---------------- 自测 ----------------
    public static class SelfTest
    {
        public static int Run()
        {
            try
            {
                if (Cipher.B64D(Cipher.B64E("你好ARG")) != "你好ARG") return 1;
                if (Cipher.HexD(Cipher.HexE("abc123")) != "abc123") return 2;
                if (Cipher.Rot13(Cipher.Rot13("Hello World")) != "Hello World") return 3;
                if (Cipher.Caesar(Cipher.Caesar("Zebra", 3), -3) != "Zebra") return 4;
                if (Cipher.Atabash(Cipher.Atabash("AbC")) != "AbC") return 5;
                if (Cipher.Vigenere(Cipher.Vigenere("HELLOWORLD", "KEY"), "KEY", true) != "HELLOWORLD") return 6;
                if (Cipher.MorseD(Cipher.MorseE("SOS 123")) != "SOS 123") return 7;
                if (Cipher.UrlD(Cipher.UrlE("a b&c=1")) != "a b&c=1") return 8;
                if (Cipher.BinD(Cipher.BinE("AB")) != "AB") return 9;
                if (Cipher.Reverse("abc") != "cba") return 10;

                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_selftest");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                var store = new ItemStore(dir);
                store.items.Clear();
                store.Add("第一段线索");
                store.Add("第二段内容\n多行");
                var i3 = store.Add("重要结论");
                store.Update(i3.id, "重要结论（改）", true);
                store.Add("第三段");
                if (store.Ordered("")[0].text != "重要结论（改）") return 11;
                if (store.Ordered("线索").Count != 1) return 12;
                var reloaded = new ItemStore(dir);
                if (reloaded.items.Count != 4) return 13;
                if (reloaded.items[0].text != "第一段线索") return 14;

                var f = new QuickPadForm(testMode: true);
                f.Dispose();

                var sd = new Dictionary<string, object>();
                sd["auto_record"] = false;
                sd["topmost"] = true;
                sd["opacity"] = 0.9;
                var flat = Json.ParseFlat(Json.Settings(sd));
                if (!flat.ContainsKey("auto_record") || flat["auto_record"] != "false") return 15;
                if (flat["topmost"] != "true") return 16;
                if (flat["opacity"] != "0.9") return 17;

                Directory.Delete(dir, true);
                return 0;
            }
            catch (Exception)
            {
                return 99;
            }
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--selftest")
            {
                int code = SelfTest.Run();
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.log"), "code=" + code, Encoding.UTF8); } catch { }
                Environment.Exit(code);
            }
            // 单实例：避免两个实例同时读写数据文件导致内容被覆盖
            bool createdNew;
            using (var mutex = new System.Threading.Mutex(true, "ArgPad_SingleInstance_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("ARG 快贴板已经在运行了，请用任务栏里的那个窗口。", "ARG 快贴板",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.Run(new QuickPadForm());
            }
        }
    }
}
