using System.Runtime.InteropServices;
using System.Text;

namespace TerminalQuest.Settings
{
    /// <summary>
    /// Keeps API keys out of <c>settings.json</c> in recoverable form.
    /// </summary>
    /// <remarks>
    /// On Windows the key is sealed with DPAPI under the current user account, so the file is
    /// useless to anyone who copies it elsewhere. Anywhere else (or when DPAPI fails) the key is
    /// stored base64-encoded — obfuscation, not protection — and the file's own permissions are
    /// the only guard. A key set through its <c>TQ2_*</c> environment variable is never written
    /// anywhere. Nothing here throws: the worst case is an empty key, which the probe reports.
    /// </remarks>
    internal static class ApiKeyProtection
    {
        private const string DpapiPrefix = "dpapi:";
        private const string PlainPrefix = "plain:";

        /// <summary>Seals plaintext for storage. Empty stays empty.</summary>
        public static string Protect(string? plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
            {
                return string.Empty;
            }

            var bytes = Encoding.UTF8.GetBytes(plaintext);
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    return DpapiPrefix + Convert.ToBase64String(ProtectDpapi(bytes));
                }
                catch
                {
                    // Fall through to the obfuscated form below.
                }
            }

            return PlainPrefix + Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Recovers storage form to plaintext. Legacy plaintext (from before protection, or
        /// hand-edited) is returned as-is so it keeps working and is sealed on the next save.
        /// </summary>
        public static string Unprotect(string? stored)
        {
            if (string.IsNullOrEmpty(stored))
            {
                return string.Empty;
            }

            try
            {
                if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        return string.Empty;
                    }

                    try
                    {
                        return Encoding.UTF8.GetString(UnprotectDpapi(Convert.FromBase64String(stored[DpapiPrefix.Length..])));
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }

                if (stored.StartsWith(PlainPrefix, StringComparison.Ordinal))
                {
                    return Encoding.UTF8.GetString(Convert.FromBase64String(stored[PlainPrefix.Length..]));
                }

                return stored;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Whether the stored form is already sealed or obfuscated rather than raw text.</summary>
        public static bool LooksProtected(string? stored) =>
            stored?.StartsWith(DpapiPrefix, StringComparison.Ordinal) == true ||
            stored?.StartsWith(PlainPrefix, StringComparison.Ordinal) == true;

        [StructLayout(LayoutKind.Sequential)]
        private struct Blob
        {
            public int cbData;
            public nint pbData;
        }

        private static byte[] ProtectDpapi(byte[] plaintext)
        {
            var input = ToBlob(plaintext);
            try
            {
                if (!CryptProtectData(ref input, nint.Zero, nint.Zero, nint.Zero, nint.Zero, 0x1 /* CRYPTPROTECT_UI_FORBIDDEN */, out var output))
                {
                    throw new InvalidOperationException("DPAPI protect failed.");
                }

                try
                {
                    var sealed_ = new byte[output.cbData];
                    Marshal.Copy(output.pbData, sealed_, 0, output.cbData);
                    return sealed_;
                }
                finally
                {
                    LocalFree(output.pbData);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(input.pbData);
            }
        }

        private static byte[] UnprotectDpapi(byte[] sealed_)
        {
            var input = ToBlob(sealed_);
            try
            {
                if (!CryptUnprotectData(ref input, nint.Zero, nint.Zero, nint.Zero, nint.Zero, 0x1 /* CRYPTPROTECT_UI_FORBIDDEN */, out var output))
                {
                    throw new InvalidOperationException("DPAPI unprotect failed.");
                }

                try
                {
                    var plaintext = new byte[output.cbData];
                    Marshal.Copy(output.pbData, plaintext, 0, output.cbData);
                    return plaintext;
                }
                finally
                {
                    LocalFree(output.pbData);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(input.pbData);
            }
        }

        private static Blob ToBlob(byte[] bytes)
        {
            var handle = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, handle, bytes.Length);
            return new Blob { cbData = bytes.Length, pbData = handle };
        }

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref Blob pDataIn, nint szDataDescr, nint pOptionalEntropy,
            nint pvReserved, nint pPromptStruct, uint dwFlags, out Blob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref Blob pDataIn, nint szDataDescr, nint pOptionalEntropy,
            nint pvReserved, nint pPromptStruct, uint dwFlags, out Blob pDataOut);

        [DllImport("kernel32.dll")]
        private static extern nint LocalFree(nint hMem);
    }
}
