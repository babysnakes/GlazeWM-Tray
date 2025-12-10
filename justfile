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
	dotnet restore --locked-mode

[doc('Run CI checks and tests')]
ci: check restore
	dotnet build
	dotnet test