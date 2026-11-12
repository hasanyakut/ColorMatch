using ColorMatch.ColorEngine.ColorAnalysis;
using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.ColorEngine.ColorScoring;
using ColorMatch.Mobile.Models;
using SkiaSharp;

namespace ColorMatch.Mobile.Services;

/// <summary>
/// Offline demo backend: the same <see cref="IApiClient"/> contract, served from memory on the device.
/// Scoring uses the real ColorEngine, so hunts are scored exactly like on the server. Data lives only
/// while the app process is alive.
/// </summary>
public sealed class DemoApiClient : IApiClient
{
    private sealed class DemoUser
    {
        public Guid Id; public string Name = ""; public int Xp; public int Best; public int Streak; public int Posts;
    }

    private sealed class DemoPost
    {
        public Guid Id = Guid.NewGuid(); public DemoUser Author = null!; public PostType Type; public string? Caption; public string? Hex;
        public List<string> Media = []; public int Likes; public bool Liked; public DateTime At; public List<Comment> Comments = [];
    }

    private static readonly (string Name, string Hex)[] Palette =
    [
        ("Gün Batımı Turuncusu", "#F07F1A"), ("Okyanus Turkuazı", "#2BB5B0"), ("Orman Yeşili", "#4CAF50"), ("Lavanta Moru", "#7E57C2"),
        ("Mercan Pembesi", "#EC6FA5"), ("Kobalt Mavisi", "#2A56C6"), ("Hardal Sarısı", "#F4C542"), ("Kiremit Kırmızısı", "#D9392B"),
    ];

    private readonly object _gate = new();
    private readonly Random _rng = new(7);
    private readonly DemoUser _me = new() { Id = Guid.Parse("00000000-0000-0000-0000-00000000d3a0"), Name = "demo", Xp = 120, Best = 0, Streak = 1 };
    private readonly List<DemoUser> _others;
    private readonly List<DemoPost> _posts = [];
    private readonly HashSet<Guid> _friends = [];
    private readonly HashSet<Guid> _incoming = [];
    private readonly HashSet<Guid> _outgoing = [];

    public DemoApiClient()
    {
        string[] names = ["elif", "mert", "zeynep", "can", "deniz", "ayşe", "burak", "selin"];
        _others = names.Select((n, i) => new DemoUser
        {
            Id = Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}"), Name = n,
            Xp = 150 + i * 190, Best = 880 - i * 55, Streak = 1 + (i * 3) % 9, Posts = 0,
        }).ToList();
        foreach (var u in _others.Take(4)) _friends.Add(u.Id);
        _incoming.Add(_others[4].Id);

