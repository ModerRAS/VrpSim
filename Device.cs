namespace VrpSim;

public sealed class AclRule
{
    public int Id;
    public string Action = "deny";
    public string Proto = "ip";
    public int PortMin = -1;
    public int PortMax = -1;
    public string? Source;
    public int SourceWildcard;
    public string Raw = "";
    public bool IsPermit => Action == "permit";
}

public sealed class Acl
{
    public int Number;
    public string? Description;
    public List<AclRule> Rules = new();
}

public sealed class LocalUser
{
    public string Name = "";
    public string Password = "";
    public int Privilege;
    public List<string> ServiceTypes = new();
    public bool PasswordInitial = true;
    public string Cipher => Device.Cipher(Password);
}

public sealed class UiCfg
{
    public string Name = "";
    public string? Auth;
    public int? Acl;
    public bool IdleSet;
    public int IdleMin = 10;
    public int IdleSec;
    public string? Protocol;
}

public sealed class Iface
{
    public string Name = "";
    public bool Shutdown;
    public string? Description;
    public string? LinkType;
    public int? AccessVlan;
    public List<int> TrunkVlans = new();
    public int? TfIn, TfOut;
    public string? Ip, Mask;
    public bool IsVlanif;
    public bool Cabled;
    public bool IsPort => Name.StartsWith("Ethernet") || Name.StartsWith("GigabitEthernet");
    public bool HasConfig => Shutdown || Description != null || LinkType != null || AccessVlan != null
        || TrunkVlans.Count > 0 || Ip != null || TfIn != null || TfOut != null;
    public bool PortConfigured => Description != null || LinkType != null || AccessVlan != null
        || TrunkVlans.Count > 0 || TfIn != null || TfOut != null;
}

public sealed class VlanCfg
{
    public int Id;
    public string? Description;
    public string? Name;
    public bool HasConfig => !string.IsNullOrEmpty(Description) || !string.IsNullOrEmpty(Name);
}

public sealed class SnmpCommunity
{
    public bool Read = true;
    public string Password = "";
    public int? Acl;
}

public sealed class SnmpTarget
{
    public string Ip = "";
    public int Port = 162;
    public string Security = "";
    public string Version = "v2c";
}

public sealed class Clock
{
    public int Year = 2000, Month = 4, Day = 12, Hour = 2, Minute = 42, Second = 0;
    public string? Tz;
    public int TzOffsetSec;
    public bool Ntp;
    public string Text => $"{Year:D4}-{Month:D2}-{Day:D2} {Hour:D2}:{Minute:D2}:{Second:D2}";
}

public sealed class AaaCfg
{
    public bool PasswordPolicyAdmin = true;
    public bool ComplexityCheck;
    public int PasswordExpire = 90;
    public int PasswordHistory = 5;
    public (int RetryInterval, int RetryTime, int BlockTime)? WrongPassword;
}

public sealed class Device
{
    public string SysName = "HUAWEI";
    public bool TelnetServer = true, FtpServer, HttpServer = true, HttpsServer;
    public bool StelnetV4, StelnetV6;
    public bool SnmpAgent, SnmpTrapEnable;
    public string SnmpVersion = "v3";
    public string SnmpContact = "";
    public string SnmpLocation = "";
    public bool InfoCenterEnabled = true;
    public string MonitorIp = "192.168.12.1";
    public HashSet<int> Vlans = new();
    public Dictionary<int, VlanCfg> VlanCfgs = new();
    public Dictionary<int, Acl> Acls = new();
    public Dictionary<string, Iface> Ifaces = new();
    public Dictionary<string, LocalUser> Users = new();
    public Dictionary<string, UiCfg> Uis = new();
    public Dictionary<string, List<string>> PortGroups = new();
    public List<SnmpCommunity> Communities = new();
    public List<SnmpTarget> Targets = new();
    public List<string> LogHosts = new();
    public Clock Clock = new();
    public AaaCfg Aaa = new();
    public bool AdminDeleted;

    public static readonly string[] FactoryAuthProfiles =
    {
        "default_authen_profile", "dot1x_authen_profile", "mac_authen_profile",
        "portal_authen_profile", "dot1xmac_authen_profile", "multi_authen_profile"
    };

