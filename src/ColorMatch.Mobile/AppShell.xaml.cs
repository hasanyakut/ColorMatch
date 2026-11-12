using ColorMatch.Mobile.Views;

namespace ColorMatch.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("result", typeof(ResultPage));
        Routing.RegisterRoute("post", typeof(PostDetailPage));
        Routing.RegisterRoute("profile", typeof(ProfilePage));
        Routing.RegisterRoute("friends", typeof(FriendsPage));
        Routing.RegisterRoute("bingo", typeof(BingoPage));
        Routing.RegisterRoute("duels", typeof(DuelsPage));
    }
}
