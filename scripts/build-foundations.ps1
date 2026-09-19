param(
    [string]$ManagedPath = 'C:\Program Files (x86)\Steam\steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL_Data\Managed',
    [string]$SourceRoot,
    [switch]$UseLocalSources,
    [switch]$DownloadHsm
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content (Join-Path $repoRoot 'dependencies.json') -Raw | ConvertFrom-Json
if (-not (Test-Path (Join-Path $ManagedPath 'LabApi.dll'))) { throw '找不到 LabApi.dll；请用 -ManagedPath 指定专用服务器的 SCPSL_Data\Managed 目录。' }
if (-not $SourceRoot) { $SourceRoot = Join-Path $repoRoot '.workspace' }
$SourceRoot = [IO.Path]::GetFullPath($SourceRoot)
$deps = Join-Path $repoRoot '.dependencies'
New-Item -ItemType Directory -Force -Path $SourceRoot, $deps | Out-Null
function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "命令执行失败：$Program，退出码 $LASTEXITCODE" }
}
foreach ($dependency in $manifest.repositories) {
    $destination = Join-Path $SourceRoot $dependency.directory
    if ($UseLocalSources) {
        if (-not (Test-Path (Join-Path $destination $dependency.project))) { throw "找不到本地项目：$destination" }
        Write-Warning "使用本地源码，未验证锁定提交：$destination"
    } else {
        if (-not (Test-Path $destination)) {
            Invoke-Checked 'git' @('clone', '--no-checkout', $dependency.url, $destination)
            Invoke-Checked 'git' @('-C', $destination, 'checkout', '--detach', $dependency.commit)
        }
        $actual = & git -C $destination rev-parse HEAD
        if ($LASTEXITCODE -ne 0 -or $actual -ne $dependency.commit) { throw "依赖提交不匹配：$destination。请另选空的 -SourceRoot；脚本不会覆盖已有工作。" }
        $dirty = & git -C $destination status --porcelain
        if ($LASTEXITCODE -ne 0 -or $dirty) { throw "依赖目录有未提交内容：$destination。请先处理，或显式使用 -UseLocalSources。" }
    }
    $buildArgs = @('build', (Join-Path $destination $dependency.project), '-c', 'Release', "-p:SCP_SL_MANAGED=$ManagedPath", '-p:DeployToLocalServer=false')
    if ($dependency.directory -eq 'CustomItems') { $buildArgs += "-p:ServerKeybindsPath=$(Join-Path $deps 'ServerKeybinds.dll')" }
    Invoke-Checked 'dotnet' $buildArgs
    Copy-Item -LiteralPath (Join-Path $destination "bin\Release\net48\$($dependency.assembly)") -Destination $deps -Force
}
Invoke-Checked 'dotnet' @('build', (Join-Path $repoRoot 'examples\Foundations\Foundations.csproj'), '-c', 'Release', "-p:SCP_SL_MANAGED=$ManagedPath", '-p:DeployToLocalServer=false')
if ($DownloadHsm) {
    foreach ($asset in $manifest.hsm.assets) {
        $target = Join-Path $deps $asset.file
        $temporary = "$target.download"
        try {
            Invoke-WebRequest -Uri $asset.url -OutFile $temporary -UseBasicParsing
            if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $asset.sha256) { throw "下载文件校验失败：$($asset.file)" }
            Move-Item -LiteralPath $temporary -Destination $target -Force
        } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
    }
    Write-Host '已下载并校验锁定版本的 HSM 与其 Harmony 依赖；文件仍在 .dependencies，尚未安装。'
}
Write-Host '构建完成。依赖位于 .dependencies，示例位于 examples\Foundations\bin\Release。尚未部署到任何服务器。'
