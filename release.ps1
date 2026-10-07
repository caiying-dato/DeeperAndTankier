# ============================================================
# 愈深愈肉（Deeper and Tankier）一键发布脚本
#
# 用法：
#   .\release.ps1 -Version 0.1.0
#   .\release.ps1 -Version 0.2.0 -SkipSelfTest
#
# 做四件事：
#   1. 把 src\DeeperAndTankier.csproj 的 <Version>/<AssemblyVersion>/<FileVersion>、
#      src\DeeperAndTankier.json 的 version、README.md 的版本行一起改成 -Version
#   2. （可选）跑一遍自测，失败即中止发布
#   3. 编译并自动安装到游戏 mods 目录；随后核对 DLL 的 FileVersion == -Version
#   4. 打包 dist\愈深愈肉(DeeperAndTankier)-v<版本>-<日期>-<时间>.zip
#      并上传到局域网传输（outgoing 共享架）
#
# ★ 「发布」= 只上传到局域网共享架，不碰 Steam 创意工坊。
#   只有用户明确提到「创意工坊」时才走 Steam（见
#   steam创意工坊（workshop）\上传交接文档.md 第 0 节）。
#
# ★ 包名规范（2026-10-03 起）：中文名(英文名)-v<版本>-<yyyyMMdd-HHmm>
#   例：全自动爬塔(AutoSTS2)-v0.1.1-20260918-2111.zip
# ============================================================
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    # 局域网传输页面地址（含 ?t=… 令牌）。令牌变化时整条 URL 换掉即可。
    [string]$TransferUrl = "http://127.0.0.1:3080/dsh-lan-xfer/ui?t=1j8WxbzbbYVLzFYYRMzjm3160r08ShFAPS7t7yCKxXE",

    [switch]$SkipSelfTest
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$modId = "DeeperAndTankier"

# 包名规范（2026-10-03 起）：中文名(英文名)-v<版本>-<yyyyMMdd-HHmm>
$cnName = "愈深愈肉"
$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$shareDir = Join-Path $env:USERPROFILE '.dsh\lan-transfer\outgoing'

# ---------- 0. 解析传输接口 ----------
$uri = [Uri]$TransferUrl
$token = $null
if ($TransferUrl -match "[?&]t=([^&]+)") { $token = [Uri]::UnescapeDataString($Matches[1]) }
if (-not $token) { throw "传输地址里找不到令牌 ?t=…：$TransferUrl" }
$api = "$($uri.Scheme)://$($uri.Authority)/dsh-lan-xfer/api"
Write-Host "[1/6] 传输接口：$api（令牌 ****$($token.Substring([Math]::Max(0, $token.Length - 4)))）"

# ---------- 1. 更新版本号 ----------
$manifestPath = Join-Path $root "src\$modId.json"
$readmePath = Join-Path $root "README.md"

$manifest = Get-Content $manifestPath -Raw -Encoding UTF8
$manifest = $manifest -replace '"version"\s*:\s*"[^"]*"', ('"version": "' + $Version + '"')
[System.IO.File]::WriteAllText($manifestPath, $manifest, (New-Object System.Text.UTF8Encoding($false)))

$readme = Get-Content $readmePath -Raw -Encoding UTF8
$readme = $readme -replace "版本：[0-9][0-9a-zA-Z\.\-]*", ("版本：" + $Version)
[System.IO.File]::WriteAllText($readmePath, $readme, (New-Object System.Text.UTF8Encoding($false)))

# csproj 的三个版本号（DLL 的 FileVersion / ProductVersion 由它们决定）
$csprojPath = Join-Path $root "src\$modId.csproj"
$csproj = Get-Content $csprojPath -Raw -Encoding UTF8
if ($csproj -notmatch '<FileVersion>') { throw "$csprojPath 里没有 <Version>/<AssemblyVersion>/<FileVersion>，先补上再跑本脚本" }
$csproj = $csproj -replace '<Version>[^<]*</Version>',                       ("<Version>" + $Version + "</Version>")
$csproj = $csproj -replace '<AssemblyVersion>[^<]*</AssemblyVersion>',       ("<AssemblyVersion>" + $Version + ".0</AssemblyVersion>")
$csproj = $csproj -replace '<FileVersion>[^<]*</FileVersion>',               ("<FileVersion>" + $Version + ".0</FileVersion>")
[System.IO.File]::WriteAllText($csprojPath, $csproj, (New-Object System.Text.UTF8Encoding($true)))
Write-Host "[2/6] 版本号已更新为 $Version（csproj + json + README）"

# ---------- 2. 自测 ----------
if (-not $SkipSelfTest) {
    Write-Host "[3/6] 运行自测…"
    dotnet build (Join-Path $root "selftest\SelfTest.csproj") -c Release | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "自测构建失败，中止发布" }
    & (Join-Path $root "selftest\bin\Release\DeeperAndTankierSelfTest.exe")
    if ($LASTEXITCODE -ne 0) { throw "自测未通过，中止发布" }
} else {
    Write-Host "[3/6] 跳过自测（-SkipSelfTest）"
}

