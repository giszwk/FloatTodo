# 在 Windows 10/11 + .NET 10 SDK 上运行，生成不依赖已安装 .NET Runtime 的程序。
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet restore .\FloatTodo.csproj
dotnet build .\FloatTodo.csproj -c Release --no-restore
dotnet publish .\FloatTodo.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
Write-Host "完成，程序位置：$PSScriptRoot\publish\FloatTodo.exe" -ForegroundColor Green