    public static string Cipher(string s) =>
        "%^%#" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s)) + "%^%#";

    public static string? Uncipher(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (!s.StartsWith("%^%#", StringComparison.Ordinal) || !s.EndsWith("%^%#", StringComparison.Ordinal) || s.Length <= 8) return s;
        try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s.Substring(4, s.Length - 8))); }
        catch { return s; }
    }

    public static Device Factory()
    {
        var d = new Device();
        d.Vlans.Add(1);
        for (int i = 1; i <= 16; i++) d.Ifaces[$"Ethernet0/0/{i}"] = new Iface { Name = $"Ethernet0/0/{i}" };
        for (int i = 1; i <= 12; i++)
            d.Ifaces[$"GigabitEthernet0/0/{i}"] = new Iface { Name = $"GigabitEthernet0/0/{i}", Cabled = i == 1 || i == 12 };
        d.Ifaces["Vlanif1"] = new Iface { Name = "Vlanif1", IsVlanif = true };
        d.Ifaces["NULL0"] = new Iface { Name = "NULL0" };
        d.Users["admin"] = new LocalUser { Name = "admin", Password = "admin@huawei.com", Privilege = 15, ServiceTypes = { "terminal", "http" } };
        d.Uis["con 0"] = new UiCfg { Name = "con 0" };
        d.Uis["vty 0 4"] = new UiCfg { Name = "vty 0 4" };
        d.Uis["vty 16 20"] = new UiCfg { Name = "vty 16 20" };
        return d;
    }

    public static int IfOrder(string name)
    {
        int idx;
        if (name.StartsWith("Ethernet")) idx = 0;
        else if (name.StartsWith("Eth-Trunk")) idx = 50;
        else if (name.StartsWith("GigabitEthernet")) idx = 100;
        else if (name.StartsWith("LoopBack")) idx = 150;
        else if (name.StartsWith("Vlanif")) idx = 200;
        else if (name.StartsWith("Tunnel")) idx = 250;
        else idx = 300;
        var digits = new string(name.Where(char.IsDigit).ToArray());
        int num = digits.Length > 0 ? int.Parse(digits) : 0;
        return idx * 1000 + num;
    }

    public string AclViewName(int n) => n switch
    {
        < 3000 => $"acl-basic-{n}",
        < 4000 => $"acl-adv-{n}",
        < 5000 => $"acl-l2-{n}",
        _ => $"acl-user-{n}"
    };

    public string UiViewName(string name) => name.Replace(" ", "") switch
    {
        "con0" => "ui-con0",
        "vty04" => "ui-vty0-4",
        "vty1620" => "ui-vty16-20",
        _ => "ui-" + name.Replace(" ", "")
    };

    public List<string> RenderConfig()
    {
        var L = new List<string>();
        void Sep() => L.Add("#");
        L.Add("!Software Version V200R011C10SPC600");
        Sep();
        L.Add($"sysname {SysName}");
        Sep();
        if (LogHosts.Count > 0)
        {
            foreach (var h in LogHosts) L.Add($"info-center loghost {h}");
            Sep();
        }
        var plainVlans = Vlans.Where(v => v != 1 && !HasVlanCfg(v)).OrderBy(v => v).ToList();
        if (plainVlans.Count > 0)
        {
            L.Add("vlan batch " + CompressVlanRuns(plainVlans));
            Sep();
        }
        foreach (var p in FactoryAuthProfiles) L.Add($"authentication-profile name {p}");
        Sep();
        var svcLines = new List<string>();
        if (!HttpServer) svcLines.Add("undo http server enable");
        if (!TelnetServer) svcLines.Add("undo telnet server enable");
        if (HttpsServer) svcLines.Add("http secure-server enable");
        foreach (var x in svcLines) L.Add(x);
        if (svcLines.Count > 0) Sep();
        L.Add("radius-server template default");
        Sep();
        foreach (var a in Acls.Values.OrderBy(a => a.Number))
        {
            L.Add($"acl number {a.Number}");
            if (!string.IsNullOrEmpty(a.Description)) L.Add($" description {a.Description}");
            foreach (var r in a.Rules.OrderBy(r => r.Id)) L.Add(" " + r.Raw);
            Sep();
        }
        L.AddRange(RenderAaa());
        Sep();
        // 真机 dis cu 会打印配置库里所有接口对象：空口也输出 # / interface X / # 空块
        foreach (var i in Ifaces.Values.OrderBy(i => IfOrder(i.Name)))
        {
            L.Add($"interface {i.Name}");
            if (!string.IsNullOrEmpty(i.Description)) L.Add($" description {i.Description}");
            if (i.LinkType != null) L.Add($" port link-type {i.LinkType}");
            if (i.AccessVlan is int av) L.Add($" port default vlan {av}");
            if (i.TrunkVlans.Count > 0) L.Add(" port trunk allow-pass vlan " + string.Join(" ", i.TrunkVlans.OrderBy(v => v)));
            if (i.TfIn is int tin) L.Add($" traffic-filter inbound acl {tin}");
            if (i.TfOut is int tout) L.Add($" traffic-filter outbound acl {tout}");
            if (i.Ip != null) L.Add($" ip address {i.Ip} {i.Mask}");
            if (i.Shutdown) L.Add(" shutdown");
            Sep();
        }
        foreach (var v in Vlans.Where(x => x != 1 && HasVlanCfg(x)).OrderBy(x => x))
        {
            L.AddRange(RenderVlan(v));
            Sep();
        }
        foreach (var pg in PortGroups.OrderBy(k => k.Key))
        {
            L.Add($"port-group {pg.Key}");
            if (pg.Value.Count > 0) L.Add(" group-member " + CompressMembers(pg.Value));
            Sep();
        }
        if (SnmpAgent)
        {
            L.Add("snmp-agent");
            L.Add("snmp-agent local-engineid 800007DB03E0CC7A085C00");
            foreach (var c in Communities)
                L.Add($"snmp-agent community {(c.Read ? "read" : "write")} cipher {Cipher(c.Password)}" + (c.Acl is int n ? $" acl {n}" : ""));
            if (SnmpContact.Length > 0) L.Add($"snmp-agent sys-info contact {SnmpContact}");
            if (SnmpLocation.Length > 0) L.Add($"snmp-agent sys-info location {SnmpLocation}");
            L.Add($"snmp-agent sys-info version {SnmpVersion}");
            foreach (var t in Targets)
                L.Add($"snmp-agent target-host trap address udp-domain {t.Ip}" + (t.Port != 162 ? $" udp-port {t.Port}" : "") +
                      $" params securityname cipher {Cipher(t.Security)} {t.Version}");
            if (SnmpTrapEnable) L.Add("snmp-agent trap enable");
            Sep();
        }
        if (StelnetV4) L.Add("stelnet ipv4 server enable");
        if (StelnetV6) L.Add("stelnet ipv6 server enable");
        if (StelnetV4 || StelnetV6) Sep();
        foreach (var u in Uis.Values)
        {
            L.Add($"user-interface {u.Name}");
            if (u.Acl is int a) L.Add($" acl {a} inbound");
            if (u.Auth != null) L.Add($" authentication-mode {u.Auth}");
            if (u.Protocol != null) L.Add($" protocol inbound {u.Protocol}");
            if (u.IdleSet) L.Add($" idle-timeout {u.IdleMin} {u.IdleSec}");
            Sep();
        }
        L.Add("return");
        return L;
    }

    public List<string> RenderAaa()
    {
        var L = new List<string> { "aaa" };
        L.Add(" authentication-scheme default");
        L.Add(" authentication-scheme radius");
        L.Add("  authentication-mode radius");
        L.Add(" authorization-scheme default");
        L.Add(" accounting-scheme default");
        if (Aaa.PasswordPolicyAdmin) L.Add(" local-aaa-user password policy administrator");
        if (Aaa.PasswordPolicyAdmin && Aaa.PasswordExpire != 90) L.Add($"  password expire {Aaa.PasswordExpire}");
        if (Aaa.PasswordPolicyAdmin && Aaa.PasswordHistory != 5) L.Add($"  password history record number {Aaa.PasswordHistory}");
        L.Add(" domain default");
        L.Add("  authentication-scheme radius");
        L.Add("  radius-server default");
        L.Add(" domain default_admin");
        L.Add("  authentication-scheme default");
        if (Aaa.WrongPassword is { } wp)
            L.Add($" local-aaa-user wrong-password retry-interval {wp.RetryInterval} retry-time {wp.RetryTime} block-time {wp.BlockTime}");
        if (Aaa.ComplexityCheck) L.Add(" user-password complexity-check");
        if (AdminDeleted) L.Add(" undo local-user admin");
        foreach (var u in Users.Values.OrderBy(u => u.Name))
        {
            L.Add($" local-user {u.Name} password irreversible-cipher {u.Cipher}");
            L.Add($" local-user {u.Name} privilege level {u.Privilege}");
            if (u.ServiceTypes.Count > 0)
                L.Add($" local-user {u.Name} service-type {string.Join(" ", u.ServiceTypes)}");
        }
        return L;
    }

    public bool HasVlanCfg(int id) => VlanCfgs.TryGetValue(id, out var c) && c.HasConfig;

    public List<string> RenderVlan(int id)
    {
        var L = new List<string> { $"vlan {id}" };
        if (VlanCfgs.TryGetValue(id, out var cfg))
        {
            if (!string.IsNullOrEmpty(cfg.Description)) L.Add($" description {cfg.Description}");
            if (!string.IsNullOrEmpty(cfg.Name)) L.Add($" name {cfg.Name}");
        }
        return L;
    }

    // VLAN 号连续段压缩成 "A to B"（真机 display current-configuration 写法）
    public static string CompressVlanRuns(List<int> ids)
    {
        var parts = new List<string>();
        var i = 0;
        while (i < ids.Count)
        {
            var j = i + 1;
            while (j < ids.Count && ids[j] == ids[j - 1] + 1) j++;
            if (j - i >= 2) parts.Add(ids[i] + " to " + ids[j - 1]);
            else parts.Add(ids[i].ToString());
            i = j;
        }
        return string.Join(" ", parts);
    }

    public List<string> RenderPortGroup(string name, List<string> members)
    {
        var L = new List<string> { $"port-group {name}" };
        if (members.Count > 0) L.Add(" group-member " + CompressMembers(members));
        return L;
    }

    // 把连续的 group-member 压成 "A to B"（真机 display current-configuration 的写法）
    public static string CompressMembers(List<string> members)
    {
        var items = members
            .Select(m => (Name: m, Split: SplitIfaceName(m)))
            .Where(x => x.Split.Num >= 0)
            .OrderBy(x => IfOrder(x.Name))
            .ToList();
        var parts = new List<string>();
        var i = 0;
        while (i < items.Count)
        {
            var start = items[i];
            var j = i + 1;
            while (j < items.Count
                   && items[j].Split.Prefix.Equals(start.Split.Prefix, StringComparison.OrdinalIgnoreCase)
                   && items[j].Split.Num == items[j - 1].Split.Num + 1) j++;
            if (j - i >= 2) parts.Add(start.Name + " to " + items[j - 1].Name);
            else parts.Add(start.Name);
            i = j;
        }
        return string.Join(" ", parts);
    }

    public static (string Prefix, int Num) SplitIfaceName(string s)
    {
        var slash = s.LastIndexOf('/');
        if (slash >= 0)
            return (s.Substring(0, slash + 1), int.TryParse(s.Substring(slash + 1), out var n) ? n : -1);
        var i = s.Length - 1;
        while (i >= 0 && char.IsDigit(s[i])) i--;
        return (s.Substring(0, i + 1), int.TryParse(s.Substring(i + 1), out var m) ? m : -1);
    }

    public List<string> RenderAcl(Acl a)
    {
        var L = new List<string> { $"acl number {a.Number}" };
        if (!string.IsNullOrEmpty(a.Description)) L.Add($" description {a.Description}");
        foreach (var r in a.Rules.OrderBy(r => r.Id)) L.Add(" " + r.Raw);
        return L;
    }

    public List<string> RenderIface(Iface i)
    {
        var L = new List<string> { $"interface {i.Name}" };
        if (!string.IsNullOrEmpty(i.Description)) L.Add($" description {i.Description}");
        if (i.LinkType != null) L.Add($" port link-type {i.LinkType}");
        if (i.AccessVlan is int av) L.Add($" port default vlan {av}");
        if (i.TrunkVlans.Count > 0) L.Add(" port trunk allow-pass vlan " + string.Join(" ", i.TrunkVlans.OrderBy(v => v)));
        if (i.TfIn is int tin) L.Add($" traffic-filter inbound acl {tin}");
        if (i.TfOut is int tout) L.Add($" traffic-filter outbound acl {tout}");
        if (i.Ip != null) L.Add($" ip address {i.Ip} {i.Mask}");
        if (i.Shutdown) L.Add(" shutdown");
        return L;
    }

    public List<string> RenderUi(UiCfg u)
    {
        var L = new List<string> { $"user-interface {u.Name}" };
        if (u.Acl is int a) L.Add($" acl {a} inbound");
        if (u.Auth != null) L.Add($" authentication-mode {u.Auth}");
        if (u.Protocol != null) L.Add($" protocol inbound {u.Protocol}");
        if (u.IdleSet) L.Add($" idle-timeout {u.IdleMin} {u.IdleSec}");
        return L;
    }
}

