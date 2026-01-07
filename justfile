[working-directory: 'src/cli/']
[doc('Run console against running GlazeWM')]
cli:
	dotnet run

[working-directory: 'src/cli/']
[doc('Run console against mock websocket (port 8181)')]
cli-mock:
	dotnet run -- 8181

format:
	dotnet fantomas .

check-format:
	dotnet fantomas --check .

lint:
	dotnet fsharplint lint GlazeWM-Tray.sln

check: check-format lint

[doc('Restore from scratch honering the lock file')]
restore:
	dotnet tool restore
	dotnet restore

[doc('Run CI checks and tests')]
ci: restore check-format
	dotnet build
	dotnet test

[working-directory: 'resources']
[doc('Generate icons from PNG images (exported from Affinity)')]
icons:
	@powershell -c '.\gen-icons.ps1'
	mv generated/*.ico ../src/TrayApp/Assets/