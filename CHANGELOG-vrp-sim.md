# vrp-sim CHANGELOG（交接 / 发布用）

> 项目：`projects/netsec/VrpSim/`（2026-11-14 由 Lead 从 `vrp-sim` 重命名，csproj/namespace/exe 一直是 VrpSim）
> 本次收尾：vrp-sim-dev，按 `HANDOFF-交接.md` 的 P0/P1/P2 清单执行
> 记录口径：只记本目录内的改动；临时文件放 `R:\scratch\vrp-sim\`（易失，收尾即弃，未进仓库）

## 2026-11-14 v1.1 收尾

### 一、改动（按 HANDOFF 条目）

**P0-1 `README.md` 重写（175 行）**
- 补齐 HANDOFF 要求的新特性：console 缩写执行、`interface range` 批量、`port-group` 端口组、本地管理员密码策略视图
  （`[HUAWEI-aaa-lupp-admin]`）、配置持久化（`vrp-sim-state.json` / `--fresh` / `--persist` / `reset`）、通用管道
  `| include / begin / exclude`、`save` 两段式回显、`display this` 真机格式。
- **修正默认口令**：原文写 `admin` / `Admin@123`，但代码（`Device.Factory()` 与启动横幅）是 `admin` / `admin@huawei.com`，
  按代码统一（并标注首次登录强制改）。
- 新增/扩写"已知限制"（原"不模拟的部分"）：路由/OSPF、STP、NTP、ACL 命中计数、口令策略只判配置、`save` 只落 cwd 文本、
  持久化是模拟器专有行为等。
- 覆盖命令表补：系统视图 `port-group` / `interface range`、密码策略视图、接口范围视图、端口组视图、
  `display saved-configuration`、`display this` 各视图格式、`undo` 新增项。
- 文件表补 `StateStore.cs` / `CHANGELOG-vrp-sim.md` / `saved-config.cfg`。

**P0-2 `演练脚本-14项.txt`**
- 逐口 shutdown 段落之后新增 `interface range Ethernet0/0/1 to Ethernet0/0/16 / shutdown / quit` 批量关端口演示
  （幂等，不影响 check 结果；161 → 165 行）。
- 确认密码策略视图那条 `local-aaa-user password policy administrator` 后面已有 `quit`（进 lupp 视图后必须退出，否则后续命令会落在错误视图）。

**P0-3 + P1-5 `display this` 真机格式（`Commands.cs`）**
- 新增 `WrapThis()`：所有子视图输出 `#` + 配置块 + `#` + `return`（iface / acl / ui / aaa）。
- `interface range` 视图：每个成员口一段，段间用 `#` 分隔，末尾 `#` + `return`。
- 密码策略视图：输出嵌在 `aaa` 里的策略块（`aaa` / ` local-aaa-user password policy administrator` / 缩进 2 空格的子项）。
- 端口组视图：输出 `port-group <名>` + ` group-member …` 配置块。
- `display interface brief`：表头与数据改用同一格式串（`{0,-28}{1,-8}{2,-9}{3,6}{4,7}{5,9}{6,10}`），
  每行 79 列、数值列右对齐（原来表头与数据列错位）。

**P0-4 `interface ?` 帮助**
- 模板顺序改为 `range` → `<interface-type> + <interface-number>` → 完整接口名，`?` 打开即可先看到 `range` 和 4 种接口类型。
- 接口类型候选带说明（`Ethernet interface` 等）；`<interface>` 后面继续列出全部已存在接口名（Tab 可直接补全）。
- `Cli.Val` 新增可选取值约束 `Accept`：接口类型必须写全称（`Ethernet/GigabitEthernet/Vlanif/NULL0`，大小写不限），
  `interface range` 不会被当成"类型=range"吃进去（此前 `interface range <Tab>` 会列出 `1 / 0/0/1 …` 这类垃圾候选）；
  进入时把类型规范化为真机拼写（`interface ethernet 0/0/2` → `Ethernet0/0/2`）。
- `Cli.Next` 帮助模式改为**先给占位符、再给候选值**（`<interface-type>` 在类型列表前）。

**P1-6 `save` 两段式 + 交互确认接通（`Commands.cs` / `Program.cs`）**
- `save`：先输出 `The current configuration will be written to the device.`，再单独一行 `Are you sure to continue?[Y/N]:`，
  确认后输出 `Now saving the current configuration to the slot 0.` + `Save the configuration successfully.`；拒绝时 `Info: The operation is canceled.`；
  批处理模式自动视为 `y`（不会去读 stdin 抢下一条命令）。
- **修掉一个隐性缺陷**：`Session.ReadLine` 从来没被赋值，导致交互模式下 `save` 确认、`undo http` 确认、
  `snmp-agent trap enable` / `info-center loghost` 警告都直接跳过。现在 `Program.Run` 在交互模式把行编辑器接上，
  这些确认会真的停下来问。

**P1-7 `undo` 候选补齐 + 不存在的命令不乱认**
- 系统视图新增候选/处理：`undo info-center enable`、`undo snmp-agent community [read|write]`、`undo snmp-agent sys-info version`、
  `undo snmp-agent target-host trap address udp-domain <ip>`、`undo vlan batch <id…>`。
- 接口视图新增：`undo port link-type`、`undo traffic-filter <inbound|outbound>`（不带 ACL 号）。
- **修 bug**：`undo ip address` 原来在任何视图都报 "Unsupported operation on an interface range."；
  现在接口视图会真正清掉 Vlanif 地址，只有 `interface range` / 端口组视图才拒绝。
