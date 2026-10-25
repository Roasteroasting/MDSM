if (System.Array.Exists(args, argument => argument == "--self-test"))
{
    System.Environment.ExitCode = MDSM.PrototypeChecks.Run();
}
else if (System.Array.Exists(args, argument => argument == "--graphics-test"))
{
    System.Environment.ExitCode = MDSM.InterfaceGraphicsChecks.Run();
}
else
{
    using var game = new MDSM.Game1();
    game.Run();
}
