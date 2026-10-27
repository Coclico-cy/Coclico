[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = "Release",
    [switch]$SkipPublish,
    [switch]$SkipTests,
    [switch]$IncludeCuda,
    [switch]$SkipClean,
    [switch]$SkipPortable,
    [string]$OutputDir = "artifacts\installer",

    [switch]$CI
)

$ErrorActionPreference = "Stop"

chcp 65001 > $null 2>&1
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$autoLang = if ($PSUICulture -like "fr*") { "FR" } else { "EN" }

if ($CI) {

    $Lang = $autoLang
    Write-Host "[CI] Mode non interactif : langue = $Lang"
}
else {
    Write-Host ""
    Write-Host "  ==============================" -ForegroundColor Cyan
    Write-Host "        C O C L I C O           " -ForegroundColor Cyan
    Write-Host "     Installer Builder          " -ForegroundColor Cyan
    Write-Host "  ==============================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  1. Francais" -ForegroundColor White
    Write-Host "  2. English" -ForegroundColor White
    Write-Host ""
    Write-Host "  [$autoLang]" -ForegroundColor DarkGray -NoNewline
    $langChoice = Read-Host " Choix / Choice (1-2, Entree = auto)"
    Write-Host ""

    switch ($langChoice) {
        "1" { $Lang = "FR" }
        "2" { $Lang = "EN" }
        default { $Lang = $autoLang }
    }
}

function T {
    param([string]$Fr, [string]$En)
    if ($Lang -eq "FR") { $Fr } else { $En }
}

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "==========================================" -ForegroundColor Cyan
}

function Write-Step {
    param([string]$Message)
    Write-Host "[>] $Message" -ForegroundColor Yellow
}

