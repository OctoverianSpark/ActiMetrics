# generate-components.ps1
$publishDir = Resolve-Path "./publish"
$output = "./Components.wxs"

# Excluir el Host.exe porque se maneja en ActiMetrics.wxs
$excludedFiles = @("ActiMetrics.Host.exe")

$files = Get-ChildItem -Path $publishDir -File -Recurse | 
Where-Object { $excludedFiles -notcontains $_.Name }

$xml = '<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <ComponentGroup Id="ProductComponents" Directory="INSTALLFOLDER">'

$counter = 0

foreach ($file in $files) {
  $relativePath = $file.FullName.Substring($publishDir.Path.Length + 1)
  $componentId = "Comp_$counter"
  $fileId = "File_$counter"
  $counter++

  $subDir = Split-Path $relativePath -Parent
    
  if ($subDir) {
    $xml += "
      <Component Id=`"$componentId`" Subdirectory=`"$subDir`">
        <File Id=`"$fileId`" Source=`"./publish/$relativePath`" />
      </Component>"
  }
  else {
    $xml += "
      <Component Id=`"$componentId`">
        <File Id=`"$fileId`" Source=`"./publish/$relativePath`" />
      </Component>"
  }
}

$xml += '
    </ComponentGroup>
  </Fragment>
</Wix>'

$xml | Out-File -FilePath $output -Encoding UTF8
Write-Host "Generado: $output con $counter archivos"