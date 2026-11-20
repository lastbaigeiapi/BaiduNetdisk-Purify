using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Purify
{
    internal sealed class ApplyService
    {
        internal const string VideoPreviewConflict = "网盘内预览需要播放器组件。请取消勾选这些组件，或关闭「在网盘内预览视频」。";
        private static readonly HashSet<string> PlaybackComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            @"module\VastPlayer",
            @"module\BrowserEngine\BaiduNetdiskPlayerLaunch.exe",
            @"module\BrowserEngine\localplayer.dll",
            @"module\BrowserEngine\vastplayer.dll",
            @"module\BrowserEngine\videoipc.dll",
            @"module\BrowserEngine\resources\video_player.asar",
            @"module\BrowserEngine\resources\5bc5a1fc2a45d4cb1415284e9905af6b"
        };

        private readonly Action<string> log;
        private readonly bool configureDesktop;

        internal ApplyService(Action<string> log, bool configureDesktop)
        {
            this.log = log;
            this.configureDesktop = configureDesktop;
        }

        internal bool Apply(ClientDirectory client, Scheme scheme, IEnumerable<string> selections, bool removeOpenWith, bool downloadVideos)
        {
            var rules = downloadVideos ? scheme.VideoDownloadRules : scheme.Rules;
            if (rules == null)
                throw new InvalidOperationException("当前方案不支持切换视频打开方式。");
            string expectedOutput = rules.expectedOutputSha256;
            bool complete = true;
            Locked(client, delegate
            {
                var state = new ClientState(client);
                if (state.OriginalHash != scheme.Entry.coreSha256)
                    throw new IOException("客户端构建已变化，请重新检查兼容性。");
                var unselected = state.ComponentPaths(scheme);
                var selected = new HashSet<string>(selections, StringComparer.OrdinalIgnoreCase);
                if (!unselected.IsSupersetOf(selected))
                    throw new InvalidDataException("所选组件与匹配的方案不一致。");
                if (HasVideoPreviewConflict(scheme, selected, downloadVideos))
                    throw new InvalidOperationException(VideoPreviewConflict);
                unselected.ExceptWith(selected);

                string output = Path.Combine(Path.GetTempPath(), "purify-" + Guid.NewGuid().ToString("N") + ".asar");
                try
                {
                    if (state.CurrentHash != expectedOutput)
                    {
                        AsarPatch.Build(state.Source, output, rules, scheme.Files);
                        log("规则锚点和补丁输出 SHA-256 已校验。");
                    }
                    client.CloseProcesses(log);
                    state.CheckUnchanged();
                    if (scheme.VideoDownloadRules != null && !downloadVideos)
                    {
                        var keepPlayer = new HashSet<string>(PlaybackComponents, StringComparer.OrdinalIgnoreCase);
                        keepPlayer.IntersectWith(unselected);
                        if (keepPlayer.Count != 0 && !state.RestoreComponents(log, keepPlayer))
                            throw new IOException("播放器组件未能放回。请查看日志，或选择下载后打开视频。");
                        unselected.ExceptWith(keepPlayer);
                    }
                    Manifest record = state.PrepareBackup(scheme, rules);
                    if (File.Exists(output))
                        ClientState.SwapCopy(output, client.Core);
                    if (Formats.Sha(client.Core) != expectedOutput)
                        throw new IOException("应用补丁后重新校验核心文件失败。");
                    record.corePatched = expectedOutput;
                    record.corePending = null;
                    record.downloadVideos = scheme.VideoDownloadRules == null ? (bool?)null : downloadVideos;
                    state.Save(record);
                    state.DisableUpdater(record);
                    complete = state.RestoreComponents(log, unselected);
                    complete = state.MoveComponents(selected, log) && complete;
                    if (configureDesktop)
                    {
                        if (removeOpenWith) OpenWith.Remove(state, record);
                        else OpenWith.Restore(state, record);
                    }
                    if (configureDesktop)
                    {
                        try { NoticeWatcher.Install(client, log); }
                        catch (Exception ex)
                        {
                            throw new IOException("客户端已应用补丁。后台提示处理器启动失败，可重新应用方案或使用还原。", ex);
                        }
                    }
                    log(complete ? "方案已应用。请正常打开百度网盘。" : "核心补丁已应用，部分组件未能调整。请查看日志。");
                }
                finally { if (File.Exists(output)) File.Delete(output); }
            });
            return complete;
        }

        internal static bool HasVideoPreviewConflict(Scheme scheme, IEnumerable<string> selections, bool downloadVideos)
        {
            return scheme.VideoDownloadRules != null && !downloadVideos && selections.Any(PlaybackComponents.Contains);
        }

        internal bool Restore(ClientDirectory client)
        {
            bool fullyRestored = true;
            Locked(client, delegate
            {
                var state = new ClientState(client);
                if (state.Record == null)
                    throw new InvalidOperationException("此目录没有可以还原的补丁记录。");
                client.CloseProcesses(log);
                state.CheckUnchanged();
                if (configureDesktop)
                    NoticeWatcher.Uninstall(client);
                if (state.CurrentHash != state.Record.coreOriginal)
                    ClientState.SwapCopy(client.Original, client.Core);
                if (Formats.Sha(client.Core) != state.Record.coreOriginal)
                    throw new IOException("还原后的 core.asar 校验失败。");
                state.RestoreUpdater();
                fullyRestored = state.RestoreComponents(log);
                if (configureDesktop)
                    OpenWith.Restore(state, state.Record);
                bool legacyShell = File.Exists(client.Under(@"nd_purify\removed\shellext\state.json"));
                if (legacyShell)
                    log("客户端文件已还原。旧版移除的 Windows 资源管理器注册仍需用旧版卸载器恢复。");
                if (fullyRestored && !legacyShell)
                {
                    state.Archive();
                    log("已还原官方客户端。");
                }
                else
                    log("已保留备份记录，方便完成剩余还原。");
                fullyRestored = fullyRestored && !legacyShell;
            });
            return fullyRestored;
        }

        internal static string ClientId(ClientDirectory client)
        {
            var bytes = Encoding.UTF8.GetBytes(client.Root.ToUpperInvariant());
            return Formats.Sha(bytes, 0, bytes.Length).Substring(0, 24);
        }

        private static void Locked(ClientDirectory client, Action operation)
        {
            using (var mutex = new Mutex(false, @"Local\BaiduNetdiskPurify-operation-" + ClientId(client)))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                    throw new InvalidOperationException("此客户端已有一个补丁操作，请等它结束。");
                try { operation(); }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
