<#
Installation et mise à jour de Gestion MEP sans Docker, sur un serveur Windows (x64), dans une console PowerShell
lancée en administrateur.

  .\installer.ps1                     installe la version de l'archive où se trouve ce script
  .\installer.ps1 -Archive <fichier>  installe une archive .zip déjà téléchargée (serveur sans accès à Internet)
  .\installer.ps1 -Version 1.4.0      télécharge puis installe une version publiée (« derniere » : la plus récente) ;
                                      une version encore présente dans C:\Program Files\GestionMep\versions n'est pas
                                      retéléchargée

Une fois installé, le script est aussi dans C:\Program Files\GestionMep\installer.ps1 (mises à jour, retour à une version
précédente). Mise à jour : sauvegarde de la base et des pièces jointes, arrêt, bascule, démarrage, contrôle de santé ; si
la nouvelle version ne démarre pas, retour automatique à la précédente avec ses données. PostgreSQL n'est pas installé
par ce script.
#>
[CmdletBinding()]
param(
    [string]$Archive,
    [string]$Version
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Depot = if ($env:GESTION_MEP_DEPOT) { $env:GESTION_MEP_DEPOT } else { 'SolaDevSolide/gestion-mep' }
$Racine = Join-Path $env:ProgramFiles 'GestionMep'
$Donnees = Join-Path $env:ProgramData 'GestionMep'
$Conf = Join-Path $Donnees 'gestion-mep.conf'
$Service = 'GestionMep'
$Compte = 'NT AUTHORITY\LocalService'
$AttenteSante = if ($env:GESTION_MEP_ATTENTE_SANTE) { [int]$env:GESTION_MEP_ATTENTE_SANTE } else { 180 }
$VersionsGardees = 3

function Info([string]$m) { Write-Host "[gestion-mep] $m" }
function Erreur([string]$m) { Write-Host "[gestion-mep] Erreur : $m" -ForegroundColor Red; exit 1 }

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Erreur 'lancer PowerShell en tant qu''administrateur (création du service et des dossiers).'
}
if (-not [Environment]::Is64BitOperatingSystem) { Erreur 'seuls les serveurs Windows x64 sont pris en charge.' }

