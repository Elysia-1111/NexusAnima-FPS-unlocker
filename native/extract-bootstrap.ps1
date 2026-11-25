param([Parameter(Mandatory=$true)][string]$ObjectPath,[Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference='Stop'
$bytes=[IO.File]::ReadAllBytes($ObjectPath)
if($bytes.Length -lt 64 -or [BitConverter]::ToUInt32($bytes,0) -ne 0x464c457f -or $bytes[4] -ne 2 -or $bytes[5] -ne 1 -or [BitConverter]::ToUInt16($bytes,16) -ne 1 -or [BitConverter]::ToUInt16($bytes,18) -ne 62){throw 'Expected x64 ELF relocatable object from TinyCC.'}
$sectionOffset=[BitConverter]::ToUInt64($bytes,40)
$sectionSize=[BitConverter]::ToUInt16($bytes,58)
$sectionCount=[BitConverter]::ToUInt16($bytes,60)
$namesIndex=[BitConverter]::ToUInt16($bytes,62)
if($sectionSize -ne 64 -or $sectionOffset+$sectionCount*64 -gt $bytes.Length){throw 'Invalid object section table.'}
function Read-ObjectString([long]$offset){$end=$offset;while($end -lt $bytes.Length -and $bytes[$end] -ne 0){$end++};if($end -eq $bytes.Length){throw 'Unterminated object string.'};[Text.Encoding]::ASCII.GetString($bytes,$offset,$end-$offset)}
$namesHeader=$sectionOffset+$namesIndex*64
$namesOffset=[BitConverter]::ToUInt64($bytes,$namesHeader+24)
$sections=@()
for($index=0;$index -lt $sectionCount;$index++){
    $header=$sectionOffset+$index*64
    $section=[pscustomobject]@{Index=$index;Name=(Read-ObjectString ($namesOffset+[BitConverter]::ToUInt32($bytes,$header)));Type=[BitConverter]::ToUInt32($bytes,$header+4);Offset=[BitConverter]::ToUInt64($bytes,$header+24);Size=[BitConverter]::ToUInt64($bytes,$header+32);Link=[BitConverter]::ToUInt32($bytes,$header+40);Info=[BitConverter]::ToUInt32($bytes,$header+44)}
    if($section.Type -ne 8 -and $section.Offset+$section.Size -gt $bytes.Length){throw 'Object section outside file.'}
    $sections+=$section
}
$textSection=@($sections | Where-Object Name -eq '.text')
if($textSection.Count -ne 1 -or $textSection[0].Size -eq 0 -or $textSection[0].Size -gt 4096){throw 'Unexpected startup helper code size.'}
$textSection=$textSection[0]
if(@($sections | Where-Object {($_.Type -eq 4 -or $_.Type -eq 9) -and $_.Info -eq $textSection.Index -and $_.Size -gt 0}).Count -ne 0){throw 'Startup helper must not contain relocations.'}
$entryFound=$false
foreach($symbols in @($sections | Where-Object Type -eq 2)){
    $strings=$sections[$symbols.Link]
    for($offset=$symbols.Offset;$offset -lt $symbols.Offset+$symbols.Size;$offset+=24){
        $name=Read-ObjectString ($strings.Offset+[BitConverter]::ToUInt32($bytes,$offset))
        if($name -eq 'NexusBootstrap'){
            if([BitConverter]::ToUInt16($bytes,$offset+6) -ne $textSection.Index -or [BitConverter]::ToUInt64($bytes,$offset+8) -ne 0){throw 'Startup helper entry must start at offset zero.'}
            $entryFound=$true
        }
    }
}
if(-not $entryFound){throw 'Startup helper entry not found.'}
$code=[byte[]]::new($textSection.Size)
[Array]::Copy($bytes,$textSection.Offset,$code,0,$code.Length)
[IO.File]::WriteAllBytes($OutputPath,$code)
Write-Output ('Verified position-independent startup helper: '+$code.Length+' bytes, zero relocations.')
