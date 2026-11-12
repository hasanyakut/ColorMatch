using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;

namespace ColorMatch.Mobile.Platforms.AndroidImpl;

/// <summary>
/// Shell renderer whose tab switches are instant. By default Shell plays an Android fragment transition when moving
/// between tabs (the new page drifts in), which reads as a stutter. Tab pages now appear immediately and show a
/// skeleton while their data loads; pushed pages (post, result, friends) keep the normal platform transition.
/// </summary>
public sealed class InstantTabShellRenderer : ShellRenderer
{
    protected override IShellItemRenderer CreateShellItemRenderer(ShellItem shellItem) => new InstantTabItemRenderer(this);

    private sealed class InstantTabItemRenderer(IShellContext context) : ShellItemRenderer(context)
    {
        protected override Task<bool> HandleFragmentUpdate(ShellNavigationSource navSource, ShellSection shellSection, Page page, bool animated)
            => navSource == ShellNavigationSource.ShellSectionChanged
                ? base.HandleFragmentUpdate(navSource, shellSection, page, false)
                : base.HandleFragmentUpdate(navSource, shellSection, page, animated);
    }
}
