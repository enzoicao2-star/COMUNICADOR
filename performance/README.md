# Desempenho da versão 2.5.0

Medição em três execuções reais do painel Release, com a janela aberta no segundo
monitor e 24 trocas de aba por execução. Os arquivos JSON desta pasta preservam
as amostras completas.

| Métrica | Antes | Depois | Redução |
|---|---:|---:|---:|
| Processo até a janela | 3.392,87 ms | 2.434,15 ms | 28,3% |
| Janela pronta | 3.289,80 ms | 2.813,58 ms | 14,5% |
| Troca média de aba | 115,03 ms | 4,23 ms | 96,3% |
| Troca de aba p95 | 552,90 ms | 8,88 ms | 98,4% |
| Pior troca de aba | 1.186,79 ms | 11,98 ms | 99,0% |
| Memória de trabalho | 238,49 MB | 192,82 MB | 19,2% |
| CPU durante o cenário | 8.588,54 ms | 3.817,71 ms | 55,6% |

As melhorias vieram da permanência das views no mesmo host, pré-aquecimento
progressivo, pausa breve do fundo durante a transição, virtualização com reciclagem
em Histórico e Logs, criação sob demanda das partículas e cache dos pincéis usados
pelos fundos animados.

Para repetir a medição:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Medir-Desempenho.ps1 -Runs 3
```

## Revisão de desempenho — 2026-09-23

O ciclo atual mediu três execuções Release do aplicativo no segundo monitor, com
24 trocas de aba por execução. As médias de inicialização e navegação melhoraram;
o uso de memória subiu nesta amostra e está registrado abaixo para não esconder
essa regressão.

| Métrica do aplicativo | Referência | Atual | Variação |
|---|---:|---:|---:|
| Processo até a janela | 1.501,26 ms | 1.175,95 ms | −21,7% |
| Janela pronta | 2.271,03 ms | 1.878,26 ms | −17,3% |
| Troca média de aba | 12,13 ms | 11,52 ms | −5,0% |
| Troca de aba p95 | 73,56 ms | 67,89 ms | −7,7% |
| Pior troca de aba | 145,35 ms | 133,62 ms | −8,1% |
| Memória de trabalho | 239,95 MB | 252,76 MB | +5,3% |

Também há um teste reprodutível dos repositórios com 10.000 registros, 250
leituras completas do histórico e 500 registros remotos mesclados:

| Operação | Referência deste ciclo | Atual | Redução |
|---|---:|---:|---:|
| Histórico: 250 snapshots | 74,38 ms | 20,54 ms | 72,4% |
| Logs: 250 snapshots | 67,53 ms | 21,35 ms | 68,4% |
| Histórico: mesclar 500 itens | 256,76 ms | 47,06 ms | 81,7% |
| Logs: mesclar 500 itens | 137,32 ms | 32,37 ms | 76,4% |

Essas melhorias vêm da busca por ID, remoção de ordenações repetidas e atualização
em lote das coleções observáveis. A animação “Onda de partículas” também reduz
de cerca de 200 mil para 800 os cálculos trigonométricos por quadro. Animações de fundo,
badges e GIFs suspendem seus relógios quando deixam de estar visíveis; erros
transitórios do receptor UDP agora aguardam antes de tentar novamente.

Para medir apenas os repositórios novamente:

```powershell
dotnet test tests\Comunicador.Tests\Comunicador.Tests.csproj -c Release --filter Category=Performance --logger "console;verbosity=detailed"
```

Há um ponto ainda aberto: o processo consumiu em média 916,67 ms de CPU durante
um segundo com a janela oculta, mesmo com os relógios de animação monitorados
desligados. A captura de pilhas do Windows foi bloqueada pela política local de
rastreamento; portanto, essa amostra não permite atribuir o custo a um método
específico. O resultado completo e o detalhe por execução estão em
[`optimization-2026-09-23.json`](optimization-2026-09-23.json).
