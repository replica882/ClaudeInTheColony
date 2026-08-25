#!/usr/bin/env bash
# 编译 ClaudeInTheColony 并部署到 ONI 的开发 mod 目录
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
DEST="$HOME/Library/Application Support/unity.Klei.Oxygen Not Included/mods/dev/ClaudeInTheColony"

cd "$HERE"
# restore 要走网络，开着代理时可能卡很久；有缓存就直接编译
if [ -d obj ]; then
  dotnet build -c Release -v quiet --nologo --no-restore
else
  dotnet build -c Release -v quiet --nologo
fi

mkdir -p "$DEST"
cp bin/Release/ClaudeInTheColony.dll "$DEST/"
cp mod.yaml mod_info.yaml "$DEST/"

echo "已部署 → $DEST"
ls -la "$DEST"
