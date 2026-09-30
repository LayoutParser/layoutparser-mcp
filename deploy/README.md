# Deploy do MCP Hub na VM Ubuntu

## 1. Atualizar o código e compilar

```bash
cd ~/layoutparser/layoutparser-mcp && git pull && dotnet build -c Release
```

## 2. Colocar o Hub como serviço (sobrevive a reboot)

Pré-requisito: `~/layoutparser/hub.env` (chmod 600, fora do git) com as variáveis do Hub
(`LAYOUTPARSER_MCP_TRANSPORT=http`, `MCP_HUB_TOKEN_PEPPER`, `MCP_HUB_SQL_CONNECTION`).
O arquivo pode conter também os tokens dos chats; o Hub ignora o que não usa.

Se o Hub estiver rodando à mão (nohup ou terminal), pare-o antes, senão a porta 5210 fica ocupada:

```bash
pkill -f LayoutParserMcp.dll
```

Instalar e iniciar:

```bash
sudo cp deploy/systemd/layoutparser-hub.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now layoutparser-hub
```

Verificar:

```bash
systemctl status layoutparser-hub --no-pager
curl -s http://localhost:5210/healthz
journalctl -u layoutparser-hub -n 30 --no-pager
```

Atualizar depois de um `git pull`:

```bash
dotnet build -c Release && sudo systemctl restart layoutparser-hub
```

## Observações

- O usuário (`User=elson`), os caminhos e o `DOTNET_ROOT` da unit assumem a VM atual. Ajuste se mudar.
- Se o pepper (`MCP_HUB_TOKEN_PEPPER`) mudar, todos os tokens dos chats deixam de valer e os hashes em
  `mcp.Chat` precisam ser refeitos.
- A VM avisou `System restart required`. Com o serviço instalado, um reboot não derruba o Hub.
