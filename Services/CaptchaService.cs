using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RestaurantReservation.Services;

// Self-hosted image CAPTCHA. The answer is kept in the server-side session only,
// and each code can be checked once (it is removed on validation, right or wrong).
public class CaptchaService
{
    private const string SessionKey = "CaptchaCode";
    private const string SessionTimeKey = "CaptchaCreatedAt";
    private const int CodeLength = 5;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    // Leaves out characters that are easy to confuse (0/O, 1/I/L).
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public string CreateCaptchaSvg(ISession session)
    {
        var code = new string(Enumerable.Range(0, CodeLength)
            .Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)])
            .ToArray());

        session.SetString(SessionKey, code);
        session.SetString(SessionTimeKey, DateTime.UtcNow.ToString("O"));

        return RenderSvg(code);
    }

    public bool Validate(ISession session, string? answer)
    {
        var expected = session.GetString(SessionKey);
        var createdAt = session.GetString(SessionTimeKey);
        session.Remove(SessionKey);
        session.Remove(SessionTimeKey);

        if (string.IsNullOrEmpty(expected) || string.IsNullOrWhiteSpace(answer) || createdAt is null)
        {
            return false;
        }

        var created = DateTime.Parse(createdAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (DateTime.UtcNow - created > Lifetime)
        {
            return false;
        }

        return string.Equals(expected, answer.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string RenderSvg(string code)
    {
        const int width = 200;
        const int height = 70;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#f4f1ea\"/>");

        // Background noise: dots and curved lines.
        for (var i = 0; i < 40; i++)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<circle cx=\"{Rand(width)}\" cy=\"{Rand(height)}\" r=\"{1 + Rand(2)}\" fill=\"{RandomColor(150, 220)}\"/>");
        }
        for (var i = 0; i < 5; i++)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<path d=\"M{Rand(width)} {Rand(height)} Q{Rand(width)} {Rand(height)} {Rand(width)} {Rand(height)}\" " +
                $"stroke=\"{RandomColor(90, 170)}\" stroke-width=\"{1 + Rand(2)}\" fill=\"none\"/>");
        }

        // The characters, each with its own position, rotation, size and colour.
        var step = width / (code.Length + 1);
        for (var i = 0; i < code.Length; i++)
        {
            var x = step * (i + 1) - 8 + Rand(8);
            var y = 45 + Rand(12);
            var angle = Rand(50) - 25;
            var size = 28 + Rand(8);
            sb.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{x}\" y=\"{y}\" font-family=\"Georgia, 'Courier New', monospace\" font-size=\"{size}\" " +
                $"font-weight=\"bold\" fill=\"{RandomColor(20, 110)}\" transform=\"rotate({angle} {x} {y})\">{code[i]}</text>");
        }

        // Lines drawn over the text.
        for (var i = 0; i < 3; i++)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<line x1=\"0\" y1=\"{Rand(height)}\" x2=\"{width}\" y2=\"{Rand(height)}\" stroke=\"{RandomColor(60, 140)}\" stroke-width=\"1.5\"/>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static int Rand(int max) => RandomNumberGenerator.GetInt32(max);

    private static string RandomColor(int min, int max) =>
        $"rgb({min + Rand(max - min)},{min + Rand(max - min)},{min + Rand(max - min)})";
}
