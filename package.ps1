$compress = @{
  Path = ".\bin\Debug\GalacticComms.dll", ".\bin\Debug\GalacticComms.pdb", ".\manifest.xml"
  CompressionLevel = "Fastest"
  DestinationPath = ".\GalacticComms Plugin.zip"
}
Compress-Archive @compress