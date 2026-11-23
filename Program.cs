if(args.Length>0 && args[0] is "--finite-world-generation-test" or "--dungeon-generation-probe" or "--overnight-generation-probe"
    or "--traversal-performance-test" or "--lighting-performance-test" or "--compression-benchmark" or "--compression-fixture"
    or "--completion-performance-test" or "--gameplay-performance-test" or "--performance-test")
{
    System.Console.Error.WriteLine("Finite-world suites are retired. Use --self-test for Infinite regressions, --infinite-graphics-test for live Infinite profiling, or --legacy-smoke-test to triage Legacy (1024x1024).");
    System.Environment.ExitCode=2;
}
else if(args.Length==1 && args[0]=="--legacy-smoke-test")
{ System.Environment.ExitCode=MDSM.Testing.LegacySmokeChecks.Run(); }
else if(args.Length==1 && args[0]=="--season-lead-test")
{ System.Environment.ExitCode=MDSM.Testing.SeasonLeadChecks.Run(); }
else if(args.Length==1 && args[0]=="--industrial-test")
{ System.Environment.ExitCode=MDSM.Testing.IndustrialChecks.Run(); }
else if(args.Length==1 && args[0]=="--season-lead-graphics-test")
{ System.Environment.ExitCode=MDSM.Testing.SeasonLeadChecks.RunGraphics(); }
else if(args.Length==1 && args[0]=="--generation-balance-test")
{ System.Environment.ExitCode=MDSM.Testing.GenerationBalanceChecks.Run(); }
else if(args.Length==1 && args[0]=="--shore-crops-test")
{ System.Environment.ExitCode=MDSM.Testing.GenerationBalanceChecks.RunShore(); }
else if(args.Length==1 && args[0]=="--world-soundtrack-test")
{ System.Environment.ExitCode=MDSM.Testing.WorldSoundtrackChecks.Run(); }
else if(args.Length==1 && args[0]=="--cactus-fire-test")
{ System.Environment.ExitCode=MDSM.Testing.CactusFireChecks.Run(); }
else if(args.Length==1 && args[0]=="--smooth-lighting-reference")
{ System.Environment.ExitCode=MDSM.Testing.SmoothLightingChecks.Run(true); }
else if(args.Length==1 && args[0]=="--smooth-lighting-test")
{ System.Environment.ExitCode=MDSM.Testing.SmoothLightingChecks.Run(); }
else if(args.Length==1 && args[0]=="--placement-drop-cloud-test")
{ System.Environment.ExitCode=MDSM.Testing.PlacementDropCloudChecks.Run(); }
else if(args.Length==1 && args[0]=="--visual-refinement-test")
{ System.Environment.ExitCode=MDSM.Testing.VisualRefinementChecks.Run(); }
else if(args.Length==1 && args[0]=="--held-item-test")
{ System.Environment.ExitCode=MDSM.Testing.HeldItemChecks.Run(); }
else if(args.Length==1 && args[0]=="--swimming-test")
{ MDSM.Testing.WaterChecks.Swimming(); MDSM.Testing.WaterChecks.SurfaceBobbing(); }
else if(args.Length==1 && args[0]=="--menu-style-test")
{ System.Environment.ExitCode=MDSM.Testing.MenuStyleChecks.Run(); }
else if(args.Length==1 && args[0]=="--capture-menu-panorama")
{ System.Environment.ExitCode=MDSM.Testing.MenuPanoramaCapture.Run(); }
else if(args.Length==1 && args[0]=="--sandstone-test")
{
    System.Environment.ExitCode=MDSM.Testing.SandstoneChecks.Run();
}
else if(args.Length==1 && args[0]=="--inventory-icon-test")
{
    System.Environment.ExitCode=MDSM.Testing.InventoryIconChecks.Run();
}
else if(args.Length==1 && args[0]=="--infinite-water-test")
{
    System.Environment.ExitCode=MDSM.Testing.InfiniteWaterChecks.Run();
}
else if(args.Length==1 && args[0]=="--fluid-test")
{
    System.Environment.ExitCode=MDSM.Testing.BetaFluidChecks.Run();
}
else if(args.Length==1 && args[0]=="--running-boots-test")
{
    MDSM.Testing.RunningBootsChecks.Boots();
    System.Console.WriteLine("PASS: Running Boots recipe, armor, sprint modes and equipment save round-trip.");
}
else if(args.Length==1 && args[0]=="--terrain-balance-test")
{
    System.Environment.ExitCode=MDSM.Testing.TerrainBalanceChecks.Run();
}
else if(args.Length==1 && args[0]=="--menu-update-test")
{
    System.Environment.ExitCode=MDSM.Testing.MenuUpdateChecks.Run();
}
else if(args.Length==1 && args[0]=="--classic-hud-test")
{
    System.Environment.ExitCode=MDSM.Testing.ClassicHudChecks.Run();
}
else if(args.Length==1 && args[0]=="--bobbing-test")
{
    System.Environment.ExitCode=MDSM.Testing.ClassicBobbingChecks.Run();
}
else if(args.Length==1 && args[0]=="--infinite-graphics-test")
{
    System.Environment.ExitCode=MDSM.Testing.InfiniteGraphicsChecks.Run();
}
else if(args.Length==1 && args[0]=="--infinite-integration-test")
{
    System.Environment.ExitCode=MDSM.Testing.InfiniteIntegrationChecks.Run();
}
else if(args.Length==1 && args[0]=="--streaming-graphics-test")
{
    System.Environment.ExitCode=MDSM.Testing.StreamingGraphicsChecks.Run();
}
else if(args.Length==1 && args[0]=="--streaming-foundation-test")
{
    System.Environment.ExitCode=MDSM.Testing.StreamingFoundationChecks.Run();
}
else if(args.Length==1 && args[0]=="--exploration-test")
{
    System.Environment.ExitCode=MDSM.Testing.ExplorationChecks.Run();
}
else if(args.Length==1 && args[0]=="--dungeon-test")
{
    System.Environment.ExitCode=MDSM.Testing.GenerationBalanceChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--landscape-update-test"))
{
    System.Environment.ExitCode=MDSM.Testing.LandscapeUpdateChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--sign-fire-test"))
{
    System.Environment.ExitCode=MDSM.Testing.SignFireChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--overnight-graphics-test"))
{
    System.Environment.ExitCode=MDSM.Testing.OvernightGraphicsChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--overnight-gameplay-test"))
{
    System.Environment.ExitCode=MDSM.Testing.OvernightGameplayChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--overnight-test"))
{
    System.Environment.ExitCode=MDSM.Testing.OvernightChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--lighting-edit-benchmark"))
{
    System.Environment.ExitCode=MDSM.Testing.OvernightChecks.LightingEdits();
}
else if(System.Array.Exists(args,a=>a=="--mob-movement-graphics-test"))
{
    System.Environment.ExitCode=System.Math.Max(MDSM.Testing.MobGraphicsChecks.Run(),MDSM.Testing.PigGraphicsChecks.Run());
}
else if(System.Array.Exists(args,a=>a=="--mob-movement-test"))
{
    System.Environment.ExitCode=MDSM.Testing.MobMovementChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--world-rules-graphics-test"))
{
    System.Environment.ExitCode=MDSM.Testing.WorldRuleGraphicsChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--world-rules-test"))
{
    System.Environment.ExitCode=MDSM.Testing.WorldRuleChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--drop-control-graphics-test"))
{
    System.Environment.ExitCode=MDSM.Testing.DropControlGraphicsChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--drop-control-test"))
{
    System.Environment.ExitCode=MDSM.Testing.DropControlChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--command-graphics-test"))
{
    System.Environment.ExitCode=MDSM.Testing.CommandGraphicsChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--command-test"))
{
    System.Environment.ExitCode=MDSM.Testing.CommandChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--bed-sleep-graphics-test"))
{
    System.Environment.ExitCode=MDSM.Testing.BedSleepGraphicsChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--bed-sleep-test"))
{
    System.Environment.ExitCode=MDSM.Testing.BedSleepChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--wheat-test"))
{
    System.Environment.ExitCode=MDSM.Testing.WheatFollowChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--wheat-input-test"))
{
    System.Environment.ExitCode=MDSM.Testing.WheatFollowChecks.RunInput();
}
else if(System.Array.Exists(args,a=>a=="--lighting-graphics-test"))
{
    System.Environment.ExitCode=MDSM.Testing.LightingGraphicsChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--lighting-test"))
{
    System.Environment.ExitCode=MDSM.Testing.LightingChecks.Run();
}
else if(System.Array.Exists(args,a=>a=="--compression-test"))
{
    System.Environment.ExitCode=MDSM.Testing.CompressionChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--audio-graphics-test"))
{
    System.Environment.ExitCode = MDSM.Testing.AudioGraphicsChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--audio-test"))
{
    System.Environment.ExitCode = MDSM.Testing.AudioChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--support-drops-test"))
{
    System.Environment.ExitCode = MDSM.Testing.SupportDropChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--collision-test"))
{
    System.Environment.ExitCode = MDSM.Testing.CollisionUpdateChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--collision-graphics-test") || System.Array.Exists(args, argument => argument == "--collision-repro-graphics"))
{
    System.Environment.ExitCode = MDSM.Testing.CollisionGraphicsChecks.Run(System.Array.Exists(args, argument => argument == "--collision-repro-graphics"));
}
else if (System.Array.Exists(args, argument => argument == "--collision-repro"))
{
    MDSM.Testing.CollisionUpdateChecks.Diagnose();
}
else if (System.Array.Exists(args, argument => argument == "--placement-update-test"))
{
    System.Environment.ExitCode = MDSM.Testing.PlacementUpdateChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--torch-test"))
{
    System.Environment.ExitCode = MDSM.Testing.TorchChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--door-graphics-test"))
{
    System.Environment.ExitCode = MDSM.Testing.DoorChecks.RunGraphics();
}
else if (System.Array.Exists(args, argument => argument == "--door-test"))
{
    System.Environment.ExitCode = MDSM.Testing.DoorChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--hostile-model-graphics-test"))
{
    System.Environment.ExitCode = MDSM.Testing.HostileModelChecks.RunGraphics();
}
else if (System.Array.Exists(args, argument => argument == "--hostile-model-test"))
{
    System.Environment.ExitCode = MDSM.Testing.HostileModelChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--hostile-spawn-test"))
{
    System.Environment.ExitCode = MDSM.Testing.HostileSpawnChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--gameplay-refinement-test"))
{
    System.Environment.ExitCode = MDSM.Testing.GameplayRefinementChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--completion-test"))
{
    System.Environment.ExitCode = MDSM.Testing.CompletionChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--interface-graphics-test"))
{
    System.Environment.ExitCode = MDSM.Testing.InterfaceGraphicsChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--refinement-test"))
{
    System.Environment.ExitCode = MDSM.Testing.RefinementChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--survival-test"))
{
    System.Environment.ExitCode = MDSM.Testing.ProgressionChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--crafting-test"))
{
    System.Environment.ExitCode = MDSM.Testing.CraftingChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--lake-test"))
{
    System.Environment.ExitCode = MDSM.Testing.InfiniteWaterChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--self-test"))
{
    System.Environment.ExitCode = MDSM.Testing.PrototypeChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--graphics-test"))
{
    int[] results =
    {
        MDSM.Testing.StreamingGraphicsChecks.Run(), MDSM.Testing.SeasonLeadChecks.RunGraphics(),
        MDSM.Testing.WorldRuleGraphicsChecks.Run(), MDSM.Testing.InterfaceGraphicsChecks.Run(), MDSM.Testing.AudioGraphicsChecks.Run(),
        MDSM.Testing.CollisionGraphicsChecks.Run(), MDSM.Testing.DoorChecks.RunGraphics(),
        MDSM.Testing.PigGraphicsChecks.Run(), MDSM.Testing.WaterGraphicsChecks.Run(),
        MDSM.Testing.MobGraphicsChecks.Run()
    };
    System.Environment.ExitCode = System.Linq.Enumerable.Max(results);
}
else if (args.Length == 1 && args[0] == "--water-graphics-test")
{
    System.Environment.ExitCode = MDSM.Testing.WaterGraphicsChecks.Run();
}
else
{
    using var game = new MDSM.Core.Game1();
    game.Run();
}
