if (System.Array.Exists(args, argument => argument == "--self-test"))
{
    System.Environment.ExitCode = MDSM.Testing.PrototypeChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--graphics-test"))
{
    System.Environment.ExitCode = System.Math.Max(System.Math.Max(MDSM.Testing.InterfaceGraphicsChecks.Run(), MDSM.Testing.PigGraphicsChecks.Run()),
        System.Math.Max(MDSM.Testing.WaterGraphicsChecks.Run(), MDSM.Testing.MobGraphicsChecks.Run()));
}
else
{
    using var game = new MDSM.Core.Game1();
    game.Run();
}
