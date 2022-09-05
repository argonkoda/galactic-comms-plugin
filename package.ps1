Remove-Item ".\GalacticComms Plugin.zip"
$compress = @{
  Path = ".\bin\Debug\GalacticComms.dll", ".\bin\Debug\GalacticComms.pdb", ".\bin\Debug\websocket-sharp.dll", ".\bin\Debug\websocket-sharp.pdb", ".\manifest.xml"
  CompressionLevel = "Fastest"
  DestinationPath = ".\GalacticComms Plugin.zip"
}
Compress-Archive @compress