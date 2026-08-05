using System.Text;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// A <c>.ps1</c> containing non-ASCII must carry a UTF-8 BOM.
///
/// <para><b>Why this is a real failure and not a style rule.</b> Windows PowerShell 5.1 — the shell
/// these tools actually run in — decodes a BOM-less script as the system ANSI code page, not UTF-8.
/// A UTF-8 em-dash is the three bytes <c>E2 80 94</c>, and in cp1252 the last of those is U+201D, a
/// RIGHT DOUBLE QUOTATION MARK. PowerShell accepts smart quotes as string delimiters, so an em-dash
/// inside a double-quoted string <b>closes the string</b> and everything after it parses as code.</para>
///
/// <para><c>tools/discover_parity_enums.ps1</c> was in exactly that state: 18 parse errors, unrunnable,
/// from one em-dash in a comment-like status description. It failed at PARSE time, so no amount of
/// care at the call site would have helped, and nothing pointed at the character — the reported error
/// was an unexpected <c>)</c> nine lines later.</para>
///
/// <para>The fix is the BOM, not banning the character: these files are correct UTF-8 and the prose in
/// them is worth keeping. This test checks the one thing that makes them readable by the shell that
/// runs them.</para>
/// </summary>
public sealed class PowerShellToolEncodingTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void Every_script_with_non_ascii_carries_a_utf8_bom()
    {
        var scripts = Directory.GetFiles(Path.Combine(RepoRoot(), "tools"), "*.ps1", SearchOption.AllDirectories);
        Assert.NotEmpty(scripts);

        var offenders = new List<string>();
        foreach (var path in scripts)
        {
            var bytes = File.ReadAllBytes(path);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            if (hasBom) continue;

            // Only non-ASCII is at risk: a pure-ASCII script decodes identically either way.
            if (bytes.Any(b => b > 0x7F))
                offenders.Add(Path.GetFileName(path));
        }

        Assert.True(offenders.Count == 0,
            "These scripts contain non-ASCII but no UTF-8 BOM, so Windows PowerShell 5.1 will decode " +
            "them as cp1252 — an em-dash becomes a smart quote and terminates the enclosing string: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void The_scripts_are_valid_utf8_in_the_first_place()
    {
        // The BOM only helps if what follows really is UTF-8. A file saved as cp1252 would pass the
        // test above by having no high bytes at all, or fail confusingly by carrying a BOM over
        // mis-encoded content.
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        foreach (var path in Directory.GetFiles(Path.Combine(RepoRoot(), "tools"), "*.ps1", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(path);
            var body = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
                ? bytes[3..] : bytes;
            var ex = Record.Exception(() => strict.GetString(body));
            Assert.True(ex is null, $"{Path.GetFileName(path)} is not valid UTF-8: {ex?.Message}");
        }
    }
}
