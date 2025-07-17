# 🧹 Project Cleanup & Security Enhancement

## 🔒 Security Improvements
- **Removed sensitive data** from `appsettings.json` for public repository sharing
- **Created template configuration** with placeholder values instead of real credentials
- **Added `appsettings.template.json`** with detailed comments for easier setup

## 🗂️ Project Structure Cleanup
- **Removed build artifacts** (`obj/`, `bin/` directories)
- **Removed IDE-specific files** (`.vs/` directory)
- **Cleaned up temporary files** and compilation outputs
- **Maintained essential project files** only

## 📋 What's Changed
### Removed Files:
- All build artifacts and temporary compilation files
- Visual Studio specific configuration files
- Binary outputs and cache files

### Updated Files:
- `appsettings.json` - Now contains template values safe for public sharing
- Added `appsettings.template.json` - Detailed configuration template with comments

### Protected Data:
- ❌ Real MySQL connection strings
- ❌ Steam API keys
- ❌ Discord webhook URLs
- ❌ Server-specific configurations

## 🚀 Getting Started
1. Copy `appsettings.template.json` to `appsettings.json`
2. Fill in your actual configuration values
3. Build and run the application

## 📝 Configuration Required
Users need to provide their own:
- MySQL/SourceBans database connection
- Steam API key
- Discord webhook URLs (optional)
- Output file path
- GitHub repository information

---
**Note**: This release focuses on repository cleanup and security. No functional changes to the application logic.
