#!/bin/sh
cd "$(dirname "$0")" || exit 1

exec dotnet build FullThrust.slnx "$@"
