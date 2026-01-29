using System.Drawing;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace ArenaVS;

public class ArenaConfig : BasePluginConfig
{
    [JsonPropertyName("AllowFriendlyFire")] public bool AllowFriendlyFire { get; set; } = false;
    [JsonPropertyName("ArenaModelPath")] public string ArenaModelPath { get; set; } = "models/arena.vmdl";
    [JsonPropertyName("ArenaDuration")] public int ArenaDuration { get; set; } = 20;
    [JsonPropertyName("ArenaX")] public float ArenaX { get; set; } = 0;
    [JsonPropertyName("ArenaY")] public float ArenaY { get; set; } = 0;
    [JsonPropertyName("ArenaZ")] public float ArenaZ { get; set; } = 4500;
    [JsonPropertyName("MaxGamesBeforeCooldown")] public int MaxGamesBeforeCooldown { get; set; } = 3;
    [JsonPropertyName("CooldownMinutes")] public int CooldownMinutes { get; set; } = 5;
}

public class ArenaLRPlugin : BasePlugin, IPluginConfig<ArenaConfig>
{
    public override string ModuleName => "CS2 Arena VS";
    public override string ModuleVersion => "1.6.0";
    public override string ModuleAuthor => "QuryWesT";
    public ArenaConfig Config { get; set; } = new();

    private class LRInvite
    {
        public CCSPlayerController Challenger { get; set; } = null!;
        public string Weapon { get; set; } = "";
    }

    private Dictionary<ulong, int> _playerGameCount = new();
    private Dictionary<ulong, DateTime> _playerCooldownEnd = new();
    private Dictionary<CCSPlayerController, LRInvite> _pendingInvites = new();
    private Dictionary<CCSPlayerController, List<string>> _savedWeapons = new();
    private List<CCSPlayerController> _arenaPlayers = new();
    private CBaseEntity? _currentPlatform = null;
    private bool _isArenaActive = false;
    private int _remainingTime;
    private bool _showTimer = false;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _logicTimer;