# ---------- 3. 编译模组（构建后自动安装到游戏 mods 目录） ----------
Write-Host "[4/6] 编译模组…"
dotnet build (Join-Path $root "src\$modId.csproj") -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "模组编译失败，中止发布" }

# 核对 DLL 的版本元数据（2026-10-04 起必查：DLL 的 FileVersion 必须等于 -Version）
$builtDll = Join-Path $root "src\bin\Release\$modId.dll"
if (-not (Test-Path $builtDll)) { throw "找不到编译产物 $builtDll" }
$fileVersion = (Get-Item $builtDll).VersionInfo.FileVersion
if ($fileVersion -notlike "$Version*") {
    throw "编译出的 DLL FileVersion 是 $fileVersion，与 $Version 不一致（检查 csproj 的 <Version>/<FileVersion> 与 GenerateAssemblyInfo）"
}
Write-Host "[通过] DLL FileVersion = $fileVersion"

# ---------- 4. 打包 ----------
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$stageMod = Join-Path $stage $modId
New-Item -ItemType Directory -Path $stageMod -Force | Out-Null

$binDir = Join-Path $root "src\bin\Release"
Copy-Item (Join-Path $binDir "$modId.dll") $stageMod
Copy-Item (Join-Path $binDir "$modId.pdb") $stageMod -ErrorAction SilentlyContinue
Copy-Item (Join-Path $binDir "$modId.json") $stageMod
Copy-Item $readmePath $stageMod

$zipPath = Join-Path $dist "$cnName($modId)-v$Version-$stamp.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $stage $modId) -DestinationPath $zipPath
$zipSize = (Get-Item $zipPath).Length
Write-Host ("[5/6] 已打包 {0}（{1:N0} 字节）" -f $zipPath, $zipSize)

# ---------- 5. 上传到局域网传输（outgoing 共享架） ----------
$name = Split-Path $zipPath -Leaf
$uploadUrl = "$api/upload?scope=outgoing&name=$([Uri]::EscapeDataString($name))&t=$([Uri]::EscapeDataString($token))"
try {
    Invoke-WebRequest -Uri $uploadUrl -Method Post -ContentType "application/octet-stream" -InFile $zipPath -UseBasicParsing -TimeoutSec 300 | Out-Null
    Write-Host "[6/6] 已上传到共享架（outgoing）：$name"
}
catch {
    # 部分实现不允许直接写 outgoing：先落收件箱，再移入共享架
    Write-Host "      直写 outgoing 失败（$($_.Exception.Message)），改为经收件箱中转…"
    $uploadUrl = "$api/upload?scope=incoming&name=$([Uri]::EscapeDataString($name))&t=$([Uri]::EscapeDataString($token))"
    Invoke-WebRequest -Uri $uploadUrl -Method Post -ContentType "application/octet-stream" -InFile $zipPath -UseBasicParsing -TimeoutSec 300 | Out-Null
    $moveBody = @{ scope = "incoming"; rel = $name; to = "outgoing" } | ConvertTo-Json
    Invoke-WebRequest -Uri "$api/move?t=$([Uri]::EscapeDataString($token))" -Method Post -ContentType "application/json" -Body $moveBody -UseBasicParsing -TimeoutSec 60 | Out-Null
    Write-Host "[6/6] 已上传并移入共享架（outgoing）：$name"
}

# ---------- 6. 核对 ----------
# 必须取 .Content 再解析：把响应对象直接管道给 ConvertFrom-Json 会把中文毁成 "????"，
# 导致包名比较永远失败（2026-10-04 踩坑）。
$list = (Invoke-WebRequest -Uri "$api/list?t=$([Uri]::EscapeDataString($token))" -UseBasicParsing -TimeoutSec 30).Content | ConvertFrom-Json
$found = @($list.outgoing) | Where-Object { $_.name -eq $name }
if (-not $found) { throw "上传后在共享架列表里找不到 $name，请检查" }

# 新包确认到位之后，再清掉共享架上该模组的旧包（免得多设备抓错版本）
if (Test-Path $shareDir) {
    Get-ChildItem $shareDir -File | Where-Object {
        ($_.Name -like "*$modId*" -or $_.Name -like "*$cnName*") -and $_.Name -ne $name
    } | ForEach-Object {
        Remove-Item $_.FullName -Force
        Write-Host "      删掉共享架上的旧包：$($_.Name)"
    }
}

# 再核一次哈希：共享架上的字节必须和本地包逐字相同（别让别的设备抓到坏包）
$shared = Join-Path $shareDir $name
if (Test-Path $shared) {
    $hashShared = (Get-FileHash $shared -Algorithm SHA256).Hash
    $hashLocal = (Get-FileHash $zipPath -Algorithm SHA256).Hash
    if ($hashShared -cne $hashLocal) {
        throw "共享架上的 $name 与本地包哈希不一致（$hashShared / $hashLocal）"
    }
    Write-Host "[通过] 共享架哈希与本地包一致：$hashShared"
} else {
    Write-Host "提示：$shared 不在本机共享目录里（可能经收件箱中转），跳过本地哈希复核" -ForegroundColor Yellow
}
Write-Host ""
Write-Host ("发布完成：{0}（{1:N0} 字节）已位于共享架" -f $name, $zipSize)
Write-Host "下载页：$TransferUrl"
