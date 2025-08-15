# Simple Steam Account Switcher

A lightweight, simple Steam account switcher that allows you to switch between Steam accounts without storing passwords. This application works by manipulating Steam's configuration files to change which account is set as the "most recent" login.

## Features

- **No Password Storage**: The application never stores or handles passwords
- **Simple Interface**: Clean, modern WPF interface
- **Account Discovery**: Automatically discovers existing Steam accounts
- **Persona State Control**: Choose how you appear when logging in (Online, Offline, Invisible, etc.)
- **Account Management**: Add, remove, and switch between accounts
- **Current Account Display**: Shows which account is currently active

## How It Works

This application works by modifying Steam's configuration files:

1. **`loginusers.vdf`**: Contains saved account information and marks which account is "most recent"
2. **`config.vdf`**: Contains Steam settings including persona state preferences

The app:
1. Closes Steam if it's running
2. Updates `loginusers.vdf` to set the target account as "MostRec" (most recent)
3. Updates `config.vdf` to set the desired persona state
4. Starts Steam, which will automatically log into the selected account

## Requirements

- Windows 10/11
- .NET 6.0 Runtime
- Steam installed on your system
- At least one Steam account that has been logged into previously

## Installation

1. Download the latest release from the releases page
2. Extract the ZIP file to a folder of your choice
3. Run `SimpleSteamSwitcher.exe`

## Usage

### First Run
1. Launch the application
2. Click "Discover Accounts" to find existing Steam accounts
3. The app will scan your Steam configuration and add any found accounts

### Switching Accounts
1. Select an account from the list
2. Choose your desired persona state (Online, Offline, Invisible, etc.)
3. Click "Switch" to switch to that account
4. Steam will close and reopen with the selected account

### Managing Accounts
- **Refresh**: Reload saved accounts from disk
- **Discover Accounts**: Scan for new Steam accounts
- **Remove**: Remove an account from the switcher (doesn't delete from Steam)

## File Locations

The application stores its data in:
- **Accounts Data**: `%APPDATA%\SimpleSteamSwitcher\accounts.json`
- **Steam Config**: `C:\Program Files (x86)\Steam\config\` (or your Steam installation path)

## Security & Privacy

- **No passwords stored**: The application never handles or stores passwords
- **Local only**: All operations are performed locally on your machine
- **File-based**: Works by manipulating existing Steam configuration files
- **Open source**: Full transparency of what the application does

## Troubleshooting

### Steam Not Found
- Ensure Steam is installed in the default location (`C:\Program Files (x86)\Steam`)
- If installed elsewhere, the app will try to detect it from the registry

### Account Switching Not Working
- Make sure Steam is completely closed before switching
- Verify that the account has been logged into at least once in Steam
- Check that you have write permissions to the Steam config folder

### No Accounts Discovered
- Ensure you have logged into Steam accounts at least once
- Try clicking "Discover Accounts" again
- Check that Steam's `loginusers.vdf` file exists and is readable

## Building from Source

### Prerequisites
- Visual Studio 2022 or .NET 6.0 SDK
- Windows development tools

### Build Steps
1. Clone the repository
2. Open `SimpleSteamSwitcher.csproj` in Visual Studio
3. Restore NuGet packages
4. Build the solution
5. Run the application

## Technical Details

### Architecture
- **WPF Application**: Modern Windows desktop application
- **MVVM Pattern**: Clean separation of concerns
- **Async/Await**: Non-blocking UI operations
- **JSON Storage**: Simple account data persistence

### Key Components
- **SteamService**: Core logic for Steam file manipulation
- **MainViewModel**: UI logic and data binding
- **SteamAccount**: Data model for account information

### Steam File Format
The application parses and modifies Steam's VDF (Valve Data Format) files:
- `loginusers.vdf`: Contains account credentials and settings
- `config.vdf`: Contains Steam client configuration

## License

This project is open source and available under the MIT License.




## Contributing

Contributions are welcome! Please feel free to submit issues, feature requests, or pull requests.

## Acknowledgments

This project was inspired by the more complex [TcNo Account Switcher](https://github.com/TCNOco/TcNo-Acc-Switcher) but simplified to focus only on Steam account switching. 
