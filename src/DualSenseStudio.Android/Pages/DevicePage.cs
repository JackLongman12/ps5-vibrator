using Android.App;
using Android.Views;
using Android.Widget;
using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Core.Protocol;
using DualSenseStudio.Droid.Ui;
using U = DualSenseStudio.Droid.Ui.Ui;

namespace DualSenseStudio.Droid.Pages;

/// <summary>Connection details, an input monitor and safety settings.</summary>
public sealed class DevicePage(Activity activity) : Page(activity)
{
    private TextView _conn = null!, _input = null!, _features = null!;
    private ChipGroup<RumbleMode> _rumble = null!;

    public override string Title => "Device";

    protected override View Build()
    {
        var c = Activity;
        var col = U.Column(c);
        col.AddView(U.Heading(c, "Device"));

        var conn = U.Card(c, col);
        conn.AddView(U.Text(c, "Connection", 15, U.Text1, bold: true));
        _conn = U.Text(c, "", 13, U.Muted);
        conn.AddView(_conn);
        _features = U.Text(c, "", 13, U.Text1);
        _features.SetPadding(0, U.Dp(c, 8), 0, U.Dp(c, 8));
        conn.AddView(_features);
        var buttons = U.Flow(c);
        buttons.AddView(U.Button(c, "Reconnect", () =>
        {
            var cur = AppHost.Controllers.Current;
            if (cur != null) AppHost.Controllers.ReportFailure(cur);
            AppHost.Controllers.TryConnect();
        }));
        buttons.AddView(U.Button(c, "Ask for USB access", AppHost.Transports.RequestUsbPermissionAgain));
        conn.AddView(buttons);

        var mon = U.Card(c, col);
        mon.AddView(U.Text(c, "Input monitor", 15, U.Text1, bold: true));
        _input = U.Text(c, "", 12, U.Muted);
        _input.Typeface = Android.Graphics.Typeface.Monospace;
        mon.AddView(_input);

        var safety = U.Card(c, col);
        safety.AddView(U.Text(c, "Safety", 15, U.Text1, bold: true));
        safety.AddView(new SliderRow(c, "Maximum intensity", 0.1, 1, 0.05, AppHost.Settings.MasterCap, v => $"{v * 100:0}%", v =>
        {
            AppHost.Settings.MasterCap = v;
            AppHost.Engine.MasterCap = v;
            AppHost.Settings.Save();
        }));
        safety.AddView(U.Help(c, "Caps every source (presets, recorder, live play). The Stop button in the player bar and the controller's mute button stop everything."));

        var rumble = U.Card(c, col);
        rumble.AddView(U.Text(c, "Rumble mode (USB)", 15, U.Text1, bold: true));
        _rumble = new ChipGroup<RumbleMode>(c, [("Auto", RumbleMode.Auto), ("v2 (strong)", RumbleMode.V2), ("Legacy v1", RumbleMode.Legacy)],
            AppHost.Settings.RumbleMode, m =>
            {
                AppHost.Settings.RumbleMode = m;
                AppHost.Engine.RumbleMode = m;
                AppHost.Settings.Save();
            });
        rumble.AddView(_rumble);
        rumble.AddView(U.Help(c, "Auto picks v2 on firmware that supports it. Over Bluetooth Android's driver handles rumble itself, so this has no effect."));

        var data = U.Card(c, col);
        data.AddView(U.Text(c, "Storage", 15, U.Text1, bold: true));
        data.AddView(U.Help(c, $"Patterns are saved as *.dspattern.json in app storage:\n{AppHost.Store.Directory}\nUse Library → Import / Export to move them to and from the Windows app."));
        return col;
    }

    public override void Refresh()
    {
        var cur = AppHost.Controllers.Current;
        if (cur == null)
        {
            _conn.Text = AppHost.Controllers.LastError ?? "No controller. Plug in the DualSense with a USB-C OTG cable, or pair it over Bluetooth in Android settings.";
            _features.Text = "";
            return;
        }
        _conn.Text = $"{cur.DevicePath}\n{(cur.IsEdge ? "DualSense Edge" : "DualSense")} · {cur.FirmwareText} · rumble {(cur.SupportsRumbleV2 ? "v2" : "v1")}";
        _features.Text = AppHost.Kind switch
        {
            ControllerKind.Usb => "USB: full support. Rumble, adaptive triggers, lightbar, player LEDs and all input (touchpad, motion).",
            ControllerKind.Bluetooth =>
                "Bluetooth (Android's controller driver):\n" +
                $"• Rumble: {(AppHost.Transports.BluetoothTransport?.HasRumble == true ? "yes" : "no (needs Android 12+)")}\n" +
                $"• Lightbar colour: {(AppHost.Transports.BluetoothTransport?.HasLightbar == true ? "yes" : "no (needs Android 12+)")}\n" +
                "• Trigger pressure, sticks, buttons: yes\n" +
                $"• Motion: {(AppHost.HasMotion ? "experimental" : "no")}\n" +
                "• Adaptive triggers, touchpad, player LEDs, mute LED: not available. Use USB.",
            _ => "",
        };
    }

    private long _lastCount = -1;

    public override void Tick()
    {
        var cur = AppHost.Controllers.Current;
        var s = AppHost.Engine.Input;
        long count = cur?.ReportCount ?? 0;
        if (count == _lastCount && cur != null) return;
        _lastCount = count;
        _input.Text =
            $"L stick  {s.LeftX,3} {s.LeftY,3}    R stick  {s.RightX,3} {s.RightY,3}\n" +
            $"L2 {s.L2,3}   R2 {s.R2,3}\n" +
            $"buttons  {(s.Buttons == Buttons.None ? "-" : s.Buttons.ToString())}\n" +
            $"touch    {(s.Touch0.Active ? $"{s.Touch0.X},{s.Touch0.Y}" : "-")}  {(s.Touch1.Active ? $"{s.Touch1.X},{s.Touch1.Y}" : "-")}\n" +
            $"accel    {s.AccelX} {s.AccelY} {s.AccelZ}\n" +
            $"gyro     {s.GyroX} {s.GyroY} {s.GyroZ}\n" +
            $"battery  {s.BatteryPercent}%{(s.Charging ? " charging" : "")}{(s.BatteryFull ? " full" : "")}\n" +
            $"reports  {count}";
    }
}
