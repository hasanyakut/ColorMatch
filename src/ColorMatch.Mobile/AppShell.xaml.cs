using ColorMatch.Mobile.Views;

namespace ColorMatch.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        // the one navigation bar lives above the shell: built once, never recreated when a page changes
        Loaded += (_, _) => { Controls.NavBar.Attach(this); _ = WarmUpAsync(); };
        Unloaded += (_, _) => Controls.NavBar.Detach();
        Routing.RegisterRoute("result", typeof(ResultPage));
        Routing.RegisterRoute("post", typeof(PostDetailPage));
        Routing.RegisterRoute("profile", typeof(ProfilePage));
        Routing.RegisterRoute("friends", typeof(FriendsPage));
        Routing.RegisterRoute("bingo", typeof(BingoPage));
        Routing.RegisterRoute("duels", typeof(DuelsPage));
    }

    /// <summary>
    /// Builds the other tab pages while the app is idle (one at a time). Shell creates a tab page the first time it is opened, which took several
    /// hundred milliseconds of blank screen on the first visit; now that cost is paid in the background right after start-up.
    /// </summary>
    private async Task WarmUpAsync()
    {
        await Task.Delay(2500);
        foreach (var content in Items.SelectMany(i => i.Items).SelectMany(s => s.Items))
        {
            try { ((IShellContentController)content).GetOrCreateContent(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("warm-up: " + ex.Message); }
            await Task.Delay(350);
        }
    }
}
