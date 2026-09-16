using System.Text;
using System.Text.Json;
using static Comunicador.Protocol.ProtocolConstants;

namespace Comunicador.Protocol;

public static class MessageValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    public static ValidationResult ValidateSize(int byteLength, bool isUdp)
    {
        var max = isUdp ? MaxUdpMessageBytes : MaxTcpMessageBytes;
        return byteLength > max
            ? ValidationResult.Fail(ErrorCode.PayloadTooLarge, $"Payload excede o limite de {max} bytes.")
            : ValidationResult.Ok();
    }

    public static bool TryParse(byte[] payload, out ComunicadorMessage? message, out ValidationResult result)
    {
        try
        {
            message = JsonSerializer.Deserialize<ComunicadorMessage>(payload, JsonOptions);
            if (message is null)
            {
                result = ValidationResult.Fail(ErrorCode.InvalidJson, "Mensagem JSON vazia ou nula.");
                return false;
            }

            result = ValidationResult.Ok();
            return true;
        }
        catch (JsonException ex)
        {
            message = null;
            result = ValidationResult.Fail(ErrorCode.InvalidJson, $"JSON inválido: {ex.Message}");
            return false;
        }
    }

    public static ValidationResult Validate(ComunicadorMessage msg)
    {
        if (msg.ProtocolVersion != ProtocolConstants.Version)
        {
            return ValidationResult.Fail(
                ErrorCode.ProtocolVersionUnsupported,
                $"Versão de protocolo não suportada: {msg.ProtocolVersion}");
        }

        if (string.IsNullOrWhiteSpace(msg.Type) || !MessageType.All.Contains(msg.Type))
        {
            return ValidationResult.Fail(ErrorCode.UnknownType, $"Tipo de mensagem desconhecido: '{msg.Type}'");
        }

        if (!Guid.TryParse(msg.Id, out _))
        {
            return ValidationResult.Fail(ErrorCode.InvalidId, "Campo 'id' não é um UUID válido.");
        }

        if (string.IsNullOrWhiteSpace(msg.Timestamp))
        {
            return ValidationResult.Fail(ErrorCode.MissingField, "Campo obrigatório ausente: timestamp");
        }

        var fieldsCheck = msg.Type switch
        {
            MessageType.Discover => RequireUuid(msg.PanelId, "panel_id")
                ?? RequireString(msg.SenderName, "sender_name", MaxNameLength),

            MessageType.Announce => RequireString(msg.ComputerId, "computer_id", MaxNameLength)
                ?? RequireString(msg.ComputerName, "computer_name", MaxNameLength)
                ?? RequireInt(msg.TcpPort, "tcp_port")
                ?? RequireBool(msg.Paired, "paired"),

            MessageType.PairRequest => RequireUuid(msg.PanelId, "panel_id")
                ?? RequireString(msg.PanelName, "panel_name", MaxNameLength),

            MessageType.PairResponse => RequireBool(msg.Accepted, "accepted")
                ?? (msg.Accepted == true
                    ? RequireString(msg.ComputerId, "computer_id", MaxNameLength)
                        ?? RequireString(msg.ComputerName, "computer_name", MaxNameLength)
                        ?? RequireString(msg.Token, "token", MaxNameLength)
                    : null),

            MessageType.Ping => RequireString(msg.Token, "token", MaxNameLength),

            MessageType.Pong => RequireString(msg.ComputerId, "computer_id", MaxNameLength)
                ?? RequireString(msg.ComputerName, "computer_name", MaxNameLength)
                ?? RequireString(msg.Status, "status", MaxNameLength),

            MessageType.Notification => RequireString(msg.Token, "token", MaxNameLength)
                ?? RequireString(msg.Sender, "sender", MaxNameLength)
                ?? RequireString(msg.Title, "title", MaxTitleLength)
                ?? RequireString(msg.Message, "message", MaxMessageLength)
                ?? RequireBool(msg.AllowReply, "allow_reply")
                ?? ValidarBotoes(msg.Buttons)
                ?? ValidarConteudoVisual(
                    msg.DisplayMode, msg.Image, msg.ScreenImages,
                    msg.ImageDurationSeconds, msg.AllowManualClose)
                ?? ValidarAparencia(msg.Appearance),

            MessageType.Ack => RequireUuid(msg.InReplyTo, "in_reply_to")
                ?? RequireString(msg.Status, "status", MaxNameLength),

            MessageType.Reply => RequireUuid(msg.InReplyTo, "in_reply_to")
                ?? RequireString(msg.ComputerId, "computer_id", MaxNameLength)
                ?? RequireString(msg.ComputerName, "computer_name", MaxNameLength)
                ?? RequireString(msg.ReplyText, "reply_text", MaxMessageLength),

            MessageType.Error => RequireString(msg.Code, "code", MaxNameLength)
                ?? RequireString(msg.Message, "message", MaxMessageLength),

            // conexao reversa: o receptor abre a conexao e se registra no painel.
            // token e opcional — na primeira vez o receptor ainda nao tem um.
            MessageType.Register => RequireString(msg.ComputerId, "computer_id", MaxNameLength)
                ?? RequireString(msg.ComputerName, "computer_name", MaxNameLength),

            MessageType.RegisterAck => RequireBool(msg.Accepted, "accepted")
                ?? (msg.Accepted == true ? RequireString(msg.Token, "token", MaxNameLength) : null),

            MessageType.UpdateRequest => RequireString(msg.Token, "token", MaxNameLength)
                ?? RequireString(msg.TargetVersion, "target_version", MaxNameLength)
                ?? ValidarArquivosAtualizacao(msg.UpdateFiles),

            MessageType.UpdateStatus => RequireUuid(msg.InReplyTo, "in_reply_to")
                ?? RequireBool(msg.Success, "success")
                ?? RequireString(msg.Status, "status", MaxNameLength)
                ?? RequireString(msg.ReceiverVersion, "receiver_version", MaxNameLength)
                ?? (msg.Message is null ? null : RequireString(msg.Message, "message", MaxMessageLength)),

            MessageType.SyncRequest => RequireString(msg.Token, "token", MaxNameLength)
                ?? RequireBool(msg.IncludeHistory, "include_history")
                ?? RequireBool(msg.IncludeLogs, "include_logs")
                ?? ValidarHistoricoSincronizado(msg.HistoryEntries)
                ?? ValidarLogsSincronizados(msg.LogEntries),

            MessageType.SyncResponse => RequireUuid(msg.InReplyTo, "in_reply_to")
                ?? ValidarHistoricoSincronizado(msg.HistoryEntries)
                ?? ValidarLogsSincronizados(msg.LogEntries),

            _ => ValidationResult.Fail(ErrorCode.UnknownType, $"Tipo de mensagem desconhecido: '{msg.Type}'"),
        };

        return fieldsCheck ?? ValidarMonitores(msg.Monitors) ?? ValidationResult.Ok();
    }

    public static byte[] Frame(ComunicadorMessage message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        return Encoding.UTF8.GetBytes(json + "\n");
    }

    /// <summary>Botões são opcionais, mas quando vêm precisam ser sãos: quantidade,
    /// rótulo e — o mais importante — só http/https no link, porque isso chega pela rede.</summary>
    private static ValidationResult? ValidarBotoes(List<BotaoResposta>? botoes)
    {
        if (botoes is null || botoes.Count == 0)
        {
            return null;
        }

        if (botoes.Count > MaxBotoes)
        {
            return ValidationResult.Fail(ErrorCode.FieldTooLong, $"São permitidos no máximo {MaxBotoes} botões.");
        }

        foreach (var botao in botoes)
        {
            if (string.IsNullOrWhiteSpace(botao.Label))
            {
                return ValidationResult.Fail(ErrorCode.MissingField, "Campo obrigatório ausente: buttons[].label");
            }

            if (botao.Label.Length > MaxBotaoLabelLength)
            {
                return ValidationResult.Fail(
                    ErrorCode.FieldTooLong, $"Rótulo de botão excede {MaxBotaoLabelLength} caracteres.");
            }

            if (string.IsNullOrWhiteSpace(botao.Url))
            {
                continue;
            }

            if (botao.Url.Length > MaxBotaoUrlLength)
            {
                return ValidationResult.Fail(
                    ErrorCode.FieldTooLong, $"URL de botão excede {MaxBotaoUrlLength} caracteres.");
            }

            if (!BotaoResposta.UrlPermitida(botao.Url))
            {
                return ValidationResult.Fail(
                    ErrorCode.InvalidFieldType, "URL de botão precisa ser http:// ou https://.");
            }
        }

        return null;
    }

    private static ValidationResult? ValidarConteudoVisual(
        string? modo, ConteudoImagem? imagem, List<ImagemMonitor>? imagensPorMonitor,
        int? duracaoSegundos, bool? permitirFechar)
    {
        modo ??= DisplayMode.Toast;
        if (!DisplayMode.All.Contains(modo))
        {
            return ValidationResult.Fail(
                ErrorCode.InvalidFieldType,
                "Campo 'display_mode' precisa ser 'toast', 'center_image' ou 'center_alert'.");
        }

        if (modo == DisplayMode.CenterImage
            && imagem is null
            && (imagensPorMonitor is null || imagensPorMonitor.Count == 0))
        {
            return ValidationResult.Fail(
                ErrorCode.MissingField, "Aviso central precisa de 'image' ou 'screen_images'.");
        }

        if (modo == DisplayMode.CenterImage && !duracaoSegundos.HasValue)
        {
            return ValidationResult.Fail(
                ErrorCode.MissingField, "Aviso central precisa do campo 'image_duration_seconds'.");
        }

        if (modo == DisplayMode.CenterImage
            && (duracaoSegundos < MinImageDurationSeconds || duracaoSegundos > MaxImageDurationSeconds))
        {
            return ValidationResult.Fail(
                ErrorCode.InvalidFieldType,
                $"'image_duration_seconds' precisa ficar entre {MinImageDurationSeconds} e {MaxImageDurationSeconds}.");
        }

        if (modo == DisplayMode.CenterImage && !permitirFechar.HasValue)
        {
            return ValidationResult.Fail(
                ErrorCode.MissingField, "Aviso central precisa do campo 'allow_manual_close'.");
        }

        var totalBytes = 0;
        if (imagem is not null)
        {
            var resultadoImagem = ValidarImagem(imagem, out var tamanho);
            if (resultadoImagem is not null)
            {
                return resultadoImagem;
            }
            totalBytes += tamanho;
        }

        if (imagensPorMonitor is { Count: > 0 })
        {
            if (imagensPorMonitor.Count > MaxScreenImages)
            {
                return ValidationResult.Fail(
                    ErrorCode.FieldTooLong, $"São permitidas no máximo {MaxScreenImages} imagens por mensagem.");
            }

            foreach (var item in imagensPorMonitor)
            {
                if (item is null || item.Image is null)
                {
                    return ValidationResult.Fail(
                        ErrorCode.MissingField, "Campo obrigatório ausente: screen_images[].image");
                }

                if (item.MonitorIndex is < 0 or >= MaxMonitors)
                {
                    return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Índice de monitor inválido.");
                }
                if (item.WidthPercent is < MinImageWidthPercent or > MaxImageWidthPercent)
                {
                    return ValidationResult.Fail(
                        ErrorCode.InvalidFieldType,
                        $"Tamanho da imagem precisa ficar entre {MinImageWidthPercent}% e {MaxImageWidthPercent}%.");
                }

                var resultadoImagem = ValidarImagem(item.Image, out var tamanho);
                if (resultadoImagem is not null)
                {
                    return resultadoImagem;
                }
                totalBytes += tamanho;
            }
        }

        if (totalBytes > MaxTotalImageBytes)
        {
            return ValidationResult.Fail(
                ErrorCode.PayloadTooLarge, $"O conjunto de imagens excede {MaxTotalImageBytes} bytes.");
        }

        return null;
    }

    private static ValidationResult? ValidarImagem(ConteudoImagem imagem, out int tamanhoBytes)
    {
        tamanhoBytes = 0;
        var nome = RequireString(imagem.Name, "image.name", MaxImageNameLength);
        if (nome is not null)
        {
            return nome;
        }

        if (!ConteudoImagem.MimePermitido(imagem.MimeType))
        {
            return ValidationResult.Fail(
                ErrorCode.InvalidFieldType, "Formato de imagem não permitido. Use PNG, JPEG, GIF ou BMP.");
        }

        if (string.IsNullOrEmpty(imagem.DataBase64))
        {
            return ValidationResult.Fail(ErrorCode.MissingField, "Campo obrigatório ausente: image.data_base64");
        }

        if (imagem.DataBase64.Length > MaxImageBase64Length)
        {
            return ValidationResult.Fail(ErrorCode.PayloadTooLarge, $"Imagem excede {MaxImageBytes} bytes.");
        }

        byte[] dados;
        try
        {
            dados = Convert.FromBase64String(imagem.DataBase64);
        }
        catch (FormatException)
        {
            return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Campo 'image.data_base64' não é Base64 válido.");
        }

        if (dados.Length == 0 || dados.Length > MaxImageBytes)
        {
            return ValidationResult.Fail(ErrorCode.PayloadTooLarge, $"Imagem precisa ter entre 1 e {MaxImageBytes} bytes.");
        }

        if (!ConteudoImagem.AssinaturaCorresponde(imagem.MimeType, dados))
        {
            return ValidationResult.Fail(
                ErrorCode.InvalidFieldType, "O conteúdo do arquivo não corresponde ao formato de imagem informado.");
        }

        tamanhoBytes = dados.Length;
        return null;
    }

    private static ValidationResult? ValidarArquivosAtualizacao(List<ArquivoAtualizacao>? arquivos)
    {
        if (arquivos is null || arquivos.Count != MaxUpdateFiles)
        {
            return ValidationResult.Fail(
                ErrorCode.MissingField,
                "A atualização precisa conter receptor.py e protocolo.py.");
        }

        var nomesPermitidos = new HashSet<string>(StringComparer.Ordinal)
        {
            "receptor.py", "protocolo.py",
        };
        var nomesEncontrados = new HashSet<string>(StringComparer.Ordinal);
        var total = 0;

        foreach (var arquivo in arquivos)
        {
            if (arquivo is null || !nomesPermitidos.Contains(arquivo.Name)
                || !nomesEncontrados.Add(arquivo.Name))
            {
                return ValidationResult.Fail(
                    ErrorCode.InvalidFieldType, "A atualização contém nome de arquivo inválido ou repetido.");
            }

            if (string.IsNullOrWhiteSpace(arquivo.Sha256)
                || arquivo.Sha256.Length != 64
                || arquivo.Sha256.Any(c => !Uri.IsHexDigit(c)))
            {
                return ValidationResult.Fail(ErrorCode.InvalidFieldType, "SHA-256 de atualização inválido.");
            }

            byte[] conteudo;
            try
            {
                conteudo = Convert.FromBase64String(arquivo.ContentBase64);
                _ = new UTF8Encoding(false, true).GetString(conteudo);
            }
            catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
            {
                return ValidationResult.Fail(
                    ErrorCode.InvalidFieldType, "Arquivo de atualização não contém texto UTF-8 válido.");
            }

            if (conteudo.Length == 0 || conteudo.Length > MaxUpdateSourceBytes)
            {
                return ValidationResult.Fail(
                    ErrorCode.PayloadTooLarge, "Arquivo de atualização excede o limite permitido.");
            }

            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(conteudo));
            if (!hash.Equals(arquivo.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Fail(
                    ErrorCode.InvalidFieldType, "SHA-256 do arquivo de atualização não confere.");
            }

            total += conteudo.Length;
        }

        return total > MaxUpdateTotalBytes
            ? ValidationResult.Fail(ErrorCode.PayloadTooLarge, "Pacote de atualização excede o limite permitido.")
            : null;
    }

    private static ValidationResult? ValidarHistoricoSincronizado(List<HistoricoSincronizado>? itens)
    {
        if (itens is null) return null;
        if (itens.Count > MaxSyncEntries)
        {
            return ValidationResult.Fail(ErrorCode.FieldTooLong, "Histórico de sincronização excede 500 itens.");
        }

        var direcoes = new HashSet<string>(StringComparer.Ordinal) { "enviada", "recebida" };
        var status = new HashSet<string>(StringComparer.Ordinal)
        {
            "enviando", "entregue", "exibido", "respondido", "erro", "semresposta",
        };
        foreach (var item in itens)
        {
            if (item is null || !Guid.TryParse(item.Id, out _)
                || !DateTimeOffset.TryParse(item.Timestamp, out _))
            {
                return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Item de histórico sincronizado inválido.");
            }
            var campos = RequireString(item.ComputerId, "history_entries[].computer_id", MaxNameLength)
                ?? RequireString(item.ComputerName, "history_entries[].computer_name", MaxNameLength)
                ?? RequireString(item.Title, "history_entries[].title", MaxTitleLength)
                ?? RequireString(item.Message, "history_entries[].message", MaxMessageLength);
            if (campos is not null) return campos;
            if (!direcoes.Contains(item.Direction) || !status.Contains(item.Status))
            {
                return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Direção ou status do histórico inválido.");
            }
            if (item.ReplyText is { Length: > MaxMessageLength }
                || item.ErrorDetail is { Length: > MaxLogDetailLength })
            {
                return ValidationResult.Fail(ErrorCode.FieldTooLong, "Detalhe do histórico excede o limite.");
            }
        }
        return null;
    }

    private static ValidationResult? ValidarLogsSincronizados(List<RegistroLogSincronizado>? itens)
    {
        if (itens is null) return null;
        if (itens.Count > MaxSyncEntries)
        {
            return ValidationResult.Fail(ErrorCode.FieldTooLong, "Sincronização de logs excede 500 itens.");
        }

        var niveis = new HashSet<string>(StringComparer.Ordinal) { "info", "aviso", "erro" };
        var origens = new HashSet<string>(StringComparer.Ordinal) { "panel", "receiver" };
        foreach (var item in itens)
        {
            if (item is null || !Guid.TryParse(item.Id, out _)
                || !DateTimeOffset.TryParse(item.Timestamp, out _)
                || !niveis.Contains(item.Level) || !origens.Contains(item.OriginType))
            {
                return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Registro de log sincronizado inválido.");
            }
            var campos = RequireString(item.OriginId, "log_entries[].origin_id", MaxNameLength)
                ?? RequireString(item.OriginName, "log_entries[].origin_name", MaxNameLength)
                ?? RequireString(item.Category, "log_entries[].category", MaxLogCategoryLength)
                ?? RequireString(item.Message, "log_entries[].message", MaxMessageLength);
            if (campos is not null) return campos;
            if (item.Details is { Length: > MaxLogDetailLength })
            {
                return ValidationResult.Fail(ErrorCode.FieldTooLong, "Detalhes do log excedem o limite.");
            }
        }
        return null;
    }

    private static ValidationResult? ValidarMonitores(List<MonitorInfo>? monitores)
    {
        if (monitores is null)
        {
            return null;
        }
        if (monitores.Count > MaxMonitors)
        {
            return ValidationResult.Fail(ErrorCode.FieldTooLong, $"São permitidos no máximo {MaxMonitors} monitores.");
        }

        foreach (var monitor in monitores)
        {
            if (monitor.Index is < 0 or >= MaxMonitors || monitor.Width is < 0 or > 32768
                || monitor.Height is < 0 or > 32768 || monitor.X is < -65536 or > 65536
                || monitor.Y is < -65536 or > 65536)
            {
                return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Dados de monitor inválidos.");
            }
            var nome = RequireString(monitor.Name, "monitors[].name", MaxNameLength);
            if (nome is not null)
            {
                return nome;
            }
        }

        return null;
    }

    private static ValidationResult? ValidarAparencia(AparenciaNotificacao? aparencia)
    {
        if (aparencia is null)
        {
            return null;
        }

        if (aparencia.AccentColor.Length != 7 || aparencia.AccentColor[0] != '#'
            || !aparencia.AccentColor.AsSpan(1).ToString().All(Uri.IsHexDigit))
        {
            return ValidationResult.Fail(
                ErrorCode.InvalidFieldType, "'appearance.accent_color' precisa usar o formato #RRGGBB.");
        }

        if (aparencia.FontScalePercent is < MinFontScalePercent or > MaxFontScalePercent)
        {
            return ValidationResult.Fail(
                ErrorCode.InvalidFieldType,
                $"Escala do texto precisa ficar entre {MinFontScalePercent}% e {MaxFontScalePercent}%.");
        }

        if (!SoundType.All.Contains(aparencia.SoundType))
        {
            return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Tipo de som inválido.");
        }

        if (aparencia.ToastDurationSeconds is < MinToastDurationSeconds or > MaxToastDurationSeconds)
        {
            return ValidationResult.Fail(
                ErrorCode.InvalidFieldType,
                $"Duração do aviso precisa ficar entre {MinToastDurationSeconds} e {MaxToastDurationSeconds} segundos.");
        }

        if (!ToastPosition.All.Contains(aparencia.ToastPosition))
        {
            return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Posição do aviso inválida.");
        }

        return null;
    }

    private static ValidationResult? RequireString(string? value, string name, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
        {
            return ValidationResult.Fail(ErrorCode.MissingField, $"Campo obrigatório ausente: {name}");
        }

        if (value.Length > maxLen)
        {
            return ValidationResult.Fail(ErrorCode.FieldTooLong, $"Campo '{name}' excede {maxLen} caracteres.");
        }

        return null;
    }

    private static ValidationResult? RequireUuid(string? value, string name)
    {
        var missing = RequireString(value, name, MaxNameLength);
        if (missing is not null)
        {
            return missing;
        }

        return Guid.TryParse(value, out _)
            ? null
            : ValidationResult.Fail(ErrorCode.InvalidId, $"Campo '{name}' não é um UUID válido.");
    }

    private static ValidationResult? RequireBool(bool? value, string name) =>
        value.HasValue ? null : ValidationResult.Fail(ErrorCode.MissingField, $"Campo obrigatório ausente: {name}");

    private static ValidationResult? RequireInt(int? value, string name) =>
        value.HasValue ? null : ValidationResult.Fail(ErrorCode.MissingField, $"Campo obrigatório ausente: {name}");
}
