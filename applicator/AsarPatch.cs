using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Purify
{
    internal static class AsarPatch
    {
        private const int BlockSize = 4194304;
        private const string HeaderPrefix = "{\"files\":{";
        private const StringComparison Ordinal = StringComparison.Ordinal;

        internal static void Build(string original, string output, PatchSet rules, Dictionary<string, byte[]> files)
        {
            using (var source = File.OpenRead(original))
            using (var reader = new BinaryReader(source))
            {
                int sizeFrame = reader.ReadInt32(), headerSize = reader.ReadInt32();
                int payloadSize = reader.ReadInt32(), jsonSize = reader.ReadInt32();
                long dataStart = 8L + headerSize;
                if (sizeFrame != 4 || headerSize < 8 || payloadSize != headerSize - 4 ||
                    jsonSize < HeaderPrefix.Length || jsonSize > payloadSize - 4 ||
                    jsonSize > 32 * 1024 * 1024 || dataStart > source.Length)
                    throw new InvalidDataException("core.asar 文件头格式无效。");
                var headerBytes = Read(reader, jsonSize);
                string header = Formats.Utf8.GetString(headerBytes);
                if (!header.StartsWith(HeaderPrefix, Ordinal))
                    throw new InvalidDataException("core.asar 文件头结构与方案不符。");
                var entries = Formats.Decode<AsarHeader>(headerBytes).files;
                var order = new List<string>();
                var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var rule in rules.patches)
                {
                    string input;
                    if (!replacements.TryGetValue(rule.file, out input))
                    {
                        AsarEntry entry;
                        long offset;
                        if (!SchemeSource.IsFileName(rule.file) || !entries.TryGetValue(rule.file, out entry) ||
                            entry.integrity == null || !long.TryParse(entry.offset, out offset) ||
                            offset < 0 || entry.size < 0 || source.Length - dataStart - offset < entry.size)
                            throw new InvalidDataException("方案中的文件在 core.asar 中不存在：" + rule.file);
                        source.Position = dataStart + offset;
                        input = Formats.Utf8.GetString(Read(reader, entry.size));
                        if (rule.file == "main.js" && input.Contains(rules.mark))
                            throw new InvalidDataException("输入文件已有补丁标记，请使用原版备份。");
                        order.Add(rule.file);
                    }
                    replacements[rule.file] = ApplyRule(rule, input);
                }

                var appended = new List<byte[]>();
                long dataLength = source.Length - dataStart;
                foreach (string name in order)
                {
                    var bytes = Formats.Utf8.GetBytes(replacements[name]);
                    header = ReplaceEntry(header, name, entries[name].integrity.hash, Entry(bytes, dataLength));
                    appended.Add(bytes);
                    dataLength += bytes.Length;
                }
                string inserted = "";
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (string name in rules.newFiles)
                {
                    byte[] bytes;
                    if (!SchemeSource.IsFileName(name) || !names.Add(name) || entries.ContainsKey(name) ||
                        !files.TryGetValue("files/" + name, out bytes))
                        throw new InvalidDataException("新增文件在方案中无效：" + name);
                    Formats.Utf8.GetString(bytes);
                    inserted += "\"" + name + "\":" + Entry(bytes, dataLength) + ",";
                    appended.Add(bytes);
                    dataLength += bytes.Length;
                }

                // Preserve source data bytes and header formatting. Existing schemes depend on the exact output hash.
                var updated = Formats.Utf8.GetBytes(HeaderPrefix + inserted + header.Substring(HeaderPrefix.Length));
                int padding = (4 - updated.Length % 4) % 4;
                int payload = 4 + updated.Length + padding;
                using (var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(target))
                {
                    writer.Write(4);
                    writer.Write(payload + 4);
                    writer.Write(payload);
                    writer.Write(updated.Length);
                    writer.Write(updated);
                    writer.Write(new byte[padding]);
                    writer.Flush();
                    source.Position = dataStart;
                    source.CopyTo(target);
                    foreach (byte[] item in appended)
                        writer.Write(item);
                    writer.Flush();
                    target.Flush(true);
                }
            }
            if (Formats.Sha(output) != rules.expectedOutputSha256)
                throw new InvalidDataException("补丁输出与签名索引中的 SHA-256 不一致。");
        }

        private static byte[] Read(BinaryReader reader, int length)
        {
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
                throw new EndOfStreamException("core.asar 文件不完整。");
            return bytes;
        }

        private static string ApplyRule(PatchRule rule, string input)
        {
            int start, length;
            string replacement;
            if (rule.kind == "literal")
            {
                start = UniqueIndex(input, rule.find, rule.file + " / " + rule.name);
                length = rule.find.Length;
                replacement = rule.repl;
            }
            else if (rule.kind == "regex")
            {
                var regex = new Regex(rule.pattern, RegexOptions.ECMAScript, TimeSpan.FromSeconds(15));
                var match = regex.Match(input);
                if (!match.Success || match.NextMatch().Success || rule.groups < 0 || rule.groups >= match.Groups.Count)
                    throw new InvalidDataException("规则锚点未唯一匹配：" + rule.file + " / " + rule.name);
                start = match.Index;
                length = match.Length;
                replacement = rule.template;
                for (int group = rule.groups; group >= 0; group--)
                    replacement = replacement.Replace("\0G" + group.ToString(CultureInfo.InvariantCulture) + "\0", match.Groups[group].Value);
                if (replacement.IndexOf('\0') >= 0)
                    throw new InvalidDataException("规则中存在未解析的替换模板：" + rule.name);
            }
            else
                throw new InvalidDataException("无法识别方案规则类型：" + rule.kind);
            return input.Substring(0, start) + replacement + input.Substring(start + length);
        }

        private static int UniqueIndex(string text, string value, string description)
        {
            int index = string.IsNullOrEmpty(value) ? -1 : text.IndexOf(value, Ordinal);
            if (index < 0 || text.IndexOf(value, index + value.Length, Ordinal) >= 0)
                throw new InvalidDataException("规则锚点未唯一匹配：" + description);
            return index;
        }

        private static string ReplaceEntry(string header, string name, string hash, string replacement)
        {
            int hashIndex = UniqueIndex(header, "\"hash\":\"" + hash + "\"", name);
            int start = header.LastIndexOf("{\"size\":", hashIndex, Ordinal);
            int end = header.IndexOf("]}}", hashIndex, Ordinal);
            string key = "\"" + name + "\":";
            if (start < key.Length || end < 0 || header.Substring(start - key.Length, key.Length) != key)
                throw new InvalidDataException("core.asar 文件头中无法定位条目：" + name);
            return header.Substring(0, start) + replacement + header.Substring(end + 3);
        }

        private static string Entry(byte[] data, long offset)
        {
            var blocks = new List<string>();
            for (int start = 0; start < data.Length; start += BlockSize)
                blocks.Add("\"" + Formats.Sha(data, start, Math.Min(BlockSize, data.Length - start)) + "\"");
            if (data.Length == 0)
                blocks.Add("\"" + Formats.Sha(data, 0, 0) + "\"");
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"size\":{0},\"offset\":\"{1}\",\"integrity\":{{\"algorithm\":\"SHA256\",\"hash\":\"{2}\",\"blockSize\":{3},\"blocks\":[{4}]}}}}",
                data.Length, offset, Formats.Sha(data, 0, data.Length), BlockSize, string.Join(",", blocks));
        }
    }
}
