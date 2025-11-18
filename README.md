[English](./README.en.md)

# <img src="./resources/screenshots/logo.png"  width="28" style="vertical-align: middle; margin-top: -4px;" /> LiteMonitor
一款轻量、可定制的开源桌面硬件监控软件 — 实时监测 CPU、GPU、内存、磁盘、网络等系统性能。

A lightweight and customizable desktop hardware monitoring tool — real-time monitoring of system performance such as CPU, GPU, memory, disk, and network.

> 🟢 **立即下载最新版本：** [📦 GitHub Releases → LiteMonitor 最新版](https://github.com/Diorser/LiteMonitor/releases/latest)    /  [💿国内镜像](https://litemonitor.piczip.cn/)    

LiteMonitor 是一款基于 **Windows** 的现代化桌面系统监控工具。  
支持多语言界面、主题切换、透明度显示、三级色值报警，界面简洁且高度可配置。

![LiteMonitor 主界面](./resources/screenshots/overview.png)

---

# 🖥️ 系统监控功能

| 分类 | 监控指标 |
|------|-----------|
| 💻 **CPU（处理器）** | 实时监测 CPU 使用率与温度，支持多核心平均与峰值显示。 |
| 🎮 **GPU（显卡）** | 展示 GPU 使用率、核心温度、显存占用情况，兼容 NVIDIA / AMD / Intel 显卡。 |
| 💾 **内存（Memory）** | 显示系统内存使用率，清晰了解整体内存负载水平。 |
| 📀 **磁盘（Disk）** | 监控磁盘读取与写入速度（KB/s、MB/s），帮助分析存储 I/O 活跃情况。 |
| 🌐 **网络（Network）** | 实时显示上传与下载速度（KB/s、MB/s），提供轻量级网络流量监控。 |

> 💡 LiteMonitor 持续完善中，如需更多监控项或功能支持，欢迎在 [GitHub Issues](https://github.com/Diorser/LiteMonitor/issues) 中反馈建议！



---

# 产品功能

| 功能 | 说明 |
|---|---|
| 🎨 自定义主题 | 通过 JSON 定义颜色、字体、间距、圆角等，主题可扩展与复用。 |
| 🔴🟡🟢 **三级色值报警** | 监控项根据阈值自动切换进度条/数值颜色，支持自定义颜色。 |
| 🌍 多语言界面 | 内置中/英/日/韩/法/德/西/俄八种语言，覆盖主流用户。 |
| 📊 监控项显示管理 | 在菜单中按需显示/隐藏 CPU、GPU、内存、磁盘、网络等监控模块，聚焦关键信息。 |
| 📏 面板宽度调整 | 右键菜单即时调整面板宽度，布局自动重排，无需重启。 |
| 🪟 窗口与界面 | 圆角显示、透明度调节、“总在最前”、阴影与高质量文本渲染，确保可读性与存在感。 |
| 🧭 靠边自动隐藏 | 靠屏幕边缘自动收起，鼠标移入唤出，节省桌面空间。 |
| 👆 鼠标穿透模式 | 启用后不拦截点击，便于与下方应用交互。 |
| 💫 动画平滑 | 可调节数值过渡速度，减小抖动与突变，阅读更稳定。 |
| 🧩 主题/语言即时切换 | 切换后界面立即刷新，无需重启。 |
| 🔠 DPI 自适应 | 字体与布局根据系统缩放比例自动适配，高分屏清晰显示。 |
| 📂 设置自动保存 | 透明度、主题、语言、显示项等更改实时写入 `settings.json`。 |
| 🚀 开机自启 | 通过计划任务实现自启动，支持管理员权限。 |
| 🔄 自动更新检测 | 拉取远程版本并提示前往下载，提高可用性与安全性。 |
| ⚙️ 配置文件存储 | 统一使用 `settings.json` 管理用户偏好，便于迁移与备份。 |


---

## 📦 安装与使用

1. 前往 [Releases 页面](https://github.com/Diorser/LiteMonitor/releases) 下载最新版压缩包  
2. 解压后运行 `LiteMonitor.exe`  
3. 程序会自动根据系统语言加载对应语言文件

---

## 🎨 主题系统


主题文件位于 `/themes/` 目录。

示例：
```json
{
  "name": "DarkFlat_Classic",
  "layout": { "rowHeight": 40, "cornerRadius": 10 },
  "color": {
    "background": "#202225",
    "textPrimary": "#EAEAEA",
    "barLow": "#00C853"
  }
}
```


---

## ⚙️ 设置文件（settings.json）

| 字段 | 说明 |
|------|------|
| `Skin` | 当前主题 |
| `PanelWidth` | 界面宽度 |
| `Opacity` | 透明度 |
| `Language` | 当前语言 |
| `TopMost` | 是否置顶 |
| `AutoStart` | 是否开机启动 |
| `AutoHide` | 靠边自动隐藏 |
| `ClickThrough` | 启用鼠标穿透 |
| `AnimationSpeed` | 数值平滑速度 |
| `Enabled` | 各项显示开关 |

---

## 🧩 架构概览

| 文件 | 功能 |
|------|------|
| `MainForm_Transparent.cs` | 主窗体与菜单逻辑 |
| `UIController.cs` | 界面与主题控制器 |
| `UIRenderer.cs` | 绘制组件与进度条 |
| `UILayout.cs` | 动态布局计算 |
| `ThemeManager.cs` | 加载与解析主题文件 |
| `LanguageManager.cs` | 语言管理与本地化 |
| `HardwareMonitor.cs` | 硬件数据采集 |
| `AutoStart.cs` | 计划任务自启管理 |
| `UpdateChecker.cs` | GitHub 更新检查 |
| `AboutForm.cs` | 关于窗口 |

---

## 🛠️ 编译说明

### 环境要求
- Windows 10 / 11  
- .NET 8 SDK  
- Visual Studio 2022 或 Rider

### 编译命令
```bash
git clone https://github.com/Diorser/LiteMonitor.git
cd LiteMonitor
dotnet build -c Release
```

输出文件：
```
/bin/Release/net8.0-windows/LiteMonitor.exe
```

---

## 📄 开源协议
本项目基于 **MIT License** 开源，可自由使用、修改与分发。

---

## 📬 联系方式
**作者**：Diorser  
**项目主页**：[https://github.com/Diorser/LiteMonitor](https://github.com/Diorser/LiteMonitor)
