#!/bin/bash
# Smoke test on the running emulator (checks.yml): installs the APK, starts it, and checks it is still running after a
# while (a crash ends the process). Leaves android-screen.png and android-logcat.txt for the artifact.
set -u
apk=$1 package=$2
adb install -r "$apk" || exit 1
adb logcat -c
adb shell monkey -p "$package" -c android.intent.category.LAUNCHER 1
sleep 30
adb exec-out screencap -p > android-screen.png
adb logcat -d > android-logcat.txt
if ! adb shell pidof "$package" > /dev/null; then
  echo "$package isn't running: it crashed or never started. Log:"
  grep -iE "$package|mono|dotnet|avalonia|FATAL|exception" android-logcat.txt | tail -80
  exit 1
fi
echo "$package is running"
