# Comunicador 2.5.1

Painel de avisos para rede local: um `Comunicador.exe` (C#/.NET, WPF)
manda notificações para outros computadores da rede, que podem
responder. Também envia avisos centrais, botões com links HTTP/HTTPS,
imagens, vídeos e áudios para computadores ou monitores específicos. Veja [PROTOCOLO.md](PROTOCOLO.md)
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
na mesma máquina sem conflito: se o receptor estiver usando as portas
principais, o painel passa automaticamente para as portas alternativas
57933/TCP e 57934/UDP. Detalhes em
[PROTOCOLO.md § Painel como seu próprio receptor](PROTOCOLO.md#painel-como-seu-próprio-receptor).

Mensagens e lembretes permanecem sempre ativos. Cada computador pode
bloquear mídias centrais e botões com links nas Configurações; esse
bloqueio local também vale quando apenas o receptor estiver aberto.

## Recursos principais

- Interface WPF responsiva com moldura própria, temas claro/escuro,
  paletas predefinidas ou criadas pelo usuário e nove opções de fundo animado.
- Navegação superior por ícones, transições e configurações salvas automaticamente.
- Ping médio em tempo real com indicador verde, amarelo, vermelho ou sem conexão.
- Avisos comuns, alertas centrais, imagens e vídeos por monitor, além de áudio invisível em segundo plano.
- Identidade permanente por computador, nomes públicos e badges sincronizados pelo Supabase.
- Um admin global com badge OWNER exclusiva pode conceder badges ADMIN personalizadas a outros painéis.
- Lembretes e respostas entregues em segundo plano, inclusive com o painel fechado.
- Alteração remota do papel de parede pelo admin em computadores pareados.
- Histórico separado por computador e logs sincronizados entre painéis, com respostas e exportação para TXT.
- Versões do painel e do receptor visíveis, atualização remota do receptor e atualização do próprio painel pela interface.
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
fonte local é mais nova. Em outro computador o BAT funciona sozinho:
se ainda não houver instalação, baixa **todos os arquivos publicados no
GitHub** para `C:\Users\<usuário>\Documents\P5`, valida o executável,
registra a instalação e cria atalhos no Desktop e no menu Iniciar. Se
algum arquivo instalado for apagado, a próxima inicialização baixa o
pacote novamente e restaura o que estiver faltando. A inicialização
também sincroniza todos os arquivos `.bat`, inclusive instalador,
diagnóstico e desinstalador do receptor. O auxiliar que libera as portas
também faz parte dessa instalação completa.

O painel também verifica atualizações sozinho quando é aberto diretamente. Em
Configurações → Painel e rede, o botão **Atualizar e reiniciar** instala a versão
publicada; ao voltar, o aplicativo mostra um resumo curto do que foi adicionado.

## Identidade, sincronização e admin

Painel e receptor compartilham o mesmo identificador em
`%LOCALAPPDATA%\Comunicador\device.json`. Ele não pode ser alterado pela
interface e evita que o mesmo computador apareça duplicado.

Na primeira utilização administrativa, abra **Configurações → Painel e rede**
e pressione a tecla `'` duas vezes. A primeira senha com pelo menos oito
caracteres torna esse computador o admin global. Repetir o gesto e a mesma
senha no admin remove o acesso; fazer isso em outro computador transfere o
admin para ele. Somente o admin global recebe a badge OWNER, pode definir o
papel de parede remoto e pode conceder, retirar ou personalizar a badge ADMIN.
Administradores delegados podem editar perfis comuns, mas não alteram o OWNER,
outros admins ou as próprias badges reservadas.

O esquema executado no Supabase está em `supabase/comunicador.sql`. O painel e
o receptor usam sessões anônimas autenticadas separadas por dispositivo; as
políticas do banco permitem que cada máquina altere seu próprio perfil e que o
admin altere os demais. Não é preciso colocar a chave `service_role` no app.

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
sempre baixa e valida a versão 2.5.1 publicada no GitHub antes de
substituir uma instalação existente. Ao concluir com sucesso, fecha
sozinho. Para remover o receptor, use
`receiver/DESINSTALAR_RECEPTOR.bat`. O desinstalador preserva as regras
e configurações compartilhadas quando há um painel na mesma máquina.
Para conferir antecipadamente todos os alvos sem alterar nada, execute
`receiver/DESINSTALAR_RECEPTOR.bat --verificar`.

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
