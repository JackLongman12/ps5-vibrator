namespace DualSenseStudio.Core.Device;

/// <summary>
/// Raw report I/O for one open DualSense. The protocol and engine use only this, so the same code
/// can run over HidSharp (Windows), Android USB host, or an emulation of the protocol.
/// Reports include the report ID in byte 0.
/// </summary>
public interface IControllerTransport : IDisposable
{
    /// <summary>A stable description of the device (path, serial, ...). For display and logs.</summary>
    string Path { get; }

    int ProductId { get; }

    /// <summary>True when reports use the Bluetooth framing (input 0x31, output 0x31 with CRC).</summary>
    bool IsBluetooth { get; }

    int MaxInputReportLength { get; }
    int MaxOutputReportLength { get; }
    int MaxFeatureReportLength { get; }

    /// <summary>
    /// Blocks until an input report arrives or about a second passes.
    /// Returns the byte count, 0 on timeout. Throws when the device is gone.
    /// </summary>
    int ReadInputReport(byte[] buffer);

    /// <summary>Writes one output report. Throws <see cref="TimeoutException"/> if the device is busy, other exceptions if it is gone.</summary>
    void WriteOutputReport(byte[] report);

    /// <summary>Fills <paramref name="buffer"/> with feature report <c>buffer[0]</c>. Throws if unsupported.</summary>
    void GetFeatureReport(byte[] buffer);
}

/// <summary>Finds and opens controllers for <see cref="ControllerManager"/>.</summary>
public interface IControllerTransportProvider : IDisposable
{
    /// <summary>Raised (any thread) when devices may have been added or removed.</summary>
    event Action? DevicesChanged;

    /// <summary>
    /// Opens the best available DualSense, or returns null. When a device was found but could not
    /// be opened, <paramref name="error"/> says why.
    /// </summary>
    IControllerTransport? TryOpen(out string? error);
}
