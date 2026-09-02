namespace Comunicador.Protocol;

public static class ProtocolConstants
{
    public const int Version = 1;

    public const int TcpPort = 57931;
    public const int UdpDiscoveryPort = 57932;

    public const int MaxTcpMessageBytes = 65536;
    public const int MaxUdpMessageBytes = 2048;

    public const int MaxTitleLength = 200;
    public const int MaxMessageLength = 4000;
    public const int MaxNameLength = 100;

    public const int MaxBotoes = 4;
    public const int MaxBotaoLabelLength = 40;
    public const int MaxBotaoUrlLength = 500;

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

        public static readonly IReadOnlySet<string> All = new HashSet<string>
        {
            Discover, Announce, PairRequest, PairResponse, Ping, Pong,
            Notification, Ack, Reply, Error, Register, RegisterAck,
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
    }
}
