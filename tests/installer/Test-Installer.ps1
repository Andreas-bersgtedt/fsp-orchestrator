[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BaselineMsi,

    [Parameter(Mandatory)]
    [string]$UpgradeMsi
)

$ErrorActionPreference = 'Stop'
$BaselineMsi = [IO.Path]::GetFullPath($BaselineMsi)
$UpgradeMsi = [IO.Path]::GetFullPath($UpgradeMsi)
if (-not (Test-Path $BaselineMsi) -or -not (Test-Path $UpgradeMsi)) {
    throw 'Baseline and upgrade MSI paths must exist.'
}

$serviceName = 'FSP Orchestrator'
$installDirectory = Join-Path $env:ProgramFiles 'FSP Orchestrator'
$dataDirectory = Join-Path $env:ProgramData 'FspOrchestrator'
$sentinel = Join-Path $dataDirectory 'installer-test-sentinel.txt'
$logDirectory = Join-Path $env:RUNNER_TEMP 'fsp-orchestrator-installer'
$installedMsi = $null
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

function Invoke-Msi {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('install', 'repair', 'uninstall')]
        [string]$Operation,

        [Parameter(Mandatory)]
        [string]$Package,

        [string[]]$Properties = @()
    )

    $log = Join-Path $logDirectory "$Operation-$([guid]::NewGuid().ToString('N')).log"
    $arguments = switch ($Operation) {
        'install' { @('/i', "`"$Package`"", '/qn', '/norestart') }
        'repair' { @('/fa', "`"$Package`"", '/qn', '/norestart') }
        'uninstall' { @('/x', "`"$Package`"", '/qn', '/norestart') }
    }
    $arguments += $Properties
    $arguments += @('/l*vx', "`"$log`"")
    $process = Start-Process msiexec.exe `
        -ArgumentList $arguments `
        -Wait `
        -PassThru
    if ($process.ExitCode -notin 0, 3010) {
        Get-Content $log -Tail 150
        throw "msiexec $Operation failed with exit code $($process.ExitCode). Log: $log"
    }
}

function Assert-ServiceRunning {
    $service = Get-Service -Name $serviceName -ErrorAction Stop
    $service.WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    if ($service.Status -ne 'Running') {
        throw "$serviceName did not reach Running."
    }
}

function Assert-HttpsReady {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    $lastError = $null
    do {
        try {
            $response = Invoke-WebRequest `
                -Uri 'https://localhost:9443/api/health' `
                -UseBasicParsing `
                -NoProxy `
                -SkipCertificateCheck `
                -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            $lastError = $_
            Start-Sleep -Seconds 2
        }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    $serviceDetails = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
    $exceptionMessages = [Collections.Generic.List[string]]::new()
    $currentException = $lastError.Exception
    while ($null -ne $currentException) {
        $exceptionMessages.Add($currentException.Message)
        $currentException = $currentException.InnerException
    }

    $eventMessages = Get-WinEvent -FilterHashtable @{
        LogName = 'Application'
        StartTime = [DateTime]::Now.AddMinutes(-5)
    } -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProviderName -in '.NET Runtime', 'Application Error', 'FSP Orchestrator'
        } |
        Select-Object -First 10 |
        ForEach-Object { "$($_.ProviderName): $($_.Message)" }
    $diagnostic = "The installed HTTPS service did not become healthy. " +
        "Service=$($service.Status); ProcessId=$($serviceDetails.ProcessId); " +
        "Errors=$($exceptionMessages -join ' -> '); " +
        "Events=$($eventMessages -join ' | ')"
    throw $diagnostic
}

function Assert-CertificateTrusted {
    $certificate = Get-ChildItem Cert:\LocalMachine\My |
        Where-Object FriendlyName -EQ 'FSP Orchestrator localhost' |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
    if ($null -eq $certificate -or -not $certificate.HasPrivateKey) {
        throw 'The installer did not provision the localhost certificate.'
    }

    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $chain.ChainPolicy.RevocationMode =
            [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
        if (-not $chain.Build($certificate)) {
            $statuses = $chain.ChainStatus |
                ForEach-Object { "$($_.Status): $($_.StatusInformation.Trim())" }
            throw "The localhost certificate is not trusted: $($statuses -join '; ')"
        }
    }
    finally {
        $chain.Dispose()
    }
}

try {
    Invoke-Msi install $BaselineMsi
    $installedMsi = $BaselineMsi
    Assert-ServiceRunning
    Assert-CertificateTrusted
    Assert-HttpsReady

    Remove-Item (Join-Path $installDirectory 'appsettings.Production.json') -Force
    Invoke-Msi repair $BaselineMsi
    if (-not (Test-Path (Join-Path $installDirectory 'appsettings.Production.json'))) {
        throw 'Repair did not restore a missing installed file.'
    }
    Assert-ServiceRunning

    New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
    Set-Content -Path $sentinel -Value 'preserve-on-upgrade'
    Invoke-Msi install $UpgradeMsi
    $installedMsi = $UpgradeMsi
    Assert-ServiceRunning
    Assert-HttpsReady
    if (-not (Test-Path $sentinel)) {
        throw 'Major upgrade removed local metadata.'
    }

    Invoke-Msi uninstall $UpgradeMsi
    $installedMsi = $null
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        throw 'Uninstall did not remove the Windows Service.'
    }
    if (-not (Test-Path $sentinel)) {
        throw 'Default uninstall did not retain local metadata.'
    }
    if (Get-ChildItem Cert:\LocalMachine\My |
            Where-Object FriendlyName -EQ 'FSP Orchestrator localhost') {
        throw 'Uninstall did not remove the installer-managed certificate.'
    }

    Invoke-Msi install $UpgradeMsi
    $installedMsi = $UpgradeMsi
    Assert-ServiceRunning
    Invoke-Msi uninstall $UpgradeMsi -Properties @('REMOVELOCALDATA=1')
    $installedMsi = $null
    if (Test-Path $dataDirectory) {
        throw 'REMOVELOCALDATA=1 did not remove local metadata.'
    }
}
finally {
    if ($null -ne $installedMsi) {
        try {
            Invoke-Msi uninstall $installedMsi -Properties @('REMOVELOCALDATA=1')
        }
        catch {
            Write-Warning "Installer cleanup failed: $($_.Exception.Message)"
        }
    }
}
