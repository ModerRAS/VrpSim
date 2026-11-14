using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VrpSim;

// NativeAOT / trimming 兼容：用源生成上下文替代反射序列化（IncludeFields/缩进与原来一致）
[JsonSourceGenerationOptions(IncludeFields = true, WriteIndented = true)]
[JsonSerializable(typeof(StateStore.State))]
internal sealed partial class StateJsonContext : JsonSerializerContext { }

public static class StateStore
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "vrp-sim-state.json");

    public sealed class State
    {
        public string SysName = "HUAWEI";
        public bool TelnetServer = true, FtpServer, HttpServer = true, HttpsServer;
        public bool StelnetV4, StelnetV6;
        public bool SnmpAgent, SnmpTrapEnable;
        public string SnmpVersion = "v3";
        public string SnmpContact = "";
        public string SnmpLocation = "";
        public string MonitorIp = "192.168.12.1";
        public bool InfoCenterEnabled = true;
        public bool AdminDeleted;
        public List<int> Vlans = new();
        public List<AclState> Acls = new();
        public List<IfaceState> Ifaces = new();
        public List<UserState> Users = new();
        public List<UiState> Uis = new();
        public List<PgState> PortGroups = new();
        public List<VlanCfgState> VlanCfgs = new();
        public List<CommunityState> Communities = new();
        public List<TargetState> Targets = new();
        public List<string> LogHosts = new();
        public ClockState Clock = new();
        public AaaState Aaa = new();
        public string SavedAt = "";
        // v1.6.0 后台计时（累计值，跨运行续加；只算实际运行时长）
        public long ElapsedMs;
        public int Steps;
        public string FirstLoginAt = "";
        public string UpdatedAt = "";
    }

    public sealed class AclState { public int Number; public string? Description; public List<RuleState> Rules = new(); }
    public sealed class RuleState
    {
        public int Id; public string Action = "deny"; public string Proto = "ip";
        public int PortMin = -1, PortMax = -1;
        public string? Source; public int SourceWildcard; public string Raw = "";
    }
    public sealed class IfaceState
    {
        public string Name = ""; public bool Shutdown; public string? Description; public string? LinkType;
        public int? AccessVlan; public List<int> TrunkVlans = new();
        public int? TfIn, TfOut; public string? Ip, Mask;
        public bool IsVlanif, Cabled;
    }
    public sealed class UserState
    {
        public string Name = ""; public string PasswordCipher = ""; public int Privilege;
        public List<string> ServiceTypes = new(); public bool PasswordInitial = true;
    }
    public sealed class UiState
    {
        public string Name = ""; public string? Auth; public int? Acl;
        public bool IdleSet; public int IdleMin = 10, IdleSec; public string? Protocol;
    }
    public sealed class PgState { public string Name = ""; public List<string> Members = new(); }
    public sealed class VlanCfgState { public int Id; public string? Description; public string? Name; }
    public sealed class CommunityState { public bool Read = true; public string PasswordCipher = ""; public int? Acl; }
    public sealed class TargetState { public string Ip = ""; public int Port = 162; public string SecurityCipher = ""; public string Version = "v2c"; }
    public sealed class ClockState
    {
        public int Year = 2000, Month = 4, Day = 12, Hour = 2, Minute = 42, Second = 0;
        public string? Tz; public int TzOffsetSec; public bool Ntp;
    }
    public sealed class AaaState
    {
        public bool PasswordPolicyAdmin = true; public bool ComplexityCheck;
        public int PasswordExpire = 90; public int PasswordHistory = 5;
        public bool HasWrongPassword; public int RetryInterval, RetryTime, BlockTime;
    }

    public static State ToState(Device d, RunTimer timer, string updatedAt)
    {
        var s = new State
        {
            SysName = d.SysName,
            TelnetServer = d.TelnetServer, FtpServer = d.FtpServer,
            HttpServer = d.HttpServer, HttpsServer = d.HttpsServer,
            StelnetV4 = d.StelnetV4, StelnetV6 = d.StelnetV6,
            SnmpAgent = d.SnmpAgent, SnmpTrapEnable = d.SnmpTrapEnable,
            SnmpVersion = d.SnmpVersion, SnmpContact = d.SnmpContact, SnmpLocation = d.SnmpLocation, MonitorIp = d.MonitorIp,
            InfoCenterEnabled = d.InfoCenterEnabled, AdminDeleted = d.AdminDeleted,
            Vlans = d.Vlans.OrderBy(v => v).ToList(),
            LogHosts = d.LogHosts.ToList(),
            SavedAt = updatedAt,
        ElapsedMs = timer.ElapsedMs,
        Steps = timer.Steps,
        FirstLoginAt = timer.FirstLoginAt ?? "",
        UpdatedAt = updatedAt
        };
        foreach (var a in d.Acls.Values.OrderBy(x => x.Number))
        {
            s.Acls.Add(new AclState
            {
                Number = a.Number,
                Description = a.Description,
                Rules = a.Rules.OrderBy(r => r.Id).Select(r => new RuleState
                {
                    Id = r.Id, Action = r.Action, Proto = r.Proto,
                    PortMin = r.PortMin, PortMax = r.PortMax,
                    Source = r.Source, SourceWildcard = r.SourceWildcard, Raw = r.Raw
                }).ToList()
            });
        }
        foreach (var i in d.Ifaces.Values)
        {
            s.Ifaces.Add(new IfaceState
            {
                Name = i.Name, Shutdown = i.Shutdown, Description = i.Description, LinkType = i.LinkType,
                AccessVlan = i.AccessVlan, TrunkVlans = i.TrunkVlans.ToList(),
                TfIn = i.TfIn, TfOut = i.TfOut, Ip = i.Ip, Mask = i.Mask,
                IsVlanif = i.IsVlanif, Cabled = i.Cabled
            });
        }
        foreach (var u in d.Users.Values)
        {
            s.Users.Add(new UserState
            {
                Name = u.Name, PasswordCipher = Device.Cipher(u.Password), Privilege = u.Privilege,
                ServiceTypes = u.ServiceTypes.ToList(), PasswordInitial = u.PasswordInitial
            });
        }
        foreach (var ui in d.Uis.Values)
        {
            s.Uis.Add(new UiState
            {
                Name = ui.Name, Auth = ui.Auth, Acl = ui.Acl,
                IdleSet = ui.IdleSet, IdleMin = ui.IdleMin, IdleSec = ui.IdleSec, Protocol = ui.Protocol
            });
        }
        foreach (var g in d.PortGroups)
            s.PortGroups.Add(new PgState { Name = g.Key, Members = g.Value.ToList() });
        foreach (var v in d.VlanCfgs)
            s.VlanCfgs.Add(new VlanCfgState { Id = v.Key, Description = v.Value.Description, Name = v.Value.Name });
        foreach (var c in d.Communities)
            s.Communities.Add(new CommunityState { Read = c.Read, PasswordCipher = Device.Cipher(c.Password), Acl = c.Acl });
        foreach (var t in d.Targets)
            s.Targets.Add(new TargetState { Ip = t.Ip, Port = t.Port, SecurityCipher = Device.Cipher(t.Security), Version = t.Version });
        s.Clock = new ClockState
        {
            Year = d.Clock.Year, Month = d.Clock.Month, Day = d.Clock.Day,
            Hour = d.Clock.Hour, Minute = d.Clock.Minute, Second = d.Clock.Second,
            Tz = d.Clock.Tz, TzOffsetSec = d.Clock.TzOffsetSec, Ntp = d.Clock.Ntp
        };
        s.Aaa = new AaaState
        {
            PasswordPolicyAdmin = d.Aaa.PasswordPolicyAdmin, ComplexityCheck = d.Aaa.ComplexityCheck,
            PasswordExpire = d.Aaa.PasswordExpire, PasswordHistory = d.Aaa.PasswordHistory,
            HasWrongPassword = d.Aaa.WrongPassword != null,
            RetryInterval = d.Aaa.WrongPassword?.RetryInterval ?? 5,
            RetryTime = d.Aaa.WrongPassword?.RetryTime ?? 5,
            BlockTime = d.Aaa.WrongPassword?.BlockTime ?? 10
        };
        return s;
    }

    public static Device FromState(State s)
    {
        var d = Device.Factory();
        d.SysName = s.SysName;
        d.TelnetServer = s.TelnetServer; d.FtpServer = s.FtpServer;
        d.HttpServer = s.HttpServer; d.HttpsServer = s.HttpsServer;
        d.StelnetV4 = s.StelnetV4; d.StelnetV6 = s.StelnetV6;
        d.SnmpAgent = s.SnmpAgent; d.SnmpTrapEnable = s.SnmpTrapEnable;
        d.SnmpVersion = s.SnmpVersion;
        d.SnmpContact = s.SnmpContact ?? ""; d.SnmpLocation = s.SnmpLocation ?? "";
        d.MonitorIp = s.MonitorIp;
        d.InfoCenterEnabled = s.InfoCenterEnabled; d.AdminDeleted = s.AdminDeleted;
        d.Vlans = new HashSet<int>(s.Vlans);
        d.LogHosts = s.LogHosts.ToList();

        if (s.Acls.Count > 0)
            d.Acls = s.Acls.ToDictionary(a => a.Number, a => new Acl
            {
                Number = a.Number,
                Description = a.Description,
                Rules = a.Rules.Select(r => new AclRule
                {
                    Id = r.Id, Action = r.Action, Proto = r.Proto,
                    PortMin = r.PortMin, PortMax = r.PortMax,
                    Source = r.Source, SourceWildcard = r.SourceWildcard, Raw = r.Raw
                }).ToList()
            });
        if (s.Ifaces.Count > 0)
            d.Ifaces = s.Ifaces.ToDictionary(i => i.Name, i => new Iface
            {
                Name = i.Name, Shutdown = i.Shutdown, Description = i.Description, LinkType = i.LinkType,
                AccessVlan = i.AccessVlan, TrunkVlans = i.TrunkVlans.ToList(),
                TfIn = i.TfIn, TfOut = i.TfOut, Ip = i.Ip, Mask = i.Mask,
                IsVlanif = i.IsVlanif, Cabled = i.Cabled
            });
        if (s.Users.Count > 0)
            d.Users = s.Users.ToDictionary(u => u.Name, u => new LocalUser
            {
                Name = u.Name,
                Password = Device.Uncipher(u.PasswordCipher) ?? "",
                Privilege = u.Privilege,
                ServiceTypes = u.ServiceTypes.ToList(),
                PasswordInitial = u.PasswordInitial
            });
        if (s.Uis.Count > 0)
            d.Uis = s.Uis.ToDictionary(u => u.Name, u => new UiCfg
            {
                Name = u.Name, Auth = u.Auth, Acl = u.Acl,
                IdleSet = u.IdleSet, IdleMin = u.IdleMin, IdleSec = u.IdleSec, Protocol = u.Protocol
            });
        if (s.PortGroups.Count > 0)
            d.PortGroups = s.PortGroups.ToDictionary(g => g.Name, g => g.Members.ToList());
        if (s.VlanCfgs.Count > 0)
            d.VlanCfgs = s.VlanCfgs.ToDictionary(v => v.Id, v => new VlanCfg { Id = v.Id, Description = v.Description, Name = v.Name });
        d.Communities = s.Communities.Select(c => new SnmpCommunity
        {
            Read = c.Read, Password = Device.Uncipher(c.PasswordCipher) ?? "", Acl = c.Acl
        }).ToList();
        d.Targets = s.Targets.Select(t => new SnmpTarget
        {
            Ip = t.Ip, Port = t.Port, Security = Device.Uncipher(t.SecurityCipher) ?? "", Version = t.Version
        }).ToList();
        d.Clock = new Clock
        {
            Year = s.Clock.Year, Month = s.Clock.Month, Day = s.Clock.Day,
            Hour = s.Clock.Hour, Minute = s.Clock.Minute, Second = s.Clock.Second,
            Tz = s.Clock.Tz, TzOffsetSec = s.Clock.TzOffsetSec, Ntp = s.Clock.Ntp
        };
        d.Aaa = new AaaCfg
        {
            PasswordPolicyAdmin = s.Aaa.PasswordPolicyAdmin,
            ComplexityCheck = s.Aaa.ComplexityCheck,
            PasswordExpire = s.Aaa.PasswordExpire,
            PasswordHistory = s.Aaa.PasswordHistory,
            WrongPassword = s.Aaa.HasWrongPassword
                ? (s.Aaa.RetryInterval, s.Aaa.RetryTime, s.Aaa.BlockTime)
                : null
        };
        return d;
    }

    public static bool Save(Device d, RunTimer timer, string path, out string? error)
    {
        try
        {
            var s = ToState(d, timer, RunTimer.LocalIsoNow());
            File.WriteAllText(path, JsonSerializer.Serialize(s, StateJsonContext.Default.State), new UTF8Encoding(false));
            error = null;
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static Device? Load(string path, out string? error, out State? raw)
    {
        try
        {
            if (!File.Exists(path)) { error = null; raw = null; return null; }
            var s = JsonSerializer.Deserialize(File.ReadAllText(path, Encoding.UTF8), StateJsonContext.Default.State);
            raw = s;
            if (s == null) { error = "存档内容为空"; return null; }
            error = null;
            return FromState(s);
        }
        catch (Exception ex) { raw = null; error = ex.Message; return null; }
    }

    public static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
