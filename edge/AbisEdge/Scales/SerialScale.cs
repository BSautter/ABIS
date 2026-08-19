using System.IO.Ports;
using System.Runtime.CompilerServices;

namespace AbisEdge.Scales;

/// <summary>Reads a real scale over an RS-232 COM port via
/// <see cref="SerialPort"/> — the modern replacement for the legacy <c>da</c>
/// object's ~29 <c>WSC32.DLL</c> <c>Sio*</c> calls. Select it with
/// <c>Edge:Scale:Provider=Serial</c> and set the port/baud. Each line the scale
/// emits is handed to <see cref="WeightParser"/>.
///
/// <para>Reading is PASSIVE: the port is opened and whatever the scale emits is parsed. Legacy
/// instead polls, sending <c>'b'</c> and reading the reply a second later
/// (<c>w_da_sheet.wf_read_scale</c>). Both work against a scale in continuous-output mode, and the
/// passive form is what the plant's readings have been tuned against (#338, #341), so it is left
/// alone here — <see cref="ZeroAsync"/> adds the command channel without changing how weight is
/// read.</para></summary>
public sealed class SerialScale : IScale, IZeroableScale
{
    /// <summary>The zero command byte. Legacy: <c>SioPutc(ii_com_port, 'a')</c> with the comment
    /// <c>//'a' for ZERO, 'b' for Print</c> (<c>w_da_sheet.srw:1249</c>).</summary>
    private const byte ZeroCommand = (byte)'a';

    /// <summary>The port while it is streaming. Zeroing has to go out of the SAME port the reader
    /// holds open — a second SerialPort on the same COM port is refused by the OS — so the command
    /// borrows this one. Guarded because the write happens on a request thread while the read loop
    /// is blocked in ReadLine on another.</summary>
    private SerialPort? _open;
    private readonly object _gate = new();

    private readonly string _port;
    private readonly int _baud;
    private readonly Parity _parity;
    private readonly int _dataBits;
    private readonly StopBits _stopBits;

    public SerialScale(string port, int baud = 9600, Parity parity = Parity.None,
        int dataBits = 8, StopBits stopBits = StopBits.One)
    {
        _port = port;
        _baud = baud;
        _parity = parity;
        _dataBits = dataBits;
        _stopBits = stopBits;
    }

    public string Name => $"serial-scale ({_port}@{_baud})";

    /// <summary>A real load cell on the other end of a serial port.</summary>
    public bool Simulated => false;

    public async IAsyncEnumerable<WeightReading> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        using var sp = new SerialPort(_port, _baud, _parity, _dataBits, _stopBits)
        {
            ReadTimeout = 2000,
            NewLine = "\r\n",
        };
        sp.Open();
        lock (_gate) _open = sp;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = null;
                try { line = sp.ReadLine(); }
                catch (TimeoutException) { /* idle indicator; keep polling */ }

                var reading = WeightParser.TryParse(line);
                if (reading is not null)
                    yield return reading with { At = DateTimeOffset.UtcNow };

                await Task.Yield();
            }
        }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_open, sp)) _open = null; }
            if (sp.IsOpen) sp.Close();
        }
    }

    /// <summary>
    /// Re-zero the scale: one byte, no reply expected.
    ///
    /// <para>Legacy reads nothing back either — its read-back after the zero is commented out — so
    /// there is nothing to wait for and nothing to confirm. This reports that the command was SENT,
    /// which is all the device tells anyone.</para>
    ///
    /// <para>False when the port is not currently open, which is the honest answer: the reader owns
    /// the port and only holds it while streaming, so no open port means no device to command.</para>
    /// </summary>
    public Task<bool> ZeroAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            var sp = _open;
            if (sp is null || !sp.IsOpen) return Task.FromResult(false);
            sp.Write([ZeroCommand], 0, 1);
            return Task.FromResult(true);
        }
    }
}
