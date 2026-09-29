# layoutparser-mcp

MCP Hub da organização LayoutParser: (1) *tools* da LayoutParserApi para agentes e (2) coordenação entre chats
(mensagens, contratos, status). Detalhes em [`LayoutParserMcp/README.md`](LayoutParserMcp/README.md) e no ADR
[`docs/adr-mcp-hub-coordenacao-entre-chats.md`](docs/adr-mcp-hub-coordenacao-entre-chats.md).

```bash
dotnet build
dotnet test
```

Origem: extraído de `layoutparser-api/mcp/` (histórico anterior fica lá). Sem segredos no repositório:
`MCP_HUB_SQL_CONNECTION`, `MCP_HUB_TOKEN_PEPPER` e os tokens por chat vêm de variáveis de ambiente.
