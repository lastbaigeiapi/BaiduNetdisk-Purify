using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Purify
{
    internal static class OpenWith
    {
        private const string Apps = @"Software\RegisteredApplications";
        private static readonly string[] Names = { "BaiduNetdiskImageViewer", "BaiduNetdiskPlayer" };
        private static readonly string[] Keys = {
            @"Software\Baidu\BaiduNetdiskImageViewer", @"Software\Baidu\BaiduNetdiskPlayer",
            @"Software\Classes\BaiduNetdiskImageViewerAssociations", @"Software\Classes\BaiduNetdiskPlayerAssociations",
            @"Software\Classes\Applications\BaiduNetdiskPlayer.open"
        };

        internal static void Remove(ClientState state, Manifest record)
        {
            if (record.registeredAppValues == null)
                record.registeredAppValues = new Dictionary<string, string>();
            using (var registrations = Registry.CurrentUser.OpenSubKey(Apps, true))
            {
                if (registrations != null)
                    foreach (string name in Names)
                    {
                        string value = registrations.GetValue(name) as string;
                        if (value == null) continue;
                        if (!record.registeredAppValues.ContainsKey(name))
                            record.registeredAppValues.Add(name, value);
                        record.registryBackedUp = true;
                        state.Save(record);
                        registrations.DeleteValue(name, false);
                    }
            }
            foreach (string key in Keys)
            {
                using (var existing = Registry.CurrentUser.OpenSubKey(key))
                {
                    if (existing == null) continue;
                }
                string backup = BackupFor(state.Client, key);
                if (!File.Exists(backup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    string temp = backup + ".new";
                    try
                    {
                        Reg("export \"HKCU\\" + key + "\" \"" + temp + "\" /y");
                        File.Move(temp, backup);
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
                record.registryBackedUp = true;
                state.Save(record);
                Registry.CurrentUser.DeleteSubKeyTree(key, false);
            }
        }

        internal static void Restore(ClientState state, Manifest record)
        {
            if (!record.registryBackedUp)
                return;
            foreach (string key in Keys)
            {
                string backup = BackupFor(state.Client, key);
                if (File.Exists(backup))
                    Reg("import \"" + backup + "\"");
            }
            var values = record.registeredAppValues;
            if (values != null && values.Count != 0)
                using (var registrations = Registry.CurrentUser.CreateSubKey(Apps))
                    foreach (string name in Names)
                    {
                        string value;
                        if (values.TryGetValue(name, out value) && registrations.GetValue(name) == null)
                            registrations.SetValue(name, value, RegistryValueKind.String);
                    }
            foreach (string key in Keys)
            {
                string backup = BackupFor(state.Client, key);
                if (File.Exists(backup)) File.Delete(backup);
            }
            record.registryBackedUp = false;
            record.registeredAppValues = null;
            state.Save(record);
        }

        private static string BackupFor(ClientDirectory client, string key)
        {
            return client.Under(@"nd_purify\backup\registry\HKCU_" + key.Replace('\\', '_').Replace(' ', '_') + ".reg");
        }

        private static void Reg(string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe"), arguments) {
                CreateNoWindow = true, UseShellExecute = false
            }))
            {
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new IOException("无法备份或还原百度网盘的「打开方式」注册。");
            }
        }
    }
}
