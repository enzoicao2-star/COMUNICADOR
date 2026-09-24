namespace Comunicador.Protocol;

public static class ProtocolConstants
{
    public const int Version = 1;
    public const string CurrentReceiverVersion = "2.5.3";
    public const string CurrentPanelVersion = "2.5.5";
    public const string MinimumManagedReceiverVersion = "2.1.0";

    public static bool SupportsRemoteManagement(string? receiverVersion) =>
        System.Version.TryParse(receiverVersion, out var installed)
        && System.Version.TryParse(MinimumManagedReceiverVersion, out var minimum)
        && installed >= minimum;

    public const int TcpPort = 57931;
    public const int UdpDiscoveryPort = 57932;
    public const int PanelFallbackTcpPort = 57933;
    public const int PanelFallbackUdpDiscoveryPort = 57934;

    // GIFs de 16 MiB viram aproximadamente 21,4 MiB em Base64. O quadro TCP
    // comporta esse crescimento, inclusive quando imagens são destinadas a monitores.
    public const int MaxTcpMessageBytes = 64 * 1024 * 1024;
    public const int MaxUdpMessageBytes = 2048;

    public const int MaxTitleLength = 200;
    public const int MaxMessageLength = 4000;
    public const int MaxNameLength = 100;

    public const int MaxBotoes = 2;
    public const int MaxBotaoLabelLength = 40;
    public const int MaxBotaoUrlLength = 500;

    public const int MaxImageBytes = 4 * 1024 * 1024;
    public const int MaxGifBytes = 16 * 1024 * 1024;
    public const int MaxImageNameLength = 255;
    public const int MaxImageBase64Length = ((MaxImageBytes + 2) / 3) * 4;
    public const int MaxGifBase64Length = ((MaxGifBytes + 2) / 3) * 4;
    public const int MinImageDurationSeconds = 3;
    public const int MaxImageDurationSeconds = 3600;
    public const int MaxMonitors = 12;
    public const int MaxScreenImages = 12;
    public const int MaxTotalImageBytes = 16 * 1024 * 1024;
    public const int MaxVideoBytes = 24 * 1024 * 1024;
    public const int MaxVideoBase64Length = ((MaxVideoBytes + 2) / 3) * 4;
    public const int MaxScreenVideos = 4;
    public const int MaxAudioBytes = 12 * 1024 * 1024;
    public const int MaxAudioBase64Length = ((MaxAudioBytes + 2) / 3) * 4;
    public const int MaxTotalMediaBytes = 40 * 1024 * 1024;
    public const int MaxUpdateFiles = 2;
    public const int MaxUpdateSourceBytes = 2 * 1024 * 1024;
    public const int MaxUpdateTotalBytes = 4 * 1024 * 1024;
    public const int MaxSyncEntries = 500;
    public const int MaxSyncProfiles = 250;
    public const int MaxBadgesPerComputer = 4;
    public const int MaxBadgeTextLength = 28;
    public const int MaxCustomBadgeIconBytes = 64 * 1024;
    public const int MaxLogCategoryLength = 100;
    public const int MaxLogDetailLength = 8000;
    public const int MinImageWidthPercent = 10;
    public const int MaxImageWidthPercent = 100;
    public const int MinToastDurationSeconds = 5;
    public const int MaxToastDurationSeconds = 300;
    public const int MinFontScalePercent = 80;
    public const int MaxFontScalePercent = 160;

    public static int MaxImageBytesForMime(string? mimeType) =>
        string.Equals(mimeType, "image/gif", StringComparison.OrdinalIgnoreCase)
            ? MaxGifBytes
            : MaxImageBytes;

    public static int MaxImageBase64LengthForMime(string? mimeType) =>
        string.Equals(mimeType, "image/gif", StringComparison.OrdinalIgnoreCase)
            ? MaxGifBase64Length
            : MaxImageBase64Length;

    public static class SoundType
    {
        public const string Information = "information";
        public const string Warning = "warning";
        public const string Error = "error";
        public static readonly IReadOnlySet<string> All = new HashSet<string> { Information, Warning, Error };
    }

    public static class ToastPosition
    {
        public const string BottomRight = "bottom_right";
        public const string TopRight = "top_right";
        public static readonly IReadOnlySet<string> All = new HashSet<string> { BottomRight, TopRight };
    }

    public static class DisplayMode
    {
        public const string Toast = "toast";
        public const string CenterImage = "center_image";
        public const string CenterVideo = "center_video";
        public const string Audio = "audio";
        public const string CenterAlert = "center_alert";
        public const string CenterMessage = "center_message";
        public const string Wallpaper = "wallpaper";

        public static readonly IReadOnlySet<string> All = new HashSet<string>
        {
            Toast, CenterImage, CenterVideo, Audio, CenterAlert, CenterMessage, Wallpaper,
        };
    }

    public static class MessageType
    {
        public const string Discover = "discover";
        public const string Announce = "announce";
        public const string PairRequest = "pair_request";
        public const string PairResponse = "pair_response";
        public const string Ping = "ping";
        public const string Pong = "pong";
        public const string Notification = "notification";
        public const string Ack = "ack";
        public const string Reply = "reply";
        public const string Error = "error";
        public const string Register = "register";
        public const string RegisterAck = "register_ack";
        public const string UpdateRequest = "update_request";
        public const string UpdateStatus = "update_status";
        public const string SyncRequest = "sync_request";
        public const string SyncResponse = "sync_response";

        public static readonly IReadOnlySet<string> All = new HashSet<string>
        {
            Discover, Announce, PairRequest, PairResponse, Ping, Pong,
            Notification, Ack, Reply, Error, Register, RegisterAck,
            UpdateRequest, UpdateStatus, SyncRequest, SyncResponse,
        };
    }

    public static class ErrorCode
    {
        public const string InvalidJson = "INVALID_JSON";
        public const string UnknownType = "UNKNOWN_TYPE";
        public const string MissingField = "MISSING_FIELD";
        public const string InvalidFieldType = "INVALID_FIELD_TYPE";
        public const string FieldTooLong = "FIELD_TOO_LONG";
        public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
        public const string InvalidId = "INVALID_ID";
        public const string Unauthorized = "UNAUTHORIZED";
        public const string ProtocolVersionUnsupported = "PROTOCOL_VERSION_UNSUPPORTED";
        public const string ContentBlocked = "CONTENT_BLOCKED";
        public const string InternalError = "INTERNAL_ERROR";
    }
}
