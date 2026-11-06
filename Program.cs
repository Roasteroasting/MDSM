if (System.Array.Exists(args, argument => argument == "--placement-update-test"))
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
else if (System.Array.Exists(args, argument => argument == "--completion-performance-test"))
{
    System.Environment.ExitCode = MDSM.Testing.CompletionPerformanceChecks.Run();
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
    System.Environment.ExitCode = MDSM.Testing.LargeLakeChecks.Run();
}
else if (args.Length >= 2 && args[0] == "--gameplay-performance-test" && System.Enum.TryParse<MDSM.Worlds.WorldSize>(args[1], out var gameplaySize))
{
    System.Environment.ExitCode = MDSM.Testing.GameplayPerformanceChecks.Run(gameplaySize,System.Array.Exists(args,a=>a=="--cave"));
}
else if (args.Length >= 2 && args[0] == "--performance-test" && System.Enum.TryParse<MDSM.Worlds.WorldSize>(args[1], out var benchmarkSize))
{
    System.Environment.ExitCode = MDSM.Testing.PerformanceChecks.Run(benchmarkSize);
}
else if (System.Array.Exists(args, argument => argument == "--self-test"))
{
    System.Environment.ExitCode = MDSM.Testing.PrototypeChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--graphics-test"))
{
    System.Environment.ExitCode = System.Math.Max(MDSM.Testing.DoorChecks.RunGraphics(),
        System.Math.Max(System.Math.Max(MDSM.Testing.InterfaceGraphicsChecks.Run(), MDSM.Testing.PigGraphicsChecks.Run()),
        System.Math.Max(MDSM.Testing.WaterGraphicsChecks.Run(), System.Math.Max(MDSM.Testing.MobGraphicsChecks.Run(), MDSM.Testing.WorldExpansionGraphicsChecks.Run()))));
}
else
{
    using var game = new MDSM.Core.Game1();
    game.Run();
}
