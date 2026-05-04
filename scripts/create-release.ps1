$RELEASE_VERSION = "0.1.0-alpha.1"

dotnet publish ../Actimetrics.UI -c Release -r win-x64 --self-contained true -o ../publish

vpk pack `
  -u ActiMetrics `
  -v $RELEASE_VERSION `
  --packDir ../publish/ `
  -o releases/ `
  --mainExe ActiMetrics.exe `
  --icon "$PSScriptRoot\assets\AppIcon.ico" `
  --splashImage "$PSScriptRoot\assets\AppIcon.ico" `
  --msiDeploymentTool

Remove-Item -Recurse -Force ../publish