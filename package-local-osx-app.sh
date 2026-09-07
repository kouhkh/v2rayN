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
script_dir="$(cd "$(dirname "$0")" && pwd)"
branded_icon="$script_dir/branding/v2rayN-codex.icns"
branded_png="$script_dir/branding/v2rayN-codex-1024.png"

if [[ -e "$staging_dir" || -e "$app_path" ]]; then
  echo "staging directory and app path must not already exist" >&2
  exit 2
fi

for required_file in v2rayN AmazTool v2rayN.icns v2rayN.png; do
  if [[ ! -f "$gui_output/$required_file" ]]; then
    echo "missing required single-file publish output: $gui_output/$required_file" >&2
    exit 2
  fi
done

# The macOS release uses separate single-file publishes for v2rayN and AmazTool.
# Publishing both projects as loose files into one directory lets AmazTool's
# trimming pass overwrite framework assemblies needed by the desktop app.
if find "$gui_output" -maxdepth 1 -type f \( -name '*.dll' -o -name '*.deps.json' -o -name '*.runtimeconfig.json' \) | grep -q .; then
  echo "GUI output contains loose managed assemblies; publish v2rayN and AmazTool separately with PublishSingleFile=true" >&2
  exit 2
fi

mkdir -p "$staging_dir" "$app_path/Contents/MacOS" "$app_path/Contents/Resources"
unzip -q "$core_zip" -d "$staging_dir"
cp -R "$gui_output"/. "$app_path/Contents/MacOS"/
cp -R "$staging_dir/v2rayN-macos-arm64"/. "$app_path/Contents/MacOS"/
if [[ -f "$branded_icon" ]]; then
  cp "$branded_icon" "$app_path/Contents/MacOS/v2rayN.icns"
fi
if [[ -f "$branded_png" ]]; then
  cp "$branded_png" "$app_path/Contents/MacOS/v2rayN.png"
fi
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
