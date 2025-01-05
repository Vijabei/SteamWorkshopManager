# Workshop Manager

![License](https://img.shields.io/badge/License-CC%20BY--NC-lightgrey.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)

A Windows application designed to simplify the installation and management of Steam Workshop mods. It provides a user-friendly interface for downloading and installing Workshop content using SteamCMD.

![Screenshot of Workshop Manager](https://github.com/user-attachments/assets/9654702e-f161-4aa4-a8e3-42d6aeca719d)




## Features

- 🎮 Easy installation of Steam Workshop mods
- 📊 Real-time progress tracking
- 📝 Comprehensive logging system
- 🧹 Optional cleanup of workshop files
- ⚙️ Configurable SteamCMD integration
- ⏹️ Cancellable operations
- 📄 Automatic mod info generation

## Prerequisites

- Windows 64-bit operating system
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [SteamCMD](https://developer.valvesoftware.com/wiki/SteamCMD) installed
- Sufficient disk space for mod downloads

## Getting Started

1. Download the latest release from the [releases page](https://github.com/Vijabei/SteamWorkshopManager/releases)
2. Extract the files to your desired location
3. Run `WorkshopManager.exe`
4. Configure the SteamCMD path in the application

## Script Generation

The Workshop Manager requires a SteamCMD script to download mods. You have two options to generate these scripts:

### Option 1: Online Generator
Visit [softknight.de](https://softknight.de) and use the provided script generator.

### Option 2: Browser Extension
1. Download the Tampermonkey script from [softknight.de](https://softknight.de)
2. Install it in your browser
3. The script will integrate with Steam Workshop pages for direct script generation

## Building from Source

1. Clone the repository:
```bash
git clone https://github.com/Vijabei/SteamWorkshopManager.git
```

2. Open the solution in Visual Studio 2022 or later

3. Build the solution:
```bash
dotnet build
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the project
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## License

This project is licensed under the [Creative Commons Attribution-NonCommercial 4.0 International License](https://creativecommons.org/licenses/by-nc/4.0/).

This means you are free to:
- Share — copy and redistribute the material in any medium or format
- Adapt — remix, transform, and build upon the material

Under the following terms:
- Attribution — You must give appropriate credit, provide a link to the license, and indicate if changes were made
- NonCommercial — You may not use the material for commercial purposes

The licensor cannot revoke these freedoms as long as you follow the license terms.

## Acknowledgments

- [SteamCMD](https://developer.valvesoftware.com/wiki/SteamCMD) by Valve Corporation
- Script generator and tools hosted at [softknight.de](https://softknight.de)

## Support

If you encounter any issues, please create an issue in the [GitHub issue tracker](https://github.com/Vijabei/SteamWorkshopManager/issues).
