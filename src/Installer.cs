using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Spider-F Undertone Thai Mod Installer")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0 - 2026-10-20")]
[assembly: AssemblyDescription("Thai Localization Mod for Undertone (底色 Demo / ม่านลวงบ่วงแค้น)")]

class Game { public string Root; public string Library; public string AppId; public override string ToString() { return Root; } }

static class Engine
{
    public static readonly string[] SupportedAppIds = new[] { "5358830", "5129220" };
    public static readonly string[] FallbackGameDirs = new[] { @"C:\SteamLibrary\steamapps\common\底色", @"D:\SteamLibrary\steamapps\common\底色", @"E:\SteamLibrary\steamapps\common\底色" };
    public const string Donate = "https://sites.google.com/view/spider-translate2/Donate";
    public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };

    public static Stream Resource(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(name); }

    public static bool Same(string a, string b) { return a != null && b != null && String.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

    public static List<string> Tokens(string text)
    {
        var values = new List<string>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/') { while (i < text.Length && text[i] != '\n') i++; continue; }
            if (text[i] != '"') continue;
            var s = new StringBuilder();
            for (i++; i < text.Length && text[i] != '"'; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length && (text[i + 1] == '\\' || text[i + 1] == '"')) i++;
                s.Append(text[i]);
            }
            values.Add(s.ToString());
        }
        return values;
    }

    public static string Value(List<string> t, string key)
    {
        for (int i = 0; i + 1 < t.Count; i++) if (t[i] == key) return t[i + 1];
        return null;
    }

    public static string SafePath(string root, string rel)
    {
        if (String.IsNullOrEmpty(rel) || Path.IsPathRooted(rel) || rel.Contains(":") || rel.Split('/', '\\').Any(x => x == ".."))
            throw new IOException("Unsafe package path: " + rel);
        string basePath = Path.GetFullPath(root).TrimEnd('\\') + "\\";
        string result = Path.GetFullPath(Path.Combine(basePath, rel.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Path escapes game folder: " + rel);
        return result;
    }

    public static void NoLinks(string root, string target)
    {
        string current = target;
        while (!String.IsNullOrEmpty(current))
        {
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("พบลิงก์ไฟล์หรือโฟลเดอร์ในตำแหน่งติดตั้ง: " + current);
            if (String.Equals(current.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current);
        }
    }

    public static List<Game> Discover()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            using (var b = RegistryKey.OpenBaseKey(hive, view))
            using (var k = b.OpenSubKey(@"Software\Valve\Steam"))
            {
                if (k != null)
                {
                    foreach (string name in new[] { "SteamPath", "InstallPath" })
                    {
                        var v = k.GetValue(name) as string;
                        if (!String.IsNullOrEmpty(v)) roots.Add(v.Replace('/', '\\'));
                    }
                }
            }
        }
        var libraries = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            string f = Path.Combine(root, @"steamapps\libraryfolders.vdf");
            if (!File.Exists(f)) continue;
            var t = Tokens(File.ReadAllText(f));
            for (int i = 0; i + 1 < t.Count; i++)
            {
                if (t[i] == "path" && Path.IsPathRooted(t[i + 1])) libraries.Add(t[i + 1]);
                int n;
                if (Int32.TryParse(t[i], out n) && Path.IsPathRooted(t[i + 1])) libraries.Add(t[i + 1]);
            }
        }

        var found = new List<Game>();
        foreach (string lib in libraries)
        {
            foreach (string appId in SupportedAppIds)
            {
                string file = Path.Combine(lib, @"steamapps\appmanifest_" + appId + ".acf");
                if (!File.Exists(file)) continue;
                var t = Tokens(File.ReadAllText(file));
                if (Value(t, "appid") != appId) continue;
                string dir = Value(t, "installdir");
                if (String.IsNullOrEmpty(dir)) continue;
                string root = SafePath(Path.Combine(lib, @"steamapps\common"), dir);
                string exePath = Path.Combine(root, "launch.exe");
                string asarPath = Path.Combine(root, @"resources\app.asar");
                if (File.Exists(exePath) && File.Exists(asarPath) && !found.Any(x => Same(x.Root, root)))
                {
                    found.Add(new Game { Root = root, Library = lib, AppId = appId });
                }
            }
        }
        foreach (string dir in FallbackGameDirs)
        {
            string exePath = Path.Combine(dir, "launch.exe");
            string asarPath = Path.Combine(dir, @"resources\app.asar");
            if (File.Exists(exePath) && File.Exists(asarPath) && !found.Any(x => Same(x.Root, dir)))
                found.Add(new Game { Root = dir, Library = Path.GetDirectoryName(Path.GetDirectoryName(dir)), AppId = "local" });
        }
        return found;
    }

    public static void CheckGameClosed(string root)
    {
        string gameExe = Path.GetFullPath(Path.Combine(root, "launch.exe"));
        foreach (var process in Process.GetProcessesByName("launch"))
        {
            bool isSelectedGame = false;
            using (process)
            {
                try { isSelectedGame = !process.HasExited && process.MainModule != null && Same(Path.GetFullPath(process.MainModule.FileName), gameExe); }
                catch { }
            }
            if (isSelectedGame) throw new IOException("ตัวเกมกำลังทำงานอยู่ กรุณาปิดเกมก่อนติดตั้งม็อดภาษาไทย");
        }
        foreach (string p in new[] { "launch.exe", @"resources\app.asar" })
        {
            string full = Path.Combine(root, p);
            if (File.Exists(full))
            {
                try { using (var s = new FileStream(full, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
                catch (IOException e) { throw new IOException("ไฟล์เกมกำลังถูกใช้งาน กรุณาปิดเกมแล้วลองใหม่: " + p, e); }
                catch (UnauthorizedAccessException e) { throw new IOException("ไม่มีสิทธิ์เข้าถึงไฟล์เกม: " + p, e); }
            }
        }
    }

    private static IDictionary<string, object> EnsureDirNode(IDictionary<string, object> root, string[] parts, int count)
    {
        var cur = root;
        for (int i = 0; i < count; i++)
        {
            string p = parts[i];
            if (!cur.ContainsKey("files") || !(cur["files"] is IDictionary<string, object>))
                cur["files"] = new Dictionary<string, object>(StringComparer.Ordinal);
            var files = (IDictionary<string, object>)cur["files"];
            if (!files.ContainsKey(p) || !(files[p] is IDictionary<string, object>))
            {
                var nextNode = new Dictionary<string, object>(StringComparer.Ordinal);
                nextNode["files"] = new Dictionary<string, object>(StringComparer.Ordinal);
                files[p] = nextNode;
            }
            cur = (IDictionary<string, object>)files[p];
        }
        if (!cur.ContainsKey("files") || !(cur["files"] is IDictionary<string, object>))
            cur["files"] = new Dictionary<string, object>(StringComparer.Ordinal);
        return (IDictionary<string, object>)cur["files"];
    }

    private static void CollectEntries(
        IDictionary<string, object> node,
        string prefix,
        Dictionary<string, ZipArchiveEntry> patchMap,
        Dictionary<string, byte[]> dynamicBytes,
        List<Tuple<IDictionary<string, object>, ZipArchiveEntry, byte[], long, long>> plan)
    {
        if (node.ContainsKey("files") && node["files"] is IDictionary<string, object>)
        {
            var files = (IDictionary<string, object>)node["files"];
            foreach (var kv in files.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                string childPath = String.IsNullOrEmpty(prefix) ? kv.Key : (prefix + "/" + kv.Key);
                var childNode = kv.Value as IDictionary<string, object>;
                if (childNode != null)
                    CollectEntries(childNode, childPath, patchMap, dynamicBytes, plan);
            }
        }
        else
        {
            if (node.ContainsKey("unpacked") && Convert.ToBoolean(node["unpacked"]))
                return;
            ZipArchiveEntry zEntry;
            byte[] dBuf;
            if (dynamicBytes.TryGetValue(prefix, out dBuf))
            {
                plan.Add(Tuple.Create(node, (ZipArchiveEntry)null, dBuf, 0L, (long)dBuf.Length));
            }
            else if (patchMap.TryGetValue(prefix, out zEntry))
            {
                plan.Add(Tuple.Create(node, zEntry, (byte[])null, 0L, zEntry.Length));
            }
            else
            {
                long origOffset = Convert.ToInt64(node["offset"]);
                long origSize = Convert.ToInt64(node["size"]);
                plan.Add(Tuple.Create(node, (ZipArchiveEntry)null, (byte[])null, origOffset, origSize));
            }
        }
    }

    public static void Install(Game selected, Action<int, string> report)
    {
        Game game = Discover().FirstOrDefault(x => Same(x.Root, selected.Root)) ?? selected;
        CheckGameClosed(game.Root);

        string resDir = SafePath(game.Root, "resources");
        NoLinks(game.Root, resDir);
        string asarPath = SafePath(game.Root, @"resources\app.asar");
        string backupAsarPath = SafePath(game.Root, @"resources\app.asar.orig_backup");
        string tmpAsarPath = SafePath(game.Root, @"resources\app.asar.mod_tmp");

        report(5, "กำลังตรวจสอบไฟล์ต้นฉบับจาก Steam...");

        // Determine source ASAR to read original unmodified streams from
        string sourceAsarPath = File.Exists(backupAsarPath) ? backupAsarPath : asarPath;

        using (var zipStream = Resource("payload.zip"))
        {
            if (zipStream == null) throw new IOException("ไม่พบข้อมูลแพ็กเกจภายในตัวติดตั้ง (payload.zip)");
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                var asarPatches = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
                var metaEntries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
                var dynamicBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                var externalSubtitles = new List<Tuple<string, ZipArchiveEntry>>();

                foreach (var entry in zip.Entries)
                {
                    if (String.IsNullOrEmpty(entry.Name)) continue;
                    string norm = entry.FullName.Replace('\\', '/');
                    if (norm.StartsWith("asar_patch/", StringComparison.Ordinal))
                    {
                        string rel = norm.Substring("asar_patch/".Length);
                        asarPatches[rel] = entry;
                    }
                    else if (norm.StartsWith("patch_meta/", StringComparison.Ordinal))
                    {
                        metaEntries[norm.Substring("patch_meta/".Length)] = entry;
                    }
                    else if (norm.StartsWith("subtitles/", StringComparison.Ordinal))
                    {
                        externalSubtitles.Add(Tuple.Create(norm, entry));
                    }
                }

                if (asarPatches.Count == 0)
                    throw new IOException("แพ็กเกจม็อดไม่สมบูรณ์ (ไม่พบไฟล์สำหรับแพตช์)");

                // 1. Extract external subtitles/th/*.srt to resources/subtitles/th/
                report(12, "กำลังติดตั้งไฟล์ซับไตเติลภาษาไทย (.srt)...");
                for (int i = 0; i < externalSubtitles.Count; i++)
                {
                    var item = externalSubtitles[i];
                    string destSrt = SafePath(resDir, item.Item1);
                    NoLinks(game.Root, destSrt);
                    Directory.CreateDirectory(Path.GetDirectoryName(destSrt));
                    using (var inS = item.Item2.Open())
                    using (var outS = File.Create(destSrt))
                    {
                        inS.CopyTo(outS);
                    }
                }

                // 2. Read base ASAR header & build merged ASAR into tmpAsarPath
                report(20, "กำลังอ่านโครงสร้างไฟล์เกม (app.asar)...");
                IDictionary<string, object> headerRoot;
                long baseDataOffset;

                using (var srcFs = new FileStream(sourceAsarPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var br = new BinaryReader(srcFs))
                {
                    if (srcFs.Length < 16) throw new IOException("ไฟล์ app.asar เสียหายหรือไม่สมบูรณ์");
                    uint pickle1 = br.ReadUInt32();
                    uint headerSize = br.ReadUInt32();
                    uint pickle2 = br.ReadUInt32();
                    uint jsonSize = br.ReadUInt32();
                    baseDataOffset = 8L + headerSize;
                    byte[] jsonBytes = br.ReadBytes((int)jsonSize);
                    string jsonStr = Encoding.UTF8.GetString(jsonBytes);
                    headerRoot = Json.DeserializeObject(jsonStr) as IDictionary<string, object>;
                    if (headerRoot == null || !headerRoot.ContainsKey("files"))
                        throw new IOException("รูปแบบส่วนหัวของ app.asar ไม่ถูกต้อง");

                    dynamicBytes = DynamicPatcher.BuildDynamicOverrides(srcFs, baseDataOffset, headerRoot, metaEntries, Json);
                    foreach (var dk in dynamicBytes.Keys) asarPatches.Remove(dk);

                    // Insert any newly added files from asarPatches into headerRoot
                    foreach (var kv in asarPatches)
                    {
                        string[] parts = kv.Key.Split('/');
                        var parentFiles = EnsureDirNode(headerRoot, parts, parts.Length - 1);
                        string leaf = parts[parts.Length - 1];
                        if (!parentFiles.ContainsKey(leaf))
                        {
                            var leafNode = new Dictionary<string, object>(StringComparer.Ordinal);
                            leafNode["size"] = kv.Value.Length;
                            leafNode["offset"] = "0";
                            parentFiles[leaf] = leafNode;
                        }
                    }

                    var plan = new List<Tuple<IDictionary<string, object>, ZipArchiveEntry, byte[], long, long>>();
                    CollectEntries(headerRoot, "", asarPatches, dynamicBytes, plan);

                    // Compute new offsets
                    long currentOffset = 0;
                    foreach (var p in plan)
                    {
                        var node = p.Item1;
                        long sz = p.Item5;
                        node["size"] = sz;
                        node["offset"] = currentOffset.ToString();
                        if (node.ContainsKey("integrity")) node.Remove("integrity");
                        currentOffset += sz;
                    }

                    string newJsonStr = Json.Serialize(headerRoot);
                    byte[] newJsonBytes = Encoding.UTF8.GetBytes(newJsonStr);
                    int jsonLen = newJsonBytes.Length;
                    int paddedJsonLen = (jsonLen + 3) & ~3;
                    int newHeaderSize = 8 + paddedJsonLen;

                    using (var outFs = new FileStream(tmpAsarPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024))
                    using (var bw = new BinaryWriter(outFs))
                    {
                        bw.Write((uint)4);
                        bw.Write((uint)newHeaderSize);
                        bw.Write((uint)(newHeaderSize - 4));
                        bw.Write((uint)jsonLen);
                        bw.Write(newJsonBytes);
                        if (paddedJsonLen > jsonLen)
                            bw.Write(new byte[paddedJsonLen - jsonLen]);

                        byte[] copyBuf = new byte[8 * 1024 * 1024];
                        long totalBytes = currentOffset;
                        long writtenBytes = 0;
                        int lastPct = 25;

                        for (int idx = 0; idx < plan.Count; idx++)
                        {
                            var p = plan[idx];
                            var zEntry = p.Item2;
                            var dBuf = p.Item3;
                            long origOff = p.Item4;
                            long sz = p.Item5;

                            if (dBuf != null)
                            {
                                outFs.Write(dBuf, 0, dBuf.Length);
                            }
                            else if (zEntry != null)
                            {
                                using (var zs = zEntry.Open())
                                {
                                    int r;
                                    while ((r = zs.Read(copyBuf, 0, copyBuf.Length)) > 0)
                                    {
                                        outFs.Write(copyBuf, 0, r);
                                    }
                                }
                            }
                            else if (sz > 0)
                            {
                                srcFs.Seek(baseDataOffset + origOff, SeekOrigin.Begin);
                                long rem = sz;
                                while (rem > 0)
                                {
                                    int toRead = (int)Math.Min(copyBuf.Length, rem);
                                    int r = srcFs.Read(copyBuf, 0, toRead);
                                    if (r <= 0) throw new IOException("อ่านข้อมูลจาก app.asar ไม่ครบถ้วน");
                                    outFs.Write(copyBuf, 0, r);
                                    rem -= r;
                                }
                            }

                            writtenBytes += sz;
                            int pct = 25 + (int)(writtenBytes * 70L / Math.Max(1L, totalBytes));
                            if (pct > lastPct)
                            {
                                lastPct = pct;
                                report(pct, "กำลังประกอบไฟล์เกมและฝังม็อดภาษาไทย (" + pct + "%)...");
                            }
                        }
                    }
                }
            }
        }

        report(96, "กำลังสำรองไฟล์ต้นฉบับและบันทึกไฟล์ม็อด...");
        CheckGameClosed(game.Root);

        if (File.Exists(asarPath)) File.Delete(asarPath);
        File.Move(tmpAsarPath, asarPath);

        report(100, "ติดตั้งสำเร็จ — เปิดเกมผ่าน Steam ได้เลย (ปรับขนาดซับได้ในหน้าตั้งค่า)");
    }
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if ((args.Length == 2 || args.Length == 3) && args[0] == "--render-preview")
        {
            using (var f = new InstallerForm())
                f.RenderPreview(args[1], args.Length == 3 ? args[2] : "ready");
            return 0;
        }
        bool created;
        using (var mutex = new System.Threading.Mutex(true, "Local\\SpiderTranslateUndertoneInstaller", out created))
        {
            if (!created)
            {
                MessageBox.Show("ตัวติดตั้งม็อดกำลังทำงานอยู่แล้ว", "Spider-F");
                return 1;
            }
            Application.Run(new InstallerForm());
        }
        return 0;
    }
}
