param([string]$UnityData = 'C:/Program Files/Unity/Hub/Editor/6000.6.6f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$out = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force $out | Out-Null
$dotnet = Join-Path $UnityData 'DotNetSdk/dotnet.exe'
$sdk = Get-ChildItem (Join-Path $UnityData 'DotNetSdk/sdk') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$compiler = Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll'
$runtime = Join-Path $repo 'src/com.stormlab.supercell/Runtime/Rainfall'
$pack = Get-ChildItem (Join-Path $UnityData 'DotNetSdk/packs/Microsoft.NETCore.App.Ref') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$netRefs = @(Get-ChildItem (Join-Path $pack.FullName 'ref/net8.0') -Filter '*.dll' | ForEach-Object { '/r:' + $_.FullName })
& $dotnet $compiler /nologo /noconfig /nostdlib /target:exe "/out:$out/PhysicsChecks.dll" $netRefs "$PSScriptRoot/Program.cs" "$runtime/RainSpectrum.cs"
if ($LASTEXITCODE -ne 0) { throw 'Physics check compilation failed' }
$config = '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"},"rollForward":"LatestPatch"}}'
Set-Content "$out/PhysicsChecks.runtimeconfig.json" $config
& $dotnet "$out/PhysicsChecks.dll"
if ($LASTEXITCODE -ne 0) { throw 'Physics checks failed' }
$refs = @('/r:' + "$UnityData/NetStandard/ref/2.1.0/netstandard.dll")
$engine = @(Get-ChildItem "$UnityData/Managed/UnityEngine" -Filter '*.dll')
$refs += $engine | ForEach-Object { '/r:' + $_.FullName }
$sources = @(Get-ChildItem $runtime -Filter '*.cs' | ForEach-Object FullName)
& $dotnet $compiler /nologo /noconfig /nostdlib /target:library /langversion:9 "/out:$out/StormLab.Rainfall.dll" $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Unity runtime compilation failed' }
$rainRef = '/r:' + "$out/StormLab.Rainfall.dll"
$supercell = Join-Path $repo 'tests/SupercellLabTest/Library/ScriptAssemblies/StormLab.Supercell.dll'
if (!(Test-Path $supercell)) { throw 'Open the host Unity project once to compile StormLab.Supercell.dll before bridge checks.' }
& $dotnet $compiler /nologo /noconfig /nostdlib /target:library /langversion:9 "/out:$out/StormLab.Rainfall.Clouds.dll" $refs $rainRef "/r:$supercell" "$runtime/Integration/BrainRainSource.cs"
if ($LASTEXITCODE -ne 0) { throw 'Cloud bridge compilation failed' }
& $dotnet $compiler /nologo /noconfig /nostdlib /target:library /langversion:9 "/out:$out/StormLab.Rainfall.Editor.dll" $refs $rainRef "$runtime/Editor/RainfallDemo.cs" "$PSScriptRoot/RainfallUnityChecks.cs"
if ($LASTEXITCODE -ne 0) { throw 'Unity editor compilation failed' }
# Run only managed sampling routines under .NET; this does not exercise native Unity rendering/physics.
$engine | Where-Object Name -like 'UnityEngine*.dll' | Copy-Item -Destination $out -Force
Copy-Item $supercell $out -Force
$bridgeRefs = @($rainRef, "/r:$out/StormLab.Rainfall.Clouds.dll", "/r:$supercell")
$bridgeRefs += ('/r:' + "$UnityData/Managed/UnityEngine/UnityEngine.CoreModule.dll")
& $dotnet $compiler /nologo /noconfig /nostdlib /target:exe "/out:$out/BridgeChecks.dll" $netRefs $bridgeRefs "$PSScriptRoot/BridgeChecks.cs"
if ($LASTEXITCODE -ne 0) { throw 'Bridge check compilation failed' }
Set-Content "$out/BridgeChecks.runtimeconfig.json" $config
& $dotnet "$out/BridgeChecks.dll"
if ($LASTEXITCODE -ne 0) { throw 'Bridge checks failed' }
Write-Output 'PASS: rainfall runtime, optional cloud bridge and editor compile against Unity references.'
