param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo 'validation/rainfall/out' }
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$stage = Join-Path $OutputDirectory ('staging-' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $stage 'package'
$runtime = Join-Path $package 'Runtime/Rainfall'
New-Item -ItemType Directory -Force $runtime,(Join-Path $package 'Documentation~') | Out-Null
$source = Join-Path $repo 'src/com.stormlab.supercell/Runtime/Rainfall'
Get-ChildItem $source | Where-Object { $_.Name -notin @('Integration','Integration.meta','README.md','README.md.meta') } | Copy-Item -Destination $runtime -Recurse
Copy-Item ($source + '.meta') (Join-Path $package 'Runtime/Rainfall.meta')
Copy-Item (Join-Path $source 'README.md') (Join-Path $package 'README.md')
Copy-Item (Join-Path $source 'README.md.meta') (Join-Path $package 'README.md.meta')
Copy-Item (Join-Path $repo 'docs/meteorology/rainfall.md') (Join-Path $package 'Documentation~/rainfall.md')
$manifest = @{
    name='com.stormlab.rainfall'; version='0.1.0'; displayName='StormLab Rainfall'; unity='6000.0';
    description='Diameter-resolved near-camera liquid rain, ground-relative wind, colliders and diagnostic point gauges.';
    license='MIT'; author=@{name='Jonny Glessner'};
    dependencies=@{'com.unity.modules.physics'='1.0.0'}
} | ConvertTo-Json -Depth 4
Set-Content (Join-Path $package 'package.json') $manifest -Encoding utf8
@'
MIT License

Copyright (c) 2026 Jonny Glessner

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and
to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions
of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO
THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
'@ | Set-Content (Join-Path $package 'LICENSE.md') -Encoding utf8
$rootMetadata = @{
    'Runtime.meta' = "guid: 7ebfca1ec7cc4d7b89af3e12d22072b6`nfolderAsset: yes`nDefaultImporter:";
    'LICENSE.md.meta' = "guid: 3519d7e76bac42d38c8bac45697cd362`nTextScriptImporter:";
    'package.json.meta' = "guid: 568f5c6d3b7941c3bb528fbf93a4ab68`nPackageManifestImporter:"
}
foreach ($entry in $rootMetadata.GetEnumerator()) {
    Set-Content (Join-Path $package $entry.Key) ("fileFormatVersion: 2`n" + $entry.Value + "`n  externalObjects: {}`n  userData: `n  assetBundleName: `n  assetBundleVariant: ") -Encoding utf8
}
$archive = Join-Path $OutputDirectory 'com.stormlab.rainfall-0.1.0.tgz'
& tar -czf $archive -C $stage package
if ($LASTEXITCODE -ne 0) { throw 'Rainfall tarball creation failed.' }
Write-Output $archive
# Staging is retained for inspection; each run uses a new folder and never deletes existing work.
