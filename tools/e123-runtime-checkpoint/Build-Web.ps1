param([string]$OutputDirectory = 'artifacts/e123-runtime-checkpoint/web')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
if (-not $out.StartsWith($root + [IO.Path]::DirectorySeparatorChar)) { throw 'Output must stay under the selected checkout.' }
dotnet publish "$root/adapters/browser-runtime/GearInvest.BrowserRuntime.csproj" -c Release -p:PublishTrimmed=false -p:RestoreLockedMode=true -o "$out/publish" -v minimal
if ($LASTEXITCODE -ne 0) { throw 'WASM publish failed' }
$web = Join-Path $out 'publish/wwwroot'
New-Item -ItemType Directory -Force -Path "$web/example" | Out-Null
Copy-Item -LiteralPath "$root/examples/runtime/web/index.html","$root/examples/runtime/web/main.js","$root/examples/runtime/web/style.css" -Destination "$web/example"
New-Item -ItemType Directory -Force -Path "$web/verification" | Out-Null
Copy-Item -LiteralPath "$root/tools/e123-runtime-checkpoint/browser-checks.html" -Destination "$web/verification/index.html"
Copy-Item -LiteralPath "$root/tools/e123-runtime-checkpoint/browser-checks.js" -Destination "$web/verification/browser-checks.js"
dotnet build "$root/examples/runtime/dotnet/Runtime.Consumer.csproj" -c Release -p:RestoreLockedMode=true -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Consumer build failed' }
$source = "$web/example/source.json"
if (-not (Test-Path -LiteralPath $source)) { dotnet "$root/examples/runtime/dotnet/bin/Release/net8.0/Runtime.Consumer.dll" create-example $source; if ($LASTEXITCODE -ne 0) { throw 'Source authoring failed' } }
$assets = Get-Content -Raw "$root/adapters/browser-runtime/obj/project.assets.json" | ConvertFrom-Json
$runtimeNotice = $null
foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $candidate = Join-Path $folder 'microsoft.netcore.app.runtime.mono.browser-wasm/10.0.12'
    if (Test-Path -LiteralPath "$candidate/LICENSE.TXT") { $runtimeNotice = $candidate; break }
}
if (-not $runtimeNotice) { throw 'Restored .NET browser runtime notices are missing.' }
New-Item -ItemType Directory -Force -Path "$web/licenses/dotnet" | Out-Null
Copy-Item -LiteralPath "$runtimeNotice/LICENSE.TXT","$runtimeNotice/THIRD-PARTY-NOTICES.TXT" -Destination "$web/licenses/dotnet"
Copy-Item -LiteralPath "$root/LICENSE","$root/LICENSE_SCOPE.md","$root/THIRD_PARTY_NOTICES.md" -Destination "$web/licenses"
Write-Output "Static root: $web"
