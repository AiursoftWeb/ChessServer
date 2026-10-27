# ChessServer

Play chess against the computer or a friend, watch games, and chat. Built on Aiursoft Template with UIStack and OIDC sign-in; anonymous play is disabled.

- Eight computer levels, including a beginner that chooses random legal moves.
- Public or unlisted challenge links, live spectators, and PGN export.
- Inactive rooms expire and release their connections and game state.

![ChessServer](screenshot.png)

## Run

Configure `AppSettings__OIDC__Authority`, `AppSettings__OIDC__ClientId`, and `AppSettings__OIDC__ClientSecret` for your identity provider. Register `https://<your-host>/signin-oidc` as its callback.

```bash
cd src/Aiursoft.ChessServer/wwwroot
npm install && npm run build
cd ..
dotnet run
```

SQLite stores account data by default. Docker deployments should persist `/data`. Live games remain in memory and end when the application restarts.

[Source](https://github.com/aiursoftweb/chessserver) · [MIT license](LICENSE)
