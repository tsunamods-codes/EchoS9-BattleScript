if ($env:_BUILD_BRANCH -eq "refs/heads/master" -Or $env:_BUILD_BRANCH -eq "refs/tags/canary") {
  $env:_IS_BUILD_CANARY = "true"
  $env:_IS_GITHUB_RELEASE = "true"
}
elseif ($env:_BUILD_BRANCH -like "refs/tags/*") {
  $env:_BUILD_VERSION = $env:_BUILD_VERSION.Substring(0, $env:_BUILD_VERSION.LastIndexOf('.')) + ".0"
  $env:_IS_GITHUB_RELEASE = "true"
}
$env:_RELEASE_VERSION = "v${env:_BUILD_VERSION}"

Write-Output "--------------------------------------------------"
Write-Output "BUILD CONFIGURATION: $env:_RELEASE_CONFIGURATION"
Write-Output "RELEASE VERSION: $env:_RELEASE_VERSION"
Write-Output "--------------------------------------------------"

Write-Output "_BUILD_VERSION=${env:_BUILD_VERSION}" >> ${env:GITHUB_ENV}
Write-Output "_RELEASE_VERSION=${env:_RELEASE_VERSION}" >> ${env:GITHUB_ENV}
Write-Output "_IS_BUILD_CANARY=${env:_IS_BUILD_CANARY}" >> ${env:GITHUB_ENV}
Write-Output "_IS_GITHUB_RELEASE=${env:_IS_GITHUB_RELEASE}" >> ${env:GITHUB_ENV}

# Extract game dependencies
7z x .\misc\Dependencies.7z -p"$($env:ARCHIVE_PASSWORD)" -oReferences -y

# Prepare fake game installation path
$memoriaRoot = Join-Path $PWD "Memoria"
New-Item -ItemType Directory -Path (Join-Path $memoriaRoot "x86/FF9_Data/Managed") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $memoriaRoot "x64/FF9_Data/Managed") -Force | Out-Null
New-Item -ItemType File -Path (Join-Path $memoriaRoot "FF9_Launcher.exe") -Force | Out-Null

# Copy missing game files
Move-Item -Path References\*.dll -Destination (Join-Path $memoriaRoot "x64/FF9_Data/Managed") -Force | Out-Null

# Download and run Memoria on top of the game installation path
$memoriaDownloadUrl = "https://github.com/Albeoris/Memoria/releases/download/canary/Memoria.Patcher.exe"
$memoriaPatcherPath = Join-Path $memoriaRoot "Memoria.Patcher.exe"
Invoke-WebRequest -Uri $memoriaDownloadUrl -OutFile $memoriaPatcherPath
Write-Output "Running Memoria patcher in: $memoriaRoot"
& $memoriaPatcherPath $memoriaRoot

$steamRegPaths = @(
  "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 377840",
  "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 377840"
)
$gogRegPaths = @(
  "HKLM:\SOFTWARE\GOG.com\Games\1375008492",
  "HKLM:\SOFTWARE\WOW6432Node\GOG.com\Games\1375008492"
)

foreach ($regPath in $steamRegPaths) {
  New-Item -Path $regPath -Force | Out-Null
  New-ItemProperty -Path $regPath -Name "InstallLocation" -Value $memoriaRoot -PropertyType String -Force | Out-Null
}

foreach ($regPath in $gogRegPaths) {
  New-Item -Path $regPath -Force | Out-Null
  New-ItemProperty -Path $regPath -Name "path" -Value $memoriaRoot -PropertyType String -Force | Out-Null
}
