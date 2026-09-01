namespace Comunicador.Protocol;

public sealed record ValidationResult(bool IsValid, string? Code, string? Message)
{
    public static ValidationResult Ok() => new(true, null, null);

    public static ValidationResult Fail(string code, string message) => new(false, code, message);
}
