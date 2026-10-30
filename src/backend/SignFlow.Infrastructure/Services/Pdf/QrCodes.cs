using QRCoder;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Infrastructure.Services.Pdf;

public class QrCodes : IQrCodes
{
    public string PngDataUri(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(6);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }
}
