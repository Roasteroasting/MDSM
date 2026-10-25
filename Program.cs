if (System.Array.Exists(args, argument => argument == "--self-test"))
{
    System.Environment.ExitCode = MDSM.Testing.PrototypeChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--graphics-test"))
{
    System.Environment.ExitCode = MDSM.Testing.InterfaceGraphicsChecks.Run();
}
else
{
    using var game = new MDSM.Core.Game1();
    game.Run();
}