function Write-Success {
    param([string]$Message)
    Write-Host "[+] $Message" -ForegroundColor Green
}
function Read-SigningPassword {
    param([string]$Prompt, [int]$MinLength = 0, [int]$MaxLength = 0)
    while ($true) {
        $secure = Read-Host $Prompt -AsSecureString
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        $password = ""
        try {
            $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        }
        finally {
            [void][Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
        if ([string]::IsNullOrEmpty($password)) {
            return ""
        }
        if (($MinLength -gt 0) -and ($password.Length -lt $MinLength)) {
            Write-Host (T "  [!] Mot de passe trop court : $MinLength caracteres exactement, recommencez." "  [!] Password too short: exactly $MinLength characters, try again.") -ForegroundColor Yellow
            continue
        }
        if (($MaxLength -gt 0) -and ($password.Length -gt $MaxLength)) {
            Write-Host (T "  [!] Mot de passe trop long : $MaxLength caracteres exactement, recommencez." "  [!] Password too long: exactly $MaxLength characters, try again.") -ForegroundColor Yellow
            continue
        }
        return $password
    }
}

function Get-SigningPasswordVaultPath {
    param([string]$KeyfilePath)
    return Join-Path (Split-Path -Parent $KeyfilePath) "key.pwd.protected"
}

function Save-SigningPasswordVault {
    param([string]$KeyfilePath, [string]$Password)
    try {
        Add-Type -AssemblyName System.Security
        $plainBytes = [System.Text.Encoding]::UTF8.GetBytes($Password)
        $entropyBytes = [System.Text.Encoding]::UTF8.GetBytes("coclico-signing-vault")
        $protectedBytes = [System.Security.Cryptography.ProtectedData]::Protect($plainBytes, $entropyBytes, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
        [System.IO.File]::WriteAllBytes((Get-SigningPasswordVaultPath -KeyfilePath $KeyfilePath), $protectedBytes)
        return $true
    }
    catch {
        return $false
    }
}

function Load-SigningPasswordVault {
    param([string]$KeyfilePath)
    try {
        $vaultPath = Get-SigningPasswordVaultPath -KeyfilePath $KeyfilePath
        if (-not (Test-Path $vaultPath)) {
            return $null
        }
        Add-Type -AssemblyName System.Security
        $protectedBytes = [System.IO.File]::ReadAllBytes($vaultPath)
        $entropyBytes = [System.Text.Encoding]::UTF8.GetBytes("coclico-signing-vault")
        $plainBytes = [System.Security.Cryptography.ProtectedData]::Unprotect($protectedBytes, $entropyBytes, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
        return [System.Text.Encoding]::UTF8.GetString($plainBytes)
    }
    catch {
        return $null
    }
}

function Clear-SigningPasswordVault {
    param([string]$KeyfilePath)
    $vaultPath = Get-SigningPasswordVaultPath -KeyfilePath $KeyfilePath
    if (Test-Path $vaultPath) {
        Remove-Item -LiteralPath $vaultPath -Force -ErrorAction SilentlyContinue
    }
}

function Confirm-RememberSigningPassword {
    param([string]$KeyfilePath, [string]$Password)
    if ($CI -or [string]::IsNullOrEmpty($Password)) {
        return
    }
    if (Test-Path (Get-SigningPasswordVaultPath -KeyfilePath $KeyfilePath)) {
        return
    }
    $answer = Read-Host (T "Memoriser ce mot de passe sur cette machine pour ne plus le retaper ? (O/N)" "Remember this password on this machine to avoid retyping it? (Y/N)")
    if ($answer -match '^[OoYy]$') {
        if (Save-SigningPasswordVault -KeyfilePath $KeyfilePath -Password $Password) {
            Write-Success (T "Mot de passe memorise : chiffre par Windows pour votre profil, les prochains builds ne le demanderont plus." "Password remembered: encrypted by Windows for your profile, next builds will not ask for it anymore.")
        }
        else {
            Write-Host (T "  [-] Impossible de memoriser le mot de passe, il sera redemande au prochain build." "  [-] Could not remember the password, it will be asked again at next build.") -ForegroundColor DarkGray
        }
    }
    else {
        Write-Host (T "  [-] Mot de passe non memorise, il sera redemande au prochain build." "  [-] Password not remembered, it will be asked again at next build.") -ForegroundColor DarkGray
    }
}

function Invoke-SigningTool {
    param([string[]]$ToolArgs)
    $fullArgs = [System.Collections.Generic.List[string]]::new()
    [void]$fullArgs.AddRange([string[]]@(
        "run", "--project", "$rootDir\Coclico.Security\Coclico.Security.csproj", "-c", "Release", "--"
    ))
    [void]$fullArgs.AddRange($ToolArgs)
    & dotnet @fullArgs | Out-Host
    return $LASTEXITCODE
}

function Write-ProgressBar {
    param([int]$Current, [int]$Total, [string]$Text)
    $width = 30
    $pct = if ($Total -gt 0) { ($Current + 1) / $Total } else { 0 }
    $filled = [math]::Floor($pct * $width)
    $bar = ("$([char]9608)" * $filled) + ("$([char]9617)" * ($width - $filled))
    $pctTxt = [math]::Round($pct * 100)
    if ($Text.Length -gt 40) { $Text = $Text.Substring(0, 40) }
    Write-Host "`r  [$bar] $pctTxt%  ($($Current + 1)/$Total) $Text        " -NoNewline -ForegroundColor Yellow
}

function New-CoclicoArchive {
    param(
        [string]$ZipPath,
        [object[]]$Files,
        [string]$BaseDir,
        [bool]$ShowProgress = $true
    )
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }

    $sevenZip = Get-Command 7z -ErrorAction SilentlyContinue
    if ($sevenZip) {
        Write-Host (T "  [*] 7-Zip detecte : compression Deflate niveau 9" "  [*] 7-Zip detected: Deflate level 9 compression") -ForegroundColor DarkGray
        $stage = Join-Path ([System.IO.Path]::GetTempPath()) ("coclico_stage_" + [guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        try {
            $index = 0
            foreach ($f in $Files) {
                $rel = $f.FullName.Substring($BaseDir.Length) -replace '^[\\/]+', ''
                $dest = Join-Path $stage $rel
                $destDir = Split-Path $dest -Parent
                if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
                Copy-Item $f.FullName $dest -Force
                if ($ShowProgress) { Write-ProgressBar -Current $index -Total $Files.Count -Text $f.Name }
                $index++
            }
            if ($ShowProgress) { Write-Host "" }
            & 7z a -tzip -mx=9 -- $ZipPath "$stage\*" | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw (T "7-Zip a echoue (code $LASTEXITCODE)." "7-Zip failed (code $LASTEXITCODE).")
            }
        }
        finally {
            Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    else {
        Write-Host (T "  [-] 7-Zip absent : compression .NET standard (Deflate Optimal)" "  [-] 7-Zip not found: standard .NET compression (Deflate Optimal)") -ForegroundColor DarkGray
        $zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
        $compressionLevel = [System.IO.Compression.CompressionLevel]::Optimal
        try {
            $index = 0
            foreach ($f in $Files) {
                $rel = $f.FullName.Substring($BaseDir.Length) -replace '^[\\/]+', ''
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $rel, $compressionLevel) | Out-Null
                if ($ShowProgress) { Write-ProgressBar -Current $index -Total $Files.Count -Text $f.Name }
                $index++
            }
        }
        finally {
            $zip.Dispose()
        }
        if ($ShowProgress) { Write-Host "" }
    }
}

$rootDir = (Resolve-Path "$PSScriptRoot\..").Path
$installerProjDir = "$rootDir\Coclico.InstallerAot"

try {
    Push-Location $rootDir

    Write-Section (T "VERIFICATIONS PRELIMINAIRES" "PRELIMINARY CHECKS")

    Write-Step (T "Verification de dotnet..." "Checking dotnet...")
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw (T "dotnet est introuvable. Installez le SDK .NET et relancez." "dotnet not found. Install the .NET SDK and retry.")
    }
    Write-Success "dotnet $(dotnet --version)"

    if (-not (Test-Path "$rootDir\Coclico\Coclico.csproj")) {
        throw "$(T "Coclico.csproj introuvable dans : " "Coclico.csproj not found in: ")$rootDir\Coclico"
    }

    if (-not (Test-Path "$installerProjDir\Coclico.InstallerAot.csproj")) {
        throw "$(T "Coclico.InstallerAot.csproj introuvable dans : " "Coclico.InstallerAot.csproj not found in: ")$installerProjDir"
    }

    if ([string]::IsNullOrWhiteSpace($Version)) {
        Write-Step (T "Lecture de la version depuis version.txt..." "Reading version from version.txt...")
        $Version = (Get-Content "$rootDir\version.txt" -Raw).Trim()
        if ([string]::IsNullOrWhiteSpace($Version)) {
            throw (T "version.txt est absent ou vide a la racine du depot" "version.txt is missing or empty at repo root")
        }
    }
    Write-Success "$(T "Version cible : " "Target version: ")$Version"

    if (-not $SkipTests) {
        Write-Section (T "TESTS UNITAIRES COCLICO" "COClico UNIT TESTS")

        if (Test-Path "$rootDir\Coclico.Tests\Coclico.Tests.csproj") {
            Write-Step (T "Compilation et execution des tests unitaires..." "Building and running unit tests...")
            & dotnet test "$rootDir\Coclico.Tests\Coclico.Tests.csproj" --configuration $Configuration
            if ($LASTEXITCODE -ne 0) {
                throw (T "Les tests unitaires ont echoue. L'installeur n'est pas genere." "Unit tests failed. Installer is not generated.")
            }
            Write-Success (T "Tous les tests passent" "All tests passed")
        }
        else {
            Write-Host (T "  [-] Coclico.Tests absent, tests ignores." "  [-] Coclico.Tests not found, tests skipped.") -ForegroundColor DarkGray
        }
    }

    if (-not $SkipClean) {
        Write-Step (T "Nettoyage des anciens artefacts..." "Cleaning old artifacts...")
        $publishDir = "$rootDir\publish"
        if (Test-Path $publishDir) {
            Remove-Item "$publishDir\*" -Recurse -Force -ErrorAction SilentlyContinue
            Write-Success (T "Dossier publish nettoye" "publish folder cleaned")
        }
        $tempBuildDir = "$rootDir\artifacts\installer_build"
        if (Test-Path $tempBuildDir) {
            Remove-Item $tempBuildDir -Recurse -Force -ErrorAction SilentlyContinue
            Write-Success (T "Dossier installer_build nettoye" "installer_build cleaned")
        }
    }

    Write-Section (T "COMPILATION DE L'APPLICATION COCLICO" "BUILDING COCLICO APPLICATION")

    $publishDir = "$rootDir\publish"
    $mainExe = "$publishDir\Coclico.exe"

    if ($SkipPublish) {
        if (Test-Path $mainExe) {
            Write-Step (T "Utilisation du dossier publish existant" "Using existing publish folder")
        }
        else {
            throw (T "-SkipPublish utilise mais Coclico.exe absent du dossier publish." "-SkipPublish used but Coclico.exe missing from publish folder.")
        }
    }
    else {
        Write-Step "$(T "Publication de Coclico" "Publishing Coclico") ($Configuration, win-x64)..."
        $coclicoArgs = @(
            "publish", "$rootDir\Coclico\Coclico.csproj",
            "--configuration", $Configuration,
            "--runtime", "win-x64",
            "--self-contained", "false",
            "--output", $publishDir,
            "-p:PublishSingleFile=false",
            "-p:DebugSymbols=false",
            "-p:DebugType=None",
            "-p:Version=$Version",
            "-warnaserror"
        )
        if ($IncludeCuda) {
            $coclicoArgs += "-p:IncludeCuda=true"
        }
        & dotnet $coclicoArgs
        if ($LASTEXITCODE -ne 0) {
            throw (T "Echec de dotnet publish pour Coclico" "dotnet publish failed for Coclico")
        }
        Write-Success "$(T "Coclico publie dans : " "Coclico published to: ")$publishDir"
    }

    Write-Section (T "SIGNATURE DU MANIFESTE D'INTEGRITE" "INTEGRITY MANIFEST SIGNING")

    $cudaRuntimeFiles = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    [void]$cudaRuntimeFiles.UnionWith([string[]]@(
        "ggml-cuda.dll",
        "cudart64_12.dll",
        "cublas64_12.dll",
        "cublasLt64_12.dll",
        "cufft64_11.dll",
        "nvrtc64_120_0.dll",
        "nvrtc64_120_0.alt.dll",
        "nvjitlink64_12.dll"
    ))
    $cudaDirExclusion = "runtimes/win-x64/native/cuda12/"

    $signExcludes = [System.Collections.Generic.List[string]]::new()
    [void]$signExcludes.Add("*.pdb")
    if (-not $IncludeCuda) {
        [void]$signExcludes.Add($cudaDirExclusion)
        foreach ($dll in $cudaRuntimeFiles) {
            [void]$signExcludes.Add($dll)
        }
    }

    $signingKeyfile = Join-Path $PSScriptRoot "Key\key.keyfile"
    $signingPubfile = Join-Path $PSScriptRoot "Key\key.pub"
    $signingPasswordMin = 128
    $signingPasswordMax = 128
    $signingGeneration = 1
    if ($env:CCLICO_SIGNING_GENERATION) {
        $signingGeneration = [int]$env:CCLICO_SIGNING_GENERATION
    }
    $signingReady = $false

    if (Test-Path $signingKeyfile) {
        Write-Step (T "Verification de la cle de signature existante..." "Validating existing signing key...")
        Write-Host (T "  [+] Cle de signature existante trouvee : installer\Key\key.keyfile. Le script la reconnait, aucune nouvelle cle a generer." "  [+] Existing signing key found: installer\Key\key.keyfile. The script recognizes it, no new key to generate.") -ForegroundColor Green
        $signingVaultUsed = $false
        if (-not $env:CCLICO_SIGNING_PASSWORD -and -not $CI) {
            $signingVaultPassword = Load-SigningPasswordVault -KeyfilePath $signingKeyfile
            if ($signingVaultPassword) {
                $env:CCLICO_SIGNING_PASSWORD = $signingVaultPassword
                $signingVaultUsed = $true
                Write-Host (T "  [+] Mot de passe memorise sur cette machine : aucune saisie necessaire." "  [+] Password remembered on this machine: no input needed.") -ForegroundColor Green
            }
        }
        if (-not $env:CCLICO_SIGNING_PASSWORD -and -not $CI) {
            Write-Host (T "  [i] La cle est chiffree : tapez son mot de passe pour la deverrouiller. Il pourra etre memorise pour les prochains builds." "  [i] The key is encrypted: type its password to unlock it. It can be remembered for next builds.") -ForegroundColor DarkGray
            $env:CCLICO_SIGNING_PASSWORD = Read-SigningPassword (T "Mot de passe de la cle de signature ($signingPasswordMin caracteres exactement, vide = build sans signature)" "Signing key password (exactly $signingPasswordMin characters, empty = unsigned build)") -MinLength $signingPasswordMin -MaxLength $signingPasswordMax
        }
        if (-not $env:CCLICO_SIGNING_PASSWORD) {
            Write-Host (T "  [-] Mot de passe non fourni, build sans signature." "  [-] Password not provided, unsigned build.") -ForegroundColor DarkGray
        }
        else {
            $signingAttempts = 0
            $signingMaxAttempts = 3
            while (-not $signingReady) {
                $exit = Invoke-SigningTool -ToolArgs @("check", "--keyfile", $signingKeyfile, "--password-env", "CCLICO_SIGNING_PASSWORD")
                if ($exit -eq 0) {
                    $signingReady = $true
                    Write-Success (T "Cle de signature existante validee (keyId affiche ci-dessus)." "Existing signing key validated (keyId shown above).")
                    Confirm-RememberSigningPassword -KeyfilePath $signingKeyfile -Password $env:CCLICO_SIGNING_PASSWORD
                }
                else {
                    if ($signingVaultUsed) {
                        $signingVaultUsed = $false
                        Clear-SigningPasswordVault -KeyfilePath $signingKeyfile
                        Write-Host (T "  [-] Le mot de passe memorise ne correspond plus a cette cle, memorisation supprimee." "  [-] The remembered password no longer matches this key, memorization removed.") -ForegroundColor DarkGray
                    }
                    $signingAttempts++
                    Write-Host (T "  [!] Mot de passe incorrect ou cle de signature invalide (tentative $signingAttempts/$signingMaxAttempts)." "  [!] Wrong password or invalid signing key (attempt $signingAttempts/$signingMaxAttempts).") -ForegroundColor Yellow
                    if ($CI -or $signingAttempts -ge $signingMaxAttempts) {
                        Write-Host (T "  [!] Cle non validee, build sans signature." "  [!] Key not validated, unsigned build.") -ForegroundColor Yellow
                        break
                    }
                    $env:CCLICO_SIGNING_PASSWORD = Read-SigningPassword (T "Reessayer : mot de passe de la cle ($signingPasswordMin caracteres exactement, vide = build sans signature)" "Retry: signing key password (exactly $signingPasswordMin characters, empty = unsigned build)") -MinLength $signingPasswordMin -MaxLength $signingPasswordMax
                    if (-not $env:CCLICO_SIGNING_PASSWORD) {
                        Write-Host (T "  [-] Mot de passe vide, build sans signature." "  [-] Empty password, unsigned build.") -ForegroundColor DarkGray
                        break
                    }
                }
            }
        }
    }
    else {
        Write-Host (T "  [-] Aucune cle de signature trouvee (installer\Key\key.keyfile)." "  [-] No signing key found (installer\Key\key.keyfile).") -ForegroundColor DarkGray
        $generateKey = ""
        if ($CI) {
            Write-Host (T "  [-] Mode CI : build sans signature." "  [-] CI mode: unsigned build.") -ForegroundColor DarkGray
        }
        else {
            $generateKey = Read-Host (T "Generer une nouvelle cle de signature ? (O/N)" "Generate a new signing key? (Y/N)")
        }
        if ($generateKey -match '^[OoYy]$') {
            if ($env:CCLICO_SIGNING_PASSWORD -and (($env:CCLICO_SIGNING_PASSWORD.Length -lt $signingPasswordMin) -or ($env:CCLICO_SIGNING_PASSWORD.Length -gt $signingPasswordMax))) {
                Write-Host (T "  [!] Mot de passe fourni non conforme ($signingPasswordMin caracteres exactement), il sera redemande." "  [!] Provided password is not compliant (exactly $signingPasswordMin characters), it will be asked again.") -ForegroundColor Yellow
                $env:CCLICO_SIGNING_PASSWORD = ""
            }
            if (-not $env:CCLICO_SIGNING_PASSWORD) {
                $env:CCLICO_SIGNING_PASSWORD = Read-SigningPassword (T "Mot de passe de la nouvelle cle de signature ($signingPasswordMin caracteres exactement)" "Password for the new signing key (exactly $signingPasswordMin characters)") -MinLength $signingPasswordMin -MaxLength $signingPasswordMax
            }
            if (-not $env:CCLICO_SIGNING_PASSWORD) {
                Write-Host (T "  [-] Mot de passe vide, build sans signature." "  [-] Empty password, unsigned build.") -ForegroundColor DarkGray
            }
            else {
                $signingDir = Split-Path -Parent $signingKeyfile
                if (-not (Test-Path $signingDir)) {
                    [void](New-Item -ItemType Directory -Path $signingDir -Force)
                }
                $exit = Invoke-SigningTool -ToolArgs @("keygen", "--out", $signingKeyfile, "--pubout", $signingPubfile, "--password-env", "CCLICO_SIGNING_PASSWORD")
                if ($exit -ne 0) {
                    throw (T "Echec de la generation de la cle de signature." "Signing key generation failed.")
                }
                Write-Success (T "Cle de signature generee dans installer\Key\key.keyfile." "Signing key generated at installer\Key\key.keyfile.")
                Write-Host (T "  [!] Ancrez la cle publique (installer\Key\key.pub) dans Coclico\Security\trust-policy.json avant de publier une release." "  [!] Anchor the public key (installer\Key\key.pub) into Coclico\Security\trust-policy.json before publishing a release.") -ForegroundColor Yellow
                Write-Host (T "  [!] Le fichier key.keyfile est exclu de Git par .gitignore : ne le committez jamais." "  [!] key.keyfile is excluded from Git by .gitignore: never commit it.") -ForegroundColor Yellow
                Confirm-RememberSigningPassword -KeyfilePath $signingKeyfile -Password $env:CCLICO_SIGNING_PASSWORD
                $signingReady = $true
            }
        }
        else {
            Write-Host (T "  [-] Generation refusee, build sans signature." "  [-] Generation declined, unsigned build.") -ForegroundColor DarkGray
        }
    }

    if ($signingReady) {
        Write-Step (T "Signature du dossier publish..." "Signing publish folder...")
        $signArgs = [System.Collections.Generic.List[string]]::new()
        [void]$signArgs.AddRange([string[]]@(
            "run", "--project", "$rootDir\Coclico.Security\Coclico.Security.csproj", "-c", "Release", "--",
            "sign", "--dir", $publishDir, "--keyfile", $signingKeyfile, "--version", $Version,
            "--generation", "$signingGeneration", "--out", (Join-Path $publishDir "integrity.json"),
            "--password-env", "CCLICO_SIGNING_PASSWORD"
        ))
        foreach ($exclude in $signExcludes) {
            [void]$signArgs.AddRange([string[]]@("--exclude", $exclude))
        }
        & dotnet @signArgs
        if ($LASTEXITCODE -ne 0) {
            throw (T "Echec de la signature du manifeste d'integrite." "Integrity manifest signing failed.")
        }
        Write-Success (T "Manifeste integrity.json signe." "integrity.json manifest signed.")
    }
    else {
        Write-Host (T "  [-] Build sans signature : le payload ne sera pas verifie par le garde d'integrite." "  [-] Unsigned build: the payload will not be verified by the integrity guard.") -ForegroundColor DarkGray
    }

    Write-Section (T "CREATION DU PAYLOAD" "CREATING PAYLOAD")

    $payloadZip = "$installerProjDir\payload.zip"

    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue

    $allPublishFiles = @(Get-ChildItem -Path $publishDir -Recurse -File)

    $files = @($allPublishFiles | Where-Object {
        if ($_.Extension -eq ".pdb") { return $false }
        if ($IncludeCuda) { return $true }
        $rel = ($_.FullName.Substring($publishDir.Length) -replace '^[\\/]+', '') -replace '\\', '/'
        if ($rel.StartsWith("runtimes/win-x64/native/cuda12/", [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
        return -not $cudaRuntimeFiles.Contains($_.Name)
    })

    if ($files.Count -eq 0) {
        throw (T "Aucun fichier a compresser dans le dossier publish." "No files to compress in publish folder.")
    }

    $excludedCount = $allPublishFiles.Count - $files.Count
    if (-not $IncludeCuda -and $excludedCount -gt 0) {
        Write-Host "$(T "  [-] $excludedCount fichiers CUDA ecartes du payload (telecharges lors de l'installation)." "  [-] $excludedCount CUDA files excluded from payload (downloaded during installation).")" -ForegroundColor DarkGray
    }

    $totalFiles = $files.Count
    Write-Step "$(T "Compression de $totalFiles fichiers..." "Compressing $totalFiles files...")"
    New-CoclicoArchive -ZipPath $payloadZip -Files $files -BaseDir $publishDir

    $zipSizeMb = [math]::Round((Get-Item $payloadZip).Length / 1MB, 2)
    Write-Success "$(T "Archive payload.zip creee : $zipSizeMb Mo ($totalFiles fichiers)" "payload.zip created: $zipSizeMb MB ($totalFiles files)")"

    Write-Section (T "COMPILATION DE COCLICO.INSTALLER (AVALONIA NATIVEAOT)" "BUILDING COCLICO.INSTALLER (AVALONIA NATIVEAOT)")

    Write-Step (T "Compilation en binaire natif unique (Avalonia + NativeAOT)..." "Building single native binary (Avalonia + NativeAOT)...")

    $tempBuildDir = "$rootDir\artifacts\installer_build"
    if (Test-Path $tempBuildDir) { Remove-Item $tempBuildDir -Recurse -Force }
    New-Item -ItemType Directory -Path $tempBuildDir -Force | Out-Null

    $aotArgs = @(
        "publish", "$installerProjDir\Coclico.InstallerAot.csproj",
        "--configuration", $Configuration,
        "--runtime", "win-x64",
        "--output", $tempBuildDir,
        "-p:Version=$Version",
        "-warnaserror"
    )
    & dotnet @aotArgs
    if ($LASTEXITCODE -ne 0) {
        throw (T "Echec de compilation de Coclico.InstallerAot" "Coclico.InstallerAot build failed")
    }

    if (-not (Test-Path "$tempBuildDir\Coclico.Installer.exe")) {
        throw (T "Coclico.Installer.exe absent apres le publish AOT." "Coclico.Installer.exe missing after AOT publish.")
    }

    Write-Success (T "Coclico.InstallerAot compile : exe natif unique (payload + DLL natives embarques)" "Coclico.InstallerAot compiled: single native exe (payload + native DLLs embedded)")

    Write-Section (T "FINALISATION" "FINALIZATION")

    $finalOutputDir = "$rootDir\$OutputDir"
    if (-not (Test-Path $finalOutputDir)) {
        New-Item -ItemType Directory -Path $finalOutputDir -Force | Out-Null
    }

    $suffix = if ($IncludeCuda) { "-Full-CUDA" } else { "" }
    $finalExePath = "$finalOutputDir\Coclico-$Version$suffix.exe"

    Copy-Item "$tempBuildDir\Coclico.Installer.exe" $finalExePath -Force

    Remove-Item $tempBuildDir -Recurse -Force -ErrorAction SilentlyContinue

    $finalSizeMb = [math]::Round((Get-Item $finalExePath).Length / 1MB, 2)
    $hash = (Get-FileHash $finalExePath -Algorithm SHA256).Hash

    Write-Section (T "INSTALLEUR GENERE AVEC SUCCES" "INSTALLER GENERATED SUCCESSFULLY")

    Write-Host "$(T "Fichier   : " "File      : ")$finalExePath" -ForegroundColor White
    Write-Host "$(T "Taille    : " "Size      : ")$finalSizeMb Mo" -ForegroundColor White
    Write-Host "SHA-256   : $hash" -ForegroundColor White
    Write-Host "$(T "Version   : " "Version   : ")$Version" -ForegroundColor White

    Write-Host ""
    Write-Host (T "Fonctionnalites incluses :" "Included features:") -ForegroundColor Cyan
    Write-Host "  [+] Coclico application" -ForegroundColor Green
    Write-Host "  [+] Uninstall system" -ForegroundColor Green
    Write-Host "  [+] Windows registry entry" -ForegroundColor Green
    Write-Host "  [+] Uninstall from Windows Settings / Control Panel" -ForegroundColor Green
    Write-Host "  [+] Single native exe (Avalonia + NativeAOT, no .NET install needed)" -ForegroundColor Green
    Write-Host ""
    if (-not $IncludeCuda) {
        Write-Host (T "  Runtime CUDA et moteurs GPU : non embarques, telecharges lors de l'installation si carte NVIDIA." "  CUDA runtime and GPU engines: not bundled, downloaded during installation on NVIDIA cards.") -ForegroundColor DarkGray
        Write-Host ""
    }

    if (-not $SkipPortable) {
        Write-Section (T "VERSION PORTABLE" "PORTABLE VERSION")

        Write-Step (T "Publication autonome (fonctionne sans installer .NET)..." "Publishing self-contained (works without installing .NET)...")
        $portableDir = "$rootDir\publish-portable"
        if (Test-Path $portableDir) {
            Remove-Item $portableDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        $portableArgs = @(
            "publish", "$rootDir\Coclico\Coclico.csproj",
            "--configuration", $Configuration,
            "--runtime", "win-x64",
            "--self-contained", "true",
            "--output", $portableDir,
            "-p:PublishSingleFile=false",
            "-p:DebugSymbols=false",
            "-p:DebugType=None",
            "-p:Version=$Version",
            "-warnaserror"
        )
        if ($IncludeCuda) {
            $portableArgs += "-p:IncludeCuda=true"
        }
        & dotnet $portableArgs
        if ($LASTEXITCODE -ne 0) {
            throw (T "Echec de dotnet publish pour la version portable" "dotnet publish failed for portable version")
        }
        Write-Success "$(T "Coclico publie en autonome dans : " "Coclico published self-contained to: ")$portableDir"

        if ($signingReady) {
            Write-Step (T "Signature de la version portable..." "Signing portable version...")
            $portableSignArgs = [System.Collections.Generic.List[string]]::new()
            [void]$portableSignArgs.AddRange([string[]]@(
                "run", "--project", "$rootDir\Coclico.Security\Coclico.Security.csproj", "-c", "Release", "--",
                "sign", "--dir", $portableDir, "--keyfile", $signingKeyfile, "--version", $Version,
                "--generation", "$signingGeneration", "--out", (Join-Path $portableDir "integrity.json"),
                "--password-env", "CCLICO_SIGNING_PASSWORD"
            ))
            foreach ($exclude in $signExcludes) {
                [void]$portableSignArgs.AddRange([string[]]@("--exclude", $exclude))
            }
            & dotnet @portableSignArgs
            if ($LASTEXITCODE -ne 0) {
                throw (T "Echec de la signature du manifeste portable." "Portable manifest signing failed.")
            }
            Write-Success (T "Manifeste portable integrite signe." "Portable integrity manifest signed.")
        }

        Write-Step (T "Creation de l'archive portable..." "Creating portable archive...")
        $portableZip = "$finalOutputDir\Coclico-Portable-$Version$suffix.zip"

        $allPortableFiles = @(Get-ChildItem -Path $portableDir -Recurse -File)
        $portableFiles = @($allPortableFiles | Where-Object {
            if ($_.Extension -eq ".pdb") { return $false }
            if ($IncludeCuda) { return $true }
            $rel = ($_.FullName.Substring($portableDir.Length) -replace '^[\\\/]+', '') -replace '\\', '/'
            if ($rel.StartsWith("runtimes/win-x64/native/cuda12/", [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
            return -not $cudaRuntimeFiles.Contains($_.Name)
        })
        if ($portableFiles.Count -eq 0) {
            throw (T "Aucun fichier pour la version portable." "No files for portable version.")
        }

        $totalPortable = $portableFiles.Count
        Write-Step "$(T "Compression de $totalPortable fichiers..." "Compressing $totalPortable files...")"

        New-CoclicoArchive -ZipPath $portableZip -Files $portableFiles -BaseDir $portableDir

        Write-Host ""

        $portableSizeMb = [math]::Round((Get-Item $portableZip).Length / 1MB, 2)
        $portableHash = (Get-FileHash $portableZip -Algorithm SHA256).Hash

        Write-Section (T "VERSION PORTABLE GENEREE AVEC SUCCES" "PORTABLE VERSION GENERATED SUCCESSFULLY")
        Write-Host "$(T "Fichier   : " "File      : ")$portableZip" -ForegroundColor White
        Write-Host "$(T "Taille    : " "Size      : ")$portableSizeMb Mo" -ForegroundColor White
        Write-Host "SHA-256   : $portableHash" -ForegroundColor White
        Write-Host "$(T "Version   : " "Version   : ")$Version" -ForegroundColor White
        Write-Host ""
        Write-Host (T "Mode d'emploi portable : decompresse le zip dans un dossier et lance Coclico.exe directement." "Portable how-to: unzip into a folder and run Coclico.exe directly.") -ForegroundColor Cyan
    }
    else {
        Write-Host (T "  [-] Version portable ignoree (-SkipPortable)." "  [-] Portable version skipped (-SkipPortable).") -ForegroundColor DarkGray
    }

    Write-Success (T "Pret pour la distribution !" "Ready for distribution!")
}
catch {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Red
    Write-Host "  $(T "ECHEC DU SCRIPT" "SCRIPT FAILURE")" -ForegroundColor Red
    Write-Host "==========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "$(T "RAISON DE L'ECHEC :" "FAILURE REASON:")" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "------------------------------------------" -ForegroundColor DarkGray
    Write-Host "  Position : $($_.InvocationInfo.PositionMessage)" -ForegroundColor DarkGray
    Write-Host "  Type     : $($_.Exception.GetType().FullName)" -ForegroundColor DarkGray
    if ($_.Exception.InnerException) {
        Write-Host "  Cause    : $($_.Exception.InnerException.Message)" -ForegroundColor DarkGray
    }
    Write-Host "------------------------------------------" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "$(T "CONSEILS :" "HINTS:")" -ForegroundColor Cyan
    Write-Host "  - dotnet build Coclico.slnx" -ForegroundColor Cyan
    Write-Host "  - dotnet --list-sdks" -ForegroundColor Cyan
    Write-Host "  $(T "- Relancer sans -SkipPublish" "- Retry without -SkipPublish")" -ForegroundColor Cyan
    Write-Host ""

    $global:LASTEXITCODE = 1
    exit 1
}
finally {
    Pop-Location -ErrorAction SilentlyContinue
    if (-not $CI) {
        Read-Host (T "Appuyez sur Entree pour fermer..." "Press Enter to close...") | Out-Null
    }
}
