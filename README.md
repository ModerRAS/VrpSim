# 华为 VRP 交换机模拟终端（加固演练用）

[![CI](https://github.com/ModerRAS/VrpSim/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/ModerRAS/VrpSim/actions/workflows/ci.yml)
[![Pages](https://github.com/ModerRAS/VrpSim/actions/workflows/pages.yml/badge.svg?branch=main)](https://github.com/ModerRAS/VrpSim/actions/workflows/pages.yml)

按 2026-11-12 / 11-13 两台现场配置（V200R011C10SPC600）写的**可交互 CLI 模拟器**：
Tab 补全、`?` 帮助、缩写执行、视图切换、`display current-configuration` / `display this` 还原、
`interface range` 批量配置、`port-group` 端口组、外加 14 项加固自查评分。
C# / .NET 10 实现，无第三方依赖，发布为 **NativeAOT 原生单文件 exe**（约 3.5 MB，不需要 .NET 运行时）。

## 网页版（GitHub Pages，免安装）

在线地址：**https://vrpsim.miaostay.com/** —— 纯前端（Blazor WebAssembly），不需要服务器，浏览器打开即用。
（该仓库 Pages 绑定了自定义子域名 `vrpsim.miaostay.com`；如果你的网络解析不到，备用地址见 GitHub Pages 设置页。）

- 和桌面版共用同一套命令树/补全/`?`/`check`/`display current-configuration` 逻辑（源码在 `web/`，共享根目录的 `Cli.cs`/`Commands.cs`/`Device.cs` 等）
- 存档只存在当前页面内存里（刷新即重置，`save` 写页面内配置）——桌面版才落盘 `vrp-sim-state.json`
- 首次加载约 2–3 MB（WASM 运行时，之后浏览器缓存）；构建/部署见 `.github/workflows/pages.yml`
- 自定义域名走 Cloudflare 代理（`vrpsim.miaostay.com` CNAME → `moderras.github.io`）；如遇 522/证书异常，把该记录临时切成 DNS only 或 SSL 模式改 Full 即可
- 刷新页面 = 重新开局（当前版本不做 localStorage 存档）

## 快速开始

```text
dist\VrpSim.exe
```

- Console 登录：`admin` / `admin@huawei.com`（首次登录会像真机一样提示改初始口令：必须先输对旧口令，
  连续错 3 次则本次不改；口令输入可用 `Backspace` 退格、按 `n` 回登录界面）
- 提示符：`<HUAWEI>` 用户视图 → `system-view` → `[HUAWEI]`；进入子视图后提示符带视图名
  （`[HUAWEI-aaa]`、`[HUAWEI-GigabitEthernet0/0/1]`、`[HUAWEI-ui-vty0-4]`、`[HUAWEI-port-group-g1]` …）
- 交互模式**退出时自动存档**到 exe 同目录的 `vrp-sim-state.json`，下次启动自动恢复；
  `--fresh` 强制新开局，`--persist` 让批处理模式也存档，`reset` 在用户视图恢复出厂配置；**要彻底重来（含后台计时累计）就删 exe 同目录的 `vrp-sim-state.json` 或加 `--fresh`**

**运行要求**：`dist/VrpSim.exe` 是 NativeAOT 原生 exe（x86，32/64 位 Windows 均可直接跑；无 .NET 运行时依赖，系统自带 UCRT 即可）；
从源码构建/发布需要 .NET 10 SDK（release.yml 用 `dotnet publish -r win-x64 -p:PublishAot=true`，CI 的 windows-latest 自带 MSVC C++ 工具链）。

命令行参数：

| 参数 | 作用 |
| --- | --- |
| （无） | 交互模式，Tab / ? / 方向键 / 历史可用 |
| `--batch` | 从标准输入读命令（脚本、管道测试用），不要求 TTY，不启用登录 |
| `--selftest` | 内置"整套加固 → 14 项自查 + 90 条逐 token 补全快照 + 顺序/大小写混用/接口写法/NULL 写法/VLAN 视图/全接口 dis cu/无报错冒烟/新接口类型/HELP FORMAT/TIMER 回归"，全过返回 0（输出 `SELFTEST OK`） |
| `--no-login` | 跳过 console 登录（配合 --batch） |
| `--fresh` | 忽略上次存档，按出厂状态重新开始 |
| `--persist` | 批处理模式也读写 `vrp-sim-state.json`（默认只有交互模式存档） |
| `--complete "<文本>"` | 打印该行 Tab 补全结果（调试补全用） |
| `--view <视图>` | 配合 `--complete` 指定起始视图（`user/system/aaa/acl/iface/ui/lupp/range/pg`） |

示例：

```powershell
.\dist\VrpSim.exe --selftest
@('system-view','display ?','return') -join "`n" | .\dist\VrpSim.exe --batch
.\dist\VrpSim.exe --view system --complete "snmp-agent comm"
```

## 发布产物（NativeAOT，v1.5.0 起）

Release **只发一份 x86 原生单文件** `VrpSim.exe`（约 3.1 MB，冷启动 ~26 ms）：**32 位 Windows 与 64 位 Windows（WOW64）都能直接跑**，
不需要 .NET 运行时；AOT 会额外产生 PDB，不随发布上传。

| 资产 | 架构 | 体积 | 冷启动（本机 10 次均值） | 说明 |
| --- | --- | --- | --- | --- |
| `VrpSim.exe` | win-x86 | 约 3.12 MB | ~26 ms | 32 位 Windows / 64 位 Windows WOW64 均可用 |
| `VrpSim.exe.sha256` | — | — | — | 对应资产的 SHA-256 校验 |

对照：旧 self-contained 单文件 37.6 MB、trimmed 单文件 11.3 MB——AOT 小 12×/3.6×，冷启动也快 3.8×。
**需要 x64 原生版**可本地自建（CI 不再上传 x64 资产）：

```powershell
dotnet publish VrpSim.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:StripSymbols=true -p:DebugType=None -o out
```

（本机 `dist/VrpSim.exe` 与 Release 同口径：x86 AOT、约 3.14 MB、无需 .NET 运行时。发布需 .NET 10 SDK + MSVC C++ 工具链；
本机 VS2026 若未被 ILCompiler 自动发现，先 `call vcvarsall.bat x86` 并加 `-p:IlcUseEnvironmentalTools=true`。）

## 计时（v1.6.1，自动累计，check 上屏）

演练用时在后台自动累计，**只在 `check` 的输出末尾显示一行**（quit / banner / 普通命令都不显示），不参与 `check` 评分、不写进配置：

- `check` 末尾：` 本次演练用时：12:34`（<1h 用 `mm:ss`，≥1h 用 `h:mm:ss`；= 已持久化累计 + 本次会话至今，即“第一次登录起累计到这次 check”的总用时）。
- **起点**：交互模式 = 第一次打印 `Username:`、等待输入用户名的那一刻；`--no-login` / `--batch` = 进程启动。
- **累计内容**：每成功执行一条命令记一步；用时只算**实际运行时长**（单调时钟，改系统时间/时区不影响），关掉再开继续累计（应用没运行的时间不计）。
- **落盘位置**：`vrp-sim-state.json` 的 `ElapsedMs`（累计毫秒）、`Steps`（累计成功命令数）、`FirstLoginAt`（首次登录开始，本地 ISO 8601 带偏移）、`UpdatedAt`（最近保存时间）。
- **彻底重来**：删掉 exe 同目录的 `vrp-sim-state.json`（或用 `--fresh`），累计清零重新开始。

## 键位


| 键 | 行为 |
| --- | --- |
| `Tab` | 补全当前词：唯一候选直接补全并加空格；多个候选列出后保留输入 |
| `?` | 显示当前位置所有候选：**两列布局**（候选 + 英文说明，与真机 console 一致），参数占位符排在候选值前面；命令行位置不显示 `<cr>` |
| `←` `→` `Home` `End` | 移动光标 |
| `↑` `↓` | 历史命令 |
| `Backspace` `Delete` | 删除 |
| `Ctrl+C` | 清空当前行 |
| `Ctrl+Z` | 回到用户视图（等价 return） |

补全按 VRP 的缩写习惯工作：`disp` → `display`，`snmp-a` → `snmp-agent`，`sys` 能直接执行 `system-view`；
关键字命中后按**规范全称**执行（`dis cu` 可用）。参数位置给候选：接口类型、已建 ACL、本地用户、VLAN、read/write、端口组名等。
接口输入按真机口径：类型可写**唯一前缀缩写**（`int vlan 20`、`int g 0/0/1`），缩写+编号也**可以连写**（`int g0/0/1`、`interface vlan20`）；
全称写法 `interface GigabitEthernet 0/0/1`、`interface Vlanif 8`、`interface Ethernet 0/0/1` 同样接受；
但**全称连写**（`interface GigabitEthernet0/0/1` / `interface Ethernet0/0/1` / `interface Vlanif8`）按真机报 `Wrong parameter found at '^' position.`（^ 指向连写 token），模拟器同口径。
接口类型全称：`Eth-Trunk` / `Ethernet` / `GigabitEthernet` / `LoopBack` / `NULL` / `Tunnel` / `Vlanif`（大小写不限）；可缩写为唯一前缀（`vlan`→`Vlanif`、`g`/`gi`→`GigabitEthernet`、`lo`→`LoopBack`、`eth-`→`Eth-Trunk`…），有歧义的前缀（`e`/`eth`）不认；
`Eth-Trunk` / `LoopBack` / `Tunnel` 按真机是"进视图即创建"（编号范围分别 0-127 / 0-1023 / 0-1023，越界报错），进 dis cu 与 `display this` 正常；
`interface range` 只支持批量可配的 `Ethernet` / `GigabitEthernet` / `Vlanif` 三种（类型同样可缩写）；
**NULL 接口**：候选只出类型 `NULL`；`interface NULL 0` 与 `interface NULL0` 两种写法都接受，都进 `[HUAWEI-NULL0]`，
`display this` 与配置渲染输出 `interface NULL0`（编号只允许 0，其它编号报错）；
`interface range` 两端类型可混用大小写（`interface range ethernet 0/0/1 to Ethernet 0/0/16` 合法），进视图与配置渲染统一成规范拼写
（配置回显仍是真机的连写形式 `interface GigabitEthernet0/0/1`）。

### 补全快照（第一批：加固/配置演练路径）

`Tab` / `?` 已按真机口径逐 token 补齐（覆盖 `演练脚本-14项.txt` 的约 60 条路径；`--selftest` 内置 90 条逐 token 快照 + 顺序/大小写混用/接口写法/NULL 写法/VLAN 视图/全接口 dis cu/无报错冒烟/新接口类型/HELP FORMAT/TIMER 回归）。代表性位置：

| 输入 | `Tab`（无占位符） | `?`（含类型占位符） |
| --- | --- | --- |
| `sysname ` | （自由文本） | `STRING<1-246>` |
| `interface ` | `range` `Eth-Trunk` `Ethernet` `GigabitEthernet` `LoopBack` `NULL` `Tunnel` `Vlanif`（顺序固定，**不列具体接口名**） | 同左（不显示占位符） |
| `interface ethernet` | 唯一补成 `Ethernet` | — |
| `interface Vlanif ` | `1` `8`（按 VLAN 给） | `<interface-number>` + VLAN 号 |
| `interface NULL ` | `0`（NULL 只有编号 0） | `<interface-number>` + `0` |
| `interface range ` | `Ethernet` `GigabitEthernet` `Vlanif`（不列具体接口名） | 同左 |
| `info-center ` | `channel` `console` `enable` `filter-id` `local` `logbuffer` `loghost` `monitor` `rate-limit` `snmp` `source` `statistic-suppress` `timestamp` `trapbuffer` | 同左 |
| `info-center loghost ` | `ipv6` `source` | `<X.X.X.X>` `ipv6` `source` |
| `acl ` | `ipv6` `name` `number` + 已建 ACL 号 | `INTEGER<2000-2999>` `INTEGER<3000-3999>` `INTEGER<4000-4999>` `INTEGER<5000-5999>` `ipv6` `name` `number` |
| `user-interface ` | `console` `current` `maximum-vty` `vty` | 同左 + `INTEGER<0,34-48,50-54>` |
| `snmp-agent sys-info ` | `contact` `location` `version` | 同左 |
| `snmp-agent target-host ` | `inform` `trap` | 同左 |
| `snmp-agent trap ` | `disable` `enable` `life` `queue-size` `source` `suppress` `type` | 同左 |
| `stelnet ` | `server` `ipv4` `ipv6` | 同左 + `STRING<1-255>` |
| `rsa ` | `local-key-pair` `peer-public-key` | 同左 |
| `aaa ` | `abnormal-offline-record` `offline-record` `online-fail-record` | 同左 + `<cr>`（排版层不显示） |
| `snmp-agent ` | `acl` `community` `extend` `group` `inform` `local-engineid` `manner` `mib-view` `notification-log` `notify-filter-profile` `packet` `packet-priority` `set-cache` `sys-info` `target-host` `trap` `usm-user` | 同左（各带英文说明） |
| `snmp-agent acl ` | — | `INTEGER<2000-3999>`（Basic or advanced ACL） |
| `aaa local-user <u> ` | `access-limit` `ftp-directory` `idle-timeout` `password` `privilege` `service-type` `state` | 同左 |
| `aaa local-user <u> password ` | `cipher` `irreversible-cipher` `simple` | 同左 |
| `aaa local-user <u> service-type ` | `8021x` `bind` `ftp` `http` `ppp` `ssh` `telnet` `terminal` `web` `x25-pad` | 同左 |
| `acl rule <id> deny tcp ` | `destination` `destination-port` `dscp` `fragment` `logging` `precedence` `source` `source-port` `tcp-flag` `time-range` `tos` | 同左 |
| `acl rule <id> permit source ` | `any` | `<X.X.X.X>` + `any` |
| `acl description ` / `iface description ` / `vlan description ` | — | `TEXT`（带各自长度说明） |
| `iface port ` | `add-tag` `discard` `hybrid` `link-flap` `link-type` `mux-vlan` `priority` `type` `vlan-mapping` `vlan-stacking` | 同左 |
| `iface port link-type ` | `access` `dot1q-tunnel` `hybrid` `trunk` | 同左 |
| `ui acl ` | `ipv6` + 已建 ACL 号 | `INTEGER<2000-3999>` + `ipv6` |
| `interface range ethernet ` | `0/0/1` … `0/0/16`（类型大小写不限） | `<interface-number>` |
| `interface range Ethernet 0/0/1 to Ethernet ` | `0/0/1` … `0/0/16` | `<interface-number>` |
| `vlan ` | `batch`（唯一，不自动插数字） | `batch` + `INTEGER<1-4094>`（batch 在前） |
| `vlan batch ` | `1` `8`（已有 VLAN） | `INTEGER<1-4094>` |
| `undo vlan ` | 已建 VLAN 号 | `INTEGER<1-4094>` + 已建 VLAN 号 |
| `acl ` / `acl number ` | `number` + 已建 ACL 号 | `INTEGER<2000-3999>` + 已建 ACL 号 |
| `rule ` | `permit` `deny` | `INTEGER<1-65535>` |
| `rule 5 deny tcp destination-port ` | `eq` `gt` `lt` `range` | 同左 |
| `rule … destination-port eq ` | `135` `137` `445` `3389` `ftp` `ssh` … | `INTEGER<0-65535>` + 常用端口名 |
| `local-user ` | 已有本地用户 | `STRING<1-63>` + 已有本地用户 |
| `local-user u password irreversible-cipher ` | （自由文本） | `STRING<8-64>` |
| `local-user u service-type ` | `terminal` `ssh` `telnet` `ftp` `http` | 同左 |
| `port default vlan ` | `1` `8`（已有 VLAN） | `INTEGER<1-4094>` |
| `traffic-filter inbound acl ` | `2000` `2001` `3000` | `INTEGER<2000-3999>` |
| `snmp-agent community read ` | `cipher` `simple` | 同左 + `STRING<1-32>` |
| `clock timezone BeiJin ` | `add` `minus` | 同左 |
| `user-interface vty ` | （自由文本） | `INTEGER<0-20>` |
| `undo `（各视图） | 该视图可 undo 的命令 | 同左 |

占位符记法统一为 `INTEGER<范围>` / `STRING<长度>` / `{a|b|c}`；参数候选按上下文生成（接口号→VLAN 号或端口号、
ACL/VLAN/用户/端口名），不存在的命令不会乱认（`show version` 直接报 `Unrecognized command`）。

`?` / Tab 候选按真机 console 格式输出：**两列**（第一列宽 = 本表最长候选 + 2，第二列是英文说明），说明超宽按控制台宽度折行、
续行缩进对齐第二列；命令行位置**不显示 `<cr>`**；列表结束后只空一行再重绘提示符。示例（`interface ?`）：

```
[HUAWEI]interface ?
  range            Set interface range
  Eth-Trunk        Ethernet-Trunk interface
  Ethernet         Ethernet interface
  GigabitEthernet  GigabitEthernet interface
  LoopBack         LoopBack interface
  NULL             NULL interface
  Tunnel           Tunnel interface
  Vlanif           Vlan interface
[HUAWEI]interface
```

说明文字按 eNSP 真机 transcripts 校准（如 `INTEGER<2000-2999>  Basic access-list(add to current using rules)`、
`life  Set the trap aging time`）；`--selftest` 的 HELP FORMAT 回归守住列宽/说明对齐/无 `<cr>`/折行。

`display current-configuration` 按真机口径打印**全部接口**（16×Ethernet0/0/1-16、12×GigabitEthernet0/0/1-12、Vlanif1、NULL0），
空口也输出 `#` / `interface X` / `#` 空块；`display current-configuration | include interface` 可过滤出全部接口行。

## 覆盖的命令（按视图）

| 视图 | 命令 |
| --- | --- |
| 用户 `<HUAWEI>` | `system-view`、`quit`、`return`、`display …`、`save`、`check`、`reset` |
| 系统 `[HUAWEI]` | `sysname`、`aaa`（含 abnormal/offline/online-fail-record 子命令）、`acl [number] <2000-5999>`（2000-2999/3000-3999/4000-4999/5000-5999 四段 + `ipv6`/`name` 写法）、`interface <类型> <编号>`（类型可缩写、缩写可连写：`int vlan 20` / `int g0/0/1`；全称连写报错；Eth-Trunk/LoopBack/Tunnel 进视图即创建）、`interface range <类型> <编号> to <类型> <编号>`、`port-group <名>`、`vlan batch <id…>` / `vlan batch <id> to <id>`、`vlan <id>`（进 VLAN 视图）、`stelnet (ipv4/ipv6) server enable` / `stelnet <IP>`、`rsa local-key-pair create` / `rsa peer-public-key`、`ssh client first-time`、`ssh user`、`snmp-agent`（acl <2000-3999> / community / sys-info contact\|location\|version / target-host inform\|trap / trap disable\|enable\|life\|queue-size\|source\|suppress\|type / extend / group / inform / local-engineid / manner / mib-view / notification-log / notify-filter-profile / packet / packet-priority / set-cache / usm-user）、`info-center`（channel/console/enable/filter-id/local/logbuffer/loghost/monitor/rate-limit/snmp/source/statistic-suppress/timestamp/trapbuffer）、`clock timezone`、`clock datetime`、`user-interface [<整数>\|console\|current\|maximum-vty\|vty]`、`undo …` |
| AAA `[HUAWEI-aaa]` | `local-user <u> password cipher/irreversible-cipher/simple <pw> [privilege level <n>]`（一条命令可带权限；`service-type` 必须另起一条）、`local-user <u> privilege level`、`local-user <u> service-type`（8021x/bind/ftp/http/ppp/ssh/telnet/terminal/web/x25-pad）、`local-user <u> access-limit`/`ftp-directory`/`state`（接受不落配置）、`local-aaa-user wrong-password`、`local-aaa-user password policy administrator`、`user-password complexity-check`、`undo local-user` |
| 本地管理员密码策略 `[HUAWEI-aaa-lupp-admin]` | `password expire <0-999>`、`password history record number <0-5>`、`display this`、`undo …`（`quit` 回 AAA 视图） |
| ACL `[HUAWEI-acl-adv-3000]` | `rule [<id>] permit/deny [ip \| <gre/icmp/igmp/ipinip/ospf/tcp/udp> [source/destination <IP> <wildcard> \| source-port/destination-port eq/gt/lt/range … \| dscp/precedence/tos/tcp-flag/time-range/fragment/logging]]`、`rule <id> description`、`description TEXT`、`undo rule` |
| 接口 `[HUAWEI-GigabitEthernet0/0/1]` | `description TEXT`、`port link-type`（access/dot1q-tunnel/hybrid/trunk）、`port default vlan`、`port trunk allow-pass vlan`、`port add-tag/discard/hybrid/link-flap/mux-vlan/priority/type/vlan-mapping/vlan-stacking`（接受不落配置）、**`port-security enable` / `port-security max-mac-num <1-1024>` / `port-security mac-address sticky`**（未 enable 时配 max-mac-num 报 `Error: Port security is not enabled.`，落地进配置）、`traffic-filter inbound/outbound acl`、`shutdown` / `undo shutdown`、`ip address <IP> <点分掩码 | 前缀长度>`（仅 Vlanif；`ip address 192.168.12.2 24` 等价 `… 255.255.255.0`）、`ip source`（接受）、`display this` |
| 接口范围 `[HUAWEI-port-group]`（模拟器扩展） | 用 `interface range <类型> <编号> to <类型> <编号>` 或真机缩写连写 `interface range g0/0/2 to g0/0/11` 进入（两端类型可混用大小写），支持 `shutdown`、`description`、`port …`、`port-security …`、`traffic-filter …` 与对应 `undo` 批量生效（**逐成员回显** `[HUAWEI-GigabitEthernet0/0/2]shutdown`，与真机一致）；`ip address` 不支持 |
| 端口组 `[HUAWEI-port-group-<名>]` | `group-member <接口> [to <接口2>]`、`undo group-member [<接口>]`，以及接口视图的 `shutdown` / `description` / `port …` / `traffic-filter …` 批量作用于成员口 |
| VLAN `[HUAWEI-vlan<id>]` | `description <text>`、`name <text>`、`undo description`、`undo name`、`display this`；`quit` 回系统视图；`vlan <id>` 已存在直接进、不存在则创建。`display this` 真机口径：**空 VLAN 只有 `#` + `return`**；有 description/name 才是 `#` / `vlan 200` / ` description …` / `#` / `return`（1 空格缩进） |
| VTY `[HUAWEI-ui-vty0-4]` | `authentication-mode`、`acl <n> inbound`、`idle-timeout`、`protocol inbound` |

`display` 支持：`current-configuration`（含 `| include / begin / exclude <文本>`）、`saved-configuration`、`version`、`clock`、`this`（各视图都按真机格式 `#` 包裹、末尾 `return`）、`interface [brief]`、`acl [all\|编号]`、`telnet server status`、`ftp server`、`http server`、`snmp-agent community`、`user-interface`、`local-user`、`local-aaa-user password policy administrator`（策略视图内走 `display this`）、`logbuffer`、`cpu-usage`、`memory-usage`、`power`、`vlan`、`ip interface brief`、`stelnet server`、`ssh server status`、`users`。

### 几条容易记错的语法（已按真机行为实现）

- **ACL**：`acl 3000` 与 `acl number 3000` 等价；`acl 2000` 进的是 basic 视图，`acl 3000` 进 adv 视图。
- **rule**：`rule <id> permit|deny …` 的 **id 可省略**，取值范围 1–65535；省略时按"当前最大 id + 5"自动编号（空 ACL 首条为 5）。
  例如 `rule permit ip` → `rule 5 permit ip`，再来一条 `rule deny tcp destination-port eq 135` → `rule 10 …`。
  显式写不连续的数字（如 `rule 100 deny …`）也合法。basic ACL 里不能用 ip/tcp/udp 关键字。
- **补全口径**：`rule ` 后面按 Tab 只补 `permit` / `deny`，不会替你填编号；按 `?` 才显示 `INTEGER<1-65535>` 占位（编号按当前 ACL 内最大 id+5 自动生成，不跨 ACL 串号）。
  参数位按上下文给候选：`interface Vlanif ` 给 VLAN 号、`interface GigabitEthernet ` 给端口号、`acl number ` 给已建 ACL、
  `port default vlan ` 给已有 VLAN、`local-user ` 给本地用户、`traffic-filter inbound acl ` 给 ACL 号、`destination-port` 给常用端口名。
- **本地用户**：`local-user user password cipher Aa123!@# privilege level 15` 一条命令有效（口令 + 权限）；`service-type` 不能挂在同一条后面，必须单独 `local-user user service-type terminal ssh`。
- **口令占位**：`local-user u password cipher ?` 显示 `STRING<8-64>`（类型提示），不是 `<password>`。
- **`display this`**：按真机格式输出——配置块前后各一行 `#`，末尾跟 `return`；**系统视图只显示系统视图自己的配置行**（sysname / loghost / vlan batch / 服务开关 / snmp / stelnet），不含 aaa/acl/接口/VLAN 等子视图块（子视图块只在 `display current-configuration` 里出现）；`interface range` 下按成员口一段一段分开，
  `[HUAWEI-aaa-lupp-admin]` 下输出的是嵌在 `aaa` 里的策略块。
- **`save` 两段式**：先输出 `The current configuration will be written to the device.`，
  再单独一行问 `Are you sure to continue?[Y/N]:`，同意后输出 `Now saving the current configuration to the slot 0.` 与
  `Save the configuration successfully.`（批处理模式下自动视为 y）。
- **`interface range` vs `port-group`**：`interface range A to B` 是本模拟器加的**批量快捷方式**（提示符 `[HUAWEI-port-group]`，真机没有这条命令）；
  真机批量配置应建端口组：`port-group g1` → `group-member GigabitEthernet0/0/2 to GigabitEthernet0/0/5` → 在组里敲批量命令。
  两者都会在 `display current-configuration` 里看到区别：`interface range` 改的是各成员口的配置，`port-group` 会多出 `port-group g1` / `group-member …` 两行。

## 模拟器专有：check（14 项加固自查）

`check` 不是 VRP 命令，只在本模拟器里存在。它按《华为交换机加固手册》14 项判定并给下一步提示
（输出末尾会附一行 ` 本次演练用时：mm:ss`，见上面的「计时」小节）：

| 项 | 判定要点 |
| --- | --- |
| 1 账户及本地认证 | con0=aaa、idle-timeout ≤5、失败锁定、存在 privilege 15 用户 |
| 2 远程 SSH | stelnet 开、vty0-4 绑 acl 2000（含 permit）、aaa、protocol inbound ssh、idle |
| 3 日志审计 | info-center loghost 已配 |
| 4 密码复杂度 | complexity-check + password policy administrator + wrong-password |
| 5 高危端口 | acl 3000 覆盖 135/137-139/445/3389（tcp+udp）+ rule permit ip + 双向 traffic-filter |
| 6 SNMP | version 含 v2c/v3、团体字带 ACL、trap enable + target-host |
| 7 服务收敛 | telnet / ftp / http 全关 |
| 8 默认路由 | 仅路由器，不适用 |
| 9 空闲端口 | 业务口 up、其余全 shutdown |
| 10 时区对时 | 时区已配且时钟 ≥2025 |
| 11 OSPF MD5 | 仅路由器，不适用 |
| 12 资源利用率 | 模拟固定 12% / 34% |
| 13 双电源接地 | 现场项，不判分 |
| 14 网监接入 | loghost/trap/acl 2001 与三个 IP 自洽、Vlanif 与监测装置同网段 |

## 出厂状态（故意还原现场问题）

- `telnet server` **开**、`http server` **开**、`ftp server` 关、`stelnet` 关
- `vty 0 4` / `vty 16 20` **没有任何限制**（auth 缺省、protocol 缺省 all、无 ACL）
- 本地用户只有 `admin`（初始口令 `admin@huawei.com`，首次登录强制改），`local-aaa-user password policy administrator` 已开
- 时钟停在 **2000-04-12**，无时区
- 28 个物理口中 G0/0/1、G0/0/12 有"网线"（Cabled），其余无链路但**都未 shutdown**
- 只有 VLAN 1；无 ACL、无 SNMP、无日志主机、无端口组

> G0/0/1 = 业务接入（access VLAN 8），G0/0/12 = 上行 trunk；监测装置 192.168.12.1、运维机 192.168.12.15、本机 `interface Vlanif 8` = 192.168.12.4。
> 这些写在 `Program.cs` 的 `HardeningScript()` 和 `Device.Factory()` 里，改常量即可换场景。

## 演练流程（照 `演练脚本-14项.txt` 顺序粘）

1. `display current-configuration` 先看底账，`check` 看当前得几分
2. `sysname` / `vlan batch 8` / `interface Vlanif 8` 地址
3. 三个 ACL（2000 管理源、2001 监测源、3000 高危端口 + `rule permit ip` 兜底）
4. 接口：G0/0/1 access + 双向 traffic-filter，G0/0/12 trunk + 双向 traffic-filter，其余全部 shutdown
   （脚本里既有逐口 shutdown 的真机写法，末尾也用 `interface range Ethernet 0/0/1 to Ethernet 0/0/16` 演示批量写法）
5. AAA：失败锁定、复杂度、建 `opsadmin`（>6 字符）、删 admin；策略视图 `local-aaa-user password policy administrator` 里敲完要 `quit` 回 AAA
6. VTY：con0 / vty0-4 / **vty16-20 三段都要配**（acl + aaa + ssh + idle）
7. SNMP + info-center 指向监测装置
8. 关 telnet/ftp/http，开 stelnet，配时区与时钟
9. `save` 存盘（生成 `saved-config.cfg`，可用 `display saved-configuration` 回看）
10. `check` 复核，应该 11/11 通过

## 不模拟的部分（已知限制，别拿它当"设备验证"）

- 真实 SSH/Telnet 会话、密钥协商、ACL 命中计数（`display acl` 只显示规则与行数，不计次数）
- 端口物理状态随网线变化：这里只按"是否 shutdown + 是否 cabled"推断 PHY/Protocol
- 路由/OSPF 整块不模拟（因此自查项 8、11 恒为不适用）；无 STP、无 DHCP、无堆叠
- NTP 实际对时（只记配置）、双电源/接地、CPU/内存真实负载（固定值）
- 口令策略只做"配置存在性"判定：`password expire` / `password history` 不真正过期、不记历史口令；
  `local-user … password cipher` 收到的口令原样存进 `vrp-sim-state.json`（训练用，别在真机导入这种密文）
- 计时相关的真实行为（idle-timeout 到点踢人、失败锁定到点解锁）不实现，只记录配置
- `save` / `display saved-configuration` 操作的是**当前工作目录**的 `saved-config.cfg` 文本，没有文件系统、没有 `vrpcfg.zip`
- 配置持久化（`vrp-sim-state.json`）是模拟器专有行为：真机重启后未 `save` 的配置会丢，这里交互模式退出会自动存档
- 只覆盖加固演练要用的命令子集；`display` 的详细接口统计、`debug`、`reset` 之外的系统维护命令都没有
- 补全快照（第一批）只覆盖加固/配置演练路径（约 60 条），其余命令的候选/占位符留待后续批次调整
- `?` 候选按"真机能看到的都补"原则处理：**有语义的落配置**（如 interface 类型创建、SNMP contact/location、trap enable/disable、ACL 区间）；
  **暂无语义的只做到"可见 + 敲了不报错"**（如 info-center 多数子命令、acl ipv6/name、user-interface maximum-vty、snmp trap life/queue-size/source/suppress/type），
  这些命令不会进 `display current-configuration`（自检里用 78 条"无报错冒烟"守着）
- 接口 `Eth-Trunk` / `LoopBack` / `Tunnel` 只实现"进视图即创建 + description/shutdown/dis cu/display this"最小行为，链路聚合/VPN 隧道语义不模拟
- `interface` 接受"类型 + 编号"分开写、类型缩写（`int vlan 20`）、缩写+编号连写（`int g0/0/1`）；**全称连写**（`GigabitEthernet0/0/1` / `Ethernet0/0/1`）仍按真机报错；配置回显是连写形式
- **MEth 不建模**（用户口径：16 个百兆 + 12 个千兆，不模拟管理口），`interface ?` 不显示 MEth；eNSP S5700 的那台 24GE 机型才会有
- `?` 排版层不显示 `<cr>`（数据层保留可回车判定；真机 eNSP 会在可回车位置列出 `<cr>`，用户反馈"开头有不是表格的东西"后按两列格式统一去掉）
- v1.5.0 第二批按 gap 报告补的候选里，`snmp-agent` 的 extend/group/mib-view/…、`local-user` 的 access-limit/ftp-directory/state、
  ACL 的 fragment/logging/time-range/tcp-flag 等**只做到"可见 + 敲了不报错"**，不落配置也不影响 `check` 评分；`port dot1q-tunnel` 仅记录 link-type 值，QinQ 语义不模拟
- VLAN 视图只实现 `description` / `name` 最小集，真机的 `statistics`、`mac-vlan` 等未实现
- `group-member` 仍按"完整接口名"输入与候选（如 `group-member GigabitEthernet0/0/1`），与 `interface` 的新口径暂不一致，待确认
- **真机 console 日志对照补全（v1.6.4，用户提供 session2.log）**：
  - `ip address` 掩码支持**点分**与**前缀长度**两种写法（`ip address 192.168.12.2 24` 等价 `... 255.255.255.0`），配置统一按点分渲染；缺掩码报 `Incomplete command`；
  - **port-security**：`enable` / `max-mac-num <1-1024>` / `mac-address sticky`（未 enable 时配 max-mac-num 报 `Error: Port security is not enabled.`），落进接口配置块；
  - `interface range g0/0/2 to g0/0/11` 缩写连写端点可用，批量执行时**逐成员回显** `[HUAWEI-GigabitEthernet0/0/2]shutdown`；
  - 真机 Info/Warning 文案对齐：`vlan batch`、`port link-type`、`port trunk allow-pass vlan` 的 "This operation may take a few seconds..."、ACL 创建 SACL 提示、新建用户与服务类型两条 Info、`info-center enable/disable`、`stelnet server enable`；
  - `stelnet server enable` 同时启用 IPv4/IPv6（dis cu 渲染两行）；`display this` 在系统视图只显示系统视图自己的配置行；
  - `snmp-agent community read cipher <pw>`（不带 acl）可直接执行（修了结尾可选段不判完成导致的 "Incomplete"）；
  - `vlan batch 10 to 12` 区间写法、console 视图提示符 `[HUAWEI-ui-console0]`。
- **eNSP 版本差异**：eNSP 内置 S5700 是 **V200R001C00 / VRP 5.110**（2016 年前后），比真机 **V200R011C10SPC600** 老 6+ 代；
  本模拟器的接口写法/候选/VLAN 视图等口径以**用户真机 V200R011**为准，eNSP 只作旁证（两者命令行文案、默认值可能有出入，冲突不要照抄 eNSP）。
  已知具体差异：eNSP 这代没有 `Ethernet` 接口类型、`GigabitEthernet` 只认连写、没有 `interface range`、`acl` 区间与 `info-center` 子命令集合也不同——
  本模拟器保留用户现场口径的 `Ethernet` 与"类型 编号分开写"，不照搬 eNSP

## 扩展

加一条命令三步：`CommandSets` 对应视图列表里加 `C("命令", "帮助", 处理函数).T(模板…)` → 模板用
`new Lit("关键字")` / `new Val("参数名","<提示>",候选源,可选取值约束)` / `new Opt(...)` / `new Rep(...)` 组起来 →
处理函数里改 `Device`。加视图时补 `CommandSets.For`、`Session.Prompt`，需要持久化的字段同步加进 `StateStore`。

## 文件

| 文件 | 说明 |
| --- | --- |
| `VrpSim.csproj` | net10.0 控制台项目 |
| `Cli.cs` | 命令树、模板匹配、Tab/? 候选计算（占位符/取值约束/上下文候选/状态去重） |
| `Device.cs` | 设备状态模型 + `display current-configuration` / 各视图渲染 |
| `Commands.cs` | 9 个视图的命令定义与处理函数（含 port-group / interface range） |
| `Checks.cs` | 14 项加固判定与报告 |
| `Editor.cs` | 行编辑器（Tab/?/方向键/历史） |
| `Program.cs` | 启动、console 登录、批处理、自检脚本（含 90 条逐 token 快照 + 顺序/大小写混用/接口写法/NULL 写法/VLAN 视图/全接口/无报错冒烟/新接口类型/HELP FORMAT 回归） |
| `HelpFormat.cs` | `?`/Tab 候选排版：两列 + 说明折行 + 命令位置去 `<cr>` |
| `RunTimer.cs` | 后台计时：t0（登录开始/进程启动）+ 单调时钟累计 + 步数；随 `vrp-sim-state.json` 持久化（ElapsedMs/Steps/FirstLoginAt/UpdatedAt）；只在 `check` 末尾上屏一行，不入评分、不进配置 |
| `StateStore.cs` | 交互存档 `vrp-sim-state.json` 的读写 |
| `演练脚本-14项.txt` | 可直接粘进模拟器的整套加固命令 |
| `saved-config.cfg` | `save` 落盘的当前配置（运行时生成） |
| `dist/VrpSim.exe` | 本地发布产物（NativeAOT **x86** 原生单文件，无需 .NET 运行时；32/64 位 Windows 均可跑，3.14 MB） |
| `.github/workflows/release.yml` | Release：单资产 x86 AOT 发布（跑 selftest + 生成 sha256，PDB 不上传；x64 可本地自建） |
| `Platform.cs` | 平台抽象：输出/宽度/存档/reset 在控制台（Console/File）与网页（JS 回调/内存）间切换 |
| `web/` | Blazor WebAssembly 网页版：`EngineService.cs`（引擎桥）、`TerminalHost.razor`、`wwwroot/`（终端 UI/JS/CSS），共享根目录命令逻辑 |
| `.github/workflows/pages.yml` | 网页版构建 + GitHub Pages 部署（先装 wasm-tools，publish 后 upload-pages-artifact → deploy-pages） |

Copyright (C) 2026 ModerRAS — GPL-3.0
