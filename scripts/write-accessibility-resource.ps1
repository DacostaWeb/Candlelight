param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$strings = @{
    101 = 'Candlelight';
    102 = 'Independent screen color and dimming for visual comfort, including protected desktops.';
    103 = 'Candlelight protected desktop filter';
    104 = 'Applies your monitor color profiles on the Windows protected desktop.'
}
$data = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($data, [Text.Encoding]::Unicode)
for ($index = 96; $index -lt 112; $index++) {
    $value = if ($strings.ContainsKey($index)) { $strings[$index] } else { '' }
    $writer.Write([uint16]$value.Length)
    $writer.Write([Text.Encoding]::Unicode.GetBytes($value))
}
$payload = $data.ToArray()
$file = [IO.MemoryStream]::new()
$resource = [IO.BinaryWriter]::new($file)
function Write-Header([uint32]$Length, [uint16]$Type, [uint16]$Name) {
    $resource.Write($Length); $resource.Write([uint32]32)
    $resource.Write([uint16]65535); $resource.Write($Type)
    $resource.Write([uint16]65535); $resource.Write($Name)
    $resource.Write([uint32]0); $resource.Write([uint16]0); $resource.Write([uint16]0)
    $resource.Write([uint32]0); $resource.Write([uint32]0)
}
Write-Header 0 0 0
Write-Header $payload.Length 6 7
$resource.Write($payload)
while ($file.Length % 4) { $resource.Write([byte]0) }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))) | Out-Null
[IO.File]::WriteAllBytes($OutputPath, $file.ToArray())
$writer.Dispose(); $data.Dispose(); $resource.Dispose(); $file.Dispose()
