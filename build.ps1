$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$iscc = 'C:\Program Files\Inno Setup 7\ISCC.exe'
if (-not (Test-Path $iscc)) { throw "Inno Setup 7 não encontrado em $iscc" }

Write-Host '==> Gerando ícone...' -ForegroundColor Cyan
& (Join-Path $root 'installer\make-icon.ps1')

Write-Host '==> Publicando (Release, win-x64, autocontido)...' -ForegroundColor Cyan
if (Test-Path publish) { Remove-Item -Recurse -Force publish }
dotnet publish src/SpardaControl/SpardaControl.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -o publish/SpardaControl -nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou' }

Write-Host '==> Gerando imagens do instalador...' -ForegroundColor Cyan
& (Join-Path $root 'installer\make-wizard-images.ps1')

Write-Host '==> Compilando instalador (Inno Setup)...' -ForegroundColor Cyan
& $iscc /Q (Join-Path $root 'installer\SpardaControl.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC falhou' }

Get-ChildItem (Join-Path $root 'dist') -Filter *.exe | ForEach-Object {
    Write-Host ("OK: {0} ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) -ForegroundColor Green
}
