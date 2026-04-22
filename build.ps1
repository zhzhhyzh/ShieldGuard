$env:DOTNET_ROOT = "C:\ran-page\.dotnet"
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:DOTNET_HOST_PATH = "C:\ran-page\.dotnet\dotnet.exe"
$env:PATH = "C:\ran-page\.dotnet;$env:PATH"
$env:MSBuildSDKsPath = "C:\ran-page\.dotnet\sdk\8.0.420\Sdks"
& "C:\ran-page\.dotnet\dotnet.exe" build "C:\ran-page\ScreenGuardAI\ScreenGuardAI\ScreenGuardAI.csproj" -v diag 2>&1 | Select-Object -First 80
