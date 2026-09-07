#!/bin/bash
set -euo pipefail

if [[ $# -ne 5 ]]; then
  echo "usage: $0 <gui-output> <core-zip> <staging-dir> <app-path> <version>" >&2
  exit 2
fi

gui_output="$1"
core_zip="$2"
staging_dir="$3"
app_path="$4"
version="$5"

if [[ -e "$staging_dir" || -e "$app_path" ]]; then
  echo "staging directory and app path must not already exist" >&2
  exit 2
fi

mkdir -p "$staging_dir" "$app_path/Contents/MacOS" "$app_path/Contents/Resources"
unzip -q "$core_zip" -d "$staging_dir"
cp -R "$gui_output"/. "$app_path/Contents/MacOS"/
cp -R "$staging_dir/v2rayN-macos-arm64"/. "$app_path/Contents/MacOS"/
cp "$app_path/Contents/MacOS/v2rayN.icns" "$app_path/Contents/Resources/AppIcon.icns"
printf '%s\n' 'When this file exists, app will not store configs under this folder' > "$app_path/Contents/MacOS/NotStoreConfigHere.txt"
chmod +x "$app_path/Contents/MacOS/v2rayN" "$app_path/Contents/MacOS/AmazTool"

cat > "$app_path/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleDisplayName</key><string>v2rayN Codex Observability</string>
  <key>CFBundleExecutable</key><string>v2rayN</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundleIdentifier</key><string>2dust.v2rayN.codex-observability</string>
  <key>CFBundleName</key><string>v2rayN Codex Observability</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${version}</string>
  <key>LSMinimumSystemVersion</key><string>13.6</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF

codesign --force --deep --sign - "$app_path"
echo "$app_path"
