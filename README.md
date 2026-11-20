# 百度网盘纯净补丁

这个项目为 **Windows 版百度网盘**制作按客户端构建匹配的精简方案。维护时使用 REA MCP 检查官方客户端的 Electron 资源和相关模块，定位广告、营销入口、AI、办公套件和非网盘组件，再为已验证的构建制作方案。日常使用保留文件浏览、上传、下载、分享、转存和基础文件操作。 [REA MCP](https://github.com/morluto/rea)

分发给用户的是一个 **Windows 方案应用工具**。它作用于本机已安装的官方百度网盘，根据原始核心文件的 SHA-256 从 GitHub 下载对应方案，校验通过后备份并应用。百度网盘客户端从下表的官方地址获取。 [下载方案应用工具](https://github.com/lastbaigeiapi/BaiduNetdisk-Purify/releases/latest/download/BaiduNetdisk-Purify-Setup.exe) · [发布说明](https://github.com/lastbaigeiapi/BaiduNetdisk-Purify/releases/latest)

## 使用方法

1. 从支持表中选择并安装对应的官方客户端。请先等上传和下载任务完成。
2. 双击 `BaiduNetdisk-Purify-Setup.exe`。工具优先读取正在运行的百度网盘进程路径，也可以手动选择安装目录。
3. 检查兼容性后，逐项勾选需要移入本机备份的组件，再点击「应用方案」。首次使用默认保留所有可选项目。取消勾选已移出的项目后重新应用，可放回对应备份。应用过程中会关闭该目录正在运行的客户端。
4. 完成后以普通用户权限打开百度网盘。需要恢复时，打开同一个 EXE，选择原安装目录，点击「还原客户端」。

应用工具以 **C# 和 WinForms** 编译为单文件 x64 EXE，使用 Windows 自带的 .NET Framework 4.7.2 或更高版本，面向 64 位 Windows 10 1809 及以上或 Windows 11。运行工具无需额外安装 Python、PowerShell 或现代 .NET SDK。 [Microsoft 的 Windows 运行时说明](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-10) · [详细使用说明](docs/使用说明.txt)

常规构建在检查兼容性时需要访问 **raw.githubusercontent.com**，应用时使用本次检查已验证的方案。还原使用本机备份，可在断网时执行。

## 已支持的百度网盘版本

工具按 **原始 core.asar 的 SHA-256** 匹配构建。版本号用于识别，这张表给出已验证的官方安装包和安装包 SHA-256。同一个版本号可能有不同构建，**8.10.3.103 的方案仅适用于官方 x64 安装包**。

| 百度网盘版本 | 官方安装包 | 官方安装包 SHA-256 |
|---|---|---|
| 8.10.3.103 x64 | [BaiduNetdisk_8.10.3.103_x64.exe](https://issuepcdn.baidupcs.com/issue/netdisk/yunguanjia/x64/BaiduNetdisk_8.10.3.103_x64.exe) | `0696430053ba455de47ea5e5567354c4eb74e17ea9940ed6f6a9a9f682dad319` |
| 8.9.7.101 | [BaiduNetdisk_8.9.7.101.exe](https://issuepcdn.baidupcs.com/issue/netdisk/yunguanjia/BaiduNetdisk_8.9.7.101.exe) | `3673bd43e98af4c93d69fa7e85cf836fc0ef7791229b10d09127e9b4c3970569` |
| 8.9.2.101 | [BaiduNetdisk_8.9.2.101.exe](https://issuepcdn.baidupcs.com/issue/netdisk/yunguanjia/BaiduNetdisk_8.9.2.101.exe) | `3bb9c6066dee502e8852170ea57cf52b3134c96521dffb6ee3d13d0862fd4086` |
| 8.8.8.101 | [BaiduNetdisk_8.8.8.101.exe](https://issuepcdn.baidupcs.com/issue/netdisk/yunguanjia/BaiduNetdisk_8.8.8.101.exe) | `cd5e4aeff87ca3f178af8ab30b2c6709112f6c5c6dcb976b04e7d7edc3d61867` |
| 8.8.6.102 | [BaiduNetdisk_8.8.6.102.exe](https://issuepcdn.baidupcs.com/issue/netdisk/yunguanjia/BaiduNetdisk_8.8.6.102.exe) | `b04a635dea283a455ada9e5d7d132d23bee6a2fa4df9d3f87efca914d0e57e96` |
| 8.8.3.101 | [BaiduNetdisk_8.8.3.101.exe](https://issuepcdn.baidupcs.com/issue/netdisk/yunguanjia/BaiduNetdisk_8.8.3.101.exe) | `82e8356f63db4bc5ecb2bc3e9fd96cb16efa1f59d149ae49ff1004cecd1fad55` |
| 8.7.9.102 | [BaiduNetdisk_8.7.9.102.exe](https://issuepcdn.baidupcs.com/issue/netdisk/yunguanjia/BaiduNetdisk_8.7.9.102.exe) | `a2f9c3ae9c51678963c9c63b61b588976f9b15761b70192bff259db931d8bb4d` |
| 8.6.8.102 | [BaiduNetdisk_8.6.8.102.exe](https://issuepcdn.baidupcs.com/issue/netdisk/yunguanjia/BaiduNetdisk_8.6.8.102.exe) | `e4f727fa845172a2840254079b6d51f034b5074c2f516de2c0ab4011d27e0811` |
| 8.5.9.102 | [BaiduNetdisk_8.5.9.102.exe](https://issuepcdn.baidupcs.com/issue/netdisk/yunguanjia/BaiduNetdisk_8.5.9.102.exe) | `fd777350c1eb91b591a312a85d8c918e87e8216102840946f3a13a0a5311d804` |

新工具已对表中的九个原版样本验证在线获取、生成补丁、移动组件和还原。历史客户端实测中，8.9.7.101 覆盖了上传、下载、创建及取消分享和系统程序打开图片，8.10.3.103 x64 覆盖了登录后浏览文件、分享页和转存页。各构建的实际上传、下载及创建分享尚未逐一验证。

## 方案修改的内容

| 范围 | 方案处理内容 |
|---|---|
| 广告和营销 | 顶栏、侧栏与右下角广告、活动弹窗、福利任务、会员推销和续费引导、游戏、金币、搜索热词和手机客户端推广 |
| AI 与办公 | 库库 AI 及后台预加载、知识库、搜索栏 AI 工具、笔记/AI PPT、Office/PDF 转换、文字提取、AI 剪辑和修图、云打印 |
| 客户端入口 | 同步空间、消息、工具、备份、隐藏空间、回收站、文档历史等入口，以及相关设置和托盘项 |
| 预览 | 支持切换视频打开方式的方案默认勾选「在网盘内预览视频」。保留播放器组件后可使用内置预览，取消勾选并应用则下载后打开。其他受支持文件下载后由系统默认应用打开。程序、脚本及压缩包在下载目录中定位 |
| 可选组件精简 | 按构建勾选看图、播放器、语音、AI 引擎、同步引擎、办公插件等组件，也可单独选择 Windows「打开方式」注册。移出的文件保存在客户端目录下的 `nd_purify\removed` |

浏览、上传、下载、分享链接、转存和订阅、云添加及基础文件操作仍通过官方客户端提供。删除的云端文件可从网页版回收站找回。会员权益和下载速度按百度服务端的规则执行。 [百度网盘网页版](https://pan.baidu.com)

### 还原

工具把原版 `core.asar`、自动更新配置及备份记录保存在客户端目录的 `nd_purify\backup`。还原会核对备份和当前核心文件，恢复客户端文件、移出的组件、自动更新及工具处理的「打开方式」注册。请保留 `nd_purify` 目录直至完成还原。

v2 的应用操作聚焦客户端组件和本项目的启动项。如果曾在旧安装器中选过「移除资源管理器集成」，还原旧版的 Windows 右键菜单和同步覆盖图标需要继续使用旧版补丁的卸载入口。v2 会保留这部分备份和还原记录。 [历史版本](https://github.com/lastbaigeiapi/BaiduNetdisk-Purify/releases)

## 方案如何制作和发布

1. **分析官方构建。** 在维护环境用 REA MCP 检查官方客户端的 `core.asar`、启动流程和组件引用。广告请求过滤、界面入口、代码锚点与可移动组件都按实际构建核对。
2. **生成并验证方案。** `scripts/portable/rules/` 定位各构建的锚点，`scripts/make_scheme.ps1` 生成 `patchset.json` 与组件清单。用原版样本核对规则、输出 SHA-256、客户端启动及核心文件操作。已记录的运行验证会随版本说明发布。
3. **发布签名方案。** 每个构建的方案位于 `schemes/<版本>/`，包含 `patchset.json`、`components.json` 和 `files/`。支持视频切换的方案另有 `video-download.json`。维护者用 RSA-3072 对 `schemes/index.json` 签名，并将方案和签名提交到 GitHub。索引记录原版与补丁输出的 SHA-256，以及每个方案文件的 SHA-256。 [已发布方案](schemes/)
4. **由用户工具应用。** 应用工具从 GitHub 下载签名索引和命中的方案，验证索引签名、文件哈希、规则锚点与补丁输出。下载失败、构建不匹配或校验未通过时，工具会在修改客户端前中止。现有工具可以读取后续按同一格式签名发布的方案。

REA MCP、Node.js 和方案签名脚本用于维护。用户仅运行 Release 中的 EXE。分发物内嵌方案验签公钥。

## 常见问题

- **GitHub 连接失败。** 确认当前网络能访问 `raw.githubusercontent.com` 后再重试。应用和还原的操作日志位于 `%LOCALAPPDATA%\BaiduNetdiskPurify\applicator.log`。
- **提示没有匹配构建。** 核对官方安装包的版本和 SHA-256。客户端可能已经更新到尚未制作方案的构建。
- **客户端出现“应用程序被修改”提示。** 应用方案后，工具为当前 Windows 用户注册一个后台提示处理进程，只处理所选百度网盘路径对应的这一个提示。请正常打开百度网盘，使用「还原客户端」可移除项目启动项。
- **旧版安装器报代码 1。** 如果旧日志在 `watcher` 阶段显示“拒绝访问”，改用当前方案应用工具。v2 使用当前用户的「启动」快捷方式，方案可以在原目录重新应用。
- **安全软件报告该 EXE。** 从本仓库 Release 下载，核对页面列出的 SHA-256。分发 EXE 当前未做 Authenticode 代码签名，可以从公开源码自行编译。如需报告检测，请提供安全软件名称、检测名和 EXE 的 SHA-256。
- **升级百度网盘。** 先还原现有补丁，再升级官方客户端。升级后需要有对应的新方案才可应用。

## 从源码构建

维护者可以在 Windows 上用 Python 3 调用系统自带的 64 位 C# 编译器。

```text
python applicator/build.py
```

产物位于 `out/applicator/BaiduNetdisk-Purify-Setup.exe`。编译时嵌入方案验签公钥，发布时方案内容仍位于 GitHub。

维护者可用 `python applicator/build.py --local-scheme <方案目录>` 生成 `BaiduNetdisk-Purify-Setup-local-test.exe`。请先用 `scripts/make_scheme.ps1` 生成方案并核对精确的原版和输出哈希。这一构建将方案编入 EXE，仅匹配指定的原始核心文件；其余客户端仍使用 GitHub 签名方案。

| 目录 | 用途 |
|---|---|
| `applicator/` | 当前发行工具的 C# 源码和构建入口 |
| `schemes/` | 已签名索引、各构建匹配的规则、注入文件及组件清单 |
| `patch/`、`loader/`、`scripts/portable/` | 维护者使用的补丁源码、样式及跨构建规则 |
| `scripts/make_scheme.ps1`、`scripts/sign_schemes.ps1` | 维护者用的方案生成与签名工具 |
| `installer/` | 历史 Inno Setup 工程、旧版卸载工具和方案验签公钥 |

## 许可

项目代码采用 [MIT 许可](LICENSE)。“百度网盘”及相关商标归百度所有。本项目为第三方开发，使用方式请以百度网盘用户协议为准。