        string[] hexes = ["#F07F1A", "#2BB5B0", "#4CAF50", "#7E57C2", "#EC6FA5", "#2A56C6", "#F4C542", "#D9392B", "#7A4E2D", "#8A8D91", "#E9A06B", "#5FB7D4"];
        string[] captions = ["Bugünün rengi şehirde bile var.", "Sokakta buldum!", "Tam isabet.", "Bu tonu çok sevdim.", "Işık harikaydı.", "Sabah yürüyüşünden.", "Kolaj denemesi", "Neredeyse 1000!"];
        for (var i = 0; i < 24; i++)
        {
            var u = _others[i % _others.Count];
            _posts.Add(new DemoPost
            {
                Author = u, Type = i % 5 == 4 ? PostType.Collage : PostType.Photo, Caption = captions[i % captions.Length], Hex = hexes[i % hexes.Length],
                Media = [DemoImages.Make(hexes[i % hexes.Length], i)], Likes = 2 + (i * 7) % 31, At = DateTime.UtcNow.AddHours(-(i * 3 + 1)),
                Comments = i % 3 == 0 ? [new Comment(Guid.NewGuid(), Author(_others[(i + 1) % _others.Count]), "Çok güzel!", DateTime.UtcNow.AddHours(-i))] : [],
            });
            u.Posts++;
        }
    }

    public string? ToAbsoluteUrl(string? relative) => string.IsNullOrEmpty(relative) ? null : relative;

    // ---- helpers ----
    private static Author Author(DemoUser u) => new(u.Id, u.Name, null);
    private static int LevelFor(int xp) => (int)Math.Floor(Math.Sqrt(Math.Max(0, xp) / 100.0)) + 1;

    private UserProfile Profile(DemoUser u)
    {
        var l = LevelFor(u.Xp);
        return new UserProfile(u.Id, u.Name, null, u.Xp, l, 100 * (l - 1) * (l - 1), 100 * l * l, u.Posts, u.Best, u.Streak, DateTime.UtcNow.AddDays(-30), TitleFor(l));
    }

    private Post ToPost(DemoPost p) => new(p.Id, Author(p.Author), p.Type, p.Caption, p.Hex, [.. p.Media], p.Likes, p.Comments.Count, p.Liked, p.At);

    private DemoUser? Find(Guid id) => id == _me.Id ? _me : _others.FirstOrDefault(o => o.Id == id);

    private string Relation(Guid id) =>
        id == _me.Id ? "self" : _friends.Contains(id) ? "friends" : _incoming.Contains(id) ? "request_received" : _outgoing.Contains(id) ? "request_sent" : "none";

    private static T Done<T>(T v) => v;

    // ---- auth / users ----
    public Task<AuthResponse> RegisterAsync(string email, string userName, string password, CancellationToken ct = default) => LoginAsync(userName, password, ct);

    public Task<AuthResponse> LoginAsync(string emailOrUserName, string password, CancellationToken ct = default)
        => Task.FromResult(new AuthResponse("demo", DateTime.UtcNow.AddDays(30), Profile(_me)));

    public Task<UserProfile> GetMeAsync(CancellationToken ct = default) { lock (_gate) return Task.FromResult(Profile(_me)); }

    public Task<UserProfile> GetUserAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(Profile(Find(id) ?? throw new ApiException("Kullanıcı bulunamadı.", System.Net.HttpStatusCode.NotFound)));
    }

    public Task<UserProfile> UploadAvatarAsync(string filePath, CancellationToken ct = default) => GetMeAsync(ct);

    public Task<Paged<Post>> GetUserPostsAsync(Guid id, string? cursor, int pageSize = 24, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(Page(_posts.Where(p => p.Author.Id == id).OrderByDescending(p => p.At), cursor, pageSize));
    }

    private Paged<Post> Page(IEnumerable<DemoPost> source, string? cursor, int size)
    {
        var skip = int.TryParse(cursor, out var s) ? s : 0;
        var list = source.ToList();
        var items = list.Skip(skip).Take(size).Select(ToPost).ToList();
        return new Paged<Post>(items, skip + size < list.Count ? (skip + size).ToString() : null);
    }

    // ---- game: three hunts per day, same rules as the server (first valid completion pays once, bonus for all three) ----
    private const int MinScore = 500, BonusXp = 150;

    private static readonly (string Name, string Hex)[] EasyColors =
        [("Kırmızı", "#D7263D"), ("Mavi", "#1E6FD9"), ("Yeşil", "#2E9E4F"), ("Sarı", "#FFD23F"), ("Turuncu", "#F57C00"), ("Mor", "#7B3FB5")];
    private static readonly (string Name, string Hex)[] HardColors =
        [("Toz Pembe", "#D8A7A0"), ("Yosun", "#6B7B4B"), ("Çelik Mavisi", "#5F7A8F"), ("Mocha", "#8A6F5C"), ("Soluk Mor", "#9B8FB0"), ("Kil", "#B98F73")];

    private sealed class HuntState { public int Best, Attempts, Xp; public bool Done; public string? Photo; }
    private readonly Dictionary<int, HuntState> _hunts = new() { [1] = new(), [2] = new(), [3] = new() };
    private bool _bonusPaid;

    private static (int Id, string Difficulty, string Name, string Hex, int Xp)[] TodayHunts()
    {
        var day = DateOnly.FromDateTime(DateTime.Now).DayNumber;
        var e = EasyColors[day % EasyColors.Length];
        var m = Palette[day % Palette.Length];
        var h = HardColors[(day * 5) % HardColors.Length];
        return [(1, "Easy", e.Name, e.Hex, 100), (2, "Medium", m.Name, m.Hex, 200), (3, "Hard", h.Name, h.Hex, 350)];
    }

    private static string Tier(int score) => score >= 950 ? "Perfect" : score >= 850 ? "Excellent" : score >= 700 ? "Great" : score >= 500 ? "Good" : "NeedsImprovement";
    private static string TitleFor(int level) => level >= 35 ? "Color Master" : level >= 20 ? "Color Hunter" : level >= 10 ? "Color Explorer" : level >= 5 ? "Color Seeker" : "Color Beginner";

    public Task<DailyHuntsState> GetDailyHuntsAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            var hunts = TodayHunts().Select(t =>
            {
                var s = _hunts[t.Id];
                return new DailyHunt(t.Id, DateOnly.FromDateTime(DateTime.Now), t.Difficulty, t.Name, t.Hex, t.Xp, MinScore, s.Best,
                    s.Attempts > 0 ? Tier(s.Best) : null, s.Done, s.Attempts, s.Xp, s.Photo);
            }).ToList();
            var done = hunts.Count(h => h.Completed);
            var progress = new DailyProgress(done, 3, done == 3, BonusXp, _bonusPaid, hunts.Sum(h => h.XpEarned) + (_bonusPaid ? BonusXp : 0), hunts.Sum(h => h.BaseXp) + BonusXp);
            return Task.FromResult(new DailyHuntsState(DateOnly.FromDateTime(DateTime.Now), DateTime.Today.AddDays(1).ToUniversalTime(), hunts, progress));
        }
    }

    // demo week: only today can have progress (the demo keeps no history)
    public async Task<WeekData> GetWeekAsync(CancellationToken ct = default)
    {
        var done = (await GetDailyHuntsAsync(ct)).Hunts.Count(h => h.Completed);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var days = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6)).Select(x => new WeekDay(x, x == today ? done : 0, 3, x == today)).ToList();
        return new WeekData(days, done > 0 ? 1 : 0, done >= 3 ? 1 : 0);
    }

    public Task<Leaderboard> GetLeaderboardAsync(int take = 5, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var rows = _others.Select(o => (o.Id, o.Name, Score: 1700 + (o.Best % 800))).ToList();
            var played = _hunts.Values.Any(s => s.Attempts > 0);
            if (played) rows.Add((_me.Id, _me.Name, _hunts.Values.Sum(s => s.Best)));
            var ordered = rows.OrderByDescending(r => r.Score).ToList();
            var entries = ordered.Take(take).Select((r, i) => new LeaderboardEntry(i + 1, r.Id, r.Name, null, r.Score, r.Id == _me.Id)).ToList();
            int? myRank = played ? ordered.FindIndex(r => r.Id == _me.Id) + 1 : null;
            return Task.FromResult(new Leaderboard(entries, myRank, ordered.Count));
        }
    }

    // ---- demo bingo: fixed nine colours, same rules as the server ----
    private sealed class BingoCellState { public bool Done; public int Best, Attempts; }
    private readonly BingoCellState[] _bingo = Enumerable.Range(0, 9).Select(_ => new BingoCellState()).ToArray();
    private static readonly int[][] BingoLines =
        [[0, 1, 2], [3, 4, 5], [6, 7, 8], [0, 3, 6], [1, 4, 7], [2, 5, 8], [0, 4, 8], [2, 4, 6]];
    private static readonly string[] BingoLineNames = ["Üst satır", "Orta satır", "Alt satır", "Sol sütun", "Orta sütun", "Sağ sütun", "Çapraz ↘", "Çapraz ↙"];
    private static readonly (string Name, string Hex, string Diff)[] BingoColors =
    [
        ("Kırmızı", "#D7263D", "Easy"), ("Mavi", "#1E6FD9", "Easy"), ("Yeşil", "#2E9E4F", "Easy"),
        ("Mercan", "#E86C52", "Medium"), ("Lavanta", "#A790D4", "Medium"), ("Zeytin", "#7C8133", "Medium"),
        ("Toz Pembe", "#D8A7A0", "Hard"), ("Çelik Mavisi", "#5F7A8F", "Hard"), ("Kil", "#B98F73", "Hard"),
    ];
    private int _bingoBoards;

    private int[] DoneLines() => Enumerable.Range(0, 8).Where(l => BingoLines[l].All(c => _bingo[c].Done)).ToArray();

    public Task<BingoBoard> GetBingoAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            var cells = BingoColors.Select((c, i) => new BingoCell(i, "demo-" + i, c.Name, c.Hex, c.Diff, _bingo[i].Done, _bingo[i].Best, _bingo[i].Attempts, Math.Max(0, 10 - _bingo[i].Attempts))).ToList();
            var done = DoneLines();
            var lines = Enumerable.Range(0, 8).Select(l => new BingoLine("line-" + l, BingoLineNames[l], BingoLines[l].ToList(), done.Contains(l), 100)).ToList();
            return Task.FromResult(new BingoBoard(Guid.Empty, "Active", DateTime.UtcNow, cells, lines, cells.Count(c => c.Completed), done.Length, false, 100, 400, MinScore, 10, _bingoBoards));
        }
    }

    public async Task<BingoResult> SubmitBingoAttemptAsync(int cellIndex, string photoPath, double x, double y, double radius, CancellationToken ct = default)
    {
        if (cellIndex < 0 || cellIndex > 8) throw new ApiException("Kare bulunamadı.", System.Net.HttpStatusCode.NotFound);
        var color = BingoColors[cellIndex];
        var (target, found, share) = await AnalyzeAsync(photoPath, x, y, radius, color.Hex, ct);
        var match = MatchScorer.Compare(target, found, share);
        lock (_gate)
        {
            var s = _bingo[cellIndex];
            var passed = match.Score >= MinScore;
            var firstDone = passed && !s.Done;
            var previousBest = s.Best;
            var levelBefore = LevelFor(_me.Xp);
            var titleBefore = TitleFor(levelBefore);
            var linesBefore = DoneLines();
            s.Attempts++; s.Best = Math.Max(s.Best, match.Score);
            if (firstDone) s.Done = true;

            var newLines = firstDone ? DoneLines().Except(linesBefore).Select(l => new BingoLineReward("line-" + l, BingoLineNames[l], 100)).ToList() : [];
            var lineXp = newLines.Sum(l => l.Xp);
            var boardDone = firstDone && _bingo.All(c => c.Done);
            var boardXp = boardDone ? 400 : 0;
            _me.Xp += lineXp + boardXp;
            var result = new BingoResult(Guid.NewGuid(), cellIndex, color.Name, color.Hex, found.ToHex(), Math.Round(match.DeltaE, 2), match.Score, Tier(match.Score), passed, MinScore,
                firstDone, s.Best, match.Score > previousBest, _bingo.Count(c => c.Done), DoneLines().Length, newLines, lineXp, boardDone, boardXp,
                _me.Xp, LevelFor(_me.Xp), LevelFor(_me.Xp) > levelBefore, TitleFor(LevelFor(_me.Xp)), TitleFor(LevelFor(_me.Xp)) != titleBefore);
            if (boardDone) { _bingoBoards++; foreach (var c in _bingo) { c.Done = false; c.Best = 0; c.Attempts = 0; } }
            return result;
        }
    }

    // ---- demo duels: the chosen friend accepts at once and has a fixed best score; XP rules mirror the server (win 200 / draw 100 / played 50) ----
    private sealed class DemoDuel
    {
        public Guid Id = Guid.NewGuid(); public Guid Other; public string Hex = "", ColorName = ""; public List<int> Scores = [];
        public bool Done; public int OtherBest; public DateTime Created = DateTime.UtcNow; public string Status = "Active"; public string? Outcome; public int Xp;
    }
    private readonly List<DemoDuel> _duels = [];

    private Duel ToDuel(DemoDuel d)
    {
        var over = d.Status != "Active";
        var left = Math.Max(0, 3 - d.Scores.Count);
        return new Duel(d.Id, d.Status, "challenger", d.Other, Find(d.Other)?.Name ?? "Arkadaş", null, d.Hex, d.ColorName, d.Created, d.Created.AddHours(24), 3,
            d.Scores.ToList(), left, d.Scores.Count > 0 ? d.Scores.Max() : null, d.Done, true, 3, over ? d.OtherBest : null, over ? d.Outcome : null, d.Xp,
            !d.Done && left > 0 && !over, false, 200, 100, 50);
    }

    private void Resolve(DemoDuel d)
    {
        d.Done = true; d.Status = "Completed";
        var best = d.Scores.Count > 0 ? d.Scores.Max() : 0;
        d.Outcome = best > d.OtherBest ? "win" : best == d.OtherBest ? "draw" : "lose";
        d.Xp = d.Scores.Count == 0 ? 0 : d.Outcome == "win" ? 200 : d.Outcome == "draw" ? 100 : 50;
        _me.Xp += d.Xp;
    }

    public Task<DuelList> GetDuelsAsync(CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(new DuelList(_duels.OrderByDescending(d => d.Created).Select(ToDuel).ToList(), _duels.Count(d => d.Status == "Active"), 5, 200, 100, 50));
    }

    public Task<Duel> CreateDuelAsync(Guid opponentId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_friends.Contains(opponentId)) throw new ApiException("Yalnızca arkadaşlarına meydan okuyabilirsin.", System.Net.HttpStatusCode.Forbidden);
            if (_duels.Any(d => d.Other == opponentId && d.Status == "Active")) throw new ApiException("Bu arkadaşınla zaten açık bir düello var.", System.Net.HttpStatusCode.Conflict);
            var c = BingoColors[Random.Shared.Next(BingoColors.Length)];
            var d = new DemoDuel { Other = opponentId, Hex = c.Hex, ColorName = c.Name, OtherBest = 650 + Random.Shared.Next(250) };
            _duels.Add(d);
            return Task.FromResult(ToDuel(d));
        }
    }

    public Task<Duel> AcceptDuelAsync(Guid id, CancellationToken ct = default) => throw new ApiException("Demo modunda düello davetleri otomatik kabul edilir.", System.Net.HttpStatusCode.Conflict);
    public Task<Duel> DeclineDuelAsync(Guid id, CancellationToken ct = default) => throw new ApiException("Demo modunda düello davetleri otomatik kabul edilir.", System.Net.HttpStatusCode.Conflict);

    public Task<Duel> FinishDuelAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var d = _duels.FirstOrDefault(x => x.Id == id) ?? throw new ApiException("Düello bulunamadı.", System.Net.HttpStatusCode.NotFound);
            if (d.Status == "Active") Resolve(d);
            return Task.FromResult(ToDuel(d));
        }
    }

    public async Task<DuelAttemptResult> SubmitDuelAttemptAsync(Guid id, string photoPath, double x, double y, double radius, CancellationToken ct = default)
    {
        DemoDuel d;
        lock (_gate) d = _duels.FirstOrDefault(v => v.Id == id) ?? throw new ApiException("Düello bulunamadı.", System.Net.HttpStatusCode.NotFound);
        if (d.Status != "Active" || d.Done) throw new ApiException("Bu düello bitti.", System.Net.HttpStatusCode.Conflict);
        var (target, found, share) = await AnalyzeAsync(photoPath, x, y, radius, d.Hex, ct);
        var match = MatchScorer.Compare(target, found, share);
        lock (_gate)
        {
            var previous = d.Scores.Count > 0 ? d.Scores.Max() : 0;
            var levelBefore = LevelFor(_me.Xp);
            d.Scores.Add(match.Score);
            var finished = d.Scores.Count >= 3;
            var xpBefore = _me.Xp;
            if (finished) Resolve(d);
            return new DuelAttemptResult(Guid.NewGuid(), d.Hex, found.ToHex(), Math.Round(match.DeltaE, 2), match.Score, Tier(match.Score), match.Score > previous, d.Scores.Max(),
                Math.Max(0, 3 - d.Scores.Count), finished, finished, _me.Xp - xpBefore, _me.Xp, LevelFor(_me.Xp), LevelFor(_me.Xp) > levelBefore, ToDuel(d));
        }
    }

    // ---- demo atlas: generated colours, one new colour per first completion ----
    private static readonly string[] AtlasNames = ["Orman", "Okyanus", "Gün Batımı", "Çöl", "Neon"];
    private static readonly string[] AtlasKeys = ["forest", "ocean", "sunset", "desert", "neon"];
    private static readonly double[] AtlasHue = [120, 200, 20, 40, 300];
    private readonly HashSet<string> _atlasUnlocked = [];

    private static string AtlasHex(int collection, int i)
    {
        var c = Color.FromHsla(((AtlasHue[collection] + i * 7) % 360) / 360.0, 0.55 + (i % 4) * 0.08, 0.35 + (i % 5) * 0.08);
        return $"#{(int)Math.Round(c.Red * 255):X2}{(int)Math.Round(c.Green * 255):X2}{(int)Math.Round(c.Blue * 255):X2}";
    }

    public Task<AtlasData> GetAtlasAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            var cols = AtlasKeys.Select((k, ci) =>
            {
                var colors = Enumerable.Range(0, 20).Select(i =>
                {
                    var key = $"{k}-{i + 1:00}";
                    var has = _atlasUnlocked.Contains(key);
                    return new AtlasColor(key, has ? $"{AtlasNames[ci]} {i + 1}" : null, has ? AtlasHex(ci, i) : null, has, has ? DateTime.UtcNow : null, has ? 800 : null, false, i % 3 == 0 ? "Easy" : i % 3 == 1 ? "Medium" : "Hard");
                }).ToList();
                var n = colors.Count(x => x.Unlocked);
                return new AtlasCollection(k, AtlasNames[ci], "Demo koleksiyonu", n, 20, n == 20, false, 500, colors);
            }).ToList();
            return Task.FromResult(new AtlasData(cols.Sum(c => c.Unlocked), 100, cols));
        }
    }

    private AtlasUnlock? DemoUnlock(int huntId)
    {
        var ci = (huntId + DateTime.Now.DayOfYear) % AtlasKeys.Length;
        for (var i = 0; i < 20; i++)
        {
            var key = $"{AtlasKeys[ci]}-{i + 1:00}";
            if (!_atlasUnlocked.Add(key)) continue;
            var n = _atlasUnlocked.Count(x => x.StartsWith(AtlasKeys[ci] + "-"));
            return new AtlasUnlock(key, $"{AtlasNames[ci]} {i + 1}", AtlasHex(ci, i), AtlasKeys[ci], AtlasNames[ci], n, 20, n == 20, 0);
        }
        return null;
    }

    public Task<List<PastChallenge>> GetRecentAsync(int days = 6, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var list = Enumerable.Range(1, days).Select(i =>
        {
            var d = today.AddDays(-i);
            var p = Palette[((d.DayNumber % Palette.Length) + Palette.Length) % Palette.Length];
            return new PastChallenge(d, p.Name, p.Hex, i % 2 == 0 ? 640 + i * 40 : 0);
        }).ToList();
        return Task.FromResult(list);
    }

    private static Task<(Rgb Target, Rgb Found, double Share)> AnalyzeAsync(string photoPath, double x, double y, double radius, string targetHex, CancellationToken ct)
        => Task.Run(() =>
        {
            // like the server: the region is found automatically, whatever the caller points at
            using var sampler = ImageService.OpenSampler(photoPath);
            var target = Rgb.FromHex(targetHex);
            var region = sampler.FindBest(target);
            var pixels = sampler.SampleCircle(region.X, region.Y, region.Radius);
            if (pixels.Count < 9) throw new ApiException("Fotoğraf analiz için çok küçük. Daha büyük bir fotoğraf çek.");
            return (target, RegionColorAnalyzer.Analyze(pixels).Color, region.ColorShare);
        }, ct);

    public async Task<DailyHuntResult> SubmitDailyHuntAttemptAsync(int huntId, string photoPath, double x, double y, double radius, CancellationToken ct = default)
    {
        var hunt = TodayHunts().FirstOrDefault(t => t.Id == huntId);
        if (hunt.Id == 0) throw new ApiException("Av bulunamadı.", System.Net.HttpStatusCode.NotFound);

        var (target, found, share) = await AnalyzeAsync(photoPath, x, y, radius, hunt.Hex, ct);

        var match = MatchScorer.Compare(target, found, share);
        lock (_gate)
        {
            var s = _hunts[huntId];
            var passed = match.Score >= MinScore;
            var first = passed && !s.Done;
            var levelBefore = LevelFor(_me.Xp);
            var titleBefore = TitleFor(levelBefore);
            var isNew = match.Score > s.Best;

            s.Attempts++; s.Best = Math.Max(s.Best, match.Score);
            if (isNew) s.Photo = photoPath;
            var award = 0;
            if (first) { s.Done = true; award = hunt.Xp; s.Xp = award; _me.Xp += award; }

            var streakExtended = false;
            if (passed && _me.Streak < 1) { _me.Streak = 1; streakExtended = true; }

            var done = _hunts.Values.Count(v => v.Done);
            var bonus = 0;
            if (first && done == 3 && !_bonusPaid) { _bonusPaid = true; bonus = BonusXp; _me.Xp += bonus; }
            _me.Best = Math.Max(_me.Best, match.Score);

            var atlas = first ? DemoUnlock(huntId) : null;
            var level = LevelFor(_me.Xp);
            return new DailyHuntResult(Guid.NewGuid(), huntId, hunt.Difficulty, hunt.Hex, found.ToHex(), Math.Round(match.DeltaE, 2), match.Score, Tier(match.Score), passed, MinScore,
                award, first, s.Best, isNew, bonus, done, done == 3, _me.Xp, level, level > levelBefore, TitleFor(level), TitleFor(level) != titleBefore,
                _me.Streak, streakExtended, photoPath, photoPath, atlas);
        }
    }

    // ---- social ----
    public Task<MediaUpload> UploadMediaAsync(string filePath, CancellationToken ct = default) => Task.FromResult(new MediaUpload(filePath, filePath));

    public Task<Post> CreatePostAsync(PostType type, string? caption, string? colorHex, IEnumerable<string> mediaKeys, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var p = new DemoPost { Author = _me, Type = type, Caption = caption, Hex = colorHex, Media = mediaKeys.ToList(), At = DateTime.UtcNow };
            _posts.Add(p); _me.Posts++;
            return Task.FromResult(ToPost(p));
        }
    }

    public Task<Paged<Post>> GetFeedAsync(string? cursor, int pageSize = 10, string scope = "all", string? color = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IEnumerable<DemoPost> q = _posts.OrderByDescending(p => p.At);
            if (scope == "friends") q = q.Where(p => p.Author.Id == _me.Id || _friends.Contains(p.Author.Id));
            if (!string.IsNullOrEmpty(color)) q = q.Where(p => ColorFamilies.Classify(p.Hex) == color);
            return Task.FromResult(Page(q, cursor, pageSize));
        }
    }

    public Task<List<ColorFacet>> GetColorFacetsAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            var counts = _posts.GroupBy(p => ColorFamilies.Classify(p.Hex)).Where(g => g.Key is not null).ToDictionary(g => g.Key!, g => g.Count());
            var list = ColorFamilies.All.Where(f => counts.ContainsKey(f.Name)).Select(f => new ColorFacet(f.Name, f.Hex, counts[f.Name]))
                .OrderByDescending(f => f.Count).ToList();
            return Task.FromResult(list);
        }
    }

    // ---- friends ----
    private Friend ToFriend(Guid id) { var u = Find(id)!; return new Friend(u.Id, u.Name, null, LevelFor(u.Xp), DateTime.UtcNow.AddDays(-9)); }

    public Task<List<Friend>> GetFriendsAsync(CancellationToken ct = default) { lock (_gate) return Task.FromResult(_friends.Select(ToFriend).ToList()); }

    public Task<FriendRequests> GetFriendRequestsAsync(CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(new FriendRequests(_incoming.Select(ToFriend).ToList(), _outgoing.Select(ToFriend).ToList()));
    }

    public Task<List<UserSearchResult>> SearchUsersAsync(string query, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var q = query.Trim();
            var list = _others.Where(o => o.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Select(o => new UserSearchResult(o.Id, o.Name, null, LevelFor(o.Xp), Relation(o.Id))).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<RelationshipResult> GetRelationshipAsync(Guid userId, CancellationToken ct = default) { lock (_gate) return Task.FromResult(new RelationshipResult(Relation(userId))); }

    public Task<RelationshipResult> SendFriendRequestAsync(Guid userId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_incoming.Remove(userId)) _friends.Add(userId); else if (!_friends.Contains(userId)) _outgoing.Add(userId);
            return Task.FromResult(new RelationshipResult(Relation(userId)));
        }
    }

    public Task<RelationshipResult> AcceptFriendRequestAsync(Guid userId, CancellationToken ct = default)
    {
        lock (_gate) { if (_incoming.Remove(userId)) _friends.Add(userId); return Task.FromResult(new RelationshipResult(Relation(userId))); }
    }

    public Task<RelationshipResult> RemoveFriendAsync(Guid userId, CancellationToken ct = default)
    {
        lock (_gate) { _friends.Remove(userId); _incoming.Remove(userId); _outgoing.Remove(userId); return Task.FromResult(new RelationshipResult(Relation(userId))); }
    }

    public Task<Post> GetPostAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(ToPost(_posts.FirstOrDefault(p => p.Id == id) ?? throw new ApiException("Gönderi bulunamadı.", System.Net.HttpStatusCode.NotFound)));
    }

    public Task<LikeState> SetLikeAsync(Guid postId, bool liked, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var p = _posts.First(p => p.Id == postId);
            if (liked && !p.Liked) { p.Liked = true; p.Likes++; }
            else if (!liked && p.Liked) { p.Liked = false; p.Likes--; }
            return Task.FromResult(new LikeState(p.Liked, p.Likes));
        }
    }

    public Task<Paged<Comment>> GetCommentsAsync(Guid postId, string? cursor, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(new Paged<Comment>([.. _posts.First(p => p.Id == postId).Comments.OrderBy(c => c.CreatedAtUtc)], null));
    }

    public Task<Comment> AddCommentAsync(Guid postId, string text, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var c = new Comment(Guid.NewGuid(), Author(_me), text.Trim(), DateTime.UtcNow);
            _posts.First(p => p.Id == postId).Comments.Add(c);
            return Task.FromResult(c);
        }
    }

    public Task DeletePostAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate) { if (_posts.RemoveAll(p => p.Id == id && p.Author.Id == _me.Id) > 0) _me.Posts--; }
        return Task.CompletedTask;
    }
}