    public void OnConfigParsed(ArenaConfig config) { Config = config; }

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest => { manifest.AddResource(Config.ArenaModelPath); });

        RegisterListener<Listeners.OnTick>(() =>
        {
            if (!_isArenaActive || !_showTimer) return;
            string hudMsg = $"<font color='orange'>Arena Düellosu</font><br>" +
                           $"<font color='white'>Kalan Süre:</font> <font color='green'> {_remainingTime} </font>";

            foreach (var player in _arenaPlayers)
                if (player.IsValid && player.PlayerPawn.Value != null) player.PrintToCenterHtml(hudMsg);
        });
        AddCommand("css_lr", "Kullanım: !lr <isim> <silah>", OnLRCommand);
        AddCommand("css_arena", "Kullanım: !lr <isim> <silah>", OnLRCommand);
        AddCommand("css_kabul", "Daveti kabul et", OnAcceptCommand);
        AddCommand("css_accept", "Daveti kabul et", OnAcceptCommand);
    }
    private bool IsInCooldown(CCSPlayerController player, out double remainingMinutes)
    {
        remainingMinutes = 0;
        if (_playerCooldownEnd.TryGetValue(player.SteamID, out DateTime end))
        {
            if (DateTime.Now < end)
            {
                remainingMinutes = (end - DateTime.Now).TotalMinutes;
                return true;
            }
            _playerCooldownEnd.Remove(player.SteamID);
            _playerGameCount[player.SteamID] = 0;
        }
        return false;
    }
    public void OnLRCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || info.ArgCount < 3) return;
        if (_isArenaActive)
        {
            player.PrintToChat(" \x02[LR]\x01 Şu an aktif bir arena düellosu devam ediyor!");
            return;
        }
        if (!player.PawnIsAlive)
        {
            player.PrintToChat(" \x02[LR]\x01 Ölü olduğunuz için düello başlatamazsınız!");
            return;
        }
        if (IsInCooldown(player, out double remaining))
        {
            player.PrintToChat($" \x02[LR]\x01 Bekleme süresindesiniz: \x04{remaining:F1}\x01 dakika.");
            return;
        }

        string targetName = info.GetArg(1);
        string weaponName = info.GetArg(2).ToLower().Replace("weapon_", "");
        var target = Utilities.GetPlayers().FirstOrDefault(p => p.PlayerName.Contains(targetName, StringComparison.OrdinalIgnoreCase));

        if (target == null || !target.IsValid || target == player) return;

        if (!target.PawnIsAlive)
        {
            player.PrintToChat($" \x02[LR]\x01 \x04{target.PlayerName} \x01şu an ölü, ona davet gönderemezsiniz!");
            return;
        }
        if (!Config.AllowFriendlyFire && player.TeamNum == target.TeamNum)
        {
            player.PrintToChat(" \x02[LR]\x01 Takım arkadaşınıza düello teklif edemezsiniz!");
            return;
        }
        if (target.IsBot) { StartArena(player, target, weaponName); return; }
        _pendingInvites[target] = new LRInvite { Challenger = player, Weapon = weaponName };
        target.PrintToChat($" \x0C{player.PlayerName} \x01seninle \x04{weaponName} \x01ile vs atmak istiyor! \x04!kabul \x01yaz.");
        player.PrintToChat($" \x04{target.PlayerName} \x01oyuncusuna davet gönderildi.");
    }
    public void OnAcceptCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !_pendingInvites.ContainsKey(player)) return;
        if (_isArenaActive)
        {
            player.PrintToChat(" \x02[LR]\x01 Şu an başka bir düello başladığı için kabul edilemez.");
            return;
        }
        if (!player.PawnIsAlive)
        {
            player.PrintToChat(" \x02[LR]\x01 Ölü olduğunuz için daveti kabul edemezsiniz!");
            return;
        }
        var invite = _pendingInvites[player];
        if (!invite.Challenger.IsValid || !invite.Challenger.PawnIsAlive)
        {
            player.PrintToChat(" \x02[LR]\x01 Meydan okuyan oyuncu şu an aktif değil veya ölü.");
            _pendingInvites.Remove(player);
            return;
        }
        if (IsInCooldown(player, out double remaining))
        {
            player.PrintToChat($" \x02[LR]\x01 Şu an bekleme süresindesiniz.");
            return;
        }
        _pendingInvites.Remove(player);
        StartArena(invite.Challenger, player, invite.Weapon);
    }
    private void StartArena(CCSPlayerController p1, CCSPlayerController p2, string weapon)
    {
        _isArenaActive = true;
        _arenaPlayers.Add(p1);
        _arenaPlayers.Add(p2);

        IncrementGameCount(p1);
        IncrementGameCount(p2);

        SaveInventory(p1);
        SaveInventory(p2);

        Vector arenaPos = new Vector(Config.ArenaX, Config.ArenaY, Config.ArenaZ);
        var platform = Utilities.CreateEntityByName<CPhysicsPropOverride>("prop_physics_override");
        if (platform != null)
        {
            platform.Teleport(arenaPos, new QAngle(180, 0, 0), Vector.Zero);
            platform.DispatchSpawn();
            platform.SetModel(Config.ArenaModelPath);
            platform.AcceptInput("DisableMotion");
            _currentPlatform = platform;
        }

        AddTimer(2.0f, () =>
        {
            foreach (var p in _arenaPlayers)
            {
                if (!p.IsValid || p.PlayerPawn.Value == null) continue;
                p.RemoveWeapons();
                p.GiveNamedItem("weapon_" + weapon);
                p.GiveNamedItem("weapon_knife");
                pGetPlayerFreeze(p);
            }

            p1.PlayerPawn.Value!.Teleport(new Vector(arenaPos.X + 200, arenaPos.Y, arenaPos.Z + 200), new QAngle(0, 0, 0), Vector.Zero);
            p2.PlayerPawn.Value!.Teleport(new Vector(arenaPos.X - 200, arenaPos.Y, arenaPos.Z + 200), new QAngle(0, 180, 0), Vector.Zero);

            AddTimer(3.0f, () =>
            {
                foreach (var p in _arenaPlayers) pGetPlayerUnFreeze(p);
                _remainingTime = Config.ArenaDuration;
                _showTimer = true;
                _logicTimer = AddTimer(1.0f, () => {
                    _remainingTime--;
                    if (_remainingTime <= 0) CleanUpArena();
                }, TimerFlags.REPEAT);
            });
        });
    }
    private void IncrementGameCount(CCSPlayerController player)
    {
        if (player.IsBot) return;
        ulong id = player.SteamID;
        if (!_playerGameCount.ContainsKey(id)) _playerGameCount[id] = 0;
        _playerGameCount[id]++;
        if (_playerGameCount[id] >= Config.MaxGamesBeforeCooldown)
        {
            _playerCooldownEnd[id] = DateTime.Now.AddMinutes(Config.CooldownMinutes);
            player.PrintToChat($" \x02[LR]\x01 Oyun limitine ulaştınız! \x04{Config.CooldownMinutes}\x01 dakika bekleme süresi.");
        }
    }
    private void SaveInventory(CCSPlayerController player)
    {
        if (player.PlayerPawn.Value?.WeaponServices == null) return;
        var weapons = new List<string>();
        var myWeapons = player.PlayerPawn.Value.WeaponServices.MyWeapons;
        if (myWeapons == null) return;
        foreach (var weapon in myWeapons)
            if (weapon.Value != null && weapon.Value.IsValid && !string.IsNullOrEmpty(weapon.Value.DesignerName))
                weapons.Add(weapon.Value.DesignerName);
        _savedWeapons[player] = weapons;
    }
    private void RestoreInventory(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || !_savedWeapons.ContainsKey(player)) return;
        player.RemoveWeapons();
        foreach (var weaponClassName in _savedWeapons[player])
            if (!string.IsNullOrEmpty(weaponClassName)) player.GiveNamedItem(weaponClassName);
        _savedWeapons.Remove(player);
    }
    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (!_isArenaActive) return HookResult.Continue;
        var victim = @event.Userid;
        var killer = @event.Attacker;
        if (victim != null && _arenaPlayers.Contains(victim))
        {
            if (killer != null && killer.IsValid && _arenaPlayers.Contains(killer))
            {
                RestoreInventory(killer);
                MoveToSpawn(killer);
                Server.PrintToChatAll($" \x04[LR] \x0C{killer.PlayerName}\x01 kazandı!");
            }
            CleanUpArena();
        }
        return HookResult.Continue;
    }
    private void CleanUpArena()
    {
        _isArenaActive = false;
        _showTimer = false;
        _logicTimer?.Kill();
        _savedWeapons.Clear();
        _arenaPlayers.Clear();
        _currentPlatform?.AcceptInput("Kill");
        _currentPlatform = null;
    }
    private void MoveToSpawn(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || player.PlayerPawn.Value == null) return;
        string spawnPointName = player.TeamNum == 2 ? "info_player_terrorist" : "info_player_counterterrorist";
        var spawnPoints = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(spawnPointName);
        if (spawnPoints.Any())
        {
            var randomSpawn = spawnPoints.ElementAt(new Random().Next(spawnPoints.Count()));
            player.PlayerPawn.Value.Teleport(randomSpawn.AbsOrigin!, randomSpawn.AbsRotation!, Vector.Zero);
        }
    }
    private void pGetPlayerFreeze(CCSPlayerController iPlayer)
    {
        if (iPlayer?.PlayerPawn.Value == null) return;
        iPlayer.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_OBSOLETE;
        Schema.SetSchemaValue(iPlayer.PlayerPawn.Value.Handle, "CBaseEntity", "m_nActualMoveType", 1);
        Utilities.SetStateChanged(iPlayer.PlayerPawn.Value, "CBaseEntity", "m_MoveType");
        iPlayer.PlayerPawn.Value.TakesDamage = false;
    }
    private void pGetPlayerUnFreeze(CCSPlayerController iPlayer)
    {
        if (iPlayer?.PlayerPawn.Value == null) return;
        iPlayer.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_WALK;
        Schema.SetSchemaValue(iPlayer.PlayerPawn.Value.Handle, "CBaseEntity", "m_nActualMoveType", 2);
        Utilities.SetStateChanged(iPlayer.PlayerPawn.Value, "CBaseEntity", "m_MoveType");
        iPlayer.PlayerPawn.Value.TakesDamage = true;
    }
}