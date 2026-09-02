# Protocolo Comunicador (TCP + JSON)

Este documento é a fonte da verdade do protocolo usado entre o painel
(`Comunicador.exe`, C#/.NET) e os receptores (`receptor.py`, Python).
Qualquer alteração aqui deve ser refletida nas duas implementações:

- C#: `src/Comunicador/Protocol/`
- Python: `receiver/protocolo.py`

## Portas

| Uso                         | Protocolo | Porta |
|------------------------------|-----------|-------|
| Descoberta / pareamento       | UDP       | 57932 |
| Mensagens (notificação, ping) | TCP       | 57931 |

Ambas configuráveis na tela **Configurações** do painel e no
`config.json` do receptor, mas o padrão acima é o que os dois lados
assumem se nada for configurado.

## Enquadramento (framing)

Cada mensagem TCP é um único objeto JSON compacto (sem quebras de
linha internas) seguido de `\n` (0x0A). O leitor consome bytes até
encontrar `\n` e faz o parse do que veio antes. Isso é suficiente
porque `json.dumps`/`JsonSerializer` nunca emitem `\n` literal dentro
da string (quebras de linha em campos de texto são escapadas como
`\n`).

Pacotes UDP de descoberta são um único objeto JSON por datagrama, sem
delimitador.

## Limites de tamanho

- Mensagem TCP completa (bytes UTF-8, incluindo o `\n`): **65 536 bytes** no máximo.
- Datagrama UDP de descoberta: **2048 bytes** no máximo.
- `title`: até 200 caracteres.
- `message` / `reply_text`: até 4000 caracteres.
- `sender` / `panel_name` / `computer_name`: até 100 caracteres.

Qualquer mensagem fora desses limites é rejeitada com `error`
(`code = "PAYLOAD_TOO_LARGE"` ou `"FIELD_TOO_LONG"`) e a conexão é
encerrada.

## Envelope comum

Todo objeto JSON trafegado tem os campos:

```json
{
  "protocol_version": 1,
  "type": "notification",
  "id": "b3b3c2b0-6f3b-4e34-9a9b-2f7e2a9a2b40",
  "timestamp": "2026-09-01T12:00:00Z"
}
```

- `protocol_version`: inteiro, hoje sempre `1`.
- `type`: string, um dos tipos abaixo.
- `id`: UUID v4 (string) — identifica esta mensagem especificamente.
- `timestamp`: ISO-8601 UTC.

## Tipos de mensagem

### `discover` (UDP broadcast, painel → todos)

`panel_id` é um GUID gerado uma única vez por instalação do
`Comunicador` (persistido em `config.json` do painel) — identifica
*aquele* painel de forma estável, independente do IP ou do
`sender_name`, e é o que permite a um receptor responder "pareado"
ou "não pareado" corretamente quando existe **mais de um painel** na
rede.

```json
{ "protocol_version": 1, "type": "discover", "id": "...", "timestamp": "...",
  "panel_id": "1a2b3c4d-...", "sender_name": "PAINEL-PC" }
```

### `announce` (UDP unicast, receptor → painel)

Resposta direta (mesmo socket UDP, endereço do remetente do
`discover`) enviada pelo receptor ao ouvir um `discover`. `paired`
reflete se o `panel_id` daquele `discover` específico já está na
lista de pareados do receptor — cada painel vê seu próprio estado,
mesmo que o receptor esteja pareado com outros painéis também.

```json
{ "protocol_version": 1, "type": "announce", "id": "...", "timestamp": "...",
  "computer_id": "b0b1...", "computer_name": "COMPUTADOR-1",
  "tcp_port": 57931, "paired": false }
```

### `pair_request` (TCP, painel → receptor)

```json
{ "protocol_version": 1, "type": "pair_request", "id": "...", "timestamp": "...",
  "panel_id": "1a2b3c4d-...", "panel_name": "PAINEL-PC" }
```

### `pair_response` (TCP, receptor → painel)

```json
{ "protocol_version": 1, "type": "pair_response", "id": "...", "timestamp": "...",
  "accepted": true, "computer_id": "b0b1...", "computer_name": "COMPUTADOR-1",
  "token": "9f8b7a6c5d4e3f2a1b0c9d8e7f6a5b4c" }
```

`token` é um segredo compartilhado gerado pelo receptor no momento do
pareamento, guardado por ambos os lados e enviado em toda mensagem
subsequente (`ping`, `notification`) para autenticar o remetente numa
rede doméstica (não é criptografia forte — é controle de acesso
simples, adequado ao caso de uso).

## Múltiplos painéis

Um mesmo receptor pode ser pareado com **vários painéis** ao mesmo
tempo (ex.: um `Comunicador.exe` no computador da sala e outro no
escritório, ambos enviando avisos para os mesmos receptores). Isso é
suportado porque:

- O receptor não guarda "um token", guarda uma **lista de
  pareamentos** (`panel_id → { token, panel_name, paired_at }`) em seu
  `config.json`. Cada pareamento é independente.
- Ao validar `ping`/`notification`, o receptor procura o `token`
  recebido entre todos os pareamentos conhecidos — não importa qual
  painel o enviou.
- O `panel_id` no `discover`/`pair_request` é o que torna cada painel
  reconhecível de forma estável, permitindo revogar ou re-parear um
  painel específico sem afetar os demais.
- O servidor TCP do receptor aceita **conexões concorrentes**: vários
  painéis podem enviar notificações ao mesmo tempo sem se
  bloquearem.
- Do lado do painel nada muda: cada instalação do `Comunicador` tem
  seu próprio `%AppData%\Comunicador`, sua própria lista de
  computadores pareados e seu próprio histórico — painéis não
  compartilham estado entre si.

### `ping` / `pong` (TCP, checagem de status)

```json
{ "protocol_version": 1, "type": "ping", "id": "...", "timestamp": "...",
  "token": "9f8b..." }
```

```json
{ "protocol_version": 1, "type": "pong", "id": "...", "timestamp": "...",
  "computer_id": "b0b1...", "computer_name": "COMPUTADOR-1", "status": "online" }
```

### `notification` (TCP, painel → receptor)

```json
{ "protocol_version": 1, "type": "notification", "id": "...", "timestamp": "...",
  "token": "9f8b...", "sender": "PAINEL-PC", "title": "Aviso",
  "message": "Olá", "allow_reply": true }
```

#### Botões de resposta rápida (`buttons`, opcional)

A `notification` pode trazer até **4** botões, mostrados no aviso:

```json
{ "protocol_version": 1, "type": "notification", "id": "...", "timestamp": "...",
  "token": "9f8b...", "sender": "PAINEL-PC", "title": "Nota fiscal disponível",
  "message": "A nota do mês já está no portal.", "allow_reply": true,
  "buttons": [
    { "label": "Abrir portal", "url": "https://exemplo.com/portal" },
    { "label": "Já vi, obrigado" }
  ] }
```

- `label`: obrigatório, até 40 caracteres.
- `url`: opcional, até 500 caracteres.

Clicar num botão devolve o `label` como `reply_text`. Se o botão tiver
`url`, o endereço também é aberto no **navegador padrão**.

**Regra de segurança:** `url` só pode ser `http://` ou `https://`.
Qualquer outro esquema (`file:`, `javascript:`, `ms-settings:`,
`data:`, `ftp:`, caminhos UNC…) é recusado com
`INVALID_FIELD_TYPE`. Isso é validado três vezes: por quem envia, por
quem recebe ao validar a mensagem, e de novo no momento do clique —
porque a URL chega pela rede e abrir endereço arbitrário seria
exatamente o "executar coisa recebida pela rede" que o projeto evita.
O link nunca abre sozinho: só com clique do usuário, e o botão mostra
um ícone indicando que sai para o navegador.

### `ack` (TCP, receptor → painel)

Confirma recebimento/exibição, correlacionando pelo `id` da
notificação original em `in_reply_to`.

```json
{ "protocol_version": 1, "type": "ack", "id": "...", "timestamp": "...",
  "in_reply_to": "b3b3...", "status": "shown" }
```

`status` é `"delivered"` (recebida, validada) ou `"shown"` (exibida ao
usuário).

### `reply` (TCP, receptor → painel)

Enviado depois que o usuário responde a uma notificação com
`allow_reply: true`, na mesma conexão.

```json
{ "protocol_version": 1, "type": "reply", "id": "...", "timestamp": "...",
  "in_reply_to": "b3b3...", "computer_id": "b0b1...",
  "computer_name": "COMPUTADOR-1", "reply_text": "Recebido!" }
```

### `register` (TCP, receptor → painel) — conexão reversa

Quem abre a conexão aqui é o **receptor**, discando para o painel. Como
conexões de saída praticamente nunca são bloqueadas por firewall
doméstico, esse caminho dispensa qualquer porta de entrada liberada na
máquina do receptor — só o painel precisa da porta aberta.

```json
{ "protocol_version": 1, "type": "register", "id": "...", "timestamp": "...",
  "computer_id": "b0b1...", "computer_name": "COMPUTADOR-1", "token": "9f8b..." }
```

`token` é opcional: na primeira vez o receptor ainda não tem um, e o
painel emite um no `register_ack`. Nas reconexões o receptor manda o
token que já tem, e o painel o valida.

### `register_ack` (TCP, painel → receptor)

```json
{ "protocol_version": 1, "type": "register_ack", "id": "...", "timestamp": "...",
  "accepted": true, "token": "9f8b...",
  "computer_id": "1a2b...", "computer_name": "PAINEL-PC" }
```

Depois do `register_ack` a conexão **permanece aberta**. O painel envia
`notification` por ela sempre que precisar, e o receptor responde `ack`
e `reply` pela mesma conexão. Se a conexão cair, o receptor reconecta
sozinho a cada 15 segundos.

Se o host contatado não for um painel (por exemplo, outro receptor, que
escuta na mesma porta), ele responde `error` com `UNKNOWN_TYPE` — e o
receptor para de tentar aquele endereço.

### `error` (qualquer direção)

```json
{ "protocol_version": 1, "type": "error", "id": "...", "timestamp": "...",
  "in_reply_to": "b3b3...", "code": "MISSING_FIELD",
  "message": "Campo obrigatório ausente: title" }
```

Códigos usados: `INVALID_JSON`, `UNKNOWN_TYPE`, `MISSING_FIELD`,
`INVALID_FIELD_TYPE`, `FIELD_TOO_LONG`, `PAYLOAD_TOO_LARGE`,
`INVALID_ID`, `UNAUTHORIZED`, `PROTOCOL_VERSION_UNSUPPORTED`.

## Validação obrigatória (nos dois lados)

Antes de processar qualquer mensagem recebida, cada lado deve
validar, nesta ordem, e responder `error` + encerrar a conexão no
primeiro problema encontrado:

1. **Tamanho** do payload dentro do limite antes mesmo de tentar
   decodificar.
2. **JSON válido** (parse não falha).
3. **`type` presente** e é um dos tipos conhecidos.
4. **Campos obrigatórios** do tipo presentes e com o tipo de dado
   correto (string/bool/int conforme a tabela acima).
5. **Tamanho dos campos de texto** dentro do limite.
6. **`id` é um UUID v4 válido.**
7. **Autenticação**: para `ping` e `notification`, o `token` deve
   bater com o token salvo para aquele `computer_id`/conexão pareada;
   caso contrário `error` com `UNAUTHORIZED`.

## Fluxo de uma notificação com resposta

```
PAINEL                                   RECEPTOR
  │  TCP connect ────────────────────────▶│
  │  notification {id=A, allow_reply}     │
  │───────────────────────────────────────▶│
  │                                        │ valida, mostra popup
  │◀─────────────────────── ack {shown,A} │
  │                                        │ usuário responde
  │◀───────────────────── reply {A, text} │
  │  (conexão fecha)                       │
```

Se `allow_reply` é `false`, o receptor envia apenas o `ack` e fecha a
conexão.

## Painel como seu próprio receptor

Todo `Comunicador.exe` também roda um receptor embutido (mesma lógica de
`receptor.py`, reimplementada nativamente em C# em
`src/Comunicador/Networking/EmbeddedReceptorServer.cs`): ele escuta TCP
e responde descoberta UDP igual a um receptor.py, guardando seus
próprios pareamentos em `%AppData%\Comunicador\paineis_pareados.json`.
Isso significa que **quem só quer usar o painel não precisa instalar
`receptor.py` na mesma máquina** — o próprio `Comunicador.exe` já
recebe avisos de outros painéis.

`receptor.py` continua existindo e é a opção certa para computadores
que devem só *receber* avisos, sem a interface completa do painel
(ex.: uma máquina compartilhada, sem monitor dedicado).

Duas garantias importantes:

- **Sem conflito de porta.** Se `receptor.py` já estiver rodando na
  mesma máquina (ou outra instância já ocupando a porta), o receptor
  embutido do painel detecta a falha ao abrir a porta, desiste de
  forma silenciosa e loga o motivo — ele **não** tenta dividir a porta
  nem derruba quem já está ouvindo. Nesse caso o painel continua
  enviando mensagens normalmente; quem já está escutando ali continua
  recebendo normalmente.
- **Bloqueio sob controle do usuário.** Em Configurações →
  *"Aceitar mensagens de outros painéis"*, dá para desligar o
  recebimento embutido inteiro (o painel só envia, não aparece mais na
  descoberta de ninguém). Também dá para bloquear um painel pareado
  específico individualmente, sem afetar os demais.

## Quem disca para quem

O Comunicador suporta os dois sentidos, e usa o que estiver disponível:

| Caminho | Quem abre a conexão | Porta de entrada necessária |
|---|---|---|
| **Reverso** (preferido) | receptor → painel | só no **painel** |
| Direto | painel → receptor | em **cada receptor** |

O caminho reverso existe porque exigir porta aberta em cada máquina com
receptor é a maior fonte de "o painel não encontra o computador". Com
ele, você configura o firewall **uma vez**, na máquina do painel, e os
receptores funcionam sem configuração nenhuma.

Ao iniciar, o receptor procura painéis na rede (testando a porta TCP do
protocolo em cada host da sub-rede), guarda os que encontrar em
`panel_hosts` no seu `config.json` e mantém uma conexão aberta com cada
um. O painel, ao enviar uma notificação, usa a conexão reversa se
existir uma viva para aquele computador; se não houver, disca para o
receptor como no caminho direto.

## Descoberta e pareamento

1. Painel envia `discover` por UDP broadcast na porta 57932.
2. Cada receptor ativo responde com `announce` (unicast) contendo
   `paired: false` se ainda não tem token salvo para este painel.
3. Usuário, na tela **Computadores**, clica em "Parear" no computador
   desejado.
4. Painel abre TCP e envia `pair_request`.
5. Receptor gera um `token`, salva localmente (`config.json`) e
   responde `pair_response { accepted: true, token }`.
6. Painel salva o token associado àquele `computer_id`. Todas as
   mensagens seguintes para esse computador incluem o token.
