# Comunicador 2.2.2

Painel de avisos para rede local: um `Comunicador.exe` (C#/.NET, WPF)
manda notificações para outros computadores da rede, que podem
responder. Também envia avisos centrais, botões com links HTTP/HTTPS e
imagens para monitores específicos. Veja [PROTOCOLO.md](PROTOCOLO.md)
para o protocolo TCP/JSON completo.

```
COMUNICADOR
│
├── PAINEL            src/Comunicador — C#/.NET (WPF), Comunicador.exe
├── RECEPTOR           receiver/ — receptor.py (Python)
├── PROTOCOLO          PROTOCOLO.md — TCP + JSON, documentado
└── TESTES             tests/ (C#) e receiver/tests/ (Python)
```

## Como cada computador recebe mensagens

Todo `Comunicador.exe` já funciona como seu próprio receptor — quem só
quer usar o painel **não precisa instalar nada em Python**. O
`receptor.py` continua existindo à parte para computadores que devem
só *receber* avisos, sem a interface completa do painel (ex.: uma
máquina compartilhada sem monitor dedicado). Os dois podem coexistir
na mesma máquina sem conflito: se as portas já estiverem em uso pelo
outro, o painel simplesmente desiste de escutar e loga o motivo, sem
derrubar quem já está rodando. Detalhes em
[PROTOCOLO.md § Painel como seu próprio receptor](PROTOCOLO.md#painel-como-seu-próprio-receptor).

Dá para bloquear o recebimento a qualquer momento em
Configurações → *"Aceitar mensagens de outros painéis"*, ou bloquear
um painel pareado específico. Mensagens, imagens e botões com links
possuem controles de permissão separados.

## Recursos principais

- Interface WPF responsiva com moldura própria, temas claro/escuro,
  paletas predefinidas ou criadas pelo usuário e oito opções de fundo animado.
- Navegação superior por ícones, transições e configurações salvas automaticamente.
- Ping médio em tempo real com indicador verde, amarelo, vermelho ou sem conexão.
- Avisos comuns, alertas centrais e imagens por monitor, com tamanho e tempo configuráveis.
- Identificação visual de computadores que também executam o painel.
- Histórico e logs sincronizados entre painéis, com exportação para TXT.
- Versão do receptor visível no painel e atualização remota autenticada.
- TCP + JSON independente de linguagem, conexão reversa e descoberta UDP/LAN.

## Painel (Comunicador.exe)

Requer o [.NET SDK 10](https://dotnet.microsoft.com/download) para
compilar (a versão publicada é self-contained — quem só usa o
`Comunicador.exe` não precisa instalar nada).

```bash
build.bat
```

Para abrir o painel, use `ABRIR_COMUNICADOR.bat`. A cada inicialização
ele consulta a versão publicada no GitHub; quando há uma mais nova,
baixa o executável self-contained, valida versão e SHA-256 e só então
substitui a cópia instalada. Sem internet, abre normalmente a última
versão válida. Dentro do repositório ele também recompila quando a
fonte local é mais nova; em outro computador o BAT funciona sozinho e
instala o painel em `%LOCALAPPDATA%\Comunicador\Painel`, além de baixar o
auxiliar que libera as portas na primeira execução.

`build.bat` gera o ícone, restaura dependências, roda os testes Python
e C# (inclusive integração real C# ↔ Python), compila em Release e
publica uma versão self-contained single-file em
`dist/Comunicador.exe`.

Para validar, criar o commit, sincronizar e enviar ao GitHub em uma
etapa, execute `SUBIR_GITHUB.bat`. Uma mensagem de commit opcional pode
ser passada como argumento.

Para rodar em modo desenvolvimento sem publicar:

```bash
dotnet run --project src/Comunicador/Comunicador.csproj
```

## Receptor (receptor.py)

Para computadores que devem só receber avisos (sem o painel
completo), baixe e execute `receiver/INSTALAR_RECEPTOR.bat` — ele
verifica/instala o Python automaticamente, baixa `receptor.py`,
instala as dependências e configura a tarefa **"Comunicador
Receptor"** no Agendador de Tarefas do Windows para iniciar com o
login do usuário (via `pythonw.exe`, sem janela de console). O arquivo
sempre baixa e valida a versão 2.2.1 publicada no GitHub antes de
substituir uma instalação existente. Ao concluir com sucesso, fecha
sozinho. Para remover tudo, use `receiver/DESINSTALAR_RECEPTOR.bat`.

Rodar os testes do receptor localmente:

```bash
python -m pip install -r receiver/requirements-dev.txt
python -m pytest receiver/tests -q
```

## Estrutura

```
P5/
├── PROTOCOLO.md
├── build.bat
├── ABRIR_COMUNICADOR.bat
├── SUBIR_GITHUB.bat
├── Comunicador.slnx
├── src/Comunicador/          painel C#/.NET (WPF)
├── tests/Comunicador.Tests/  testes C# (unitários + integração com receptor.py)
├── receiver/
│   ├── receptor.py
│   ├── protocolo.py
│   ├── requirements.txt
│   ├── INSTALAR_RECEPTOR.bat
│   ├── DESINSTALAR_RECEPTOR.bat
│   └── tests/                testes Python (protocolo + integração)
└── dist/                     gerado pelo build.bat — Comunicador.exe
```
