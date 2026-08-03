using System.IO.Ports;
using System.Runtime.CompilerServices;

namespace AbisEdge.Scales;

/// <summary>Reads a real scale over an RS-232 COM port via
/// <see cref="SerialPort"/> — the modern replacement for the legacy <c>da</c>
/// object's ~29 <c>WSC32.DLL</c> <c>Sio*</c> calls. Select it with
/// <c>Edge:Scale:Provider=Serial</c> and set the port/baud. Each line the scale
/// emits is handed to <see cref="WeightParser"/>.</summary>
public sealed class SerialScale : IScale
{
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
            if (sp.IsOpen) sp.Close();
        }
    }
}
