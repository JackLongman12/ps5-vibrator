using Android.App;
using Android.Views;
using Android.Widget;
using DualSenseStudio.Droid.Ui;

namespace DualSenseStudio.Droid.Pages;

/// <summary>One tab. Builds its view once; gets UI-thread ticks (~30 Hz) while visible.</summary>
public abstract class Page(Activity activity)
{
    private View? _root;

    protected Activity Activity { get; } = activity;
    public abstract string Title { get; }

    public View Root => _root ??= Wrap(Build());

    protected abstract View Build();

    private View Wrap(View content)
    {
        var scroll = new ScrollView(Activity) { FillViewport = true };
        content.SetPadding(Ui.Ui.Dp(Activity, 14), Ui.Ui.Dp(Activity, 12), Ui.Ui.Dp(Activity, 14), Ui.Ui.Dp(Activity, 24));
        scroll.AddView(content, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return scroll;
    }

    /// <summary>Called when the tab becomes visible and whenever the connection changes.</summary>
    public virtual void Refresh() { }

    public virtual void Tick() { }

    public virtual void OnHidden() { }

    /// <summary>Unsubscribes from process-wide events when the Activity goes away.</summary>
    public virtual void Detach() { }

    protected void Show(View v, bool visible) => v.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
}
