using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Purify
{
    internal sealed class SchemeSource
    {
        private const string GitHub = "https://raw.githubusercontent.com/lastbaigeiapi/BaiduNetdisk-Purify/main/schemes/";
        private const int MaxFileLength = 16 * 1024 * 1024;
        private static readonly Regex FileName = new Regex(@"\A[A-Za-z0-9_-][A-Za-z0-9._-]*\z");
        private static readonly Regex Sha256 = new Regex(@"\A[a-f0-9]{64}\z");
        private readonly Func<string, byte[]> read;
        private readonly string publicKey;
        private readonly SchemeEntry localEntry;

        internal SchemeSource() : this(Download, LoadPublicKey())
        {
            var index = ReadResource("LocalScheme");
            if (index != null)
                localEntry = Formats.Decode<SchemeEntry>(index);
        }

        internal SchemeSource(Func<string, byte[]> read, string publicKey)
        {
            this.read = read;
            this.publicKey = publicKey;
        }

        internal static bool IsFileName(string name)
        {
            return name != null && FileName.IsMatch(name) && !name.EndsWith(".", StringComparison.Ordinal);
        }

        private static string LoadPublicKey()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SchemePublicKey"))
            using (var reader = new StreamReader(stream, Formats.Utf8))
                return reader.ReadToEnd();
        }

        private static byte[] ReadResource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) return null;
                using (var result = new MemoryStream())
                {
                    stream.CopyTo(result);
                    return result.ToArray();
                }
            }
        }

        private static byte[] Download(string relative)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(GitHub + relative);
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            request.AllowAutoRedirect = false;
            request.UserAgent = "BaiduNetdisk-Purify/2.0";
            request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var source = response.GetResponseStream())
                using (var result = new MemoryStream())
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new IOException("HTTP " + (int)response.StatusCode);
                    var buffer = new byte[65536];
                    int count;
                    while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        if (result.Length + count > MaxFileLength)
                            throw new InvalidDataException("在线方案文件超出大小限制。");
                        result.Write(buffer, 0, count);
                    }
                    return result.ToArray();
                }
            }
            catch (WebException ex)
            {
                throw new IOException("无法从 GitHub 下载补丁方案，请确认可以访问 raw.githubusercontent.com。", ex);
            }
        }

        internal Scheme Resolve(string originalHash)
        {
            if (localEntry != null && localEntry.coreSha256 == originalHash)
                return Load(localEntry, originalHash, path => ReadResource("LocalScheme/" + path), true);

            var indexBytes = read("index.json");
            var signature = read("index.json.sig");
            try
            {
                using (var rsa = new RSACryptoServiceProvider { PersistKeyInCsp = false })
                {
                    rsa.FromXmlString(publicKey);
                    if (!rsa.VerifyData(indexBytes, "SHA256", Convert.FromBase64String(Formats.Utf8.GetString(signature))))
                        throw new InvalidDataException("GitHub 方案索引的 RSA 签名校验失败。");
                }
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("GitHub 方案索引的签名格式无效。", ex);
            }

            var index = Formats.Decode<SchemeIndex>(indexBytes);
            if (index == null || index.format != 1 || index.schemes == null)
                throw new InvalidDataException("当前工具无法读取这个方案索引。");
            var entry = index.schemes.SingleOrDefault(item => item.coreSha256 == originalHash);
            if (entry == null)
                return null;
            return Load(entry, originalHash, path => read(entry.name + "/" + path), false);
        }

        private static Scheme Load(SchemeEntry entry, string originalHash, Func<string, byte[]> readFile, bool local)
        {
            if (!IsFileName(entry.name) || entry.outputSha256 == null || !Sha256.IsMatch(entry.outputSha256) ||
                entry.files == null || !entry.files.ContainsKey("patchset.json") || !entry.files.ContainsKey("components.json"))
                throw new InvalidDataException("签名索引中的方案条目不完整。");

            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var file in entry.files)
            {
                if (!file.Key.Split('/').All(IsFileName) || file.Value == null || !Sha256.IsMatch(file.Value))
                    throw new InvalidDataException("方案索引文件名或哈希无效。");
                var bytes = readFile(file.Key);
                if (bytes == null || Formats.Sha(bytes, 0, bytes.Length) != file.Value)
                    throw new InvalidDataException("方案文件 SHA-256 校验失败：" + file.Key);
                files.Add(file.Key, bytes);
            }
            var rules = Formats.Decode<PatchSet>(files["patchset.json"]);
            var components = Formats.Decode<ComponentList>(files["components.json"]);
            if (!ValidRules(rules, originalHash) || rules.expectedOutputSha256 != entry.outputSha256 ||
                components == null || components.components == null)
                throw new InvalidDataException("方案内容与签名索引不一致。");

            PatchSet videoDownloadRules = null;
            byte[] variant;
            if (files.TryGetValue("video-download.json", out variant))
            {
                videoDownloadRules = Formats.Decode<PatchSet>(variant);
                if (!ValidRules(videoDownloadRules, originalHash) ||
                    videoDownloadRules.expectedOutputSha256 == entry.outputSha256 ||
                    videoDownloadRules.target.appVersion != rules.target.appVersion ||
                    videoDownloadRules.mark != rules.mark || !videoDownloadRules.newFiles.SequenceEqual(rules.newFiles))
                    throw new InvalidDataException("视频下载方案与基础方案不一致。");
            }
            return new Scheme {
                Entry = entry, Rules = rules, VideoDownloadRules = videoDownloadRules,
                Components = components.components, Files = files, IsLocal = local
            };
        }

        private static bool ValidRules(PatchSet rules, string originalHash)
        {
            return rules != null && rules.format == 1 && rules.target != null &&
                rules.target.originalCoreSha256 == originalHash &&
                rules.expectedOutputSha256 != null && Sha256.IsMatch(rules.expectedOutputSha256) &&
                rules.patches != null && rules.patches.Count != 0 && rules.newFiles != null && !string.IsNullOrEmpty(rules.mark);
        }
    }

    internal sealed class Scheme
    {
        internal SchemeEntry Entry;
        internal PatchSet Rules;
        internal PatchSet VideoDownloadRules;
        internal List<string> Components;
        internal Dictionary<string, byte[]> Files;
        internal bool IsLocal;
    }
}
