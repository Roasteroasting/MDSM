using System;
using System.Collections.Generic;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;

namespace MDSM.Input;

internal static class CommandCompletion
{
    // Completion is an input-time operation, never a simulation/update scan.
    public static string[] Candidates(string text,int caret,out int start,out int end)
    {
        caret=Math.Clamp(caret,0,text.Length);start=caret;end=caret;
        while(start>0 && !char.IsWhiteSpace(text[start-1]))start--;
        while(end<text.Length && !char.IsWhiteSpace(text[end]))end++;
        string prefix=text[start..caret];string[] before=text[..start].Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var options=new List<string>();
        if(before.Length==0)options.AddRange(new[]{"/give","/spawn","/kill","/time","/gamemode","/help"});
        else switch(before[0].ToLowerInvariant())
        {
            case "/give":
                if(before.Length==1)options.Add("@p");
                else if(before.Length==2)foreach(var item in Items.Obtainable)options.Add(WorldCommands.Key(Items.Name(item)));
                break;
            case "/spawn": if(before.Length==1)foreach(var species in Enum.GetValues<MobSpecies>())options.Add(species.ToString().ToLowerInvariant());break;
            case "/kill":
                if(before.Length==1)options.AddRange(new[]{"@p","@e"});
                else if(before.Length==2 && before[1]=="@e")
                {options.AddRange(new[]{"type=mob","type=passive","type=hostile","type=player","type=item","type=arrow","type=snowball","type=falling_block"});foreach(var species in Enum.GetValues<MobSpecies>())options.Add("type="+species.ToString().ToLowerInvariant());}
                break;
            case "/time":
                if(before.Length==1)options.AddRange(new[]{"set","query"});
                else if(before.Length==2 && before[1].Equals("set",StringComparison.OrdinalIgnoreCase))options.AddRange(new[]{"sunrise","day","noon","sunset","night","midnight"});break;
            case "/gamemode":if(before.Length==1)options.AddRange(new[]{"survival","creative"});break;
            case "/help":if(before.Length==1)options.AddRange(new[]{"give","spawn","kill","time","gamemode","items","mobs"});break;
        }
        options.RemoveAll(s=>!s.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));options.Sort(StringComparer.Ordinal);return options.ToArray();
    }
}
