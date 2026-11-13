# 宝塔网络代理修复

解决「宝塔面板」和「梯子（系统代理）」共存时的两个网络坑：

1. **面板被代理吃掉** —— 梯子开启系统代理后，宝塔面板的地址也走代理，导致面板打不开。
   本工具在代理开启时自动把面板地址加入 Windows 代理例外（`ProxyOverride`），
   梯子开关、客户端升级把例外清空后都能自动补回来。
2. **死代理导致全网打不开**（v1.1 新增）—— 加速器/梯子异常退出或重启前没复位，
   系统代理被留在 `127.0.0.1:端口` 上但没有任何程序监听，表现为
   「ping 能通、网页全打不开」。本工具检测到这种状态持续 `grace_seconds` 秒后，
   自动关闭系统代理、清空代理地址并刷新 DNS。

## 运行方式

无窗口常驻（`winexe`），登录时由 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
自动启动。单实例靠 `run.lock` 文件锁实现——进程退出时系统必然释放句柄，
不会像命名互斥体那样残留僵尸句柄导致新实例起不来。默认每 2 秒检查一次。

## 配置（`config.ini`，与 exe 同目录）

| 键 | 说明 | 默认 |
|---|---|---|
| `hosts` | 宝塔面板 IP/域名，多个用逗号分隔；代理开启时自动加入例外 | `8.153.176.147` |
| `keep_local` | 是否同时加入 `<local>`（本机/局域网直连） | `1` |
| `repair_dead_proxy` | 死代理自愈开关，`0` 则只维护例外列表 | `1` |
| `grace_seconds` | 死代理连续持续多少秒才修复（留时间给加速器起监听） | `10` |
| `poll_seconds` | 轮询间隔（秒） | `2` |

## 安全边界

- **只处理本机回环代理**（`127.0.0.1` / `localhost` / `::1`）。代理指向远端主机时一律不动。
- 例外层采用**合并**策略：保留梯子自己写的例外（如 `localhost;127.*`），只把缺失的补到最前面，
  不会像旧版那样整体覆盖。
- 修复前先连续观察 `grace_seconds` 秒，避免「加速器先写代理、后起监听」的正常启动过程被误判。
- 改动代理设置后调用 `InternetSetOption`（`SETTINGS_CHANGED` / `REFRESH`），
  浏览器和系统设置立即生效，无需重启。

## 构建

```cmd
build.cmd
```

`build.cmd` 只是 ASCII 外壳，实际调用 `build.ps1`（UTF-8 + BOM）。
需要 .NET Framework 4.x 自带的 `csc.exe`（Windows 自带，无需安装 SDK）。
源码为 C# 5 语法，`/codepage:65001` 编译。

> 早期版本的 `.cmd` 直接写中文变量，在 UTF-8/GBK 混用环境下会把 exe 名写成乱码
> （`APPNAME` 变`瀹濆缃戠粶…`）。现在所有中文都在 `.ps1` 里，`.cmd` 保持纯 ASCII。

## 安装 / 卸载

安装：把 `宝塔网络代理修复.exe` 和 `config.ini` 放到 `%LOCALAPPDATA%\宝塔网络代理修复\`，
并写入开机自启：

```cmd
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "宝塔网络代理修复" /t REG_SZ /d "\"%LOCALAPPDATA%\宝塔网络代理修复\宝塔网络代理修复.exe\"" /f
```

卸载：

```cmd
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "宝塔网络代理修复" /f
taskkill /f /im "宝塔网络代理修复.exe"
rmdir /s /q "%LOCALAPPDATA%\宝塔网络代理修复"
```

## 变更记录

### v1.1.1
- 单实例从命名互斥体改为 `run.lock` 文件锁，修复僵尸句柄导致程序无法启动
- `build` / `install` / `uninstall` 改为 `.ps1` 实现 + 纯 ASCII `.cmd` 外壳，修复中文乱码

### v1.1.0
- 新增死代理自愈：代理开启但端口无监听，持续 `grace_seconds` 后自动关闭代理 + 刷新 DNS
- 例外层改为合并策略，保留其它程序写入的例外项
- 只对本机回环代理做端口探测，远端代理不干预
- 代理设置变更后通知 WinINET 立即刷新
- 日志超过 1 MB 自动轮转为 `run.log.1`

### v1.0.0
- 代理开启时自动维护宝塔面板地址的 Windows 代理例外

## 维护与发布

```cmd
build.cmd        :: 编译到 bin\
install.cmd      :: 装到本机并重启
git add -A
git commit -m "vX.Y.Z: 变更说明"
git push
```

注意事项：

- **换行**：`.cmd` / `.ps1` 由 `.gitattributes` 固定为 CRLF，改脚本后不要手动转换，
  否则 cmd.exe 解析会掉每行首字符（历史故障）。
- **编码**：`.cmd` 一律纯 ASCII；中文文案只放在 `.ps1`（UTF-8 + BOM）里，
  避免 UTF-8/GBK 混用把变量名写成乱码。
- **凭据**：HTTPS 推送，凭据存于 Windows 凭据管理器（`credential.helper=wincred`），
  无需在仓库里保存任何密钥。
- **网络**：直连 GitHub 需要梯子；仓库已配置 `http.proxy` 指向本机代理端口，
  代理端口变化时用 `git config http.proxy http://127.0.0.1:<端口>` 更新。
