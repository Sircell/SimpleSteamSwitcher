# SimpleSteamSwitcher Fresh Install Test Script
# This script tests the application as a new user would experience it

Write-Host "🧪 SimpleSteamSwitcher Fresh User Test" -ForegroundColor Green
Write-Host "=======================================" -ForegroundColor Green

# Step 1: Create test directory
$testDir = "C:\Users\$env:USERNAME\Desktop\SimpleSteamSwitcher-FreshTest"
Write-Host "📁 Creating test directory: $testDir" -ForegroundColor Yellow

if (Test-Path $testDir) {
    Write-Host "⚠️  Test directory already exists. Removing..." -ForegroundColor Yellow
    Remove-Item $testDir -Recurse -Force
}

New-Item -ItemType Directory -Path $testDir | Out-Null
Set-Location $testDir

# Step 2: Backup existing app data
$appDataPath = "$env:APPDATA\SimpleSteamSwitcher"
$backupPath = "$env:APPDATA\SimpleSteamSwitcher-BACKUP-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

if (Test-Path $appDataPath) {
    Write-Host "💾 Backing up existing app data to: $backupPath" -ForegroundColor Yellow
    Move-Item $appDataPath $backupPath
} else {
    Write-Host "ℹ️  No existing app data found (good for fresh test)" -ForegroundColor Cyan
}

# Step 3: Clone fresh from GitHub
Write-Host "📥 Cloning fresh code from GitHub..." -ForegroundColor Yellow
try {
    git clone https://github.com/Sircell/SimpleSteamSwitcher.git
    Set-Location SimpleSteamSwitcher
    Write-Host "✅ Clone successful!" -ForegroundColor Green
} catch {
    Write-Host "❌ Clone failed. Please check your internet connection." -ForegroundColor Red
    exit 1
}

# Step 4: Build the application
Write-Host "🔨 Building application..." -ForegroundColor Yellow
try {
    dotnet build
    Write-Host "✅ Build successful!" -ForegroundColor Green
} catch {
    Write-Host "❌ Build failed. Please check .NET installation." -ForegroundColor Red
    exit 1
}

# Step 5: Run the application
Write-Host "🚀 Starting application for fresh user test..." -ForegroundColor Yellow
Write-Host "" -ForegroundColor White
Write-Host "📋 FRESH USER TEST CHECKLIST:" -ForegroundColor Cyan
Write-Host "  1. App starts without crashes" -ForegroundColor White
Write-Host "  2. No sensitive data visible" -ForegroundColor White  
Write-Host "  3. Clear setup instructions" -ForegroundColor White
Write-Host "  4. API key setup works" -ForegroundColor White
Write-Host "  5. Account discovery works" -ForegroundColor White
Write-Host "  6. Games list functionality" -ForegroundColor White
Write-Host "" -ForegroundColor White
Write-Host "🔍 Watch for any errors or confusing UX..." -ForegroundColor Yellow
Write-Host "Press Enter to launch the app..." -ForegroundColor Green
Read-Host

try {
    dotnet run
} catch {
    Write-Host "❌ Application failed to start." -ForegroundColor Red
}

# Step 6: Cleanup instructions
Write-Host "" -ForegroundColor White
Write-Host "🧹 CLEANUP INSTRUCTIONS:" -ForegroundColor Cyan
Write-Host "  1. Close the application" -ForegroundColor White
Write-Host "  2. Run: Remove-Item '$testDir' -Recurse -Force" -ForegroundColor White
Write-Host "  3. Restore your data: Move-Item '$backupPath' '$appDataPath'" -ForegroundColor White
Write-Host "" -ForegroundColor White
Write-Host "✅ Fresh user test complete!" -ForegroundColor Green 