public sealed class Session
{
    public Device Dev = Device.Factory();
    public string View = "user";
    public int AclNumber;
    public string IfName = "GigabitEthernet0/0/1";
    public string UiName = "con 0";
    public string PgName = "";
    public int VlanId;
    public List<string> RangeIfaces = new();
    public RunTimer Timer = new();
    public bool ExitRequested;
    public bool Saved;
    public Func<string, string?>? ReadLine;
    public bool Interactive;

    public string Prompt() => View switch
    {
        "user" => $"<{Dev.SysName}>",
        "system" => $"[{Dev.SysName}]",
        "aaa" => $"[{Dev.SysName}-aaa]",
        "acl" => $"[{Dev.SysName}-{Dev.AclViewName(AclNumber)}]",
        "iface" => $"[{Dev.SysName}-{IfName}]",
        "ui" => $"[{Dev.SysName}-{Dev.UiViewName(UiName)}]",
        "lupp" => $"[{Dev.SysName}-aaa-lupp-admin]",
        "range" => $"[{Dev.SysName}-port-group]",
        "pg" => $"[{Dev.SysName}-port-group-{PgName}]",
        "vlan" => $"[{Dev.SysName}-vlan{VlanId}]",
        _ => $"[{Dev.SysName}]"
    };

    public List<Command> Commands() => CommandSets.For(View);
}
