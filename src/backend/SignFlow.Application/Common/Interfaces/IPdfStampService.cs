using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Interfaces;

public interface IPdfStampService
{
    /// <summary>
    /// Draws all signed field values onto the source PDF and returns the stamped document as a stream.
    /// </summary>
    Task<Stream> StampAsync(string sourcePath, IEnumerable<StampFieldDto> fields, CancellationToken cancellationToken = default);
}

public record StampFieldDto(
    FieldType FieldType,
    int Page,
    float X,
    float Y,
    float Width,
    float Height,
    string? TextValue,
    string? ImageStoragePath);