$Travail = Join-Path ([IO.Path]::GetTempPath()) ("gestion-mep-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $Travail | Out-Null
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    # 1. Archive de la version à installer
    $Ici = $PSScriptRoot
    $Source = $null
    if ($Version) { $Version = $Version.TrimStart('v') }
    if ($env:GESTION_MEP_SOURCE) { $Source = $env:GESTION_MEP_SOURCE }
    elseif ($Version -and (Test-Path (Join-Path $Racine "versions\$Version\GestionMep.Api.exe"))) {
        # Version déjà présente sur le serveur (retour à une version précédente) : rien à télécharger.
        Info "Version $Version déjà présente dans $Racine\versions."
        $Source = Join-Path $Racine "versions\$Version"
    }
    elseif ($Version) {
        if ($Version -eq 'derniere') {
            $Version = ((Invoke-RestMethod "https://api.github.com/repos/$Depot/releases/latest").tag_name).TrimStart('v')
        }
        if ($Version -notmatch '^\d+\.\d+\.\d+$') { Erreur "numéro de version attendu, par exemple 1.4.0 (reçu : $Version)." }
        $nom = "gestion-mep-$Version-win-x64.zip"
        $base = "https://github.com/$Depot/releases/download/v$Version"
        Info "Téléchargement de la version $Version."
        try {
            Invoke-WebRequest -UseBasicParsing "$base/$nom" -OutFile (Join-Path $Travail $nom)
            Invoke-WebRequest -UseBasicParsing "$base/SHA256SUMS" -OutFile (Join-Path $Travail 'SHA256SUMS')
        }
        catch { Erreur "téléchargement impossible de $base/$nom." }
        $attendue = (Get-Content (Join-Path $Travail 'SHA256SUMS') | Where-Object { $_ -match " \*?$([regex]::Escape($nom))$" } | Select-Object -First 1) -split '\s+' | Select-Object -First 1
        $reelle = (Get-FileHash -Algorithm SHA256 (Join-Path $Travail $nom)).Hash
        if (-not $attendue -or $attendue -ne $reelle) { Erreur 'empreinte SHA-256 incorrecte : archive corrompue ou modifiée.' }
        $Archive = Join-Path $Travail $nom
    }
    if ($Source) { }
    elseif ($Archive) {
        if (-not (Test-Path $Archive)) { Erreur "archive introuvable : $Archive" }
        $extrait = Join-Path $Travail 'archive'
        Expand-Archive -Path $Archive -DestinationPath $extrait
        $fichierVersion = Get-ChildItem -Path $extrait -Filter VERSION -File -Recurse -Depth 1 | Select-Object -First 1
        if (-not $fichierVersion) { Erreur "$Archive n'est pas une archive de Gestion MEP pour Windows." }
        $Source = $fichierVersion.DirectoryName
    }
    elseif ((Test-Path (Join-Path $Ici 'VERSION')) -and (Test-Path (Join-Path $Ici 'GestionMep.Api.exe'))) { $Source = $Ici }
    elseif (Test-Path (Join-Path $Racine 'actuelle\VERSION')) {
        # Sans option, depuis C:\Program Files\GestionMep : réinstalle et redémarre la version en place.
        $Source = (Get-Item (Join-Path $Racine 'actuelle')).Target | Select-Object -First 1
    }
    else { Erreur 'indiquer -Version <numéro> ou -Archive <fichier>, ou lancer le script depuis l''archive décompressée.' }
    if (-not (Test-Path (Join-Path $Source 'GestionMep.Api.exe'))) { Erreur "$Source ne contient pas l'application." }
    # Le script de la version à installer fait la suite : chaque version installe avec son propre script.
    $scriptVersion = Join-Path $Source 'installer.ps1'
    if (-not $env:GESTION_MEP_SOURCE -and (Test-Path $scriptVersion) -and
        (Get-FileHash $scriptVersion).Hash -ne (Get-FileHash $PSCommandPath).Hash) {
        $env:GESTION_MEP_SOURCE = $Source
        & powershell -NoProfile -ExecutionPolicy Bypass -File $scriptVersion
        $code = $LASTEXITCODE
        Remove-Item Env:\GESTION_MEP_SOURCE
        exit $code
    }
    $Nouvelle = (Get-Content (Join-Path $Source 'VERSION') -TotalCount 1).Trim()
    if ($Nouvelle -notmatch '^[0-9A-Za-z.+-]+$') { Erreur 'numéro de version illisible dans l''archive.' }

    # 2. Dossiers, droits, configuration
    New-Item -ItemType Directory -Force -Path (Join-Path $Racine 'versions') | Out-Null
    $dossierDonnees = Join-Path $Donnees 'donnees'
    foreach ($d in 'pieces-jointes', 'cles', 'sauvegardes', 'mise-a-jour') {
        New-Item -ItemType Directory -Force -Path (Join-Path $dossierDonnees $d) | Out-Null
    }
    # Données et configuration : administrateurs et système seulement, plus le compte du service (lecture de la
    # configuration, modification des données).
    & icacls $Donnees /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' '*S-1-5-19:(OI)(CI)RX' | Out-Null
    & icacls $dossierDonnees /grant:r '*S-1-5-19:(OI)(CI)M' | Out-Null
    $nouvelleConf = $false
    if (-not (Test-Path $Conf)) {
        $exemple = Get-Content (Join-Path $Source 'gestion-mep.conf.exemple')
        $exemple = $exemple -replace '^DOSSIER_DONNEES=.*$', "DOSSIER_DONNEES=$dossierDonnees"
        Set-Content -Path $Conf -Value $exemple -Encoding UTF8
        $nouvelleConf = $true
    }

    # 3. Fichiers de la version, service et commande gestion-mep
    $Cible = Join-Path $Racine "versions\$Nouvelle"
    if ((Resolve-Path $Source).Path.TrimEnd('\') -ne $Cible) {
        if (Test-Path "$Cible.tmp") { Remove-Item -Recurse -Force "$Cible.tmp" }
        Copy-Item -Recurse -Path $Source -Destination "$Cible.tmp"
        if (Test-Path $Cible) { Remove-Item -Recurse -Force $Cible }
        Rename-Item "$Cible.tmp" (Split-Path $Cible -Leaf)
    }
    Copy-Item -Force (Join-Path $Cible 'installer.ps1') (Join-Path $Racine 'installer.ps1')
    Set-Content -Path (Join-Path $Racine 'gestion-mep.cmd') -Encoding ASCII -Value @(
        '@echo off',
        'rem Commandes d''exploitation de Gestion MEP (gestion-mep aide), dans une console lancée en administrateur.',
        '"%ProgramFiles%\GestionMep\actuelle\GestionMep.Api.exe" %*'
    )
    $chemin = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    if (($chemin -split ';') -notcontains $Racine) {
        [Environment]::SetEnvironmentVariable('Path', "$chemin;$Racine", 'Machine')
        Info "Commande gestion-mep ajoutée au PATH (nouvelles consoles)."
    }
    $actuelle = Join-Path $Racine 'actuelle'
    function Basculer([string]$vers) {
        if (Test-Path $actuelle) { (Get-Item $actuelle).Delete() }
        New-Item -ItemType Junction -Path $actuelle -Target $vers | Out-Null
    }
    if (-not (Get-Service $Service -ErrorAction SilentlyContinue)) {
        if (-not (Test-Path $actuelle)) { Basculer $Cible }
        New-Service -Name $Service -DisplayName 'Gestion MEP' -StartupType Automatic `
            -BinaryPathName "`"$actuelle\GestionMep.Api.exe`"" -Description 'Gestion des mises en production' | Out-Null
        & sc.exe config $Service obj= $Compte password= '' | Out-Null
        & sc.exe failure $Service reset= 86400 actions= restart/10000/restart/10000/restart/60000 | Out-Null
        Info "Service $Service créé (compte $Compte)."
    }

    $config = Get-Content $Conf
    if ($nouvelleConf -or ($config | Where-Object { $_ -match '^BASE_DE_DONNEES=.*Password=;?\s*$' })) {
        if (-not (Test-Path $actuelle)) { Basculer $Cible }
        Info "Version $Nouvelle copiée. Avant le premier démarrage :"
        Info '  1. créer la base PostgreSQL (voir docs/Installer.md, Installation sans Docker) ;'
        Info "  2. renseigner BASE_DE_DONNEES (et les autres réglages) dans $Conf ;"
        Info "  3. relancer : & `"$Racine\installer.ps1`""
        exit 0
    }

    $exe = Join-Path $actuelle 'GestionMep.Api.exe'
    function Sante {
        $limite = (Get-Date).AddSeconds($AttenteSante)
        while ((Get-Date) -lt $limite) {
            $etat = (Get-Service $Service).Status
            if ($etat -eq 'Running') {
                & $exe --sante | Out-Null
                if ($LASTEXITCODE -eq 0) { return $true }
            }
            elseif ($etat -eq 'Stopped') { return $false }
            Start-Sleep -Seconds 3
        }
        return $false
    }

    # 4. Bascule vers la nouvelle version
    $Precedente = $null
    if (Test-Path $actuelle) { $Precedente = (Get-Item $actuelle).Target | Select-Object -First 1 }
    $Sauvegarde = $null
    if ($Precedente -and $Precedente.TrimEnd('\') -ne $Cible -and (Test-Path (Join-Path $Precedente 'GestionMep.Api.exe'))) {
        Info "Arrêt de la version $(Split-Path $Precedente -Leaf)."
        Stop-Service $Service -ErrorAction SilentlyContinue
        $Sauvegarde = Join-Path $dossierDonnees "sauvegardes\avant-$Nouvelle-$(Get-Date -Format yyyyMMdd-HHmmss).tar.gz"
        Info "Sauvegarde de la base et des pièces jointes : $Sauvegarde"
        & (Join-Path $Precedente 'GestionMep.Api.exe') sauvegarder $Sauvegarde
        if ($LASTEXITCODE -ne 0) {
            Start-Service $Service -ErrorAction SilentlyContinue
            Erreur 'sauvegarde impossible : rien n''a été changé, la version précédente est relancée.'
        }
    }
    elseif ($Precedente) { Info "Version $Nouvelle déjà en place : redémarrage." }

    Stop-Service $Service -ErrorAction SilentlyContinue
    Basculer $Cible
    Info "Démarrage de la version $Nouvelle (migration de la base si nécessaire)."
    Start-Service $Service -ErrorAction SilentlyContinue
    if (Sante) {
        Info "Version $Nouvelle installée et en marche."
        Get-ChildItem (Join-Path $Racine 'versions') -Directory | Sort-Object LastWriteTime -Descending |
            Where-Object { $_.FullName -ne $Cible -and $_.FullName -ne $Precedente } |
            Select-Object -Skip ($VersionsGardees - 2) | Remove-Item -Recurse -Force
        if ($config | Where-Object { $_ -match '^ADMIN_MOT_DE_PASSE=.+' }) {
            Info "Pensez à retirer ADMIN_MOT_DE_PASSE de $Conf (compte administrateur créé)."
        }
        Info 'Journal : observateur d''événements, journal Application ; commandes : gestion-mep aide'
        exit 0
    }

    if (-not $Sauvegarde) {
        Erreur "la version $Nouvelle ne démarre pas : voir l'observateur d'événements (journal Application) et $Conf."
    }
    Info "La version $Nouvelle ne démarre pas : retour à la version précédente et à ses données."
    Stop-Service $Service -ErrorAction SilentlyContinue
    Basculer $Precedente
    & $exe sauvegardes restaurer $Sauvegarde --oui
    Start-Service $Service -ErrorAction SilentlyContinue
    if (Sante) {
        Erreur "la version $Nouvelle n'a pas démarré : version précédente remise, avec les données d'avant la mise à jour. Voir l'observateur d'événements avant de réessayer."
    }
    Erreur "la version $Nouvelle n'a pas démarré et la version précédente non plus : voir docs/Installer-sans-Docker.md ; sauvegarde d'avant la mise à jour : $Sauvegarde"
}
finally {
    Remove-Item -Recurse -Force $Travail -ErrorAction SilentlyContinue
}
