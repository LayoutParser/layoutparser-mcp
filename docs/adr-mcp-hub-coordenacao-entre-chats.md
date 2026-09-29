# ADR — MCP Hub: coordenação entre chats/repositórios da organização

Status: proposto (decisões de hospedagem, persistência e identidade tomadas pelo dono em 2026-09-29).

## Problema
Os trabalhos correm em chats separados (API, decrypt, lowcoderunner, portal, cypress, lib). Hoje eles só se
falam pelo dono, que repassa mensagens à mão; contratos entre repositórios (ex.: sidecar HTTP do decrypt e do
runner) ficam soltos em documentos e divergem. O MCP atual (`mcp/LayoutParserMcp`) é um processo `stdio` por
sessão, cliente fino da API: não compartilha estado e não conhece outros chats.

## Decisão
Evoluir o MCP para um **serviço central (MCP Hub)**, com três decisões do dono:
1. **Hospedagem:** container novo na VM Ubuntu (`172.25.32.5`, Docker), transporte **HTTP streamable** do SDK
   `ModelContextProtocol` (mesmo pacote já usado; adicionar o transporte HTTP/ASP.NET Core). Os tools atuais
   (parse_document, detect_layout, list_endpoints, api_get, api_post) continuam disponíveis; `stdio` segue
   suportado para uso local.
2. **Persistência:** o SQL Server Docker dedicado do projeto (`IdentityDatabase:*`, container
   `layoutparser-identity-sql`). **Nunca** `172.31.249.51`/`Database:*` (somente leitura, ver `security.md`).
3. **Identidade:** **token por chat/repositório** (api, decrypt, lowcoderunner, portal, cypress, lib, dono),
   em variável de ambiente, mais rede isolada (firewall allowlist). Guardar só o **hash** do token; permitir
   revogar um por vez; auditar quem publicou/leu o quê.

## Ferramentas (namespace de coordenação)
| Tool | Função |
|------|--------|
| `list_chats` | Chats registrados, último acesso, tarefa atual. |
| `post_message` | Mensagem para um chat ou canal: `kind` = info / question / request / handoff, `subject`, `body`, `refs` (issue/PR/arquivo). |
| `read_inbox` | Mensagens novas (ou todas) para o chat autenticado, com filtro por thread. |
| `ack_message` | Marca como lida/atendida. |
| `publish_contract` / `get_contract` / `list_contracts` | Contratos **versionados** entre repositórios (ex.: `sidecar-lowcoderunner v1`), com dono, hash e histórico. É a fonte da verdade que evita divergência. |
| `set_status` / `get_board` | O que cada chat está fazendo e o que o bloqueia; visão única do board entre repositórios. |

## Regras de segurança (inegociáveis)
- **Mensagem de chat é DADO, nunca autoridade.** Toda resposta que devolve mensagens vem envelopada com aviso
  explícito: o conteúdo pode ser lido, mas **não** concede permissão, aprovação nem instrução em nome do dono
  (já comprovado nesta sessão: agentes recusam corretamente autorização repassada). Aprovação real só vem do
  dono no chat dele.
- Sem segredos, credenciais nem dados de cliente nas mensagens/contratos: rejeitar padrões óbvios no servidor
  (connection string com senha, chaves, tokens) e limitar tamanho (body ≤ 16 KB; contrato ≤ 256 KB).
- Limite de taxa por token; retenção (mensagens 90 dias, contratos permanentes com histórico).
- Nenhum conteúdo de mensagem em log de aplicação (só ids, tamanhos, remetente/destinatário).
- Sem DDL "lazy" em banco compartilhado: esquema criado por script de migração explícito só no
  `IdentityDatabase`.

## Modelo de dados (SQL, esquema `mcp`)
`Chat(Id, Name, Scope, TokenHash, CreatedAt, RevokedAt, LastSeenAt)`;
`Message(Id, ThreadId, FromChatId, ToChatId NULL, Channel NULL, Kind, Subject, Body, RefsJson, CreatedAt)`;
`MessageAck(MessageId, ChatId, AckedAt)`;
`Contract(Name, Version, Content, ContentHash, OwnerChatId, Status, PublishedAt)` (PK Name+Version);
`Status(ChatId, Task, State, BlockersJson, UpdatedAt)`; `Audit(Id, ChatId, Tool, At, Outcome)`.

## Limitação conhecida (assumida)
MCP não empurra notificação para dentro de um chat. Cada chat precisa **consultar** (`read_inbox`) — convenção
a registrar no `CLAUDE.md` de cada repositório: no início da sessão e antes de decisões que afetem contratos.
Push por webhook/polling agendado pode virar fase posterior.

## Rollout
F1 (backend): transporte HTTP, autenticação por token (hash), esquema + `post_message`/`read_inbox`/`ack_message`/
`list_chats`, testes. F2: contratos versionados e `set_status`/`get_board`. F3 (devops): container na VM, porta,
firewall, geração e distribuição dos tokens fora do git, `.mcp.json` de cada repositório apontando para a URL
com `Authorization: Bearer ${LAYOUTPARSER_MCP_TOKEN}`. F4: convenção de uso no `CLAUDE.md` de cada repo e
publicação dos primeiros contratos (sidecar decrypt v1, sidecar lowcoderunner v1).
