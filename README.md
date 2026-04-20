# GeekToolDownloader

一款极致现代化的 Windows 极客装机与工具分发引擎，基于 WPF 和 .NET Framework 4.8 打造。拥有 Win11 原生圆角、亚克力毛玻璃动效、无锁并发下载以及自动代理感知等高级特性。

## 编译与打包

```bash
dotnet build -c Release
```
---

## 数据结构：`Assets\tools.json`

应用的工具列表通过 JSON 清单进行管理，支持本地缓存与远端拉取覆盖。采用了**“约定优于配置”**的极简设计，大部分字段可智能推导：

### 参数详解：

* **`Name`** *(字符串, 必填)*: 展现在 UI 列表中的软件显示名称。
* **`Url`** *(字符串, 必填)*: 软件的直链下载地址。
* **`Version`** *(字符串, 可选)*: 软件版本号，展现在 UI 列表右侧。
* **`Check`** *(字符串数组, 可选，强烈建议填写)*: 智能安装检测规则。支持两种前缀：
  * `reg:xxx` - 检测注册表卸载项（按 `DisplayName` 包含关系）。
  * `path:xxx` - 检测文件/目录是否存在。推荐同时写两种：
    * 可执行文件名（如 `path:node.exe` / `path:python.exe`），用于命中 PATH。
    * 典型相对安装路径（如 `path:nodejs\\node.exe` / `path:Python312\\python.exe`），用于命中常见安装目录。
* **`Args`** *(字符串, 可选)*: 静默安装参数。如果不填，系统会根据 Url 后缀自动推导（如 `.msi` 默认使用 `/quiet /norestart`，`.exe` 默认使用 `/S`）。
* **`Hash`** *(字符串, 可选)*: SHA256 校验哈希值。若在“高级安全”中开启了哈希校验，引擎下载完毕后将验证文件完整性。
* **`TypeOverride`** *(字符串, 可选)*: 强制指定安装包类型（`Msi`, `ExeInstaller`, `Zip` 等）。如果不填，系统会自动通过 Url 后缀名智能推导。

### 推荐填写模板（最稳妥）

```json
[
  {
    "Name": "工具名称",
    "Version": "vX.Y.Z",
    "Url": "https://example.com/installer.exe",
    "Args": "/S",
    "Hash": "SHA256_HEX_UPPER_OR_LOWER",
    "Check": [
      "reg:厂商或产品名关键字",
      "path:tool.exe",
      "path:Vendor\\Tool\\tool.exe"
    ]
  }
]
```

### 填写建议

* `Name` 使用稳定显示名，避免频繁变化（否则影响临时文件命名与识别）。
* `Url` 建议使用不带重定向链的直链，避免下载端不必要重试。
* `Check` 至少 2 条（`reg` + `path`），可显著降低“已装却判定未装”的概率。
* `Hash` 建议始终填写（配合“高级安全-哈希校验”开启），可防篡改与下载损坏。
