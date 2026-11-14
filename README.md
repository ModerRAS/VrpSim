# 华为 VRP 交换机模拟终端（加固演练用）

[![CI](https://github.com/ModerRAS/VrpSim/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/ModerRAS/VrpSim/actions/workflows/ci.yml)

按 2026-11-12 / 11-13 两台现场配置（V200R011C10SPC600）写的**可交互 CLI 模拟器**：
Tab 补全、`?` 帮助、缩写执行、视图切换、`display current-configuration` / `display this` 还原、
`interface range` 批量配置、`port-group` 端口组、外加 14 项加固自查评分。
C# / .NET 10 实现，无第三方依赖，发布为单文件 exe。

## 快速开始

```text
dist\VrpSim.exe
```

- Console 登录：`admin` / `admin@huawei.com`（首次登录会像真机一样提示改初始口令：必须先输对旧口令，
  连续错 3 次则本次不改；口令输入可用 `Backspace` 退格、按 `n` 回登录界面）
- 提示符：`<HUAWEI>` 用户视图 → `system-view` → `[HUAWEI]`；进入子视图后提示符带视图名
  （`[HUAWEI-aaa]`、`[HUAWEI-GigabitEthernet0/0/1]`、`[HUAWEI-ui-vty0-4]`、`[HUAWEI-port-group-g1]` …）
- 交互模式**退出时自动存档**到 exe 同目录的 `vrp-sim-state.json`，下次启动自动恢复；
  `--fresh` 强制新开局，`--persist` 让批处理模式也存档，`reset` 在用户视图恢复出厂配置

命令行参数：

| 参数 | 作用 |
| --- | --- |
| （无） | 交互模式，Tab / ? / 方向键 / 历史可用 |
| `--batch` | 从标准输入读命令（脚本、管道测试用），不要求 TTY，不启用登录 |
| `--selftest` | 内置"整套加固 → 14 项自查 + 73 条逐 token 补全快照 + 顺序/大小写混用/接口写法/VLAN 视图回归"，全过返回 0（输出 `SELFTEST OK`） |
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

## 键位

| 键 | 行为 |
| --- | --- |
| `Tab` | 补全当前词：唯一候选直接补全并加空格；多个候选列出后保留输入 |
| `?` | 显示当前位置所有候选（帮助模式下含 `<cr>` 和参数占位符，占位符排在候选值前面） |
| `←` `→` `Home` `End` | 移动光标 |
| `↑` `↓` | 历史命令 |
| `Backspace` `Delete` | 删除 |
| `Ctrl+C` | 清空当前行 |
| `Ctrl+Z` | 回到用户视图（等价 return） |

补全按 VRP 的缩写习惯工作：`disp` → `display`，`snmp-a` → `snmp-agent`，`sys` 能直接执行 `system-view`；
关键字命中后按**规范全称**执行（`dis cu` 可用）。参数位置给候选：接口类型、已建 ACL、本地用户、VLAN、read/write、端口组名等。
接口输入**只接受"类型 + 编号"分开写**：`interface GigabitEthernet 0/0/1`、`interface Vlanif 8`、`interface Ethernet 0/0/1`；
`interface Ethernet0/0/1` / `interface Vlanif8` 这类连写在真机上直接报 `Wrong parameter found at '^' position.`（^ 指向连写 token），模拟器同口径。
接口类型要写全称（`Ethernet` / `GigabitEthernet` / `Vlanif` / `NULL0`，大小写不限），`Eth 0/0/1` 这种缩写不认；
`interface range` 两端类型可混用大小写（`interface range ethernet 0/0/1 to Ethernet 0/0/16` 合法），进视图与配置渲染统一成规范拼写
（配置回显仍是真机的连写形式 `interface GigabitEthernet0/0/1`）。

### 补全快照（第一批：加固/配置演练路径）

`Tab` / `?` 已按真机口径逐 token 补齐（覆盖 `演练脚本-14项.txt` 的约 60 条路径；`--selftest` 内置 73 条逐 token 快照 + 顺序/大小写混用/接口写法/VLAN 视图回归）。代表性位置：

| 输入 | `Tab`（无占位符） | `?`（含类型占位符） |
| --- | --- | --- |
| `sysname ` | （自由文本） | `STRING<1-246>` |
| `interface ` | `range` `Ethernet` `GigabitEthernet` `Vlanif` `NULL0`（顺序固定，**不列具体接口名**） | 同左（不显示占位符） |
| `interface ethernet` | 唯一补成 `Ethernet` | — |
| `interface Vlanif ` | `1` `8`（按 VLAN 给） | `<interface-number>` + VLAN 号 |
| `interface range ` | 同 4 种接口类型（不列具体接口名） | 同左 |
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

## 覆盖的命令（按视图）

| 视图 | 命令 |
| --- | --- |
| 用户 `<HUAWEI>` | `system-view`、`quit`、`return`、`display …`、`save`、`check`、`reset` |
| 系统 `[HUAWEI]` | `sysname`、`aaa`、`acl [number] <2000-3999>`（两种写法都行）、`interface <类型> <编号>`（**必须分开写**，连写报错）、`interface range <类型> <编号> to <类型> <编号>`、`port-group <名>`、`vlan batch <id…>`、`vlan <id>`（进 VLAN 视图）、`stelnet (ipv4/ipv6) server enable`、`ssh client first-time`、`ssh user`、`snmp-agent`（community / sys-info version / target-host / trap enable）、`info-center (enable / loghost)`、`clock timezone`、`clock datetime`、`user-interface con/vty`、`undo …` |
| AAA `[HUAWEI-aaa]` | `local-user <u> password cipher/irreversible-cipher <pw> [privilege level <n>]`（一条命令可带权限；`service-type` 必须另起一条）、`local-user <u> privilege level`、`local-user <u> service-type`、`local-aaa-user wrong-password`、`local-aaa-user password policy administrator`、`user-password complexity-check`、`undo local-user` |
| 本地管理员密码策略 `[HUAWEI-aaa-lupp-admin]` | `password expire <0-999>`、`password history record number <0-5>`、`display this`、`undo …`（`quit` 回 AAA 视图） |
| ACL `[HUAWEI-acl-adv-3000]` | `rule [<id>] permit/deny [ip \| source <IP> <wildcard> \| <tcp/udp> destination-port eq/gt/lt/range …]`、`description`、`undo rule` |
| 接口 `[HUAWEI-GigabitEthernet0/0/1]` | `description`、`port link-type`、`port default vlan`、`port trunk allow-pass vlan`、`traffic-filter inbound/outbound acl`、`shutdown` / `undo shutdown`、`ip address`（仅 Vlanif）、`display this` |
| 接口范围 `[HUAWEI-port-group]`（模拟器扩展） | 用 `interface range <类型> <编号> to <类型> <编号>` 进入（两端类型可混用大小写），支持 `shutdown`、`description`、`port …`、`traffic-filter …` 与对应 `undo` 批量生效；`ip address` 不支持 |
| 端口组 `[HUAWEI-port-group-<名>]` | `group-member <接口> [to <接口2>]`、`undo group-member [<接口>]`，以及接口视图的 `shutdown` / `description` / `port …` / `traffic-filter …` 批量作用于成员口 |
| VLAN `[HUAWEI-vlan<id>]` | `description <text>`、`name <text>`、`undo description`、`undo name`、`display this`（格式 `#` / `vlan 200` / ` description …` / `#` / `return`）；`quit` 回系统视图；`vlan <id>` 已存在直接进、不存在则创建 |
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
- **`display this`**：按真机格式输出——配置块前后各一行 `#`，末尾跟 `return`；`interface range` 下按成员口一段一段分开，
  `[HUAWEI-aaa-lupp-admin]` 下输出的是嵌在 `aaa` 里的策略块。
- **`save` 两段式**：先输出 `The current configuration will be written to the device.`，
  再单独一行问 `Are you sure to continue?[Y/N]:`，同意后输出 `Now saving the current configuration to the slot 0.` 与
  `Save the configuration successfully.`（批处理模式下自动视为 y）。
- **`interface range` vs `port-group`**：`interface range A to B` 是本模拟器加的**批量快捷方式**（提示符 `[HUAWEI-port-group]`，真机没有这条命令）；
  真机批量配置应建端口组：`port-group g1` → `group-member GigabitEthernet0/0/2 to GigabitEthernet0/0/5` → 在组里敲批量命令。
  两者都会在 `display current-configuration` 里看到区别：`interface range` 改的是各成员口的配置，`port-group` 会多出 `port-group g1` / `group-member …` 两行。

## 模拟器专有：check（14 项加固自查）

`check` 不是 VRP 命令，只在本模拟器里存在。它按《华为交换机加固手册》14 项判定并给下一步提示：

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
- `interface` 只接受"类型 + 编号"分开写（真机口径）；配置回显仍是连写形式（`interface GigabitEthernet0/0/1`），这两者不矛盾
- VLAN 视图只实现 `description` / `name` 最小集，真机的 `statistics`、`mac-vlan` 等未实现
- `group-member` 仍按"完整接口名"输入与候选（如 `group-member GigabitEthernet0/0/1`），与 `interface` 的新口径暂不一致，待确认

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
| `Program.cs` | 启动、console 登录、批处理、自检脚本（含 73 条逐 token 快照 + 顺序/大小写混用/接口写法/VLAN 视图回归） |
| `StateStore.cs` | 交互存档 `vrp-sim-state.json` 的读写 |
| `演练脚本-14项.txt` | 可直接粘进模拟器的整套加固命令 |
| `saved-config.cfg` | `save` 落盘的当前配置（运行时生成） |
| `dist/VrpSim.exe` | 发布产物（单文件，需 .NET 10 运行时） |

Copyright (C) 2026 ModerRAS — GPL-3.0
