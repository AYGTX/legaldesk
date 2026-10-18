# Registers Harvey's MCP server in Claude Desktop.
# Run it from a normal PowerShell window while Claude Desktop is fully closed (taskbar tray icon > Quit):
# Claude Desktop rewrites its configuration when it quits, so a change made while it runs is lost.
#
#   powershell -ExecutionPolicy Bypass -File src\legal-corpus\tools\install-claude-desktop-mcp.ps1

$ErrorActionPreference = "Stop"

if (Get-Process -Name "Claude" -ErrorAction SilentlyContinue) {
    Write-Host "Claude Desktop is still running. Quit it from the tray icon (right-click > Quit), then run this again." -ForegroundColor Yellow
    exit 1
}

$corpus = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$python = Join-Path $corpus ".venv\Scripts\python.exe"
if (-not (Test-Path $python)) { throw "Harvey's Python environment was not found at $python" }

$targets = @(
    (Join-Path $env:APPDATA "Claude\claude_desktop_config.json"),
    (Join-Path $env:LOCALAPPDATA "Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json")
) | Where-Object { Test-Path $_ }
if (-not $targets) { $targets = @(Join-Path $env:APPDATA "Claude\claude_desktop_config.json") }

$script = @'
import json, sys, shutil, pathlib
path, python, corpus = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3]
data = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}
if path.exists():
    shutil.copy(path, path.with_suffix(".json.bak"))
data.setdefault("mcpServers", {})["harvey"] = {
    "command": python, "args": ["-m", "app.mcp_server"], "cwd": corpus, "env": {"PYTHONPATH": corpus},
}
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
json.loads(path.read_text(encoding="utf-8"))
print("Harvey registered in", path)
'@

foreach ($config in $targets) {
    $script | & $python - $config $python $corpus
}
Write-Host "Done. Start Claude Desktop again: 'harvey' will be listed under the tools of a chat." -ForegroundColor Green
