# 宝塔网络代理修复

一个 Windows 小工具：**开着梯子（系统代理）时，也能正常打开宝塔面板**。

## 问题背景

使用宝塔面板（堡塔多机管理）时，很多人需要同时挂着梯子用 GPT。
Windows 的系统代理是全局的，会把访问面板的流量也交给梯子节点；
而梯子节点回连国内服务器的高位端口经常失败，于是面板就连不上了。

## 原理

把面板地址写进 Windows 的 **代理例外列表（ProxyOverride）**：
面板流量直连，其余流量（GPT 等）继续走梯子。

本程序常驻后台，监听代理注册表项：

- 梯子一开 → 自动补上例外；
- 例外被客户端升级/重置清掉 → 自动补回；
- 梯子关闭 → 不干预。

事件驱动（`RegNotifyChangeKeyValue`），空闲时 CPU 为 0，常驻内存约 18 MB。

## 功能特性

- ✅ 静默运行，无窗口、无控制台
- ✅ 随用户登录自动启动（`HKCU\...\Run`）
- ✅ 毫秒级感知代理开关，自动修复例外
- ✅ 单实例、Idle 优先级、日志自动轮转
- ✅ 面板换 IP 只需改一行配置
- ✅ 纯 .NET Framework，无需安装运行时

## 使用

### 直接使用（推荐）

1. 下载 `bin\宝塔网络代理修复.exe` 和 `config.ini`，放到同一目录；
2. 运行 `install.cmd`（会复制到 `%LOCALAPPDATA%\宝塔网络代理修复` 并设置开机自启）；
3. 完成，梯子和面板可同时使用。

### 从源码构建

需要 Windows 自带的 .NET Framework 编译器：

```bat
build.cmd
```

生成 `bin\宝塔网络代理修复.exe`。

## 配置 `config.ini`

```ini
# 面板 IP 或域名，多个用逗号分隔
hosts=8.153.176.147

# 是否同时加入 <local>（本机/局域网直连）
keep_local=1
```

修改后保存，最多 1 分钟内生效；重启程序可立即生效。

## 日志

`%LOCALAPPDATA%\宝塔网络代理修复\run.log`

## 卸载

运行 `uninstall.cmd`，或手动：

```bat
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "宝塔网络代理修复" /f
taskkill /f /im "宝塔网络代理修复.exe"
rd /s /q "%LOCALAPPDATA%\宝塔网络代理修复"
```

## 兼容性

- Windows 10 / 11（Windows 7 理论上可用，未测试）
- 适用于任何使用「系统代理」模式的梯子客户端（Clash、v2rayN、一元机场等）

## 免责声明

本工具只修改当前用户的代理例外列表，不修改系统代理本身，也不涉及任何网络劫持。
请自行确认添加的地址可信。
