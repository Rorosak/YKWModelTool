using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Metanoia;
using Metanoia.Exporting;
using Metanoia.Formats._3DS.Level5;
using Metanoia.Modeling;
using Metanoia.Rendering;
using Metanoia.Tools;
using OpenTK;

namespace YKWModelTool
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        private const int ATTACH_PARENT_PROCESS = -1;
        private static string _baseDir;

        [STAThread]
        public static int Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveMetanoiaAssembly;

            bool cliMode = false;
            bool helpMode = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "--cli", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-c", StringComparison.OrdinalIgnoreCase))
                {
                    cliMode = true;
                }
                else if (string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "/?", StringComparison.OrdinalIgnoreCase))
                {
                    helpMode = true;
                }
            }

            if (cliMode || helpMode)
            {
                if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
                {
                    try { AttachConsole(ATTACH_PARENT_PROCESS); } catch { }
                }
            }

            return AppRunner.Run(args, cliMode, helpMode);
        }

        private static Assembly ResolveMetanoiaAssembly(object sender, ResolveEventArgs args)
        {
            string simpleName = new AssemblyName(args.Name).Name;
            string[] extCandidates = new string[] { simpleName + ".dll", simpleName + ".exe" };
            string[] dirCandidates = new string[]
            {
                _baseDir,
                Path.Combine(_baseDir, "Metanoia"),
                Path.Combine(_baseDir, "Metanoia", "lib"),
                Path.Combine(_baseDir, "lib"),
                Path.GetFullPath(Path.Combine(_baseDir, "..")),
                Path.GetFullPath(Path.Combine(_baseDir, "..", "Metanoia")),
                Path.GetFullPath(Path.Combine(_baseDir, "..", "Metanoia", "lib"))
            };

            foreach (string dir in dirCandidates)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string file in extCandidates)
                {
                    string path = Path.Combine(dir, file);
                    if (File.Exists(path))
                    {
                        try
                        {
                            return Assembly.LoadFrom(path);
                        }
                        catch
                        {
                            return Assembly.Load(File.ReadAllBytes(path));
                        }
                    }
                }
            }
            return null;
        }
    }

    internal static class AppRunner
    {
        public static int Run(string[] args, bool cliMode, bool helpMode)
        {
            if (helpMode)
            {
                PrintHelp();
                return 0;
            }

            if (cliMode)
            {
                return RunCli(args);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string initialPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("-", StringComparison.Ordinal) && (Directory.Exists(args[i]) || File.Exists(args[i])))
                {
                    initialPath = args[i];
                    break;
                }
            }

            using (YKWUniversalForm form = new YKWUniversalForm(initialPath))
            {
                Application.Run(form);
            }
            return 0;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("YKWModelTool - Universal Yo-kai Watch Model & Animation Exporter");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  YKWModelTool.exe [folderOrFile]               Launch GUI (optionally preloading path)");
            Console.WriteLine("  YKWModelTool.exe --cli <inputPath> [options]  Run headless batch export");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --out <dir>         Output directory (defaults to input folder)");
            Console.WriteLine("  --formats <list>    Comma-separated: glb,fbx,blend,dae,static_dae,smd,png,raw");
            Console.WriteLine("                      Default: glb,fbx,blend,dae,static_dae,smd,png");
            Console.WriteLine("  --fps <num>         Animation frame rate (default: 30)");
            Console.WriteLine("  --anims <mode>      all | sub | reel (default: all)");
            Console.WriteLine("  --blender <path>    Path to blender.exe (auto-detected if omitted)");
            Console.WriteLine("  --help              Show this help");
        }

        private static int RunCli(string[] args)
        {
            string inputPath = null;
            string outDir = null;
            string formatsRaw = "glb,fbx,blend,dae,static_dae,smd,png";
            string animMode = "all";
            string blenderPath = YKWExporter.FindBlenderExecutable();
            float fps = 30f;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "--cli", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-c", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                else if (string.Equals(a, "--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    outDir = args[++i];
                }
                else if (string.Equals(a, "--formats", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    formatsRaw = args[++i];
                }
                else if (string.Equals(a, "--fps", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    float parsed;
                    if (float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed > 0f)
                        fps = parsed;
                }
                else if (string.Equals(a, "--anims", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    animMode = args[++i].ToLowerInvariant();
                }
                else if (string.Equals(a, "--blender", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    blenderPath = args[++i];
                }
                else if (!a.StartsWith("-", StringComparison.Ordinal) && inputPath == null)
                {
                    inputPath = a;
                }
            }

            if (string.IsNullOrEmpty(inputPath) || (!Directory.Exists(inputPath) && !File.Exists(inputPath)))
            {
                Console.Error.WriteLine("Error: Please provide a valid input folder or .xc/.xv file.");
                return 1;
            }

            ExportOptions options = ExportOptions.FromFormatList(formatsRaw);
            options.Fps = fps;
            options.BlenderExe = blenderPath;

            Action<string> log = delegate(string msg) { Console.WriteLine(msg); };
            List<YKWCharacterGroup> groups = YKWScanner.Scan(new string[] { inputPath }, log);
            if (groups.Count == 0)
            {
                Console.Error.WriteLine("Error: No Yo-kai Watch models found in: " + inputPath);
                return 2;
            }

            foreach (YKWCharacterGroup group in groups)
            {
                string targetOut = outDir;
                if (string.IsNullOrEmpty(targetOut))
                {
                    targetOut = Directory.Exists(inputPath) ? inputPath : Path.GetDirectoryName(inputPath);
                }
                else if (groups.Count > 1)
                {
                    targetOut = Path.Combine(outDir, group.Prefix);
                }

                List<YKWAnimationEntry> selectedAnims = new List<YKWAnimationEntry>();
                foreach (YKWAnimationEntry anim in group.Animations)
                {
                    if (animMode == "sub" && anim.IsFullReel) continue;
                    if (animMode == "reel" && !anim.IsFullReel) continue;
                    selectedAnims.Add(anim);
                }

                Console.WriteLine(string.Format("=== Exporting {0} -> {1} ({2} animations) ===", group.DisplayName, targetOut, selectedAnims.Count));
                YKWExporter.ExportGroup(group, selectedAnims, targetOut, options, log, null);
            }

            Console.WriteLine("Done!");
            return 0;
        }
    }

    internal static class YKWTranslation
    {
        private static readonly KeyValuePair<string, string>[] TokenPairs = new KeyValuePair<string, string>[]
        {
            new KeyValuePair<string, string>("アイドリング", "Idle"),
            new KeyValuePair<string, string>("ダメージ", "Damage"),
            new KeyValuePair<string, string>("ワープ消", "Warp Out"),
            new KeyValuePair<string, string>("ワープ現", "Warp In"),
            new KeyValuePair<string, string>("ローリング", "Rolling"),
            new KeyValuePair<string, string>("ラッシュ殴", "Rush Punch"),
            new KeyValuePair<string, string>("ラッシュ撃", "Rush Shot"),
            new KeyValuePair<string, string>("ビーム右", "Beam R"),
            new KeyValuePair<string, string>("ビーム左", "Beam L"),
            new KeyValuePair<string, string>("ショット", "Shot"),
            new KeyValuePair<string, string>("ピヨり", "Dizzy"),
            new KeyValuePair<string, string>("立ち", "Stand"),
            new KeyValuePair<string, string>("歩き", "Walk"),
            new KeyValuePair<string, string>("走り", "Run"),
            new KeyValuePair<string, string>("疲れ", "Tired"),
            new KeyValuePair<string, string>("勝利", "Victory"),
            new KeyValuePair<string, string>("発見", "Notice"),
            new KeyValuePair<string, string>("痺れ", "Stun"),
            new KeyValuePair<string, string>("ショー", "Show/Soultimate"),
            new KeyValuePair<string, string>("必殺", "Soultimate"),
            new KeyValuePair<string, string>("攻撃", "Attack"),
            new KeyValuePair<string, string>("待機", "Wait"),
            new KeyValuePair<string, string>("登場", "Appear"),
            new KeyValuePair<string, string>("退場", "Exit"),
            new KeyValuePair<string, string>("ガード", "Guard"),
            new KeyValuePair<string, string>("回復", "Heal"),
            new KeyValuePair<string, string>("召喚", "Summon"),
            new KeyValuePair<string, string>("死", "KO/Death"),
            new KeyValuePair<string, string>("構", "Pose"),
            new KeyValuePair<string, string>("入", "Intro"),
            new KeyValuePair<string, string>("戻", "Return"),
            new KeyValuePair<string, string>("終", "End"),
            new KeyValuePair<string, string>("避", "Dodge"),
            new KeyValuePair<string, string>("戦", "Battle "),
            new KeyValuePair<string, string>("バ", "Blasters ")
        };

        public static string GetEnglishHint(string animName)
        {
            if (string.IsNullOrEmpty(animName)) return "";
            string work = animName;
            if (work.StartsWith("out_00_", StringComparison.OrdinalIgnoreCase))
                work = work.Substring(7);
            else if (string.Equals(work, "out_00", StringComparison.OrdinalIgnoreCase))
                return "Full Reel";

            List<string> parts = new List<string>();
            int idx = 0;
            while (idx < work.Length)
            {
                bool matched = false;
                for (int t = 0; t < TokenPairs.Length; t++)
                {
                    string jp = TokenPairs[t].Key;
                    if (idx + jp.Length <= work.Length && string.CompareOrdinal(work, idx, jp, 0, jp.Length) == 0)
                    {
                        parts.Add(TokenPairs[t].Value.Trim());
                        idx += jp.Length;
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                {
                    char c = work[idx];
                    if (c == 'L' && idx == work.Length - 1)
                    {
                        parts.Add("(Loop)");
                    }
                    else if (char.IsDigit(c))
                    {
                        if (parts.Count > 0 && parts[parts.Count - 1] != "(Loop)")
                        {
                            // Keep variant number compact
                        }
                    }
                    else if (c < 128 && c != '_')
                    {
                        parts.Add(c.ToString());
                    }
                    idx++;
                }
            }
            if (parts.Count == 0) return "";
            return string.Join(" ", parts.ToArray());
        }
    }

    internal sealed class YKWAnimationEntry
    {
        public string Pack { get; set; }
        public int Index { get; set; }
        public string SourcePath { get; set; }
        public GenericAnimation Animation { get; set; }
        public string EnglishHint { get; set; }
        public bool IsFullReel { get; set; }

        public string ActionName
        {
            get
            {
                return string.Format("{0}_{1:D2}_{2}", Pack, Index, YKWExporter.SanitizeFileName(Animation.Name));
            }
        }
    }

    internal sealed class YKWCharacterGroup
    {
        public string Prefix { get; set; }
        public string ModelPath { get; set; }
        public string ModelPack { get; set; }
        public GenericModel Model { get; set; }
        public List<string> SourceArchives { get; private set; }
        public List<YKWAnimationEntry> Animations { get; private set; }

        public YKWCharacterGroup()
        {
            SourceArchives = new List<string>();
            Animations = new List<YKWAnimationEntry>();
        }

        public string DisplayName
        {
            get
            {
                int mCount = (Model != null && Model.Meshes != null) ? Model.Meshes.Count : 0;
                int bCount = (Model != null && Model.Skeleton != null && Model.Skeleton.Bones != null) ? Model.Skeleton.Bones.Count : 0;
                int tCount = (Model != null && Model.TextureBank != null) ? Model.TextureBank.Count : 0;
                return string.Format("{0} ({1}) - {2} meshes, {3} bones, {4} tex, {5} anims",
                    Prefix, ModelPack, mCount, bCount, tCount, Animations.Count);
            }
        }
    }

    internal static class YKWScanner
    {
        private sealed class PendingAnimPack
        {
            public string Prefix;
            public string Pack;
            public string FilePath;
            public List<GenericAnimation> Animations;
        }

        private static readonly Regex PackRegex = new Regex(@"^(.*?)_(p\d+|model|mdl|anim|motion|mtn)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<YKWCharacterGroup> Scan(string[] paths, Action<string> log)
        {
            List<string> directories = new List<string>();
            List<string> explicitFiles = new List<string>();

            foreach (string p in paths)
            {
                if (Directory.Exists(p))
                {
                    if (!directories.Contains(p)) directories.Add(p);
                }
                else if (File.Exists(p))
                {
                    string dir = Path.GetDirectoryName(p);
                    if (!directories.Contains(dir)) directories.Add(dir);
                    if (!explicitFiles.Contains(p)) explicitFiles.Add(p);
                }
            }

            List<YKWCharacterGroup> allGroups = new List<YKWCharacterGroup>();

            foreach (string dir in directories)
            {
                List<YKWCharacterGroup> dirGroups = ScanDirectory(dir, log);
                allGroups.AddRange(dirGroups);
            }

            return allGroups;
        }

        private static List<YKWCharacterGroup> ScanDirectory(string dir, Action<string> log)
        {
            List<YKWCharacterGroup> groups = new List<YKWCharacterGroup>();
            List<PendingAnimPack> animPacks = new List<PendingAnimPack>();
            HashSet<string> seenMotionHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<string> xcFiles = new List<string>();
            xcFiles.AddRange(Directory.GetFiles(dir, "*.xc"));
            xcFiles.AddRange(Directory.GetFiles(dir, "*.xv"));
            xcFiles.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string xcPath in xcFiles)
            {
                try
                {
                    byte[] data = File.ReadAllBytes(xcPath);
                    FileItem fi = new FileItem(Path.GetFileName(xcPath), data);
                    if (!string.Equals(fi.MagicString, "XPCK", StringComparison.Ordinal))
                    {
                        if (log != null) log("Skipping non-XPCK file: " + Path.GetFileName(xcPath));
                        continue;
                    }

                    Level5_XC xc = new Level5_XC();
                    xc.Open(fi);
                    FileItem[] subfiles = xc.GetFiles();

                    bool hasModel = false;
                    bool hasAnim = false;
                    foreach (FileItem sub in subfiles)
                    {
                        string ext = Path.GetExtension(sub.FileName).ToLowerInvariant();
                        if (ext == ".prm" || ext == ".mbn") hasModel = true;
                        if (ext == ".mtn2" || ext == ".mtn3")
                        {
                            hasAnim = true;
                            seenMotionHashes.Add(ComputeBytesHash(sub.GetFileBinary()));
                        }
                    }

                    string prefix;
                    string pack;
                    SplitPrefixAndPack(Path.GetFileNameWithoutExtension(xcPath), out prefix, out pack);

                    if (hasModel)
                    {
                        GenericModel mdl = LoadModelSafe(xc, log);
                        if (mdl != null && mdl.Meshes.Count > 0)
                        {
                            YKWCharacterGroup grp = new YKWCharacterGroup();
                            grp.Prefix = prefix;
                            grp.ModelPath = xcPath;
                            grp.ModelPack = pack;
                            grp.Model = mdl;
                            grp.SourceArchives.Add(xcPath);
                            groups.Add(grp);
                        }
                    }

                    if (hasAnim)
                    {
                        GenericAnimation[] anims = xc.ToGenericAnimation();
                        if (anims != null && anims.Length > 0)
                        {
                            PendingAnimPack ap = new PendingAnimPack();
                            ap.Prefix = prefix;
                            ap.Pack = pack;
                            ap.FilePath = xcPath;
                            ap.Animations = new List<GenericAnimation>(anims);
                            animPacks.Add(ap);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (log != null) log("Warning reading " + Path.GetFileName(xcPath) + ": " + ex.Message);
                }
            }

            // Scan loose .mtn2 / .mtn3 files not already contained inside .xc archives in the same directory
            List<string> looseMtnFiles = new List<string>();
            looseMtnFiles.AddRange(Directory.GetFiles(dir, "*.mtn2"));
            looseMtnFiles.AddRange(Directory.GetFiles(dir, "*.mtn3"));
            looseMtnFiles.Sort(StringComparer.OrdinalIgnoreCase);

            List<string> uniqueLooseMtn = new List<string>();
            foreach (string mtnPath in looseMtnFiles)
            {
                byte[] mtnBytes = File.ReadAllBytes(mtnPath);
                if (!seenMotionHashes.Contains(ComputeBytesHash(mtnBytes)))
                {
                    uniqueLooseMtn.Add(mtnPath);
                }
            }

            if (uniqueLooseMtn.Count > 0)
            {
                PendingAnimPack loosePack = LoadLooseAnimations(dir, uniqueLooseMtn, log);
                if (loosePack != null && loosePack.Animations.Count > 0)
                {
                    animPacks.Add(loosePack);
                }
            }

            // Pair animation packs to character model groups
            if (groups.Count == 1)
            {
                YKWCharacterGroup single = groups[0];
                foreach (PendingAnimPack ap in animPacks)
                {
                    AddAnimPackToGroup(single, ap);
                }
            }
            else if (groups.Count > 1)
            {
                foreach (PendingAnimPack ap in animPacks)
                {
                    YKWCharacterGroup best = null;
                    foreach (YKWCharacterGroup g in groups)
                    {
                        if (string.Equals(g.Prefix, ap.Prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            best = g;
                            break;
                        }
                    }
                    if (best == null)
                    {
                        int bestLen = 0;
                        foreach (YKWCharacterGroup g in groups)
                        {
                            int common = CommonPrefixLength(g.Prefix, ap.Prefix);
                            if (common > bestLen)
                            {
                                bestLen = common;
                                best = g;
                            }
                        }
                    }
                    if (best == null) best = groups[0];
                    AddAnimPackToGroup(best, ap);
                }
            }

            return groups;
        }

        private static void AddAnimPackToGroup(YKWCharacterGroup group, PendingAnimPack ap)
        {
            if (!group.SourceArchives.Contains(ap.FilePath))
                group.SourceArchives.Add(ap.FilePath);

            // Deduplicate pack prefix if the same pack name occurs twice
            string packLabel = ap.Pack;
            int suffix = 2;
            while (HasPackIndexCollision(group, packLabel, ap.Animations.Count))
            {
                packLabel = ap.Pack + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            for (int i = 0; i < ap.Animations.Count; i++)
            {
                GenericAnimation anim = ap.Animations[i];
                YKWAnimationEntry entry = new YKWAnimationEntry();
                entry.Pack = packLabel;
                entry.Index = i;
                entry.SourcePath = ap.FilePath;
                entry.Animation = anim;
                entry.IsFullReel = string.Equals(anim.Name, "out_00", StringComparison.OrdinalIgnoreCase) ||
                                   (i == 0 && !anim.Name.Contains("_"));
                entry.EnglishHint = YKWTranslation.GetEnglishHint(anim.Name);
                group.Animations.Add(entry);
            }
        }

        private static bool HasPackIndexCollision(YKWCharacterGroup group, string packLabel, int count)
        {
            foreach (YKWAnimationEntry e in group.Animations)
            {
                if (string.Equals(e.Pack, packLabel, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static PendingAnimPack LoadLooseAnimations(string dir, List<string> mtnFiles, Action<string> log)
        {
            Level5_Resource resFile = null;
            string resPath = Path.Combine(dir, "RES.bin");
            if (File.Exists(resPath))
            {
                try { resFile = new Level5_Resource(File.ReadAllBytes(resPath)); } catch { }
            }

            Dictionary<uint, List<Level5_MINF>> minfMap = new Dictionary<uint, List<Level5_MINF>>();
            foreach (string f in Directory.GetFiles(dir, "*.mtninf"))
            {
                try
                {
                    Level5_MINF minf = new Level5_MINF();
                    minf.Open(File.ReadAllBytes(f));
                    if (!minfMap.ContainsKey(minf.AnimationName))
                        minfMap[minf.AnimationName] = new List<Level5_MINF>();
                    minfMap[minf.AnimationName].Add(minf);
                }
                catch { }
            }

            Dictionary<uint, List<Level5_MINF2.SubAnimation>> minf2Map = new Dictionary<uint, List<Level5_MINF2.SubAnimation>>();
            foreach (string f in Directory.GetFiles(dir, "*.mtninf2"))
            {
                try
                {
                    Level5_MINF2 minf2 = new Level5_MINF2();
                    minf2.Open(File.ReadAllBytes(f));
                    foreach (Level5_MINF2.SubAnimation sub in minf2.SubAnimations)
                    {
                        if (!minf2Map.ContainsKey(sub.AnimationName))
                            minf2Map[sub.AnimationName] = new List<Level5_MINF2.SubAnimation>();
                        minf2Map[sub.AnimationName].Add(sub);
                    }
                }
                catch { }
            }

            List<GenericAnimation> result = new List<GenericAnimation>();
            foreach (string mtnPath in mtnFiles)
            {
                try
                {
                    GenericAnimation anim = null;
                    string ext = Path.GetExtension(mtnPath).ToLowerInvariant();
                    byte[] bytes = File.ReadAllBytes(mtnPath);
                    if (ext == ".mtn2")
                    {
                        Level5_MTN2 m2 = new Level5_MTN2();
                        m2.Open(Path.GetFileName(mtnPath), bytes);
                        anim = m2.ToGenericAnimation();
                    }
                    else if (ext == ".mtn3")
                    {
                        Level5_MTN3 m3 = new Level5_MTN3();
                        m3.Open(Path.GetFileName(mtnPath), bytes);
                        anim = m3.ToGenericAnimation();
                    }

                    if (anim == null) continue;
                    result.Add(anim);
                    uint crc = CRC32.Crc32C(anim.Name);

                    if (minfMap.ContainsKey(crc))
                    {
                        foreach (Level5_MINF minf in minfMap[crc])
                        {
                            GenericAnimation trimmed = anim.TrimAnimation((float)minf.FrameStart, (float)minf.FrameEnd);
                            string subName = minf.AnimationSubName.ToString("X8");
                            if (resFile != null)
                            {
                                string resolved = resFile.GetResourceName(minf.AnimationSubName);
                                if (!string.IsNullOrEmpty(resolved)) subName = resolved;
                            }
                            trimmed.Name = anim.Name + "_" + subName;
                            result.Add(trimmed);
                        }
                    }
                    if (minf2Map.ContainsKey(crc))
                    {
                        foreach (Level5_MINF2.SubAnimation sub in minf2Map[crc])
                        {
                            GenericAnimation trimmed = anim.TrimAnimation((float)sub.FrameStart, (float)sub.FrameEnd);
                            trimmed.Name = anim.Name + "_" + sub.AnimationSubName;
                            result.Add(trimmed);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (log != null) log("Warning loading loose motion " + Path.GetFileName(mtnPath) + ": " + ex.Message);
                }
            }

            PendingAnimPack ap = new PendingAnimPack();
            ap.Prefix = Path.GetFileName(dir);
            ap.Pack = "loose";
            ap.FilePath = mtnFiles[0];
            ap.Animations = result;
            return ap;
        }

        private static GenericModel LoadModelSafe(Level5_XC xc, Action<string> log)
        {
            try
            {
                return xc.ToGenericModel();
            }
            catch (Exception ex)
            {
                if (log != null) log("Fallback model loader used (" + ex.Message + ")");
            }

            GenericModel model = new GenericModel();
            GenericSkeleton skel = new GenericSkeleton();
            model.Skeleton = skel;
            Level5_Resource resourceFile = null;
            List<GenericTexture> textures = new List<GenericTexture>();

            foreach (KeyValuePair<string, byte[]> kvp in xc.Files)
            {
                if (kvp.Key.EndsWith("RES.bin", StringComparison.OrdinalIgnoreCase))
                {
                    resourceFile = new Level5_Resource(kvp.Value);
                    model.Name = resourceFile.ModelName;
                }
                else if (kvp.Key.EndsWith(".mbn", StringComparison.OrdinalIgnoreCase))
                {
                    skel.Bones.Add(Level5_MBN.ToBone(kvp.Value));
                }
                else if (kvp.Key.EndsWith(".prm", StringComparison.OrdinalIgnoreCase))
                {
                    model.Meshes.Add(Level5_PRM.ToGenericMesh(kvp.Value));
                }
                else if (kvp.Key.EndsWith(".xi", StringComparison.OrdinalIgnoreCase))
                {
                    GenericTexture tex = Level5_XI.ToGenericTexture(kvp.Value);
                    tex.Name = kvp.Key;
                    textures.Add(tex);
                }
            }

            if (resourceFile != null)
            {
                foreach (Level5_Resource.Level5_Material mat in resourceFile.Materials)
                {
                    GenericMaterial gm = new GenericMaterial();
                    gm.TextureDiffuse = mat.TexName;
                    model.MaterialBank[mat.Name] = gm;
                }
                for (int i = 0; i < textures.Count; i++)
                {
                    string texName = (i < resourceFile.TextureNames.Count) ? resourceFile.TextureNames[i] : ("tex_" + i.ToString("D2"));
                    model.TextureBank[texName] = textures[i];
                }
                foreach (GenericBone bone in skel.Bones)
                {
                    bone.Name = resourceFile.GetResourceName((uint)bone.ID);
                    bone.ID = 0;
                }
                foreach (GenericBone bone in skel.Bones)
                {
                    if (bone.ParentIndex == 0)
                    {
                        bone.ParentIndex = -1;
                    }
                    else
                    {
                        string parentName = resourceFile.GetResourceName((uint)bone.ParentIndex);
                        bone.ParentIndex = skel.Bones.FindIndex(delegate(GenericBone b) { return b.Name == parentName; });
                    }
                }
            }

            int unnamedIdx = 0;
            foreach (GenericBone bone in skel.Bones)
            {
                if (string.IsNullOrEmpty(bone.Name))
                    bone.Name = "Bone_" + (unnamedIdx++).ToString(CultureInfo.InvariantCulture);
            }

            foreach (GenericMesh mesh in model.Meshes)
            {
                for (int vi = 0; vi < mesh.Vertices.Count; vi++)
                {
                    GenericVertex v = mesh.Vertices[vi];
                    Vector4 newBones = new Vector4();
                    for (int j = 0; j < 4; j++)
                    {
                        if (v.Weights[j] > 0f)
                        {
                            uint hash = BitConverter.ToUInt32(BitConverter.GetBytes(v.Bones[j]), 0);
                            string bName = resourceFile != null ? resourceFile.GetResourceName(hash) : "";
                            newBones[j] = (float)skel.Bones.FindIndex(delegate(GenericBone b) { return b.Name == bName; });
                        }
                    }
                    v.Bones = newBones;
                    mesh.Vertices[vi] = v;
                }
            }

            return model;
        }

        private static void SplitPrefixAndPack(string stem, out string prefix, out string pack)
        {
            Match m = PackRegex.Match(stem);
            if (m.Success)
            {
                prefix = m.Groups[1].Value;
                pack = m.Groups[2].Value;
                return;
            }
            prefix = stem;
            pack = "main";
        }

        private static int CommonPrefixLength(string a, string b)
        {
            int len = Math.Min(a.Length, b.Length);
            int i = 0;
            while (i < len && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i])) i++;
            return i;
        }

        private static string ComputeBytesHash(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }

    internal sealed class ExportOptions
    {
        public bool ExportGlb { get; set; }
        public bool ExportFbx { get; set; }
        public bool ExportBlend { get; set; }
        public bool ExportAnimDae { get; set; }
        public bool ExportStaticDae { get; set; }
        public bool ExportSmd { get; set; }
        public bool ExportPng { get; set; }
        public bool ExtractRaw { get; set; }
        public float Fps { get; set; }
        public string BlenderExe { get; set; }

        public ExportOptions()
        {
            ExportGlb = true;
            ExportFbx = true;
            ExportBlend = true;
            ExportAnimDae = true;
            ExportStaticDae = true;
            ExportSmd = true;
            ExportPng = true;
            ExtractRaw = false;
            Fps = 30f;
            BlenderExe = YKWExporter.FindBlenderExecutable();
        }

        public static ExportOptions FromFormatList(string csv)
        {
            ExportOptions opts = new ExportOptions();
            opts.ExportGlb = false;
            opts.ExportFbx = false;
            opts.ExportBlend = false;
            opts.ExportAnimDae = false;
            opts.ExportStaticDae = false;
            opts.ExportSmd = false;
            opts.ExportPng = false;
            opts.ExtractRaw = false;

            string[] tokens = (csv ?? "").Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawTok in tokens)
            {
                string tok = rawTok.Trim().ToLowerInvariant();
                if (tok == "all")
                {
                    opts.ExportGlb = opts.ExportFbx = opts.ExportBlend = opts.ExportAnimDae = opts.ExportStaticDae = opts.ExportSmd = opts.ExportPng = true;
                }
                else if (tok == "glb" || tok == "gltf") opts.ExportGlb = true;
                else if (tok == "fbx") opts.ExportFbx = true;
                else if (tok == "blend" || tok == "blender") opts.ExportBlend = true;
                else if (tok == "dae" || tok == "anim_dae") opts.ExportAnimDae = true;
                else if (tok == "static_dae") opts.ExportStaticDae = true;
                else if (tok == "smd") opts.ExportSmd = true;
                else if (tok == "png" || tok == "tex" || tok == "textures") opts.ExportPng = true;
                else if (tok == "raw") opts.ExtractRaw = true;
            }
            return opts;
        }
    }

    internal static class YKWExporter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly char[] InvalidFileChars = Path.GetInvalidFileNameChars();

        public static string FindBlenderExecutable()
        {
            string[] roots = new string[]
            {
                @"C:\Program Files\Blender Foundation",
                @"C:\Program Files (x86)\Blender Foundation"
            };

            List<string> candidates = new List<string>();
            foreach (string r in roots)
            {
                if (!Directory.Exists(r)) continue;
                try
                {
                    foreach (string exe in Directory.GetFiles(r, "blender.exe", SearchOption.AllDirectories))
                    {
                        candidates.Add(exe);
                    }
                }
                catch { }
            }

            if (candidates.Count > 0)
            {
                candidates.Sort(StringComparer.OrdinalIgnoreCase);
                return candidates[candidates.Count - 1];
            }

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathEnv.Split(';'))
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string exe = Path.Combine(dir.Trim(), "blender.exe");
                if (File.Exists(exe)) return exe;
            }

            return "";
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "anim";
            StringBuilder sb = new StringBuilder(name.Length);
            foreach (char ch in name)
            {
                bool invalid = false;
                for (int i = 0; i < InvalidFileChars.Length; i++)
                {
                    if (InvalidFileChars[i] == ch) { invalid = true; break; }
                }
                sb.Append(invalid ? '_' : ch);
            }
            string s = sb.ToString().Trim();
            return string.IsNullOrEmpty(s) ? "anim" : s;
        }

        public static void ExportGroup(
            YKWCharacterGroup group,
            List<YKWAnimationEntry> animations,
            string outDir,
            ExportOptions options,
            Action<string> log,
            Action<int, int> progress)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            Directory.CreateDirectory(outDir);
            GenericModel model = group.Model;
            bool needBlender = options.ExportGlb || options.ExportFbx || options.ExportBlend;

            if (needBlender && (string.IsNullOrEmpty(options.BlenderExe) || !File.Exists(options.BlenderExe)))
            {
                throw new FileNotFoundException("Blender executable not found. Select blender.exe or uncheck GLB/FBX/BLEND.");
            }

            int blenderSteps = needBlender
                ? (1 + animations.Count + (options.ExportGlb ? 1 : 0) + (options.ExportFbx ? 1 : 0) + (options.ExportBlend ? 1 : 0))
                : 0;
            int totalSteps = 1 + animations.Count + blenderSteps;
            int currentStep = 0;
            Action advanceProgress = delegate()
            {
                int next = Interlocked.Increment(ref currentStep);
                if (progress != null) progress(next, totalSteps);
            };
            if (progress != null) progress(0, totalSteps);

            string tempSkelDir = Path.Combine(outDir, "exports", "temp_skel");
            if (needBlender) Directory.CreateDirectory(tempSkelDir);

            // Create hidden OpenGL context so Metanoia's RenderTexture / ExportSMD / DAE texture export work headless
            GLControl glControl = new GLControl();
            try
            {
                glControl.CreateControl();
                glControl.MakeCurrent();

                // 1. Extract raw subfiles if requested
                if (options.ExtractRaw)
                {
                    ExtractRawArchives(group, outDir, log);
                }

                // 2. Export PNG textures to outDir (if requested) or tempSkelDir (for Blender if png unchecked)
                List<string> exportedPngPaths = new List<string>();
                if (options.ExportPng || options.ExportStaticDae || options.ExportAnimDae)
                {
                    exportedPngPaths = ExportTextures(model, outDir, log);
                }
                else if (needBlender)
                {
                    exportedPngPaths = ExportTextures(model, tempSkelDir, log);
                }

                // 3. Export static COLLADA model.dae
                string baseDaePath = (options.ExportStaticDae || options.ExportAnimDae)
                    ? Path.Combine(outDir, "model.dae")
                    : Path.Combine(tempSkelDir, "model.dae");

                if (options.ExportStaticDae || options.ExportAnimDae || needBlender)
                {
                    ExportStaticDAE(model, baseDaePath, true, log);
                    if (options.ExportStaticDae)
                    {
                        string namedDaePath = Path.Combine(outDir, group.Prefix + "_model.dae");
                        if (!string.Equals(namedDaePath, baseDaePath, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(baseDaePath, namedDaePath, true);
                        }
                    }
                }

                // 4. Export reference Valve SMD model
                if (options.ExportSmd)
                {
                    string smdDir = Path.Combine(outDir, "exports", "smd");
                    Directory.CreateDirectory(smdDir);
                    ExportSMD smdExporter = new ExportSMD();
                    string modelSmdPath = Path.Combine(smdDir, group.Prefix + "_model.smd");
                    ResetSkeleton(model.Skeleton);
                    smdExporter.Export(modelSmdPath, model);
                    if (log != null) log("Exported SMD model: " + modelSmdPath);
                }
                advanceProgress();

                // Prepare COLLADA templates for animation injection and skeleton-only Blender import
                string baseDaeText = null;
                string baseDaePrefix = null;
                string skelPrefix = null;
                if ((options.ExportAnimDae || needBlender) && File.Exists(baseDaePath))
                {
                    baseDaeText = File.ReadAllText(baseDaePath, Utf8NoBom);
                    int closingIdx = baseDaeText.LastIndexOf("</COLLADA>", StringComparison.Ordinal);
                    if (closingIdx < 0) throw new InvalidDataException("Invalid COLLADA file (missing </COLLADA>): " + baseDaePath);
                    baseDaePrefix = baseDaeText.Substring(0, closingIdx);

                    string armatureXml = ExtractArmatureNodeOuterXml(baseDaeText);
                    skelPrefix = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                                 "<COLLADA xmlns=\"http://www.collada.org/2005/11/COLLADASchema\" version=\"1.4.1\">\r\n" +
                                 "  <asset />\r\n" +
                                 "  <library_visual_scenes>\r\n" +
                                 "    <visual_scene id=\"Scene\" name=\"Scene\">\r\n" +
                                 "      " + armatureXml + "\r\n" +
                                 "    </visual_scene>\r\n" +
                                 "  </library_visual_scenes>\r\n";
                }

                // 5. Export each selected animation
                List<Dictionary<string, string>> manifestItems = new List<Dictionary<string, string>>();
                HashSet<string> copiedDaeTextureDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                YKWAnimationEntry longestReel = null;
                string longestReelLibAnim = null;

                ExportSMD smdAnimExporter = options.ExportSmd ? new ExportSMD() : null;

                for (int ai = 0; ai < animations.Count; ai++)
                {
                    YKWAnimationEntry entry = animations[ai];
                    GenericAnimation anim = entry.Animation;
                    string fileBase = string.Format(CultureInfo.InvariantCulture, "{0:D2}_{1}", entry.Index, SanitizeFileName(anim.Name));
                    string actionName = entry.ActionName;

                    if (options.ExportSmd)
                    {
                        string packSmdDir = Path.Combine(outDir, "exports", "smd", entry.Pack);
                        Directory.CreateDirectory(packSmdDir);
                        string smdAnimPath = Path.Combine(packSmdDir, fileBase + ".smd");
                        ResetSkeleton(model.Skeleton);
                        smdAnimExporter.Export(smdAnimPath, model.Skeleton, anim);
                    }

                    if (options.ExportAnimDae || needBlender)
                    {
                        string libAnimXml = BuildLibraryAnimationsXml(model.Skeleton, anim, options.Fps, actionName);

                        if (options.ExportAnimDae)
                        {
                            string packDaeDir = Path.Combine(outDir, "exports", "dae", entry.Pack);
                            Directory.CreateDirectory(packDaeDir);
                            if (!copiedDaeTextureDirs.Contains(packDaeDir))
                            {
                                foreach (string png in exportedPngPaths)
                                {
                                    if (File.Exists(png))
                                        File.Copy(png, Path.Combine(packDaeDir, Path.GetFileName(png)), true);
                                }
                                copiedDaeTextureDirs.Add(packDaeDir);
                            }

                            string daeOutPath = Path.Combine(packDaeDir, fileBase + ".dae");
                            File.WriteAllText(daeOutPath, baseDaePrefix + libAnimXml + "</COLLADA>\r\n", Utf8NoBom);

                            if (longestReel == null || anim.FrameCount > longestReel.Animation.FrameCount)
                            {
                                longestReel = entry;
                                longestReelLibAnim = libAnimXml;
                            }
                        }

                        if (needBlender)
                        {
                            string skelDaePath = Path.Combine(tempSkelDir, SanitizeFileName(actionName) + ".dae");
                            string skelContent = skelPrefix + libAnimXml + "  <scene>\r\n    <instance_visual_scene url=\"#Scene\" />\r\n  </scene>\r\n</COLLADA>\r\n";
                            File.WriteAllText(skelDaePath, skelContent, Utf8NoBom);

                            Dictionary<string, string> item = new Dictionary<string, string>();
                            item["action_name"] = actionName;
                            item["skel_dae"] = skelDaePath;
                            manifestItems.Add(item);
                        }
                    }

                    if (log != null)
                        log(string.Format("[{0}/{1}] {2} ({3} frames)", ai + 1, animations.Count, actionName, anim.FrameCount));
                    advanceProgress();
                }

                if (options.ExportAnimDae && longestReel != null && longestReelLibAnim != null)
                {
                    string mainAnimDaePath = Path.Combine(outDir, "model_animated.dae");
                    File.WriteAllText(mainAnimDaePath, baseDaePrefix + longestReelLibAnim + "</COLLADA>\r\n", Utf8NoBom);
                    if (log != null) log("Exported main animated DAE (" + longestReel.ActionName + "): " + mainAnimDaePath);
                }

                // 6. Run Blender to assemble GLB / FBX / BLEND
                if (needBlender)
                {
                    RunBlenderExport(group, baseDaePath, tempSkelDir, manifestItems, outDir, options, log, advanceProgress);
                }
            }
            finally
            {
                glControl.Dispose();
                if (Directory.Exists(tempSkelDir))
                {
                    try { Directory.Delete(tempSkelDir, true); } catch { }
                }
                string exportsRoot = Path.Combine(outDir, "exports");
                if (Directory.Exists(exportsRoot))
                {
                    try
                    {
                        if (Directory.GetFileSystemEntries(exportsRoot).Length == 0)
                            Directory.Delete(exportsRoot, false);
                    }
                    catch { }
                }
            }
        }

        private static void ExtractRawArchives(YKWCharacterGroup group, string outDir, Action<string> log)
        {
            string rawBase = Path.Combine(outDir, "raw");
            foreach (string archivePath in group.SourceArchives)
            {
                string ext = Path.GetExtension(archivePath).ToLowerInvariant();
                if (ext != ".xc" && ext != ".xv") continue;

                try
                {
                    FileItem fi = new FileItem(Path.GetFileName(archivePath), File.ReadAllBytes(archivePath));
                    if (!string.Equals(fi.MagicString, "XPCK", StringComparison.Ordinal)) continue;
                    Level5_XC xc = new Level5_XC();
                    xc.Open(fi);
                    string targetDir = Path.Combine(rawBase, Path.GetFileNameWithoutExtension(archivePath));
                    Directory.CreateDirectory(targetDir);
                    foreach (FileItem sub in xc.GetFiles())
                    {
                        string subOut = Path.Combine(targetDir, sub.FileName);
                        Directory.CreateDirectory(Path.GetDirectoryName(subOut));
                        File.WriteAllBytes(subOut, sub.GetFileBinary());
                    }
                    if (log != null) log("Extracted raw files: " + targetDir);
                }
                catch (Exception ex)
                {
                    if (log != null) log("Warning extracting raw " + Path.GetFileName(archivePath) + ": " + ex.Message);
                }
            }
        }

        public static List<string> ExportTextures(GenericModel model, string dir, Action<string> log)
        {
            Directory.CreateDirectory(dir);
            List<string> paths = new List<string>();
            if (model == null || model.TextureBank == null) return paths;

            foreach (KeyValuePair<string, GenericTexture> kvp in model.TextureBank)
            {
                if (kvp.Value == null || kvp.Value.Mipmaps == null || kvp.Value.Mipmaps.Count == 0) continue;
                string outPng = Path.Combine(dir, kvp.Key + ".png");
                RenderTexture rt = new RenderTexture();
                rt.LoadGenericTexture(kvp.Value);
                rt.ExportPNG(outPng);
                rt.Delete();
                paths.Add(outPng);
            }
            if (log != null) log(string.Format("Exported {0} textures to {1}", paths.Count, dir));
            return paths;
        }

        public static void ExportStaticDAE(GenericModel model, string filePath, bool includeMaterials, Action<string> log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            string dir = Path.GetDirectoryName(filePath);

            using (DAEWriter writer = new DAEWriter(filePath, false))
            {
                writer.WriteAsset();

                if (includeMaterials)
                {
                    List<string> texNames = new List<string>();
                    foreach (KeyValuePair<string, GenericTexture> kvp in model.TextureBank)
                    {
                        texNames.Add(kvp.Key);
                        string pngPath = Path.Combine(dir, kvp.Key + ".png");
                        if (!File.Exists(pngPath) && kvp.Value != null && kvp.Value.Mipmaps.Count > 0)
                        {
                            RenderTexture rt = new RenderTexture();
                            rt.LoadGenericTexture(kvp.Value);
                            rt.ExportPNG(pngPath);
                            rt.Delete();
                        }
                    }
                    writer.WriteLibraryImages(texNames.ToArray(), ".png");

                    writer.StartMaterialSection();
                    foreach (KeyValuePair<string, GenericMaterial> kvp in model.MaterialBank)
                    {
                        writer.WriteMaterial(kvp.Key);
                    }
                    writer.EndMaterialSection();

                    writer.StartEffectSection();
                    foreach (KeyValuePair<string, GenericMaterial> kvp in model.MaterialBank)
                    {
                        writer.WriteEffect(kvp.Key, kvp.Value.TextureDiffuse ?? "");
                    }
                    writer.EndEffectSection();
                }
                else
                {
                    writer.WriteLibraryImages(null, ".png");
                }

                Dictionary<int, int> oldToNew = new Dictionary<int, int>();
                Dictionary<GenericBone, int> boneToOldIndex = new Dictionary<GenericBone, int>();

                if (model.Skeleton != null && model.Skeleton.Bones != null && model.Skeleton.Bones.Count > 0)
                {
                    for (int i = 0; i < model.Skeleton.Bones.Count; i++)
                    {
                        boneToOldIndex[model.Skeleton.Bones[i]] = i;
                    }

                    GenericBone[] breathFirst = model.Skeleton.BreathFirst;
                    for (int i = 0; i < breathFirst.Length; i++)
                    {
                        GenericBone bone = breathFirst[i];
                        oldToNew[boneToOldIndex[bone]] = i;

                        Matrix4 tf = bone.Transform;
                        float[] transformArr = new float[]
                        {
                            tf.M11, tf.M21, tf.M31, tf.M41,
                            tf.M12, tf.M22, tf.M32, tf.M42,
                            tf.M13, tf.M23, tf.M33, tf.M43,
                            tf.M14, tf.M24, tf.M34, tf.M44
                        };

                        Matrix4 inv = model.Skeleton.GetWorldTransform(bone, false).Inverted();
                        float[] invArr = new float[]
                        {
                            inv.M11, inv.M21, inv.M31, inv.M41,
                            inv.M12, inv.M22, inv.M32, inv.M42,
                            inv.M13, inv.M23, inv.M33, inv.M43,
                            inv.M14, inv.M24, inv.M34, inv.M44
                        };

                        string parentName = (bone.ParentIndex >= 0 && bone.ParentIndex < model.Skeleton.Bones.Count)
                            ? model.Skeleton.Bones[bone.ParentIndex].Name
                            : "";

                        writer.AddJoint(bone.Name, parentName, transformArr, invArr);
                    }
                }

                writer.StartGeometrySection();
                foreach (GenericMesh mesh in model.Meshes)
                {
                    if (!mesh.Export || mesh.Vertices == null || mesh.Vertices.Count == 0) continue;
                    mesh.MakeTriangles();
                    if (mesh.Triangles == null || mesh.Triangles.Count == 0) continue;

                    writer.StartGeometryMesh(mesh.Name);
                    if (!string.IsNullOrEmpty(mesh.MaterialName) && model.MaterialBank.ContainsKey(mesh.MaterialName))
                        writer.CurrentMaterial = mesh.MaterialName;
                    else
                        writer.CurrentMaterial = null;

                    List<float> pos = new List<float>(mesh.Vertices.Count * 3);
                    List<float> nrm = new List<float>(mesh.Vertices.Count * 3);
                    List<float> uv0 = new List<float>(mesh.Vertices.Count * 2);
                    List<float> clr = new List<float>(mesh.Vertices.Count * 4);
                    List<float[]> weights = new List<float[]>(mesh.Vertices.Count);
                    List<int[]> indices = new List<int[]>(mesh.Vertices.Count);

                    foreach (GenericVertex v in mesh.Vertices)
                    {
                        pos.Add(v.Pos.X); pos.Add(v.Pos.Y); pos.Add(v.Pos.Z);
                        nrm.Add(v.Nrm.X); nrm.Add(v.Nrm.Y); nrm.Add(v.Nrm.Z);
                        clr.Add(v.Clr.X); clr.Add(v.Clr.Y); clr.Add(v.Clr.Z); clr.Add(v.Clr.W);
                        uv0.Add(v.UV0.X); uv0.Add(1f - v.UV0.Y);

                        List<float> vw = new List<float>(4);
                        List<int> vb = new List<int>(4);
                        for (int j = 0; j < 4; j++)
                        {
                            if (v.Weights[j] > 0f && oldToNew.ContainsKey((int)v.Bones[j]))
                            {
                                vw.Add(v.Weights[j]);
                                vb.Add(oldToNew[(int)v.Bones[j]]);
                            }
                        }
                        weights.Add(vw.ToArray());
                        indices.Add(vb.ToArray());
                    }

                    uint[] triIndices = mesh.Triangles.ToArray();
                    writer.WriteGeometrySource(mesh.Name, DAEWriter.VERTEX_SEMANTIC.POSITION, pos.ToArray(), triIndices, -1);
                    writer.WriteGeometrySource(mesh.Name, DAEWriter.VERTEX_SEMANTIC.NORMAL, nrm.ToArray(), triIndices, -1);
                    writer.WriteGeometrySource(mesh.Name, DAEWriter.VERTEX_SEMANTIC.TEXCOORD, uv0.ToArray(), triIndices, -1);
                    writer.WriteGeometrySource(mesh.Name, DAEWriter.VERTEX_SEMANTIC.COLOR, clr.ToArray(), triIndices, -1);
                    writer.AttachGeometryController(indices, weights);
                    writer.EndGeometryMesh();
                }
                writer.EndGeometrySection();
            }

            if (log != null) log("Exported static COLLADA DAE: " + filePath);
        }

        private static string ExtractArmatureNodeOuterXml(string daeXml)
        {
            XmlDocument doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.LoadXml(daeXml);

            XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("c", "http://www.collada.org/2005/11/COLLADASchema");

            XmlNode armNode = doc.SelectSingleNode("//c:visual_scene/c:node[@id='Armature']", nsmgr);
            if (armNode == null)
                armNode = doc.SelectSingleNode("//node[@id='Armature']");
            if (armNode == null)
                throw new InvalidDataException("Could not locate <node id='Armature'> in COLLADA file.");

            string outer = armNode.OuterXml;
            outer = outer.Replace(" xmlns=\"http://www.collada.org/2005/11/COLLADASchema\"", "");
            return outer;
        }

        private static void ResetSkeleton(GenericSkeleton skeleton)
        {
            if (skeleton == null || skeleton.Bones == null) return;
            Matrix4 id = Matrix4.Identity;
            foreach (GenericBone b in skeleton.Bones)
            {
                b.AnimatedTransform = id;
            }
        }

        private static string BuildLibraryAnimationsXml(GenericSkeleton skeleton, GenericAnimation anim, float fps, string label)
        {
            int frameCount = anim.FrameCount;
            if (frameCount <= 0) throw new InvalidDataException("Animation '" + label + "' has FrameCount=" + frameCount);

            List<GenericBone> bones = skeleton.Bones;
            StringBuilder timeSb = new StringBuilder(frameCount * 10);
            StringBuilder interpSb = new StringBuilder(frameCount * 7);
            StringBuilder[] boneMatSbs = new StringBuilder[bones.Count];
            for (int bi = 0; bi < bones.Count; bi++)
            {
                boneMatSbs[bi] = new StringBuilder(frameCount * 128);
            }

            for (int f = 0; f < frameCount; f++)
            {
                double t = ((double)f) / (double)fps;
                if (f > 0)
                {
                    timeSb.Append(' ');
                    interpSb.Append(' ');
                }
                timeSb.Append(t.ToString("0.######", CultureInfo.InvariantCulture));
                interpSb.Append("LINEAR");

                ResetSkeleton(skeleton);
                anim.UpdateSkeleton((float)f, skeleton);

                for (int bi = 0; bi < bones.Count; bi++)
                {
                    Matrix4 m = bones[bi].GetTransform(true);
                    float[] vals = new float[]
                    {
                        m.M11, m.M21, m.M31, m.M41,
                        m.M12, m.M22, m.M32, m.M42,
                        m.M13, m.M23, m.M33, m.M43,
                        m.M14, m.M24, m.M34, m.M44
                    };

                    StringBuilder sb = boneMatSbs[bi];
                    if (f > 0) sb.Append(' ');
                    for (int k = 0; k < 16; k++)
                    {
                        float v = vals[k];
                        if (float.IsNaN(v) || float.IsInfinity(v))
                        {
                            throw new InvalidDataException(string.Format("Invalid float (NaN/Infinity) in {0} frame {1} bone {2}", label, f, bones[bi].Name));
                        }
                        if (k > 0) sb.Append(' ');
                        if (Math.Abs(v) < 1e-6f) v = 0f;
                        sb.Append(v.ToString("0.######", CultureInfo.InvariantCulture));
                    }
                }
            }

            string timeStr = timeSb.ToString();
            string interpStr = interpSb.ToString();
            string escapedAnimName = EscapeXmlAttr(anim.Name);
            int matFloatCount = frameCount * 16;

            StringBuilder libSb = new StringBuilder(frameCount * bones.Count * 130 + 4096);
            libSb.AppendLine("  <library_animations>");
            for (int bi = 0; bi < bones.Count; bi++)
            {
                string bName = bones[bi].Name;
                string animId = "Anim_" + bName;
                string escapedId = EscapeXmlAttr(animId);
                string escapedTarget = EscapeXmlAttr("Armature_" + bName + "/transform");

                libSb.AppendLine(string.Format("    <animation id=\"{0}\" name=\"{1}\">", escapedId, escapedAnimName));
                libSb.AppendLine(string.Format("      <source id=\"{0}-input\">", escapedId));
                libSb.AppendLine(string.Format("        <float_array id=\"{0}-input-array\" count=\"{1}\">{2}</float_array>", escapedId, frameCount, timeStr));
                libSb.AppendLine("        <technique_common>");
                libSb.AppendLine(string.Format("          <accessor source=\"#{0}-input-array\" count=\"{1}\" stride=\"1\">", escapedId, frameCount));
                libSb.AppendLine("            <param name=\"TIME\" type=\"float\" />");
                libSb.AppendLine("          </accessor>");
                libSb.AppendLine("        </technique_common>");
                libSb.AppendLine("      </source>");
                libSb.AppendLine(string.Format("      <source id=\"{0}-output\">", escapedId));
                libSb.AppendLine(string.Format("        <float_array id=\"{0}-output-array\" count=\"{1}\">{2}</float_array>", escapedId, matFloatCount, boneMatSbs[bi].ToString()));
                libSb.AppendLine("        <technique_common>");
                libSb.AppendLine(string.Format("          <accessor source=\"#{0}-output-array\" count=\"{1}\" stride=\"16\">", escapedId, frameCount));
                libSb.AppendLine("            <param name=\"TRANSFORM\" type=\"float4x4\" />");
                libSb.AppendLine("          </accessor>");
                libSb.AppendLine("        </technique_common>");
                libSb.AppendLine("      </source>");
                libSb.AppendLine(string.Format("      <source id=\"{0}-interpolation\">", escapedId));
                libSb.AppendLine(string.Format("        <Name_array id=\"{0}-interpolation-array\" count=\"{1}\">{2}</Name_array>", escapedId, frameCount, interpStr));
                libSb.AppendLine("        <technique_common>");
                libSb.AppendLine(string.Format("          <accessor source=\"#{0}-interpolation-array\" count=\"{1}\" stride=\"1\">", escapedId, frameCount));
                libSb.AppendLine("            <param name=\"INTERPOLATION\" type=\"name\" />");
                libSb.AppendLine("          </accessor>");
                libSb.AppendLine("        </technique_common>");
                libSb.AppendLine("      </source>");
                libSb.AppendLine(string.Format("      <sampler id=\"{0}-sampler\">", escapedId));
                libSb.AppendLine(string.Format("        <input semantic=\"INPUT\" source=\"#{0}-input\" />", escapedId));
                libSb.AppendLine(string.Format("        <input semantic=\"OUTPUT\" source=\"#{0}-output\" />", escapedId));
                libSb.AppendLine(string.Format("        <input semantic=\"INTERPOLATION\" source=\"#{0}-interpolation\" />", escapedId));
                libSb.AppendLine("      </sampler>");
                libSb.AppendLine(string.Format("      <channel source=\"#{0}-sampler\" target=\"{1}\" />", escapedId, escapedTarget));
                libSb.AppendLine("    </animation>");
            }
            libSb.AppendLine("  </library_animations>");
            return libSb.ToString();
        }

        private static void RunBlenderExport(
            YKWCharacterGroup group,
            string baseDaePath,
            string tempSkelDir,
            List<Dictionary<string, string>> manifestItems,
            string outDir,
            ExportOptions options,
            Action<string> log,
            Action advanceProgress)
        {
            string manifestPath = Path.Combine(tempSkelDir, "manifest.json");
            StringBuilder jsonSb = new StringBuilder();
            jsonSb.AppendLine("[");
            for (int i = 0; i < manifestItems.Count; i++)
            {
                Dictionary<string, string> it = manifestItems[i];
                jsonSb.Append("  {\"action_name\": \"");
                jsonSb.Append(EscapeJson(it["action_name"]));
                jsonSb.Append("\", \"skel_dae\": \"");
                jsonSb.Append(EscapeJson(it["skel_dae"]));
                jsonSb.Append("\"}");
                if (i + 1 < manifestItems.Count) jsonSb.Append(',');
                jsonSb.AppendLine();
            }
            jsonSb.AppendLine("]");
            File.WriteAllText(manifestPath, jsonSb.ToString(), Utf8NoBom);

            string glbOut = Path.Combine(outDir, group.Prefix + "_animated.glb");
            string fbxOut = Path.Combine(outDir, group.Prefix + "_animated.fbx");
            string blendOut = Path.Combine(outDir, group.Prefix + "_animated.blend");

            string pyPath = Path.Combine(tempSkelDir, "run_blender.py");
            StringBuilder pySb = new StringBuilder();
            pySb.AppendLine("import json, os, bpy");
            pySb.AppendLine(string.Format("base_dae = r'''{0}'''", baseDaePath));
            pySb.AppendLine(string.Format("manifest_path = r'''{0}'''", manifestPath));
            pySb.AppendLine(string.Format("glb_out = r'''{0}'''", glbOut));
            pySb.AppendLine(string.Format("fbx_out = r'''{0}'''", fbxOut));
            pySb.AppendLine(string.Format("blend_out = r'''{0}'''", blendOut));
            pySb.AppendLine(string.Format("do_glb = {0}", options.ExportGlb ? "True" : "False"));
            pySb.AppendLine(string.Format("do_fbx = {0}", options.ExportFbx ? "True" : "False"));
            pySb.AppendLine(string.Format("do_blend = {0}", options.ExportBlend ? "True" : "False"));
            pySb.AppendLine(string.Format("fps_val = {0}", ((int)Math.Round(options.Fps)).ToString(CultureInfo.InvariantCulture)));
            pySb.AppendLine();
            pySb.AppendLine("with open(manifest_path, 'r', encoding='utf-8') as f:");
            pySb.AppendLine("    manifest = json.load(f)");
            pySb.AppendLine();
            pySb.AppendLine("bpy.ops.wm.read_factory_settings(use_empty=True)");
            pySb.AppendLine("bpy.context.scene.render.fps = fps_val");
            pySb.AppendLine("bpy.context.scene.render.fps_base = 1.0");
            pySb.AppendLine();
            pySb.AppendLine("bpy.ops.wm.collada_import(filepath=base_dae)");
            pySb.AppendLine("armatures = [o for o in bpy.data.objects if o.type == 'ARMATURE']");
            pySb.AppendLine("if not armatures:");
            pySb.AppendLine("    raise RuntimeError('No Armature imported from base DAE')");
            pySb.AppendLine("main_arm = armatures[0]");
            pySb.AppendLine("if not main_arm.animation_data:");
            pySb.AppendLine("    main_arm.animation_data_create()");
            pySb.AppendLine("print('YKW_PROGRESS: LOG Imported base model (' + str(len(main_arm.data.bones)) + ' bones)', flush=True)");
            pySb.AppendLine();
            pySb.AppendLine("imported_actions = []");
            pySb.AppendLine("preferred_idle = None");
            pySb.AppendLine("for idx, entry in enumerate(manifest):");
            pySb.AppendLine("    skel_dae = entry['skel_dae']");
            pySb.AppendLine("    action_name = entry['action_name']");
            pySb.AppendLine("    before_objs = set(bpy.data.objects)");
            pySb.AppendLine("    before_acts = set(bpy.data.actions)");
            pySb.AppendLine("    bpy.ops.wm.collada_import(filepath=skel_dae)");
            pySb.AppendLine("    new_objs = [o for o in bpy.data.objects if o not in before_objs]");
            pySb.AppendLine("    temp_arms = [o for o in new_objs if o.type == 'ARMATURE']");
            pySb.AppendLine("    if not temp_arms:");
            pySb.AppendLine("        raise RuntimeError('No Armature in ' + skel_dae)");
            pySb.AppendLine("    temp_arm = temp_arms[0]");
            pySb.AppendLine("    act = temp_arm.animation_data.action if temp_arm.animation_data else None");
            pySb.AppendLine("    if act is None:");
            pySb.AppendLine("        new_acts = [a for a in bpy.data.actions if a not in before_acts]");
            pySb.AppendLine("        if new_acts: act = new_acts[0]");
            pySb.AppendLine("    if act is None:");
            pySb.AppendLine("        raise RuntimeError('No Action imported for ' + action_name)");
            pySb.AppendLine("    act.name = action_name");
            pySb.AppendLine("    act.use_fake_user = True");
            pySb.AppendLine("    imported_actions.append(act)");
            pySb.AppendLine("    if ('立ち1L' in action_name or 'アイドリング' in action_name) and preferred_idle is None:");
            pySb.AppendLine("        preferred_idle = act");
            pySb.AppendLine("    for o in new_objs:");
            pySb.AppendLine("        data = o.data");
            pySb.AppendLine("        bpy.data.objects.remove(o, do_unlink=True)");
            pySb.AppendLine("        if data is not None and data.users == 0 and isinstance(data, bpy.types.Armature):");
            pySb.AppendLine("            bpy.data.armatures.remove(data)");
            pySb.AppendLine("    track = main_arm.animation_data.nla_tracks.new()");
            pySb.AppendLine("    track.name = action_name");
            pySb.AppendLine("    strip = track.strips.new(action_name, 1, act)");
            pySb.AppendLine("    if hasattr(strip, 'action_slot') and hasattr(act, 'slots') and len(act.slots) > 0:");
            pySb.AppendLine("        strip.action_slot = act.slots[0]");
            pySb.AppendLine("    track.mute = False");
            pySb.AppendLine("    if (idx + 1) == 1 or (idx + 1) % 5 == 0 or (idx + 1) == len(manifest):");
            pySb.AppendLine("        print('YKW_PROGRESS: LOG Blender imported ' + str(idx + 1) + '/' + str(len(manifest)) + ': ' + action_name, flush=True)");
            pySb.AppendLine("    else:");
            pySb.AppendLine("        print('YKW_PROGRESS: STEP', flush=True)");
            pySb.AppendLine();
            pySb.AppendLine("bpy.ops.file.pack_all()");
            pySb.AppendLine("main_arm.animation_data.action = None");
            pySb.AppendLine();
            pySb.AppendLine("if do_glb:");
            pySb.AppendLine("    print('YKW_PROGRESS: LOG Exporting GLB...', flush=True)");
            pySb.AppendLine("    bpy.ops.export_scene.gltf(filepath=glb_out, export_format='GLB')");
            pySb.AppendLine("    print('Exported GLB:', glb_out, flush=True)");
            pySb.AppendLine();
            pySb.AppendLine("if do_fbx:");
            pySb.AppendLine("    print('YKW_PROGRESS: LOG Exporting FBX (baking ' + str(len(manifest)) + ' animations)...', flush=True)");
            pySb.AppendLine("    bpy.ops.export_scene.fbx(filepath=fbx_out, path_mode='COPY', embed_textures=True, bake_anim=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False)");
            pySb.AppendLine("    print('Exported FBX:', fbx_out, flush=True)");
            pySb.AppendLine();
            pySb.AppendLine("if do_blend:");
            pySb.AppendLine("    print('YKW_PROGRESS: LOG Saving BLEND...', flush=True)");
            pySb.AppendLine("    active_act = preferred_idle if preferred_idle is not None else (imported_actions[0] if imported_actions else None)");
            pySb.AppendLine("    if active_act is not None:");
            pySb.AppendLine("        main_arm.animation_data.action = active_act");
            pySb.AppendLine("        if hasattr(main_arm.animation_data, 'action_slot') and hasattr(active_act, 'slots') and len(active_act.slots) > 0:");
            pySb.AppendLine("            main_arm.animation_data.action_slot = active_act.slots[0]");
            pySb.AppendLine("        bpy.context.scene.frame_start = int(active_act.frame_range[0])");
            pySb.AppendLine("        bpy.context.scene.frame_end = int(active_act.frame_range[1])");
            pySb.AppendLine("    bpy.ops.wm.save_as_mainfile(filepath=blend_out)");
            pySb.AppendLine("    blend1 = blend_out + '1'");
            pySb.AppendLine("    if os.path.exists(blend1): os.remove(blend1)");
            pySb.AppendLine("    print('Saved BLEND:', blend_out, flush=True)");

            File.WriteAllText(pyPath, pySb.ToString(), Utf8NoBom);

            if (log != null) log("Launching Blender (" + options.BlenderExe + ")...");

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = options.BlenderExe;
            psi.Arguments = string.Format("--background --python \"{0}\"", pyPath);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            object syncLock = new object();
            StringBuilder stderrSb = new StringBuilder();
            StringBuilder tailStdoutSb = new StringBuilder();

            using (Process proc = new Process())
            {
                proc.StartInfo = psi;
                proc.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    string line = e.Data.Trim();
                    if (line.Length == 0) return;
                    if (line.StartsWith("YKW_PROGRESS: ", StringComparison.Ordinal))
                    {
                        if (advanceProgress != null) advanceProgress();
                        if (line.StartsWith("YKW_PROGRESS: LOG ", StringComparison.Ordinal) && log != null)
                        {
                            log("  [Blender] " + line.Substring("YKW_PROGRESS: LOG ".Length));
                        }
                    }
                    else if (line.StartsWith("Exported ", StringComparison.Ordinal) ||
                             line.StartsWith("Saved ", StringComparison.Ordinal))
                    {
                        if (log != null) log("  [Blender] " + line);
                    }
                    else
                    {
                        lock (syncLock)
                        {
                            if (tailStdoutSb.Length > 8192) tailStdoutSb.Remove(0, 4096);
                            tailStdoutSb.AppendLine(line);
                        }
                    }
                };
                proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (syncLock)
                    {
                        if (stderrSb.Length < 16384)
                            stderrSb.AppendLine(e.Data);
                    }
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                {
                    string errText;
                    lock (syncLock)
                    {
                        errText = stderrSb.ToString() + "\n" + tailStdoutSb.ToString();
                    }
                    throw new Exception("Blender exited with code " + proc.ExitCode + "\n" + errText);
                }
            }
        }

        private static string EscapeXmlAttr(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if (ch == '\\') sb.Append("\\\\");
                else if (ch == '"') sb.Append("\\\"");
                else if (ch == '\r') sb.Append("\\r");
                else if (ch == '\n') sb.Append("\\n");
                else if (ch == '\t') sb.Append("\\t");
                else if (ch < 32 || ch > 126) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(ch);
            }
            return sb.ToString();
        }
    }

    internal sealed class YKWUniversalForm : Form
    {
        private TextBox txtInput;
        private TextBox txtOutput;
        private TextBox txtBlender;
        private ListBox lstModels;
        private ListView lvAnimations;
        private CheckBox chkGlb;
        private CheckBox chkFbx;
        private CheckBox chkBlend;
        private CheckBox chkAnimDae;
        private CheckBox chkStaticDae;
        private CheckBox chkSmd;
        private CheckBox chkPng;
        private CheckBox chkRaw;
        private NumericUpDown numFps;
        private ProgressBar progressBar;
        private TextBox txtLog;
        private Button btnExport;
        private Button btnOpenOut;

        private List<YKWCharacterGroup> _groups = new List<YKWCharacterGroup>();
        private bool _busy;

        public YKWUniversalForm(string initialPath)
        {
            InitializeUi();

            if (!string.IsNullOrEmpty(initialPath))
            {
                LoadPath(initialPath);
            }
            else
            {
                string defaultDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "x561000");
                if (!Directory.Exists(defaultDir))
                    defaultDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "x561000"));
                if (Directory.Exists(defaultDir))
                    LoadPath(defaultDir);
            }
        }

        private void InitializeUi()
        {
            Text = "Yo-kai Watch Universal Model & Animation Exporter";
            Size = new Size(1060, 740);
            MinimumSize = new Size(920, 620);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 5;
            mainLayout.Padding = new Padding(10);
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 62f));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));
            Controls.Add(mainLayout);

            // Row 0: Paths panel
            TableLayoutPanel pathGrid = new TableLayoutPanel();
            pathGrid.Dock = DockStyle.Top;
            pathGrid.AutoSize = true;
            pathGrid.ColumnCount = 4;
            pathGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            pathGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            txtInput = new TextBox(); txtInput.Dock = DockStyle.Fill;
            txtOutput = new TextBox(); txtOutput.Dock = DockStyle.Fill;
            txtBlender = new TextBox(); txtBlender.Dock = DockStyle.Fill;
            txtBlender.Text = YKWExporter.FindBlenderExecutable();

            Button btnBrowseFolder = new Button(); btnBrowseFolder.Text = "Folder..."; btnBrowseFolder.AutoSize = true;
            btnBrowseFolder.Click += delegate { BrowseInputFolder(); };
            Button btnBrowseFiles = new Button(); btnBrowseFiles.Text = ".xc File..."; btnBrowseFiles.AutoSize = true;
            btnBrowseFiles.Click += delegate { BrowseInputFiles(); };

            Button btnBrowseOut = new Button(); btnBrowseOut.Text = "Browse..."; btnBrowseOut.AutoSize = true;
            btnBrowseOut.Click += delegate { BrowseOutputFolder(); };

            Button btnBrowseBlender = new Button(); btnBrowseBlender.Text = "Browse..."; btnBrowseBlender.AutoSize = true;
            btnBrowseBlender.Click += delegate { BrowseBlender(); };

            pathGrid.Controls.Add(MakeLabel("Input Folder / File (or Drag & Drop):"), 0, 0);
            pathGrid.Controls.Add(txtInput, 1, 0);
            pathGrid.Controls.Add(btnBrowseFolder, 2, 0);
            pathGrid.Controls.Add(btnBrowseFiles, 3, 0);

            pathGrid.Controls.Add(MakeLabel("Output Folder:"), 0, 1);
            pathGrid.Controls.Add(txtOutput, 1, 1);
            pathGrid.Controls.Add(btnBrowseOut, 2, 1);

            pathGrid.Controls.Add(MakeLabel("Blender Executable (GLB/FBX/BLEND):"), 0, 2);
            pathGrid.Controls.Add(txtBlender, 1, 2);
            pathGrid.Controls.Add(btnBrowseBlender, 2, 2);

            mainLayout.Controls.Add(pathGrid, 0, 0);

            // Row 1: Split models + animations
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.SplitterDistance = 300;

            GroupBox grpModels = new GroupBox();
            grpModels.Text = "Detected Character Models";
            grpModels.Dock = DockStyle.Fill;
            lstModels = new ListBox();
            lstModels.Dock = DockStyle.Fill;
            lstModels.SelectedIndexChanged += delegate { RefreshAnimationList(); };
            grpModels.Controls.Add(lstModels);
            split.Panel1.Controls.Add(grpModels);

            GroupBox grpAnims = new GroupBox();
            grpAnims.Text = "Animations";
            grpAnims.Dock = DockStyle.Fill;

            TableLayoutPanel animPanel = new TableLayoutPanel();
            animPanel.Dock = DockStyle.Fill;
            animPanel.RowCount = 2;
            animPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            animPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            FlowLayoutPanel animBtns = new FlowLayoutPanel();
            animBtns.Dock = DockStyle.Top;
            animBtns.AutoSize = true;
            Button btnSelAll = new Button(); btnSelAll.Text = "Select All"; btnSelAll.AutoSize = true;
            btnSelAll.Click += delegate { SetAllAnimsChecked(true); };
            Button btnSelNone = new Button(); btnSelNone.Text = "Select None"; btnSelNone.AutoSize = true;
            btnSelNone.Click += delegate { SetAllAnimsChecked(false); };
            Button btnSelSub = new Button(); btnSelSub.Text = "Sub-Animations Only"; btnSelSub.AutoSize = true;
            btnSelSub.Click += delegate { FilterAnimsChecked(false); };
            Button btnSelReel = new Button(); btnSelReel.Text = "Full Reels Only (out_00)"; btnSelReel.AutoSize = true;
            btnSelReel.Click += delegate { FilterAnimsChecked(true); };
            animBtns.Controls.AddRange(new Control[] { btnSelAll, btnSelNone, btnSelSub, btnSelReel });

            lvAnimations = new ListView();
            lvAnimations.Dock = DockStyle.Fill;
            lvAnimations.View = View.Details;
            lvAnimations.CheckBoxes = true;
            lvAnimations.FullRowSelect = true;
            lvAnimations.GridLines = true;
            lvAnimations.Columns.Add("Pack", 65);
            lvAnimations.Columns.Add("#", 40);
            lvAnimations.Columns.Add("Name", 220);
            lvAnimations.Columns.Add("English Hint", 150);
            lvAnimations.Columns.Add("Frames", 60);
            lvAnimations.Columns.Add("Duration", 75);

            animPanel.Controls.Add(animBtns, 0, 0);
            animPanel.Controls.Add(lvAnimations, 0, 1);
            grpAnims.Controls.Add(animPanel);
            split.Panel2.Controls.Add(grpAnims);

            mainLayout.Controls.Add(split, 0, 1);

            // Row 2: Format options
            GroupBox grpFormats = new GroupBox();
            grpFormats.Text = "Export Formats & Settings";
            grpFormats.Dock = DockStyle.Top;
            grpFormats.AutoSize = true;

            FlowLayoutPanel fmtFlow = new FlowLayoutPanel();
            fmtFlow.Dock = DockStyle.Fill;
            fmtFlow.AutoSize = true;

            chkGlb = MakeCheck("GLB (.glb)", true);
            chkFbx = MakeCheck("FBX (.fbx)", true);
            chkBlend = MakeCheck("Blender (.blend)", true);
            chkAnimDae = MakeCheck("Animated COLLADA (.dae)", true);
            chkStaticDae = MakeCheck("Static COLLADA (.dae)", true);
            chkSmd = MakeCheck("Valve SMD (.smd)", true);
            chkPng = MakeCheck("Textures (.png)", true);
            chkRaw = MakeCheck("Extract Raw Archive Files", false);

            Label lblFps = new Label();
            lblFps.Text = "FPS:";
            lblFps.AutoSize = true;
            lblFps.Margin = new Padding(12, 6, 2, 0);

            numFps = new NumericUpDown();
            numFps.Minimum = 1;
            numFps.Maximum = 120;
            numFps.Value = 30;
            numFps.Width = 55;

            fmtFlow.Controls.AddRange(new Control[]
            {
                chkGlb, chkFbx, chkBlend, chkAnimDae, chkStaticDae, chkSmd, chkPng, chkRaw, lblFps, numFps
            });
            grpFormats.Controls.Add(fmtFlow);
            mainLayout.Controls.Add(grpFormats, 0, 2);

            // Row 3: Action buttons & progress bar
            TableLayoutPanel actionBar = new TableLayoutPanel();
            actionBar.Dock = DockStyle.Top;
            actionBar.AutoSize = true;
            actionBar.ColumnCount = 3;
            actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            progressBar = new ProgressBar();
            progressBar.Dock = DockStyle.Fill;
            progressBar.Height = 26;

            btnExport = new Button();
            btnExport.Text = "Export Selected";
            btnExport.Width = 140;
            btnExport.Height = 28;
            btnExport.Click += delegate { StartExport(); };

            btnOpenOut = new Button();
            btnOpenOut.Text = "Open Output Folder";
            btnOpenOut.Width = 140;
            btnOpenOut.Height = 28;
            btnOpenOut.Click += delegate { OpenOutputFolder(); };

            actionBar.Controls.Add(progressBar, 0, 0);
            actionBar.Controls.Add(btnExport, 1, 0);
            actionBar.Controls.Add(btnOpenOut, 2, 0);
            mainLayout.Controls.Add(actionBar, 0, 3);

            // Row 4: Log
            GroupBox grpLog = new GroupBox();
            grpLog.Text = "Log";
            grpLog.Dock = DockStyle.Fill;
            txtLog = new TextBox();
            txtLog.Dock = DockStyle.Fill;
            txtLog.Multiline = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.ReadOnly = true;
            txtLog.Font = new Font("Consolas", 9f);
            grpLog.Controls.Add(txtLog);
            mainLayout.Controls.Add(grpLog, 0, 4);
        }

        private static Label MakeLabel(string text)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.AutoSize = true;
            lbl.Anchor = AnchorStyles.Left;
            lbl.Margin = new Padding(3, 6, 6, 3);
            return lbl;
        }

        private static CheckBox MakeCheck(string text, bool isChecked)
        {
            CheckBox cb = new CheckBox();
            cb.Text = text;
            cb.Checked = isChecked;
            cb.AutoSize = true;
            cb.Margin = new Padding(6, 4, 6, 4);
            return cb;
        }

        private void AppendLog(string msg)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), msg);
                return;
            }
            txtLog.AppendText(msg + Environment.NewLine);
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (_busy) return;
            string[] dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (dropped != null && dropped.Length > 0)
            {
                LoadPaths(dropped);
            }
        }

        private void BrowseInputFolder()
        {
            if (_busy) return;
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                if (Directory.Exists(txtInput.Text)) dlg.SelectedPath = txtInput.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    LoadPath(dlg.SelectedPath);
                }
            }
        }

        private void BrowseInputFiles()
        {
            if (_busy) return;
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Multiselect = true;
                dlg.Filter = "Level-5 Archives (*.xc;*.xv)|*.xc;*.xv|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.FileNames.Length > 0)
                {
                    LoadPaths(dlg.FileNames);
                }
            }
        }

        private void BrowseOutputFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                if (Directory.Exists(txtOutput.Text)) dlg.SelectedPath = txtOutput.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    txtOutput.Text = dlg.SelectedPath;
            }
        }

        private void BrowseBlender()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Blender Executable (blender.exe)|blender.exe|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    txtBlender.Text = dlg.FileName;
            }
        }

        public void LoadPath(string path)
        {
            LoadPaths(new string[] { path });
        }

        public void LoadPaths(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            string primary = paths[0];
            string dir = Directory.Exists(primary) ? primary : Path.GetDirectoryName(primary);
            txtInput.Text = dir;
            if (string.IsNullOrEmpty(txtOutput.Text) || Directory.Exists(dir))
                txtOutput.Text = dir;

            txtLog.Clear();
            AppendLog("Scanning: " + string.Join(", ", paths));
            _groups = YKWScanner.Scan(paths, AppendLog);

            lstModels.Items.Clear();
            foreach (YKWCharacterGroup g in _groups)
            {
                lstModels.Items.Add(g.DisplayName);
            }

            if (_groups.Count > 0)
            {
                lstModels.SelectedIndex = 0;
                AppendLog(string.Format("Loaded {0} model group(s).", _groups.Count));
            }
            else
            {
                lvAnimations.Items.Clear();
                AppendLog("No Yo-kai Watch models (.xc/.xv with .prm/.mbn) found.");
            }
        }

        private void RefreshAnimationList()
        {
            lvAnimations.Items.Clear();
            int idx = lstModels.SelectedIndex;
            if (idx < 0 || idx >= _groups.Count) return;

            YKWCharacterGroup grp = _groups[idx];
            lvAnimations.BeginUpdate();
            foreach (YKWAnimationEntry a in grp.Animations)
            {
                double secs = (double)a.Animation.FrameCount / (double)numFps.Value;
                ListViewItem lvi = new ListViewItem(a.Pack);
                lvi.SubItems.Add(a.Index.ToString("D2", CultureInfo.InvariantCulture));
                lvi.SubItems.Add(a.Animation.Name);
                lvi.SubItems.Add(a.EnglishHint);
                lvi.SubItems.Add(a.Animation.FrameCount.ToString(CultureInfo.InvariantCulture));
                lvi.SubItems.Add(secs.ToString("0.00", CultureInfo.InvariantCulture) + "s");
                lvi.Checked = true;
                lvi.Tag = a;
                lvAnimations.Items.Add(lvi);
            }
            lvAnimations.EndUpdate();
        }

        private void SetAllAnimsChecked(bool check)
        {
            foreach (ListViewItem item in lvAnimations.Items) item.Checked = check;
        }

        private void FilterAnimsChecked(bool reelsOnly)
        {
            foreach (ListViewItem item in lvAnimations.Items)
            {
                YKWAnimationEntry a = item.Tag as YKWAnimationEntry;
                if (a != null) item.Checked = reelsOnly ? a.IsFullReel : !a.IsFullReel;
            }
        }

        private void StartExport()
        {
            if (_busy) return;
            int idx = lstModels.SelectedIndex;
            if (idx < 0 || idx >= _groups.Count)
            {
                MessageBox.Show(this, "Please select a character model first.", "YKWModelTool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            YKWCharacterGroup grp = _groups[idx];
            string outDir = txtOutput.Text.Trim();
            if (string.IsNullOrEmpty(outDir))
            {
                MessageBox.Show(this, "Please choose an output folder.", "YKWModelTool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<YKWAnimationEntry> selectedAnims = new List<YKWAnimationEntry>();
            foreach (ListViewItem item in lvAnimations.Items)
            {
                if (item.Checked && item.Tag is YKWAnimationEntry)
                    selectedAnims.Add((YKWAnimationEntry)item.Tag);
            }

            ExportOptions opts = new ExportOptions();
            opts.ExportGlb = chkGlb.Checked;
            opts.ExportFbx = chkFbx.Checked;
            opts.ExportBlend = chkBlend.Checked;
            opts.ExportAnimDae = chkAnimDae.Checked;
            opts.ExportStaticDae = chkStaticDae.Checked;
            opts.ExportSmd = chkSmd.Checked;
            opts.ExportPng = chkPng.Checked;
            opts.ExtractRaw = chkRaw.Checked;
            opts.Fps = (float)numFps.Value;
            opts.BlenderExe = txtBlender.Text.Trim();

            if ((opts.ExportGlb || opts.ExportFbx || opts.ExportBlend) &&
                (string.IsNullOrEmpty(opts.BlenderExe) || !File.Exists(opts.BlenderExe)))
            {
                MessageBox.Show(this, "Blender executable not found. Browse for blender.exe or uncheck GLB/FBX/BLEND.", "YKWModelTool", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _busy = true;
            btnExport.Enabled = false;
            progressBar.Value = 0;
            AppendLog("=== Starting export for " + grp.Prefix + " ===");

            Thread worker = new Thread(delegate()
            {
                try
                {
                    YKWExporter.ExportGroup(
                        grp,
                        selectedAnims,
                        outDir,
                        opts,
                        AppendLog,
                        delegate(int cur, int tot)
                        {
                            if (!IsDisposed)
                            {
                                BeginInvoke(new Action(delegate()
                                {
                                    progressBar.Maximum = Math.Max(1, tot);
                                    progressBar.Value = Math.Min(cur, progressBar.Maximum);
                                }));
                            }
                        });

                    if (!IsDisposed)
                    {
                        BeginInvoke(new Action(delegate()
                        {
                            AppendLog("=== Export Complete! ===");
                            _busy = false;
                            btnExport.Enabled = true;
                            MessageBox.Show(this, "Export complete!\nOutput: " + outDir, "YKWModelTool", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }));
                    }
                }
                catch (Exception ex)
                {
                    if (!IsDisposed)
                    {
                        BeginInvoke(new Action(delegate()
                        {
                            AppendLog("ERROR: " + ex.ToString());
                            _busy = false;
                            btnExport.Enabled = true;
                            MessageBox.Show(this, "Export failed:\n" + ex.Message, "YKWModelTool", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }));
                    }
                }
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = true;
            worker.Start();
        }

        private void OpenOutputFolder()
        {
            string outDir = txtOutput.Text.Trim();
            if (Directory.Exists(outDir))
            {
                Process.Start("explorer.exe", outDir);
            }
        }
    }
}
