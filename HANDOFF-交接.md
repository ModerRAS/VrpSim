# vrp-sim 交接单（Lead → vrp-sim-dev）

## 你负责什么
独占 `projects/netsec/VrpSim/`，继续把这个华为 VRP 模拟器做“真机保真”，改完自己构建、自测、发布 dist exe，并更新 README/演练脚本。**只改这个目录**；临时文件走 `R:\scratch\vrp-sim\`；不要大文件 I/O 到 C:；不要碰 `R:\ensp`（别人的）。

## 构建/测试/发布
```powershell
cd C:\WorkSpace\Claw\projects\netsec\VrpSim
dotnet build -c Release -v q --nologo
dotnet run -c Release --no-build -- --selftest        # 必须 11/11 + SELFTEST OK
Get-Content -Encoding UTF8 演练脚本-14项.txt | .\dist\VrpSim.exe --batch --no-login   # 必须 0 条 Error
# 发布（exe 被占用会失败，先关掉运行中的窗口/进程）
Get-Process VrpSim -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o dist --nologo
```

## 已完成（别改坏）
- 视图：user/system/aaa/acl/iface/range/ui/lupp；Tab 补全 + `?` 帮助 + 缩写执行（关键字命中后按规范全称执行，`dis cu` 可用）
- 接口：`interface Ethernet 0/0/1`（类型+编号分开补全）、`interface range … to …` 批量（提示符 `[HUAWEI-port-group]`，shutdown/description/port/traffic-filter/undo 批量）
- ACL：`acl 3000` 短写法、rule id 1-65535 可省（当前 ACL 最大+5）、basic ACL 拒绝 ip/tcp、source 后 wildcard `?` = 0 / X.X.X.X
- AAA：`local-user x password cipher/irreversible-cipher <STRING> [privilege level n]`（service-type 单独）、`local-aaa-user password policy administrator` 进入 `[HUAWEI-aaa-lupp-admin]`（`password expire` 默认90、`password history record number` 默认5，非默认才显示，配置缩进2空格）
- 服务：rsa local-key-pair create、stelnet Info、undo telnet/ftp Info、undo http 确认、snmp v2c 警告、trap enable 确认、loghost 警告、vty authentication-mode aaa 警告
- 通用管道：任意 `display … | include/begin/exclude <text>`
- 登录：默认 admin / admin@huawei.com；初始口令必须改（旧口令校验；按 n 回登录）；密码输入可退格
- 持久化：交互退出自动存 exe 同目录 `vrp-sim-state.json`，下次自动恢复；`reset` 重开；`--fresh` 跳过；`--persist` 批处理也存
- 14 项 `check` 自查；`--selftest` 回归

## 待办（按优先级）
**P0**
1. 更新 `README.md`：补上面新特性（console 缩写、interface range、policy view、持久化、默认口令、通用管道、缩写执行）；补“已知限制”。
2. 更新 `演练脚本-14项.txt`：策略视图那条后要有 `quit`（已有，确认）；可加 interface range 的批量关端口段落做演示。
3. range 视图 `display this` 现在直接列 interface 块，建议加 `#` 分隔与 `return` 尾巴，贴近真机。
4. `interface ?` 帮助里应能看到 `range` 和接口类型；检查 help 候选排序/去重是否合理。

**P1**
5. `display this` 在各视图贴近真机格式（`#` 包起来、末尾 return）；`display interface brief` 表头对齐。
6. `save` 回显贴真机两段式（“The current configuration will be written to the device.” 换行 + “Are you sure to continue?[Y/N]”）。
7. 补 `undo` 在 aaa/iface/ui 视图的候选完整性；`show` 类不存在的命令别乱认。
8. 给 `display` 子命令补简短 help 文本（`?` 时显示）。

**P2**
9. 可选：`port-group` 命令（建组+group-member）、`display users`、`display saved-configuration`。
10. 代码整理：Checks.cs 有个 CS8604 警告（SameSubnet mask 可空），修掉。

## 交付
- 每次改完：build → selftest → 跑演练脚本 → 发布 dist，然后把“改了哪些文件/哪些行为、测试结果”写进 `CHANGELOG-vrp-sim.md`（新建，追加）。
- 完成后在 team task 里 complete，并给 Lead 回一条简短汇报（改了什么、selftest/演练结果、exe 时间戳）。
