using System;
using System.Text;
using Scada;
using Xunit;

namespace ScadaCommon.Tests;

public class ScadaUtilsCryptoTests
{
    [Fact]
    public void GetRandomLong_GeneratesVaryingValues()
    {
        long val1 = ScadaUtils.GetRandomLong();
        long val2 = ScadaUtils.GetRandomLong();
        long val3 = ScadaUtils.GetRandomLong();

        // At least two should differ (cryptographically negligible chance of all equal)
        Assert.True(val1 != val2 || val2 != val3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(128)]
    public void GetRandomBytes_ReturnsRequestedLength(int count)
    {
        byte[] bytes = ScadaUtils.GetRandomBytes(count);
        Assert.NotNull(bytes);
        Assert.Equal(count, bytes.Length);

        if (count >= 16)
        {
            // Verify not all zeroes
            bool allZero = true;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != 0)
                {
                    allZero = false;
                    break;
                }
            }
            Assert.False(allZero);
        }
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_WithDefaultSecretKeyAndDefaultIV_String()
    {
        string[] testStrings = new[]
        {
            "Hello, Arch SCADA CHPP!",
            "",
            "A quick brown fox jumps over the lazy dog 1234567890!@#$%^&*()_+",
            "Unicode test: Привет мир! 煤炭处理工厂 🚀",
            new string('X', 2048)
        };

        foreach (string original in testStrings)
        {
            // Test default overloads
            string encrypted = ScadaUtils.Encrypt(original);
            Assert.NotNull(encrypted);
            string decrypted = ScadaUtils.Decrypt(encrypted);
            Assert.Equal(original, decrypted);

            // Test explicit DefaultSecretKey and DefaultIV overloads
            string explicitEncrypted = ScadaUtils.Encrypt(original, ScadaUtils.DefaultSecretKey, ScadaUtils.DefaultIV);
            string explicitDecrypted = ScadaUtils.Decrypt(explicitEncrypted, ScadaUtils.DefaultSecretKey, ScadaUtils.DefaultIV);
            Assert.Equal(original, explicitDecrypted);

            // Both default and explicit encryption should decrypt interchangeably
            Assert.Equal(original, ScadaUtils.Decrypt(explicitEncrypted));
            Assert.Equal(original, ScadaUtils.Decrypt(encrypted, ScadaUtils.DefaultSecretKey, ScadaUtils.DefaultIV));
        }
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_WithDefaultSecretKeyAndDefaultIV_Bytes()
    {
        byte[] rawBytes = Encoding.UTF8.GetBytes("Tamper-evident SCADA payload test bytes");
        byte[] encrypted = ScadaUtils.EncryptBytes(rawBytes, ScadaUtils.DefaultSecretKey, ScadaUtils.DefaultIV);

        Assert.NotNull(encrypted);
        Assert.NotEqual(rawBytes, encrypted);

        byte[] decrypted = ScadaUtils.DecryptBytes(encrypted, ScadaUtils.DefaultSecretKey, ScadaUtils.DefaultIV);
        Assert.Equal(rawBytes, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_CustomKeyAndIV_RoundTrip()
    {
        byte[] customKey = ScadaUtils.GetRandomBytes(ScadaUtils.SecretKeySize);
        byte[] customIV = ScadaUtils.GetRandomBytes(ScadaUtils.IVSize);

        string original = "Custom key encryption test";
        string encrypted = ScadaUtils.Encrypt(original, customKey, customIV);
        string decrypted = ScadaUtils.Decrypt(encrypted, customKey, customIV);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_NullArguments_ThrowsArgumentNullException()
    {
        byte[] validKey = ScadaUtils.DefaultSecretKey;
        byte[] validIV = ScadaUtils.DefaultIV;
        byte[] validBytes = new byte[] { 1, 2, 3 };

        Assert.Throws<ArgumentNullException>(() => ScadaUtils.EncryptBytes(null!, validKey, validIV));
        Assert.Throws<ArgumentNullException>(() => ScadaUtils.EncryptBytes(validBytes, null!, validIV));
        Assert.Throws<ArgumentNullException>(() => ScadaUtils.EncryptBytes(validBytes, validKey, null!));

        Assert.Throws<ArgumentNullException>(() => ScadaUtils.DecryptBytes(null!, validKey, validIV));
        Assert.Throws<ArgumentNullException>(() => ScadaUtils.DecryptBytes(validBytes, null!, validIV));
        Assert.Throws<ArgumentNullException>(() => ScadaUtils.DecryptBytes(validBytes, validKey, null!));
    }
}