/// <summary>Routes every call to the offline demo backend or the real HTTP client, depending on <see cref="AppSettings.DemoMode"/>.</summary>
public sealed class SwitchableApiClient(ApiClient real, DemoApiClient demo, AppSettings settings) : IApiClient
{
    private IApiClient Cur => settings.DemoMode ? demo : real;

    public Task<AuthResponse> RegisterAsync(string email, string userName, string password, CancellationToken ct = default) => Cur.RegisterAsync(email, userName, password, ct);
    public Task<AuthResponse> LoginAsync(string emailOrUserName, string password, CancellationToken ct = default) => Cur.LoginAsync(emailOrUserName, password, ct);
    public Task<UserProfile> GetMeAsync(CancellationToken ct = default) => Cur.GetMeAsync(ct);
    public Task<UserProfile> GetUserAsync(Guid id, CancellationToken ct = default) => Cur.GetUserAsync(id, ct);
    public Task<UserProfile> UploadAvatarAsync(string filePath, CancellationToken ct = default) => Cur.UploadAvatarAsync(filePath, ct);
    public Task<Paged<Post>> GetUserPostsAsync(Guid id, string? cursor, int pageSize = 24, CancellationToken ct = default) => Cur.GetUserPostsAsync(id, cursor, pageSize, ct);
    public Task<DailyHuntsState> GetDailyHuntsAsync(CancellationToken ct = default) => Cur.GetDailyHuntsAsync(ct);
    public Task<Leaderboard> GetLeaderboardAsync(int take = 5, CancellationToken ct = default) => Cur.GetLeaderboardAsync(take, ct);
    public Task<WeekData> GetWeekAsync(CancellationToken ct = default) => Cur.GetWeekAsync(ct);
    public Task<AtlasData> GetAtlasAsync(CancellationToken ct = default) => Cur.GetAtlasAsync(ct);
    public Task<BingoBoard> GetBingoAsync(CancellationToken ct = default) => Cur.GetBingoAsync(ct);
    public Task<BingoResult> SubmitBingoAttemptAsync(int cellIndex, string photoPath, double x, double y, double radius, CancellationToken ct = default) => Cur.SubmitBingoAttemptAsync(cellIndex, photoPath, x, y, radius, ct);
    public Task<DuelList> GetDuelsAsync(CancellationToken ct = default) => Cur.GetDuelsAsync(ct);
    public Task<Duel> CreateDuelAsync(Guid opponentId, CancellationToken ct = default) => Cur.CreateDuelAsync(opponentId, ct);
    public Task<Duel> AcceptDuelAsync(Guid id, CancellationToken ct = default) => Cur.AcceptDuelAsync(id, ct);
    public Task<Duel> DeclineDuelAsync(Guid id, CancellationToken ct = default) => Cur.DeclineDuelAsync(id, ct);
    public Task<Duel> FinishDuelAsync(Guid id, CancellationToken ct = default) => Cur.FinishDuelAsync(id, ct);
    public Task<DuelAttemptResult> SubmitDuelAttemptAsync(Guid id, string photoPath, double x, double y, double radius, CancellationToken ct = default) => Cur.SubmitDuelAttemptAsync(id, photoPath, x, y, radius, ct);
    public Task<List<PastChallenge>> GetRecentAsync(int days = 6, CancellationToken ct = default) => Cur.GetRecentAsync(days, ct);
    public Task<DailyHuntResult> SubmitDailyHuntAttemptAsync(int huntId, string photoPath, double x, double y, double radius, CancellationToken ct = default) => Cur.SubmitDailyHuntAttemptAsync(huntId, photoPath, x, y, radius, ct);
    public Task<MediaUpload> UploadMediaAsync(string filePath, CancellationToken ct = default) => Cur.UploadMediaAsync(filePath, ct);
    public Task<Post> CreatePostAsync(PostType type, string? caption, string? colorHex, IEnumerable<string> mediaKeys, CancellationToken ct = default) => Cur.CreatePostAsync(type, caption, colorHex, mediaKeys, ct);
    public Task<Paged<Post>> GetFeedAsync(string? cursor, int pageSize = 10, string scope = "all", string? color = null, CancellationToken ct = default) => Cur.GetFeedAsync(cursor, pageSize, scope, color, ct);
    public Task<List<ColorFacet>> GetColorFacetsAsync(CancellationToken ct = default) => Cur.GetColorFacetsAsync(ct);
    public Task<List<Friend>> GetFriendsAsync(CancellationToken ct = default) => Cur.GetFriendsAsync(ct);
    public Task<FriendRequests> GetFriendRequestsAsync(CancellationToken ct = default) => Cur.GetFriendRequestsAsync(ct);
    public Task<List<UserSearchResult>> SearchUsersAsync(string query, CancellationToken ct = default) => Cur.SearchUsersAsync(query, ct);
    public Task<RelationshipResult> GetRelationshipAsync(Guid userId, CancellationToken ct = default) => Cur.GetRelationshipAsync(userId, ct);
    public Task<RelationshipResult> SendFriendRequestAsync(Guid userId, CancellationToken ct = default) => Cur.SendFriendRequestAsync(userId, ct);
    public Task<RelationshipResult> AcceptFriendRequestAsync(Guid userId, CancellationToken ct = default) => Cur.AcceptFriendRequestAsync(userId, ct);
    public Task<RelationshipResult> RemoveFriendAsync(Guid userId, CancellationToken ct = default) => Cur.RemoveFriendAsync(userId, ct);
    public Task<Post> GetPostAsync(Guid id, CancellationToken ct = default) => Cur.GetPostAsync(id, ct);
    public Task<LikeState> SetLikeAsync(Guid postId, bool liked, CancellationToken ct = default) => Cur.SetLikeAsync(postId, liked, ct);
    public Task<Paged<Comment>> GetCommentsAsync(Guid postId, string? cursor, CancellationToken ct = default) => Cur.GetCommentsAsync(postId, cursor, ct);
    public Task<Comment> AddCommentAsync(Guid postId, string text, CancellationToken ct = default) => Cur.AddCommentAsync(postId, text, ct);
    public Task DeletePostAsync(Guid id, CancellationToken ct = default) => Cur.DeletePostAsync(id, ct);
    public string? ToAbsoluteUrl(string? relative) => Cur.ToAbsoluteUrl(relative);
}

