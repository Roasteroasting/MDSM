using System;
using System.Collections.Generic;

namespace MDSM.Audio;

public enum SoundCategory { Effects, Mobs, Interface }
public enum SoundMaterial { Stone, Dirt, Grass, Wood, Sand, Gravel, Glass, Metal, Wool, Leaves, Plant }
public enum SoundKind
{
    Hit, Break, Place, Step, Land, MobIdle, MobHurt, MobDeath,
    PlayerHurt, PlayerDeath, Pickup, Jump, WaterEnter, WaterExit, Swim, WaterFlow,
    DoorOpen, DoorClose, GateOpen, GateClose, ChestOpen, ChestClose,
    InventoryOpen, InventoryClose, InventoryMove, Craft, Equip, Click,
    Eat, Drop, ToolBreak, BowDraw, BowShoot, ArrowHit, Swing, Shear, Till, Plant, Pour, Scoop, Ignite, Smelt, Fizz, Feed
}

public sealed record SoundGroup(SoundKind Kind, SoundMaterial Material, int Species, string Path,
    SoundCategory Category, int Variations, float Gain, float Range, double Interval, int Priority)
{
    public bool InMenus => Category == SoundCategory.Interface || Kind is SoundKind.PlayerDeath or SoundKind.ChestOpen or SoundKind.ChestClose or SoundKind.Ignite or SoundKind.Smelt;
    public string Asset(int variation) => $"Sounds/{Path}-{variation}.wav";
}

// Shared with the development generator. Asset indices are unrelated to saved block/item IDs.
public static class SoundCatalog
{
    public static readonly SoundGroup[] Groups = Create();
    private static readonly Dictionary<(SoundKind,SoundMaterial,int),int> Indices = Index();
    public static int Find(SoundKind kind, SoundMaterial material=SoundMaterial.Stone, int species=0)
        => Indices[(kind, kind<=SoundKind.Land?material:SoundMaterial.Stone,
            kind is >=SoundKind.MobIdle and <=SoundKind.MobDeath?species:0)];
    private static Dictionary<(SoundKind,SoundMaterial,int),int> Index()
    {
        var result=new Dictionary<(SoundKind,SoundMaterial,int),int>();
        for(int i=0;i<Groups.Length;i++){var g=Groups[i];result.Add((g.Kind,g.Material,g.Species),i);}return result;
    }
    private static SoundGroup[] Create()
    {
        var result=new List<SoundGroup>();
        foreach(SoundMaterial material in Enum.GetValues<SoundMaterial>())
        for(int action=0;action<5;action++)
        {
            var kind=(SoundKind)action;
            result.Add(new(kind,material,0,$"Blocks/{material.ToString().ToLowerInvariant()}/{kind.ToString().ToLowerInvariant()}",
                SoundCategory.Effects,3,kind==SoundKind.Step?.35f:kind==SoundKind.Hit?.5f:.75f,kind==SoundKind.Step?16:28,
                kind==SoundKind.Hit?.16:kind==SoundKind.Step?.045:.035,kind is SoundKind.Hit or SoundKind.Step?1:2));
        }
        string[] species={"pig","cow","sheep","chicken","zombie","skeleton","spider"};
        for(int mob=0;mob<species.Length;mob++)for(int action=0;action<3;action++)
        {
            var kind=(SoundKind)((int)SoundKind.MobIdle+action);
            result.Add(new(kind,SoundMaterial.Stone,mob,$"Mobs/{species[mob]}/{new[]{"idle","hurt","death"}[action]}",
                SoundCategory.Mobs,2,action==0?.65f:.9f,action==0?28:36,action==0?1:.08,action==0?0:3));
        }
        foreach(SoundKind kind in Enum.GetValues<SoundKind>())
        {
            if(kind<=SoundKind.MobDeath)continue;
            bool ui=kind is SoundKind.InventoryOpen or SoundKind.InventoryClose or SoundKind.InventoryMove or SoundKind.Craft or SoundKind.Equip or SoundKind.Click or SoundKind.Drop;
            string folder=ui?"Interface":kind is SoundKind.WaterEnter or SoundKind.WaterExit or SoundKind.Swim or SoundKind.WaterFlow or SoundKind.Pour or SoundKind.Scoop?"Water"
                :kind is SoundKind.PlayerHurt or SoundKind.PlayerDeath or SoundKind.Eat or SoundKind.Jump or SoundKind.Pickup?"Player":"Interactions";
            result.Add(new(kind,SoundMaterial.Stone,0,$"{folder}/{kind.ToString().ToLowerInvariant()}",ui?SoundCategory.Interface:SoundCategory.Effects,
                2,ui?.55f:.8f,28,kind==SoundKind.WaterFlow?.5:kind==SoundKind.Swim?.3:kind==SoundKind.PlayerHurt?.12:.07,
                ui || kind is SoundKind.PlayerDeath or SoundKind.PlayerHurt?4:2));
        }
        return result.ToArray();
    }
}
