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
                ?? ValidarTextoNotificacao(msg)
                ?? RequireBool(msg.AllowReply, "allow_reply")
                ?? ValidarBotoes(msg.Buttons)
                ?? ValidarConteudoVisual(
                    msg.DisplayMode, msg.Image, msg.ScreenImages,
                    msg.Video, msg.ScreenVideos, msg.ImageDurationSeconds,
                    msg.AllowManualClose, msg.VideoLoop, msg.Audio, msg.AudioLoop)
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
                ?? ValidarLogsSincronizados(msg.LogEntries)
                ?? ValidarPerfisComputadores(msg.ComputerProfiles),

            MessageType.SyncResponse => RequireUuid(msg.InReplyTo, "in_reply_to")
                ?? ValidarHistoricoSincronizado(msg.HistoryEntries)
                ?? ValidarLogsSincronizados(msg.LogEntries)
                ?? ValidarPerfisComputadores(msg.ComputerProfiles),

            _ => ValidationResult.Fail(ErrorCode.UnknownType, $"Tipo de mensagem desconhecido: '{msg.Type}'"),
        };

        return fieldsCheck ?? ValidarMonitores(msg.Monitors) ?? ValidationResult.Ok();
    }

    /// <summary>Uma imagem central é conteúdo completo por si só, portanto título e
    /// mensagem podem ser strings vazias nesse modo. Nos avisos textuais ambos
    /// continuam obrigatórios.</summary>
    private static ValidationResult? ValidarTextoNotificacao(ComunicadorMessage msg)
    {
        if (msg.DisplayMode is DisplayMode.CenterImage or DisplayMode.CenterVideo or DisplayMode.Audio or DisplayMode.Wallpaper)
        {
            return RequireStringAllowEmpty(msg.Title, "title", MaxTitleLength)
                ?? RequireStringAllowEmpty(msg.Message, "message", MaxMessageLength);
        }

        return RequireString(msg.Title, "title", MaxTitleLength)
            ?? RequireString(msg.Message, "message", MaxMessageLength);
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
        ConteudoVideo? video, List<VideoMonitor>? videosPorMonitor,
        int? duracaoSegundos, bool? permitirFechar, bool? repetirVideo,
        ConteudoAudio? audio, bool? repetirAudio)
    {
        modo ??= DisplayMode.Toast;
        if (!DisplayMode.All.Contains(modo))
        {
            return ValidationResult.Fail(
                ErrorCode.InvalidFieldType,
                "Campo 'display_mode' precisa ser 'toast', 'center_image', 'center_video', 'audio' ou 'center_alert'.");
        }

        if (modo == DisplayMode.CenterImage
            && imagem is null
            && (imagensPorMonitor is null || imagensPorMonitor.Count == 0))
        {
            return ValidationResult.Fail(
                ErrorCode.MissingField, "Aviso central precisa de 'image' ou 'screen_images'.");
        }

        if (modo == DisplayMode.Wallpaper && imagem is null)
        {
            return ValidationResult.Fail(ErrorCode.MissingField, "Papel de parede precisa do campo 'image'.");
        }

        if (modo == DisplayMode.CenterVideo
            && video is null
            && (videosPorMonitor is null || videosPorMonitor.Count == 0))
        {
            return ValidationResult.Fail(
                ErrorCode.MissingField, "Vídeo central precisa de 'video' ou 'screen_videos'.");
        }

        if (modo == DisplayMode.Audio && audio is null)
        {
            return ValidationResult.Fail(ErrorCode.MissingField, "Reprodução de áudio precisa do campo 'audio'.");
        }

        if (modo == DisplayMode.CenterImage && (video is not null || videosPorMonitor is { Count: > 0 }))
        {
            return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Modo de imagem não aceita conteúdo de vídeo.");
        }

        if (modo == DisplayMode.CenterVideo && (imagem is not null || imagensPorMonitor is { Count: > 0 }))
        {
            return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Modo de vídeo não aceita conteúdo de imagem.");
        }

        if (modo == DisplayMode.Audio
            && (imagem is not null || imagensPorMonitor is { Count: > 0 }
                || video is not null || videosPorMonitor is { Count: > 0 }))
        {
            return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Modo de áudio não aceita imagem ou vídeo.");
        }

        if (modo == DisplayMode.CenterImage && !duracaoSegundos.HasValue)
        {
            return ValidationResult.Fail(
                ErrorCode.MissingField, "Aviso central precisa do campo 'image_duration_seconds'.");
        }

        if (modo == DisplayMode.CenterVideo && !repetirVideo.HasValue)
        {
            return ValidationResult.Fail(ErrorCode.MissingField, "Vídeo central precisa do campo 'video_loop'.");
        }

        if (modo == DisplayMode.Audio && !repetirAudio.HasValue)
        {
            return ValidationResult.Fail(ErrorCode.MissingField, "Reprodução de áudio precisa do campo 'audio_loop'.");
        }

        if (modo == DisplayMode.CenterVideo && repetirVideo == true && !duracaoSegundos.HasValue)
        {
            return ValidationResult.Fail(
                ErrorCode.MissingField, "Vídeo em loop precisa do campo 'image_duration_seconds'.");
        }

        if (modo == DisplayMode.Audio && repetirAudio == true && !duracaoSegundos.HasValue)
        {
            return ValidationResult.Fail(ErrorCode.MissingField, "Áudio em loop precisa do campo 'image_duration_seconds'.");
        }

        if ((modo is DisplayMode.CenterImage or DisplayMode.CenterVideo or DisplayMode.Audio)
            && duracaoSegundos.HasValue
            && (duracaoSegundos < MinImageDurationSeconds || duracaoSegundos > MaxImageDurationSeconds))
        {
            return ValidationResult.Fail(
                ErrorCode.InvalidFieldType,
                $"'image_duration_seconds' precisa ficar entre {MinImageDurationSeconds} e {MaxImageDurationSeconds}.");
        }

        if ((modo is DisplayMode.CenterImage or DisplayMode.CenterVideo) && !permitirFechar.HasValue)
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

        var totalVideoBytes = 0;
        if (video is not null)
        {
            var resultadoVideo = ValidarVideo(video, out var tamanho);
            if (resultadoVideo is not null)
            {
                return resultadoVideo;
            }
            totalVideoBytes += tamanho;
        }

        if (videosPorMonitor is { Count: > 0 })
        {
            if (videosPorMonitor.Count > MaxScreenVideos)
            {
                return ValidationResult.Fail(
                    ErrorCode.FieldTooLong, $"São permitidos no máximo {MaxScreenVideos} vídeos por mensagem.");
            }

            var monitoresUsados = new HashSet<int>();
            foreach (var item in videosPorMonitor)
            {
                if (item is null || item.Video is null)
                {
                    return ValidationResult.Fail(
                        ErrorCode.MissingField, "Campo obrigatório ausente: screen_videos[].video");
                }
                if (item.MonitorIndex is < 0 or >= MaxMonitors || !monitoresUsados.Add(item.MonitorIndex))
                {
                    return ValidationResult.Fail(
                        ErrorCode.InvalidFieldType, "Cada vídeo precisa apontar para um monitor válido e único.");
                }
                if (item.WidthPercent is < MinImageWidthPercent or > MaxImageWidthPercent)
                {
                    return ValidationResult.Fail(
                        ErrorCode.InvalidFieldType,
                        $"Tamanho do vídeo precisa ficar entre {MinImageWidthPercent}% e {MaxImageWidthPercent}%.");
                }

                var resultadoVideo = ValidarVideo(item.Video, out var tamanho);
                if (resultadoVideo is not null)
                {
                    return resultadoVideo;
                }
                totalVideoBytes += tamanho;
            }
        }

        if (totalBytes + totalVideoBytes > MaxTotalMediaBytes)
        {
            return ValidationResult.Fail(
                ErrorCode.PayloadTooLarge, $"O conjunto de mídias excede {MaxTotalMediaBytes} bytes.");
        }


        var totalAudioBytes = 0;
        if (audio is not null)
        {
            var resultadoAudio = ValidarAudio(audio, out totalAudioBytes);
            if (resultadoAudio is not null)
            {
                return resultadoAudio;
            }
        }

        if (totalBytes + totalVideoBytes + totalAudioBytes > MaxTotalMediaBytes)
        {
            return ValidationResult.Fail(
                ErrorCode.PayloadTooLarge, $"O conjunto de mídias excede {MaxTotalMediaBytes} bytes.");
        }

        return null;
    }

    private static ValidationResult? ValidarAudio(ConteudoAudio audio, out int tamanhoBytes)
    {
        tamanhoBytes = 0;
        var nome = RequireString(audio.Name, "audio.name", MaxImageNameLength);
        if (nome is not null)
        {
            return nome;
        }
        if (!ConteudoAudio.MimePermitido(audio.MimeType))
        {
            return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Formato de áudio não permitido. Use MP3 ou WAV.");
        }
        if (string.IsNullOrEmpty(audio.DataBase64))
        {
            return ValidationResult.Fail(ErrorCode.MissingField, "Campo obrigatório ausente: audio.data_base64");
        }
        if (audio.DataBase64.Length > MaxAudioBase64Length)
        {
            return ValidationResult.Fail(ErrorCode.PayloadTooLarge, $"Áudio excede {MaxAudioBytes} bytes.");
        }

        byte[] dados;
        try
        {
            dados = Convert.FromBase64String(audio.DataBase64);
        }
        catch (FormatException)
        {
            return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Campo 'audio.data_base64' não é Base64 válido.");
        }
        if (dados.Length == 0 || dados.Length > MaxAudioBytes)
        {
            return ValidationResult.Fail(ErrorCode.PayloadTooLarge, $"Áudio precisa ter entre 1 e {MaxAudioBytes} bytes.");
        }
        if (!ConteudoAudio.AssinaturaCorresponde(audio.MimeType, dados))
        {
            return ValidationResult.Fail(
                ErrorCode.InvalidFieldType, "O conteúdo do arquivo não corresponde ao formato de áudio informado.");
        }

        tamanhoBytes = dados.Length;
        return null;
    }

    private static ValidationResult? ValidarVideo(ConteudoVideo video, out int tamanhoBytes)
    {
        tamanhoBytes = 0;
        var nome = RequireString(video.Name, "video.name", MaxImageNameLength);
        if (nome is not null)
        {
            return nome;
        }

        if (!ConteudoVideo.MimePermitido(video.MimeType))
        {
            return ValidationResult.Fail(
                ErrorCode.InvalidFieldType, "Formato de vídeo não permitido. Use MP4 (H.264) ou WMV.");
        }
        if (string.IsNullOrEmpty(video.DataBase64))
        {
            return ValidationResult.Fail(ErrorCode.MissingField, "Campo obrigatório ausente: video.data_base64");
        }
        if (video.DataBase64.Length > MaxVideoBase64Length)
        {
            return ValidationResult.Fail(ErrorCode.PayloadTooLarge, $"Vídeo excede {MaxVideoBytes} bytes.");
        }

        byte[] dados;
        try
        {
            dados = Convert.FromBase64String(video.DataBase64);
        }
        catch (FormatException)
        {
            return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Campo 'video.data_base64' não é Base64 válido.");
        }

        if (dados.Length == 0 || dados.Length > MaxVideoBytes)
        {
            return ValidationResult.Fail(ErrorCode.PayloadTooLarge, $"Vídeo precisa ter entre 1 e {MaxVideoBytes} bytes.");
        }
        if (!ConteudoVideo.AssinaturaCorresponde(video.MimeType, dados))
        {
            return ValidationResult.Fail(
                ErrorCode.InvalidFieldType, "O conteúdo do arquivo não corresponde ao formato de vídeo informado.");
        }

        tamanhoBytes = dados.Length;
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

    private static ValidationResult? ValidarPerfisComputadores(List<PerfilComputadorSincronizado>? perfis)
    {
        if (perfis is null) return null;
        if (perfis.Count > MaxSyncProfiles)
            return ValidationResult.Fail(ErrorCode.FieldTooLong, "Sincronização de perfis excede o limite.");
        var estilos = new HashSet<string>(StringComparer.Ordinal)
            { "Holográfica", "Metal", "Pílula", "Contorno", "Selo" };
        foreach (var perfil in perfis)
        {
            if (perfil is null || !DateTimeOffset.TryParse(perfil.UpdatedAt, out _))
                return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Perfil sincronizado inválido.");
            var campos = RequireString(perfil.ComputerId, "computer_profiles[].computer_id", MaxNameLength)
                ?? RequireString(perfil.DisplayName, "computer_profiles[].display_name", MaxNameLength)
                ?? RequireString(perfil.UpdatedBy, "computer_profiles[].updated_by", MaxNameLength);
            if (campos is not null) return campos;
            if (perfil.Badges.Count > MaxBadgesPerComputer)
                return ValidationResult.Fail(ErrorCode.FieldTooLong, "Um computador excede o limite de badges.");
            foreach (var badge in perfil.Badges)
            {
                if (badge is null || string.IsNullOrWhiteSpace(badge.Id)
                    || string.IsNullOrWhiteSpace(badge.Texto) || badge.Texto.Length > MaxBadgeTextLength
                    || badge.Cor.Length != 7 || badge.Cor[0] != '#' || badge.Cor[1..].Any(c => !Uri.IsHexDigit(c))
                    || !estilos.Contains(badge.Estilo) || string.IsNullOrWhiteSpace(badge.Icone))
                    return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Badge sincronizada inválida.");
                if (badge.IconePersonalizadoBase64 is { Length: > 0 } icon)
                {
                    try
                    {
                        if (Convert.FromBase64String(icon).Length > MaxCustomBadgeIconBytes)
                            return ValidationResult.Fail(ErrorCode.PayloadTooLarge, "Ícone personalizado da badge é grande demais.");
                    }
                    catch (FormatException)
                    {
                        return ValidationResult.Fail(ErrorCode.InvalidFieldType, "Ícone personalizado da badge não é Base64 válido.");
                    }
                }
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

    private static ValidationResult? RequireStringAllowEmpty(string? value, string name, int maxLen)
    {
        if (value is null)
        {
            return ValidationResult.Fail(ErrorCode.MissingField, $"Campo obrigatório ausente: {name}");
        }

        return value.Length > maxLen
            ? ValidationResult.Fail(ErrorCode.FieldTooLong, $"Campo '{name}' excede {maxLen} caracteres.")
            : null;
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
