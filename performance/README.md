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