/// <summary>Draws the sample photos of the offline demo (soft colour compositions) into the cache folder.</summary>
internal static class DemoImages
{
    public static string Make(string hex, int variant)
    {
        var dir = Path.Combine(FileSystem.CacheDirectory, "demo");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{hex.TrimStart('#')}_{variant % 4}.jpg");
        if (File.Exists(path)) return path;

        var rgb = Rgb.FromHex(hex);
        var baseColor = new SKColor(rgb.R, rgb.G, rgb.B);
        SKColor Mix(SKColor c, float t, byte target) => new(
            (byte)(c.Red + (target - c.Red) * t), (byte)(c.Green + (target - c.Green) * t), (byte)(c.Blue + (target - c.Blue) * t));

        using var surface = SKSurface.Create(new SKImageInfo(720, 900));
        var canvas = surface.Canvas;
        using (var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(720, 900),
                [Mix(baseColor, 0.35f, 255), baseColor, Mix(baseColor, 0.35f, 0)], [0f, 0.55f, 1f], SKShaderTileMode.Clamp),
            IsAntialias = true,
        }) canvas.DrawRect(0, 0, 720, 900, paint);

        var rnd = new Random(variant * 31 + rgb.R);
        for (var i = 0; i < 4; i++)
        {
            using var blob = new SKPaint { Color = Mix(baseColor, 0.25f + 0.12f * i, i % 2 == 0 ? (byte)255 : (byte)0).WithAlpha(110), IsAntialias = true };
            canvas.DrawCircle(rnd.Next(80, 640), rnd.Next(120, 780), rnd.Next(110, 240), blob);
        }

        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 85);
        using (var fs = File.Create(path)) data.SaveTo(fs);
        return path;
    }
}
