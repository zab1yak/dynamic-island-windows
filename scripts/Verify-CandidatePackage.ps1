param(
    [Parameter(Mandatory)][string] $PublishDirectory,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string] $RuntimeIdentifier
)
$ErrorActionPreference = 'Stop'
$expectedMachine = if ($RuntimeIdentifier -eq 'win-x64') { 0x8664 } else { 0xAA64 }

foreach ($name in @('DynamicIsland.exe', 'onnxruntime.dll', 'Assets/Sounds/illuminate.mp3', 'Assets/Sounds/stargaze.mp3')) {
    $path = Join-Path $PublishDirectory $name
    if (!(Test-Path $path -PathType Leaf) -or (Get-Item $path).Length -eq 0) {
        throw "Missing or empty candidate asset: $name"
    }
}

# Validate the actual published native library, not merely NuGet's declared RIDs.
foreach ($name in @('DynamicIsland.exe', 'onnxruntime.dll')) {
    $stream = [IO.File]::OpenRead((Join-Path $PublishDirectory $name))
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Invalid PE header: $name" }
        $stream.Position = 0x3C
        $stream.Position = $reader.ReadInt32()
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid PE signature: $name" }
        $machine = $reader.ReadUInt16()
        if ($machine -ne $expectedMachine) { throw "Wrong native architecture in $name for $RuntimeIdentifier" }
    } finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}
Write-Output "Candidate assets and native architecture verified for $RuntimeIdentifier. Interactive WPF, audio and inference acceptance remain separate."
