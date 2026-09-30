# WinDocker

A small native Windows 11 app (WinUI 3) that manages the local Docker engine.

- **Containers**: a `docker ps` style list with a "Show all" toggle (`docker ps -a`); start, stop and delete.
- **Images** and **volumes**: list and delete.
- **Logs**: read a container's logs, search them, and follow new lines live.

WinDocker finds the engine the way the `docker` CLI does: `DOCKER_HOST` first, then the current `docker context`,
then the default named pipe of Docker Desktop.

## Prerequisites

- Windows 11
- [Docker Desktop](https://www.docker.com/products/docker-desktop/), running, and your Windows account in the
  `docker-users` group (otherwise the engine is not reachable)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source. Visual Studio is optional.

## Build and run

On Windows:

```powershell
dotnet run --project src/WinDocker -p:Platform=x64
```

The app is unpackaged and self-contained: publishing gives a folder with `WinDocker.exe` that runs without installing
anything.

```powershell
dotnet publish src/WinDocker/WinDocker.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained -o artifacts/WinDocker-win-x64
```

Use `-r win-arm64 -p:Platform=ARM64` for ARM64.

## CI

The `Build` workflow runs the tests on Linux and Windows and publishes the app on Windows. Download the
`WinDocker-win-x64` artifact from a workflow run and start `WinDocker.exe` from the extracted folder.

## Tests

```powershell
dotnet test --project tests/WinDocker.Core.Tests
```

The tests run on any OS. The Docker integration tests use the local engine and skip themselves when there is none.
The ones that prune containers, images and volumes also need `WINDOCKER_DESTRUCTIVE_TESTS=1`. They only prune with a label
filter, so resources that the tests did not create are left alone, but the variable keeps them from running by accident.
The CI job on Linux sets it.

## Project structure

| Path | Contents |
|---|---|
| `src/WinDocker.Core` | Models, Docker service and view models. No UI dependencies. |
| `src/WinDocker` | The WinUI 3 app: pages, dialog and resource services, `Strings/en-US/Resources.resw`. |
| `tests/WinDocker.Core.Tests` | xUnit tests for the core library. |

All user-visible text lives in the `.resw` file. To add a language, add `src/WinDocker/Strings/<language>/Resources.resw`.

## License

[GPL-3.0](LICENSE)
