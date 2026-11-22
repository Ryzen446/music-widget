#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$destination = Join-Path ([IO.Path]::GetTempPath()) ('MusicWidget-fonts-' + [guid]::NewGuid().ToString('N') + '.resources')
$writer = [Resources.ResourceWriter]::new($destination)
try {
    foreach ($name in 'Inter-Regular.ttf', 'Inter-SemiBold.ttf') {
        $source = [IO.File]::OpenRead((Join-Path $PSScriptRoot "fonts\$name"))
        $writer.AddResource(('fonts/' + $name.ToLowerInvariant()), $source, $true)
    }
} finally {
    $writer.Dispose()
}
$destination
