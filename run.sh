#!/bin/sh
# Launches the game, building first when sources changed; arguments pass straight through as game flags (see README).
cd "$(dirname "$0")" || exit 1

exec dotnet run --project src/FullThrust.Game -- "$@"
