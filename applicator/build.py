import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile


ROOT = Path(__file__).resolve().parents[1]
REFERENCES = [
    "Microsoft.CSharp.dll",
    "System.Drawing.dll",
    "System.Management.dll",
    "System.Web.Extensions.dll",
    "System.Windows.Forms.dll",
]


def local_scheme_resources(directory, temporary):
    patchset = json.loads((directory / "patchset.json").read_text(encoding="utf-8-sig"))
    names = ["patchset.json", "components.json"]
    if (directory / "video-download.json").is_file():
        names.append("video-download.json")
    for name in patchset["newFiles"]:
        if Path(name).name != name or name in (".", ".."):
            raise ValueError(f"Invalid local scheme filename: {name}")
        names.append("files/" + name)
    entry = {
        "name": directory.name,
        "coreSha256": patchset["target"]["originalCoreSha256"],
        "outputSha256": patchset["expectedOutputSha256"],
        "files": {name: hashlib.sha256((directory / name).read_bytes()).hexdigest() for name in names},
    }
    index = temporary / "local-scheme.json"
    index.write_text(json.dumps(entry), encoding="utf-8")
    return [f"/resource:{index},LocalScheme"] + [
        f"/resource:{directory / name},LocalScheme/{name}" for name in names
    ]


def main():
    parser = argparse.ArgumentParser(description="Build the BaiduNetdisk Purify Windows applicator.")
    parser.add_argument("--output", type=Path, default=ROOT / "out" / "applicator")
    parser.add_argument("--compiler", type=Path, default=Path(os.environ.get("WINDIR", r"C:\Windows")) /
                        "Microsoft.NET/Framework64/v4.0.30319/csc.exe")
    parser.add_argument("--local-scheme", type=Path, help="Embed one locally verified scheme in a test build.")
    args = parser.parse_args()
    filename = "BaiduNetdisk-Purify-Setup-local-test.exe" if args.local_scheme else "BaiduNetdisk-Purify-Setup.exe"
    output = args.output.resolve() / filename
    output.parent.mkdir(parents=True, exist_ok=True)
    files = sorted((ROOT / "applicator").glob("*.cs"))
    key = ROOT / "installer" / "payload" / "scheme_pubkey.xml"
    command = [
        str(args.compiler),
        "/nologo", "/utf8output", "/codepage:65001", "/langversion:5", "/target:winexe", "/platform:x64", "/optimize+",
        "/out:" + str(output),
        "/resource:" + str(key) + ",SchemePublicKey",
        *("/reference:" + item for item in REFERENCES),
        *(str(item) for item in files),
    ]
    with tempfile.TemporaryDirectory(dir=output.parent) as temporary:
        if args.local_scheme:
            command.extend(local_scheme_resources(args.local_scheme.resolve(), Path(temporary)))
        subprocess.run(command, cwd=ROOT, check=True)
    print(output)


if __name__ == "__main__":
    main()
