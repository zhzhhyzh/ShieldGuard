$env:DOTNET_ROOT = "C:\ran-page\.dotnet"
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:PATH = "C:\ran-page\.dotnet;$env:PATH"
& "C:\ran-page\.dotnet\dotnet.exe" run --project "C:\ran-page\ScreenGuardAI\ScreenGuardAI\ScreenGuardAI.csproj"
