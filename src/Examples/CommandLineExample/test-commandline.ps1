# CommandLine Provider Test Script

Write-Host "=== Building CommandLineExample ===" -ForegroundColor Cyan
dotnet build

Write-Host "`n=== Test 1: Default (--) prefix ===" -ForegroundColor Yellow
dotnet run -- --host=localhost --port=8080 --verbose 

Write-Host "`n=== Test 2: Multiple prefixes (mixed) ===" -ForegroundColor Yellow
dotnet run -- --host=server1 -port=9000 /verbose

Write-Host "`n=== Test 3: Semantic prefixes ===" -ForegroundColor Yellow
dotnet run -- '@host=10.10.10.10' '#id=123' '%name=production'

Write-Host "`n=== Test 4: Prefix filtering ===" -ForegroundColor Yellow
dotnet run -- --app_host=appserver --app_port=3000 --db_connectionstring="Server=dbserver"

Write-Host "`n=== Test 5: Nested configuration (colon syntax) ===" -ForegroundColor Yellow
dotnet run -- --database:host=localhost --database:port=5432 --cache:host=redis --cache:ttl=300

Write-Host "`n=== Test 6: Nested configuration (double underscore syntax) ===" -ForegroundColor Yellow
dotnet run -- --database__host=localhost --database__port=5432 --cache__host=redis --cache__ttl=300

Write-Host "`n=== All tests complete! ===" -ForegroundColor Green