- 端口组视图支持 `undo group-member [<接口>]`（清全部 / 删一个）。
- 抽查伪命令：`show version`、`display show`、`display acl abc` 均报 `Unrecognized command found at '^' position.`；
  `display acl 2000`（不存在）报 `Error: The ACL does not exist.`。

**P1-8 `display` 子命令 help 文本**
- `display ?` 全部子命令都有说明；补上 `saved-configuration`；`local-aaa-user password policy administrator` 三级关键字、
  `|` 过滤器等原来空 help 的都补了短说明。

**P2-9 `port-group` + `display saved-configuration`（`display users` 原本已有，验证可用）**
- 新视图 `[HUAWEI-port-group-<名>]`：`port-group <名>` 进入，`group-member <接口> [to <接口2>]` 加成员，
  接口视图的 `shutdown` / `description` / `port …` / `traffic-filter …` 及 `undo` 批量作用于成员口。
- `display current-configuration` 渲染 `port-group <名>` + ` group-member …`，连续成员压成 `A to B`（真机写法）。
- 端口组随 `vrp-sim-state.json` 持久化（`StateStore` 增加 `PortGroups`）。
- 新增 `display saved-configuration`：读当前目录 `saved-config.cfg`，没保存过时提示 `Info: The saved configuration file does not exist.`。

**P2-10 代码整理**
- `Checks.cs` 的 `CS8604`（`SameSubnet(mask)` 可空）已修，现编译 **0 warning**。
- 顺手删掉 Lead 编辑工具残留的空目录 `.Commands.cs.*.tmpdir`。

### 二、测试结果（2026-11-14 20:49，本机）

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 编译 | `dotnet build -c Release -v q --nologo` | **0 error / 0 warning**（改前 1 个 CS8604） |
| 回归 | `dotnet run -c Release --no-build -- --selftest`（等价 bin exe --selftest） | **11/11 通过，SELFTEST OK**（可考 11 项：通过 11、未通过 0、不适用 3） |
| 演练脚本 | `Get-Content -Encoding UTF8 演练脚本-14项.txt \| .\dist\VrpSim.exe --batch --no-login` | **0 条 Error**（223 行输出），末尾 `check` **11/11**；`save` 两段式回显正常 |
| 发布 | `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o dist` | 成功，`dist/VrpSim.exe` **299,639 字节**，LastWriteTime **2026-11-14 20:49:09** |
| dist 复测 | dist exe 的 `--selftest` + 演练脚本 | 同上（11/11、0 Error） |
| 抽查 | `display this`（iface/acl/ui/lupp/range/pg）、`save`、`interface ?`、`undo ?`（system/aaa/iface/ui）、`display saved-configuration`、`display interface brief`、port-group 配置渲染 + 存档 roundtrip（`--persist` 两次启动） | 全部符合预期，见下"验证样例" |

验证样例（dist exe，节选）：

```text
[HUAWEI-port-group]display this
#
interface Ethernet0/0/1
 description Idle
 shutdown
#
interface Ethernet0/0/2
#
return

[HUAWEI]display interface brief
 Interface                   PHY     Protocol  InUti OutUti inErrors outErrors
 Ethernet0/0/1               down    down         0%     0%        0         0
 GigabitEthernet0/0/1        up      up           0%     0%        0         0

[HUAWEI-port-group-g1]display this
#
port-group g1
 group-member GigabitEthernet0/0/2 to GigabitEthernet0/0/5
#
return

<HUAWEI>save
The current configuration will be written to the device.
Are you sure to continue?[Y/N]:y
Now saving the current configuration to the slot 0.
Save the configuration successfully.
```

### 三、遗留 TODO（未做，别当成已实现）

- `display interface`（不带 `brief`）仍返回 brief 表，没有真机的详细统计视图。
- `display vlan <id>`（按 VLAN 号过滤）未支持，只支持 `display vlan` 全量。
- 计时类真实行为（idle-timeout 踢人、失败锁定到期解锁、口令过期/历史）只记录配置，不生效。
- port-group 的成员口批量 `undo` 覆盖了常用几条，真机还有更多组内命令未覆盖。
- eNSP 安装包里的真实 S5700 资源（`help_zh_CN_UTF8.res`、13 份真实配置）可作为下一轮保真校准旁证
  （Lead 的 eNSP 调查：`artifacts/netsec/eNSP-安装包与设备包-调查结论-2026-11-14.md`），本次未纳入。

### 四、疑问（不确定的没猜，留在这里待裁决）

1. **演练脚本里的 `interface range` 段落**：这是模拟器扩展命令（真机没有），加进"现场加固操作顺序"后，若要求脚本能原样在真机执行，
   应删掉那 3 行或改成 `port-group` 写法。目前按"演示批量关端口"的诉求保留，README 已注明区别。
2. **`display saved-configuration` 文案**：没保存时用的是 `Info: The saved configuration file does not exist.`，
   真机确切措辞未现场核对；如果有真机截图/录像可据此改。
3. **初始口令**：README 原写 `Admin@123`、代码是 `admin@huawei.com`。已按代码统一；若现场初始口令确为 `Admin@123`，
   应改 `Device.Factory()` 并同步文档（本目录内两处：`Program.Banner` / `Device.Factory`）。
