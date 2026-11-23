## 2026-11-23 · Workbench face textures

The crafting table and metalworking bench now use their dedicated textures on all four vertical faces. Both retain their original top and bottom tiles and saved placement orientation. The furnace, smoker, blast furnace, and brick kiln retain their directional face mappings.

The placement regression checks all four lateral faces and verifies both benches' full face mappings after an Infinite chunk unload and reload. `dotnet build`, `--placement-update-test`, `--crafting-test`, and `--interface-graphics-test` passed.

Run CPU regressions in the background and keep graphics tests off the active Windows desktop. All 273 checks in `dotnet run --no-build -- --self-test` passed in the background. On this host, SDL's offscreen driver could not initialize OpenGL, and several graphics checks require a focused window. A separate graphics runner with an isolated display still needs verification.

## Background graphics runner

The Windows checkout builds the game and MonoGame content with `dotnet build`. The Linux runner uses that output with the .NET 10 runtime through the project's major roll-forward setting. [RunHeadlessGraphics.sh](../Tools/RunHeadlessGraphics.sh) starts a private Xvfb display with Openbox, checks the Mesa OpenGL context, and runs the existing graphics suite with a silent audio device.

From an administrator PowerShell, install Ubuntu 24.04 on WSL and restart Windows if prompted.

```powershell
wsl --install --distribution Ubuntu-24.04 --no-launch
```

After Ubuntu's first-launch account setup, install the runner dependencies in Ubuntu.

```bash
sudo apt-get update
sudo apt-get install --no-install-recommends dotnet-runtime-10.0 xvfb xauth openbox x11-utils mesa-utils libgl1-mesa-dri libglx-mesa0
```

From the repository in PowerShell, run the default graphics suite or pass a focused test switch such as `--interface-graphics-test`.

```powershell
wsl --distribution Ubuntu-24.04 --exec bash Tools/RunHeadlessGraphics.sh
```

`dotnet build` and ShellCheck passed. WSL installation remains blocked on this host because WinGet requires Windows administrator privileges for the WSL package. End-to-end graphics verification is pending.
