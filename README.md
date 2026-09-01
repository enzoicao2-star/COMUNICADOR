# Comunicador

Painel de avisos para rede local: um `Comunicador.exe` (C#/.NET, WPF)
manda notificações para outros computadores da rede, que podem
responder. Veja [PROTOCOLO.md](PROTOCOLO.md) para o protocolo TCP/JSON
completo.

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
um painel pareado específico.

## Painel (Comunicador.exe)

Requer o [.NET SDK 10](https://dotnet.microsoft.com/download) para
compilar (a versão publicada é self-contained — quem só usa o
`Comunicador.exe` não precisa instalar nada).

```bash
build.bat
```

Restaura dependências, roda os testes (`tests/Comunicador.Tests`,
inclusive os que sobem `receptor.py` de verdade para testar a
comunicação C# ↔ Python), compila em Release e publica uma versão
self-contained single-file em `dist/Comunicador.exe`.

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
login do usuário (via `pythonw.exe`, sem janela de console). Para
remover tudo, use `receiver/DESINSTALAR_RECEPTOR.bat`.

Rodar os testes do receptor localmente:

```bash
pip install -r receiver/tests/requirements-test.txt
pytest receiver/tests
```

## Estrutura

```
P5/
├── PROTOCOLO.md
├── build.bat
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
