#!/usr/bin/env bash
set -euo pipefail

project_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
suite=${1:---graphics-test}
if [[ $# -gt 1 || ! $suite =~ ^--[a-z0-9-]+-test$ ]] ||
    ! grep -Fq -- "\"$suite\"" "$project_dir/Program.cs"; then
    printf 'Usage: bash Tools/RunHeadlessGraphics.sh [--graphics-test or another test switch from Program.cs]\n' >&2
    exit 2
fi

build_dir="$project_dir/bin/Debug/net9.0"
if [[ ! -f $build_dir/MDSM.dll || ! -f $build_dir/Content/Effects/BlockLighting.xnb ]]; then
    printf 'Run dotnet build in the Windows checkout before starting the graphics suite.\n' >&2
    exit 2
fi

for tool in dotnet Xvfb xvfb-run xauth xprop openbox glxinfo; do
    if ! command -v "$tool" >/dev/null; then
        printf 'Missing Linux runner dependency: %s\n' "$tool" >&2
        exit 2
    fi
done

# Give all test processes the same build and keep .NET from mapping DLLs on DrvFS.
run_dir=$(mktemp -d /tmp/mdsm-graphics.XXXXXXXX)
trap 'rm -r -- "$run_dir"' EXIT
cp -R -- "$build_dir"/*.dll "$build_dir"/MDSM.{deps,runtimeconfig}.json \
    "$build_dir"/Content "$build_dir"/runtimes "$run_dir/"

# Force a private X11 display, Mesa software OpenGL, and a silent OpenAL device.
export SDL_VIDEODRIVER=x11 LIBGL_ALWAYS_SOFTWARE=1 ALSOFT_DRIVERS=null
status=0
# Expand the variables inside the Bash process that inherits the private display.
# shellcheck disable=SC2016
xvfb-run --auto-servernum --server-args='-screen 0 3840x2160x24 -nolisten tcp' bash -eu -o pipefail -c '
        openbox --sm-disable &
        wm_pid=$!
        stop_window_manager() {
            kill "$wm_pid" 2>/dev/null || true
            wait "$wm_pid" 2>/dev/null || true
        }
        trap stop_window_manager EXIT

        # Some suites assert real window focus. Wait for the private window manager.
        deadline=$((SECONDS + 10))
        until xprop -root _NET_SUPPORTING_WM_CHECK 2>/dev/null | grep -F "window id #" >/dev/null; do
            if (( SECONDS >= deadline )) || ! kill -0 "$wm_pid" 2>/dev/null; then
                printf "Openbox did not initialize on the virtual display.\n" >&2
                exit 1
            fi
            sleep 0.1
        done

        glxinfo -B
        dotnet "$1" "$2"
    ' mdsm-graphics "$run_dir/MDSM.dll" "$suite" || status=$?

shopt -s nullglob
screenshots=("$run_dir"/*.png)
if (( ${#screenshots[@]} )); then
    artifacts="$project_dir/bin/headless-graphics-artifacts"
    mkdir -p -- "$artifacts"
    cp -- "${screenshots[@]}" "$artifacts/"
fi
exit "$status"
