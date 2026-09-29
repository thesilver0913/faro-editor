#!/bin/sh
# Spec 11.5 spike: runs the app under `dotnet watch`, rewrites the method body mid-run,
# and shows whether the MethodInfo cached before the reload now runs the new body.
cd "$(dirname "$0")"
LOG=$(mktemp)
printf 'public class Target\n{\n    public string Hello() => "v1";\n}\n' > Target.cs
DOTNET_USE_POLLING_FILE_WATCHER=1 timeout 60 dotnet watch run --non-interactive > "$LOG" 2>&1 &
until grep -q "cached=v1" "$LOG"; do sleep 1; done
sleep 3
sed 's/"v1"/"v2"/' Target.cs > Target.tmp && cat Target.tmp > Target.cs && rm Target.tmp
sleep 10
kill $!
printf 'public class Target\n{\n    public string Hello() => "v1";\n}\n' > Target.cs
grep "^cached=" "$LOG" | uniq
rm -f "$LOG"
