## 2026-11-23 · Workbench face textures

The crafting table and metalworking bench now use their dedicated textures on all four vertical faces. Both retain their original top and bottom tiles and saved placement orientation. The furnace, smoker, blast furnace, and brick kiln retain their directional face mappings.

The placement regression checks all four lateral faces and verifies both benches' full face mappings after an Infinite chunk unload and reload. `dotnet build`, `--placement-update-test`, `--crafting-test`, and `--interface-graphics-test` passed.

Run CPU regressions in the background and keep graphics tests off the active Windows desktop. All 273 checks in `dotnet run --no-build -- --self-test` passed in the background. On this host, SDL's Windows offscreen driver could not initialize OpenGL, and several graphics checks require a focused window.

## Background graphics runner

The Windows checkout builds the game and MonoGame content with `dotnet build`. [RunHeadlessGraphics.sh](../Tools/RunHeadlessGraphics.sh) copies that build to a temporary Linux directory so WSL reads an independent copy of the assembly. It uses .NET 10 through the project's major roll-forward setting, starts a private Xvfb display with Openbox, and runs the existing graphics suite with Mesa llvmpipe and a silent audio device. Exported PNGs go to `bin/headless-graphics-artifacts`.

From an administrator PowerShell, install Ubuntu 24.04 on WSL and restart Windows if prompted.

```powershell
wsl --install --distribution Ubuntu-24.04 --no-launch
```

Install the runner dependencies as root in Ubuntu, then create an unprivileged account for the graphics runs if the distro has no regular account.

```bash
apt-get update
apt-get install --no-install-recommends dotnet-runtime-10.0 xvfb xauth openbox x11-utils mesa-utils libgl1-mesa-dri libglx-mesa0
/usr/sbin/useradd --create-home --shell /bin/bash mdsm-runner
```

From the repository in PowerShell, run the default graphics suite or pass a focused test switch such as `--interface-graphics-test`.

```powershell
wsl --distribution Ubuntu-24.04 --user mdsm-runner --exec bash Tools/RunHeadlessGraphics.sh
```

The Linux aggregate runs each graphics suite in a fresh process because the in-process aggregate crashed after the seasonal suite. Interface, water, and mob screenshot checks let the SDL/X11 drawable resize complete before comparing exact pixels. The runner is installed on Ubuntu 24.04 under the unprivileged `mdsm-runner` account.

`dotnet build`, Bash syntax validation, ShellCheck, the focused water and mob movement checks, and all ten suites in the headless graphics aggregate passed. The aggregate log is in `bin/headless-graphics.log`.
