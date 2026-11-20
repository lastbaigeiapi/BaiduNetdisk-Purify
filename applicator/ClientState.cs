using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Purify
{
    public sealed class Manifest
    {
        public string installPath { get; set; }
        public string appVersion { get; set; }
        public string coreOriginal { get; set; }
        public string corePatched { get; set; }
        public string corePending { get; set; }
        public bool renamedUpdater { get; set; }
        public bool updaterConfigPatched { get; set; }
        public bool registryBackedUp { get; set; }
        public bool? downloadVideos { get; set; }
        public Dictionary<string, string> registeredAppValues { get; set; }
        public string scheme { get; set; }
        public string schemeFrom { get; set; }
        public string installedBy { get; set; }

        internal bool Matches(string hash)
        {
            return hash == coreOriginal || hash == corePatched || hash == corePending;
        }
    }

    public sealed class RemovedItem
    {
        public string rel { get; set; }
        public long bytes { get; set; }
        public string stage { get; set; }
    }

    internal sealed class ClientState
    {
        internal readonly ClientDirectory Client;
        internal readonly Manifest Record;
        internal readonly string CurrentHash;

        internal ClientState(ClientDirectory client)
        {
            Client = client;
            CurrentHash = Formats.Sha(client.Core);
            if (!File.Exists(client.Manifest))
                return;
            Record = Formats.Decode<Manifest>(File.ReadAllBytes(client.Manifest));
            if (Record == null || !Record.Matches(CurrentHash))
                throw new InvalidDataException("目前的 core.asar 与本机补丁记录不符。为避免把旧组件混入新客户端，请先检查官方客户端和备份。");
            if (!File.Exists(client.Original) || Formats.Sha(client.Original) != Record.coreOriginal)
                throw new InvalidDataException("原版 core.asar 备份缺失或 SHA-256 校验失败。");
        }

        internal string OriginalHash { get { return Record == null ? CurrentHash : Record.coreOriginal; } }
        internal string Source { get { return Record == null ? Client.Core : Client.Original; } }

        internal void CheckUnchanged()
        {
            if (Formats.Sha(Client.Core) != CurrentHash)
                throw new IOException("百度网盘文件在准备过程中发生变化，请重新打开工具后重试。");
        }

        internal Manifest PrepareBackup(Scheme scheme, PatchSet rules)
        {
            var record = Record ?? new Manifest { installPath = Client.Root, coreOriginal = CurrentHash, installedBy = "applicator" };
            Directory.CreateDirectory(Client.Backup);
            if (Record == null && (!File.Exists(Client.Original) || Formats.Sha(Client.Original) != CurrentHash))
                SwapCopy(Client.Core, Client.Original);
            if (Formats.Sha(Client.Original) != record.coreOriginal)
                throw new IOException("原版文件备份验证失败。");
            record.appVersion = rules.target.appVersion;
            record.scheme = scheme.Entry.name;
            record.schemeFrom = scheme.IsLocal ? "embedded" : "online";
            record.corePending = rules.expectedOutputSha256;
            Save(record);
            return record;
        }

        internal void Save(Manifest record)
        {
            Formats.WriteJson(Client.Manifest, record);
        }

        internal void Archive()
        {
            File.Move(Client.Manifest, Client.Manifest + ".restored-" + Guid.NewGuid().ToString("N"));
        }

        internal void DisableUpdater(Manifest record)
        {
            string config = Client.Under(@"AutoUpdate\config.ini");
            if (File.Exists(config))
            {
                string content = File.ReadAllText(config);
                string changed = Regex.Replace(content, @"(?m)^(IsAutoUpdate[ \t]*=[ \t]*)1(?=[ \t]*\r?$)",
                    match => match.Groups[1].Value + "0");
                if (content != changed)
                {
                    string backup = Client.Under(@"nd_purify\backup\config.ini.orig");
                    if (!record.updaterConfigPatched || !File.Exists(backup))
                        SwapCopy(config, backup);
                    record.updaterConfigPatched = true;
                    Save(record);
                    string staged = config + ".purify-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        File.WriteAllText(staged, changed, Formats.Utf8);
                        File.Replace(staged, config, null);
                    }
                    finally { if (File.Exists(staged)) File.Delete(staged); }
                }
            }
            string executable = Client.Under(@"AutoUpdate\Autoupdate.exe");
            string disabled = Client.Under(@"AutoUpdate\Autoupdate.exe.nd-disabled");
            if (File.Exists(executable) && !File.Exists(disabled))
            {
                record.renamedUpdater = true;
                Save(record);
                File.Move(executable, disabled);
            }
        }

        internal void RestoreUpdater()
        {
            string config = Client.Under(@"nd_purify\backup\config.ini.orig");
            if (Record.updaterConfigPatched && File.Exists(config))
                SwapCopy(config, Client.Under(@"AutoUpdate\config.ini"));
            string active = Client.Under(@"AutoUpdate\Autoupdate.exe");
            string disabled = Client.Under(@"AutoUpdate\Autoupdate.exe.nd-disabled");
            if (Record.renamedUpdater && File.Exists(disabled) && !File.Exists(active))
                File.Move(disabled, active);
        }

        internal static void SwapCopy(string source, string target)
        {
            string staged = target + ".purify-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(source, staged);
                if (File.Exists(target))
                    File.Replace(staged, target, null);
                else
                    File.Move(staged, target);
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }

        internal List<KeyValuePair<string, bool>> InspectComponents(Scheme scheme)
        {
            var options = new List<KeyValuePair<string, bool>>();
            foreach (string relative in ComponentPaths(scheme))
            {
                bool removed = Exists(RemovedPath(relative));
                if (removed || Exists(Client.Under(relative)))
                    options.Add(new KeyValuePair<string, bool>(relative, removed));
            }
            return options;
        }

        internal HashSet<string> ComponentPaths(Scheme scheme)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in scheme.Components)
            {
                Client.Under(path);
                string relative = path.Replace('/', '\\');
                RemovedPath(relative);
                paths.Add(relative);
            }
            return paths;
        }

        private string RemovedPath(string relative)
        {
            return Client.Under(@"nd_purify\removed\" + relative);
        }

        private static bool Exists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        internal bool MoveComponents(IEnumerable<string> paths, Action<string> log)
        {
            var moves = ReadMoves();
            bool complete = true;
            foreach (string relative in paths)
            {
                string source = Client.Under(relative);
                string target = RemovedPath(relative);
                if (!Exists(source))
                    continue;
                if (Exists(target))
                {
                    log("组件与备份同时存在，已保留两个位置：" + relative);
                    complete = false;
                    continue;
                }
                if (!moves.Any(entry => string.Equals(entry.rel, relative, StringComparison.OrdinalIgnoreCase)))
                {
                    moves.Add(new RemovedItem { rel = relative, stage = "applicator", bytes = File.Exists(source) ? new FileInfo(source).Length : 0 });
                    Directory.CreateDirectory(Client.Store);
                    Formats.WriteJson(Client.MoveLog, moves);
                }
                if (TryMoveComponent(source, target, "无法移出组件，已保留：" + relative, log))
                    log("已移出组件：" + relative);
                else
                    complete = false;
            }
            return complete;
        }

        internal bool RestoreComponents(Action<string> log, ISet<string> paths = null)
        {
            var remaining = new List<RemovedItem>();
            bool complete = true;
            foreach (var move in ReadMoves())
            {
                if (paths != null && !paths.Contains(move.rel))
                {
                    remaining.Add(move);
                    continue;
                }
                string target = Client.Under(move.rel);
                string source = RemovedPath(move.rel);
                if (!Exists(source))
                    continue;
                if (Exists(target))
                {
                    remaining.Add(move);
                    log("当前位置已有同名文件，备份已保留：" + move.rel);
                    complete = false;
                    continue;
                }
                if (TryMoveComponent(source, target, "无法放回组件，备份已保留：" + move.rel, log))
                    log("已还原组件：" + move.rel);
                else
                {
                    remaining.Add(move);
                    complete = false;
                }
            }
            if (File.Exists(Client.MoveLog))
                Formats.WriteJson(Client.MoveLog, remaining);
            return complete;
        }

        private static bool TryMoveComponent(string source, string target, string failure, Action<string> log)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (Directory.Exists(source))
                    Directory.Move(source, target);
                else
                    File.Move(source, target);
                return true;
            }
            catch (IOException ex) { log(failure + " (" + ex.Message + ")"); }
            catch (UnauthorizedAccessException ex) { log(failure + " (" + ex.Message + ")"); }
            return false;
        }

        private List<RemovedItem> ReadMoves()
        {
            var moves = new List<RemovedItem>();
            if (File.Exists(Client.MoveLog))
                AddMoves(Formats.Decode<object>(File.ReadAllBytes(Client.MoveLog)), moves);
            return moves.GroupBy(move => move.rel, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
        }

        private static void AddMoves(object parsed, List<RemovedItem> moves)
        {
            var array = parsed as object[];
            if (array != null)
                foreach (object item in array) AddMoves(item, moves);
            var map = parsed as Dictionary<string, object>;
            if (map == null)
                return;
            object value;
            if (map.TryGetValue("rel", out value) && value is string)
            {
                object bytes, stage;
                map.TryGetValue("bytes", out bytes);
                map.TryGetValue("stage", out stage);
                moves.Add(new RemovedItem { rel = ((string)value).Replace('/', '\\'), bytes = Convert.ToInt64(bytes), stage = stage as string });
            }
            else if (map.TryGetValue("value", out value))
                AddMoves(value, moves);
        }
    }
}
