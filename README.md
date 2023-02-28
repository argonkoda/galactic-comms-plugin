# GalacticComms Plugin
This repository holds the GalacticComms TorchAPI Plugin for the [Space Engineers](https://www.spaceengineersgame.com/) Dedicated Server.

Its role is to hook into the game state and relay that information to the voice server.

This is one of three parts that make up the system and should be installed on the **server side**.

The other parts can be found here:
- [The Torch Plugin (This Repo)](https://github.com/argonkoda/galactic-comms-plugin)
- [The Voice Server](https://github.com/argonkoda/galactic-comms-server)
- [The Voice Client](https://github.com/argonkoda/galactic-comms-client)

## Installation
For most users you'll just want to install the program. To do so just follow the steps below:

> Note: This system is only available for Windows PC at the moment. This is mostly because Space Engineers is also Windows only. If someone wants to configure MacOS or Linux builds for the server or client, open an issue in the relevant repo. Unfortunately XBox support is unlikely due to the nature of modding and the permissions required for microphone access etc.

> Note: This plugin is designed for a TorchAPI dedicated server. Make sure that you have that installed and configured before installing this plugin.

1. To start you'll want to grab the latest release from the [Releases Page](https://github.com/argonkoda/galactic-comms-plugin/releases). To do that find the latest release and download the `GalacticComms Plugin.zip` file.
2. Move the zip file into the Plugins folder of your server.
3. You can then start the server as normal.

## Setup
This plugin has very little setup as much of the effects and other configuration is controlled by the other parts of this system.

If you want to configure the port the plugin uses to connect to the voice server then you can modify it in the `SignalServerConfig.cfg` file located in the same folder as `Torch.Server.exe`

## Contributing
Because this plugin's purpose is very niche and modifying the behaviour of it requires changes to the other parts of the system. open contribution to this project isn't enabled.

You can still however submit bug reports and feature requests under the issues tab. When doing so please make sure there aren't already issues making the same report or request.
