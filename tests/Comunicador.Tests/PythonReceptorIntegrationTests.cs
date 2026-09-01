using System.Diagnostics;
using System.IO;
using Comunicador.Networking;
using Xunit;

namespace Comunicador.Tests;

/// <summary>Sobe o receptor.py de verdade (--test-mode, sem GUI) como processo filho e
/// conversa com ele usando o ReceptorClient real do painel — cobre a comunicação
/// C# ↔ Python ponta-a-ponta, nas duas linguagens de verdade, não só mocks.</summary>
public sealed class PythonReceptorFixture : IAsyncLifetime
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "comunicador_test_" + Guid.NewGuid());
    private Process? _process;

    public int TcpPort { get; private set; }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDir);
        var receptorPath = FindReceptorPath();
        var python = FindPythonExecutable();

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = python,
                ArgumentList =
                {
                    receptorPath, "--test-mode", "--port", "0", "--udp-port", "0",
                    "--config-dir", _tempDir, "--computer-name", "TESTE-CSHARP",
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        _process.Start();

        var readyLine = await ReadUntilReadyAsync(_process, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        TcpPort = ParseTcpPort(readyLine);
    }

    public Task DisposeAsync()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // já encerrado.
        }

        _process?.Dispose();

        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // best-effort.
        }

        return Task.CompletedTask;
    }

    private static async Task<string> ReadUntilReadyAsync(Process process, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var lineTask = process.StandardOutput.ReadLineAsync();
            var remaining = deadline - DateTime.UtcNow;
            var completed = await Task.WhenAny(lineTask, Task.Delay(remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining))
                .ConfigureAwait(false);

            if (completed != lineTask)
            {
                break;
            }

            var line = await lineTask.ConfigureAwait(false);
            if (line is null)
            {
                if (process.HasExited)
                {
                    var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                    throw new InvalidOperationException($"receptor.py encerrou antes de ficar pronto. Saída: {stderr}");
                }

                continue;
            }

            if (line.StartsWith("COMUNICADOR_RECEPTOR_READY", StringComparison.Ordinal))
            {
                return line;
            }
        }

        throw new TimeoutException("Tempo esgotado esperando receptor.py ficar pronto.");
    }

    private static int ParseTcpPort(string readyLine)
    {
        var part = readyLine.Split(' ').First(p => p.StartsWith("tcp=", StringComparison.Ordinal));
        return int.Parse(part["tcp=".Length..]);
    }

    private static string FindReceptorPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "receiver", "receptor.py");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Não encontrei receiver/receptor.py a partir de {AppContext.BaseDirectory}");
    }

    private static string FindPythonExecutable()
    {
        var fromEnv = Environment.GetEnvironmentVariable("COMUNICADOR_PYTHON");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        if (TentarExecutar("python"))
        {
            return "python";
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programsPython = Path.Combine(localAppData, "Programs", "Python");
        if (Directory.Exists(programsPython))
        {
            var encontrado = Directory.GetDirectories(programsPython)
                .OrderByDescending(d => d)
                .Select(d => Path.Combine(d, "python.exe"))
                .FirstOrDefault(File.Exists);
            if (encontrado is not null)
            {
                return encontrado;
            }
        }

        throw new InvalidOperationException(
            "Python não encontrado para os testes de integração. Defina a variável de ambiente COMUNICADOR_PYTHON.");
    }

    private static bool TentarExecutar(string exeName)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(exeName, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc is null)
            {
                return false;
            }

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return proc.ExitCode == 0 && output.Contains("Python", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}

public class PythonReceptorIntegrationTests : IClassFixture<PythonReceptorFixture>
{
    private readonly int _tcpPort;

    public PythonReceptorIntegrationTests(PythonReceptorFixture fixture)
    {
        _tcpPort = fixture.TcpPort;
    }

    private static ReceptorClient NovoCliente(string nome = "PAINEL-TESTE") =>
        new(Guid.NewGuid().ToString(), nome);

    [Fact]
    public async Task Pareamento_ComReceptorPython_Funciona()
    {
        var client = NovoCliente();
        var resultado = await client.PairAsync("127.0.0.1", _tcpPort);
        Assert.True(resultado.Accepted);
        Assert.False(string.IsNullOrEmpty(resultado.Token));
    }

    [Fact]
    public async Task PingAposParear_RetornaOnline()
    {
        var client = NovoCliente();
        var par = await client.PairAsync("127.0.0.1", _tcpPort);
        var online = await client.PingAsync("127.0.0.1", _tcpPort, par.Token);
        Assert.True(online);
    }

    [Fact]
    public async Task PingSemParear_RetornaFalse()
    {
        var client = NovoCliente();
        var online = await client.PingAsync("127.0.0.1", _tcpPort, "token-invalido-nao-pareado");
        Assert.False(online);
    }

    [Fact]
    public async Task NotificacaoComResposta_RecebeReplyAutomaticaDoModoTeste()
    {
        var client = NovoCliente();
        var par = await client.PairAsync("127.0.0.1", _tcpPort);

        var resultado = await client.SendNotificationAsync(
            "127.0.0.1", _tcpPort, par.Token, "Aviso do C#", "Mensagem de teste", allowReply: true);

        Assert.True(resultado.Delivered);
        Assert.True(resultado.WasShown);
        Assert.True(resultado.GotReply);
        Assert.False(string.IsNullOrEmpty(resultado.ReplyText));
    }

    [Fact]
    public async Task NotificacaoSemPermitirResposta_NaoEsperaReply()
    {
        var client = NovoCliente();
        var par = await client.PairAsync("127.0.0.1", _tcpPort);

        var resultado = await client.SendNotificationAsync(
            "127.0.0.1", _tcpPort, par.Token, "Aviso do C#", "Sem resposta", allowReply: false);

        Assert.True(resultado.Delivered);
        Assert.False(resultado.GotReply);
    }

    [Fact]
    public async Task MultiplosPaineis_PareiamIndependentementeComOMesmoReceptor()
    {
        var clientA = NovoCliente("PAINEL-A");
        var clientB = NovoCliente("PAINEL-B");

        var parA = await clientA.PairAsync("127.0.0.1", _tcpPort);
        var parB = await clientB.PairAsync("127.0.0.1", _tcpPort);

        Assert.NotEqual(parA.Token, parB.Token);
        Assert.True(await clientA.PingAsync("127.0.0.1", _tcpPort, parA.Token));
        Assert.True(await clientB.PingAsync("127.0.0.1", _tcpPort, parB.Token));
    }

    [Fact]
    public async Task VariosEnviosConcorrentes_TodosRecebemAck()
    {
        var client = NovoCliente();
        var par = await client.PairAsync("127.0.0.1", _tcpPort);

        var tarefas = Enumerable.Range(0, 5).Select(i => client.SendNotificationAsync(
            "127.0.0.1", _tcpPort, par.Token, $"Aviso {i}", "Concorrente", allowReply: false));

        var resultados = await Task.WhenAll(tarefas);
        Assert.All(resultados, r => Assert.True(r.Delivered));
    }

    [Fact]
    public async Task ComputadorOffline_PingRetornaFalseSemLancarExcecao()
    {
        var client = NovoCliente();
        var online = await client.PingAsync("127.0.0.1", 1, "qualquer-token");
        Assert.False(online);
    }
}
