# ChessServer

[![MIT licensed](https://img.shields.io/badge/license-MIT-blue.svg)](https://gitlab.aiursoft.cn/aiursoft/ChessServer/-/blob/master/LICENSE)
[![Pipeline stat](https://gitlab.aiursoft.cn/aiursoft/ChessServer/badges/master/pipeline.svg)](https://gitlab.aiursoft.cn/aiursoft/ChessServer/-/pipelines)
[![Test Coverage](https://gitlab.aiursoft.cn/aiursoft/ChessServer/badges/master/coverage.svg)](https://gitlab.aiursoft.cn/aiursoft/ChessServer/-/pipelines)
[![ManHours](https://manhours.aiursoft.cn/r/gitlab.aiursoft.cn/aiursoft/ChessServer.svg)](https://gitlab.aiursoft.cn/aiursoft/ChessServer/-/commits/master?ref_type=heads)

ChessServer is just a simple chess server for [Aiursoft](https://www.aiursoft.com) to test our new features.


## Run in Ubuntu

The following script will install\update this app on your Ubuntu server. Supports Ubuntu 22.04.

On your Ubuntu server, run the following command:

```bash
curl -sL https://gitlab.aiursoft.cn/aiursoft/chessserver/-/raw/master/install.sh | sudo bash
```

Of course it is suggested that append a custom port number to the command:

```bash
curl -sL https://gitlab.aiursoft.cn/aiursoft/chessserver/-/raw/master/install.sh | sudo bash -s 8080
```

It will install the app as a systemd service, and start it automatically. Binary files will be located at `/opt/apps`. Service files will be located at `/etc/systemd/system`.

## Run locally

Requirements about how to run

1. [.NET 7 SDK](http://dot.net/)
2. Run `npm i` at directory `/src/Aiursoft.ChessServer/wwwroot/`
3. Execute `dotnet run` to run the app
4. Use your browser to view [http://localhost:5000](http://localhost:5000)

## Run in Microsoft Visual Studio

1. Open the `.sln` file in the project path.
2. Press `F5`.

## How to contribute

There are many ways to contribute to the project: logging bugs, submitting pull requests, reporting issues, and creating suggestions.

Even if you with push rights on the repository, you should create a personal fork and create feature branches there when you need them. This keeps the main repository clean and your workflow cruft out of sight.

We're also interested in your feedback on the future of this project. You can submit a suggestion or feature request through the issue tracker. To make this process more effective, we're asking that these include more information to help define them more clearly.