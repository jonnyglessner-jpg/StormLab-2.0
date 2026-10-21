$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$unityData = 'C:/Program Files/Unity/Hub/Editor/6000.6.6f1/Editor/Data'
$dotnet = Join-Path $unityData 'DotNetSdk/dotnet.exe'
$sdk = Get-ChildItem (Join-Path $unityData 'DotNetSdk/sdk') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$compiler = Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll'
$output = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force $output | Out-Null
Push-Location $repo
try {
    & $dotnet run --project validation/lightning/LightningChecks.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Core lightning checks failed.' }
    & $dotnet run --project validation/lightning/StormChecks/StormChecks.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Storm-driven lightning checks failed.' }
    $refs = @('/r:' + "$unityData/NetStandard/ref/2.1.0/netstandard.dll")
    $refs += Get-ChildItem "$unityData/Managed/UnityEngine" -Filter '*.dll' | ForEach-Object { '/r:' + $_.FullName }
    $sources = Get-ChildItem src/com.stormlab.supercell/Runtime/Lightning -Filter '*.cs' | ForEach-Object { $_.FullName }
    $runtime = Join-Path $output 'StormLab.Lightning.dll'
    & $dotnet $compiler /nologo /noconfig /nostdlib /target:library /langversion:9 "/out:$runtime" $refs $sources
    if ($LASTEXITCODE -ne 0) { throw 'Unity runtime compile failed.' }
    $sources = Get-ChildItem src/com.stormlab.supercell/Runtime/Lightning/Editor -Filter '*.cs' | ForEach-Object { $_.FullName }
    & $dotnet $compiler /nologo /noconfig /nostdlib /target:library /langversion:9 "/out:$output/StormLab.Lightning.Editor.dll" "/r:$runtime" $refs $sources
    if ($LASTEXITCODE -ne 0) { throw 'Unity editor compile failed.' }
    Write-Output 'PASS: runtime and editor compile against Unity 6000.6.6f1 references.'
}
finally { Pop-Location }
