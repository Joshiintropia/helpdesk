using System.Security.Cryptography;

namespace HelpDesk.Infrastructure.Identity;

/// <summary>Passwords an administrator hands to a new user; the user must change it on first sign-in.</summary>
public static class TemporaryPassword
{
    // No look-alike characters (0/O, 1/l/I) so it can be read aloud or dictated over the phone.
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";

    public static string Generate(int length = 12)
    {
        var all = Upper + Lower + Digits;
        var chars = new char[length];
        chars[0] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[1] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        for (var i = 3; i < length; i++)
        {
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }
}
