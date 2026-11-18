namespace DualSenseStudio.Core.Protocol;

[Flags]
public enum Buttons : uint
{
    None = 0,
    Square = 1 << 0,
    Cross = 1 << 1,
    Circle = 1 << 2,
    Triangle = 1 << 3,
    L1 = 1 << 4,
    R1 = 1 << 5,
    L2 = 1 << 6,
    R2 = 1 << 7,
    Create = 1 << 8,
    Options = 1 << 9,
    L3 = 1 << 10,
    R3 = 1 << 11,
    PS = 1 << 12,
    Touchpad = 1 << 13,
    Mute = 1 << 14,
    DPadUp = 1 << 15,
    DPadRight = 1 << 16,
    DPadDown = 1 << 17,
    DPadLeft = 1 << 18,
}

public readonly record struct TouchPoint(bool Active, byte Id, int X, int Y)
{
    public const int MaxX = 1919;
    public const int MaxY = 1079;
}

/// <summary>Snapshot of everything the controller reports.</summary>
public sealed class InputState
{
    public static readonly InputState Empty = new();

    public byte LeftX = 128, LeftY = 128, RightX = 128, RightY = 128;
    public byte L2, R2;
    public Buttons Buttons;

    public short GyroX, GyroY, GyroZ;
    public short AccelX, AccelY, AccelZ;

    public TouchPoint Touch0, Touch1;

    public int BatteryPercent;
    public bool Charging;
    public bool BatteryFull;

    /// <summary>False while a Bluetooth controller is still sending its reduced "simple" report.</summary>
    public bool FullReport;

    public bool IsPressed(Buttons b) => (Buttons & b) == b;

    public float L2Value => L2 / 255f;
    public float R2Value => R2 / 255f;

    public InputState Clone() => (InputState)MemberwiseClone();
}
