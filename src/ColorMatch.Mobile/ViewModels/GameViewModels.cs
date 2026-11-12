using System.Collections.ObjectModel;
using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.Models;
using ColorMatch.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ColorMatch.Mobile.ViewModels;

public enum CameraIssue { None, PermissionDenied, Unavailable }

public partial class HuntViewModel(IGameService game, IApiClient api, ImageService images, ISessionService session) : BaseViewModel
{
    public const double MinRadius = 0.02, MaxRadius = 0.2;

    private PhotoSampler? _sampler;

    /// <summary>The hunt being played (one of today's three).</summary>
    [ObservableProperty] private DailyHunt? hunt;
    public ObservableCollection<HuntTaskItem> Tasks { get; } = [];
    public HuntTaskItem? EasyTask => Tasks.FirstOrDefault(t => t.Level == 1);
    public HuntTaskItem? MediumTask => Tasks.FirstOrDefault(t => t.Level == 2);
    public HuntTaskItem? HardTask => Tasks.FirstOrDefault(t => t.Level == 3);
    public HuntTaskItem? SelectedTask => Tasks.FirstOrDefault(t => t.Id == Hunt?.Id);
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPhoto)), NotifyPropertyChangedFor(nameof(NoPhoto)), NotifyPropertyChangedFor(nameof(ShowNavBar))] private string? photoPath;
    [ObservableProperty] private double photoAspect = 1;
    [ObservableProperty] private double selX = 0.5;
    [ObservableProperty] private double selY = 0.5;
    [ObservableProperty] private double selRadius = 0.07;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPreview))] private string? previewHex;
    [ObservableProperty] private int previewScore;
    /// <summary>What to look for to get closer ("Daha açık ve daha sarımsı bir ton ara."); empty when the match is already good.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasHint))] private string hintText = "";
    public bool HasHint => HintText.Length > 0;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanCheck))] private bool isAnalyzing;
    [ObservableProperty] private bool isPreparing;

    // ---- bingo mode: the same capture screen plays one square of the bingo board ----
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowTabs)), NotifyPropertyChangedFor(nameof(PageTitle)), NotifyPropertyChangedFor(nameof(PillText)), NotifyPropertyChangedFor(nameof(RewardText)), NotifyPropertyChangedFor(nameof(StatusLine))]
    private bool isBingo;
    // ---- duel mode: the same capture screen plays one friend duel ----
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowTabs)), NotifyPropertyChangedFor(nameof(PageTitle)), NotifyPropertyChangedFor(nameof(PillText)), NotifyPropertyChangedFor(nameof(RewardText)), NotifyPropertyChangedFor(nameof(StatusLine))]
    private bool isDuel;
    private Guid _duelId;
    private Duel? _duel;
    private int _bingoCell;
    private BingoCell? _bingoCellData;
    private BingoBoard? _bingoBoard;
    public bool ShowTabs => ShowStage && !IsBingo && !IsDuel;
    partial void OnCameraIssueChanged(CameraIssue value) => OnPropertyChanged(nameof(ShowTabs));
    public string PageTitle => IsDuel ? "Düello" : IsBingo ? "Bingo karesi" : "Günün avları";
    public string PillText => IsDuel ? $"Düello · {_duel?.OtherUserName}" : IsBingo ? $"Bingo · {_bingoCell + 1}. kare" : SelectedTask is { } t ? $"{t.DifficultyText} av" : "";
    public string RewardText => IsDuel ? $"Kazanana +{_duel?.WinXp ?? 200} XP" : IsBingo ? $"Çizgi +{_bingoBoard?.LineXp ?? 100} XP" : SelectedTask?.XpText ?? "";
    public string StatusLine => IsDuel ? DuelStatusLine() : IsBingo ? BingoStatus() : SelectedTask?.StatusText ?? "";
    private string DuelStatusLine()
        => _duel is not { } d ? "" : d.MyBest is { } b ? $"En iyin {b} · {d.MyAttemptsLeft} hak kaldı · rakip skoru gizli" : $"{d.MyAttemptsLeft} hak · rakip skoru gizli";
    private string BingoStatus()
    {
        if (_bingoCellData is not { } c || _bingoBoard is not { } b) return "";
        return c.Completed ? $"Tamamlandı · en iyi {c.BestScore}"
             : c.Attempts > 0 ? $"En iyi {c.BestScore} · {b.MinScore}+ gerekli · {c.AttemptsLeft} hak kaldı"
             : $"{b.MinScore}+ puan gerekli";
    }

    // ---- screen states beyond the happy path ----
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsPermissionDenied)), NotifyPropertyChangedFor(nameof(IsCameraUnavailable)), NotifyPropertyChangedFor(nameof(HasCameraIssue)), NotifyPropertyChangedFor(nameof(ShowStage))]
    private CameraIssue cameraIssue;
    /// <summary>The server refused the chosen region (too small / unusable): the photo stays, the user moves the marker.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(InvalidSample)), NotifyPropertyChangedFor(nameof(CanCheck))] private bool serverRejectedSample;
    [ObservableProperty] private bool uploadFailed;
    [ObservableProperty] private string? failureText;

    public bool IsPermissionDenied => CameraIssue == CameraIssue.PermissionDenied;
    public bool IsCameraUnavailable => CameraIssue == CameraIssue.Unavailable;
    public bool HasCameraIssue => CameraIssue != CameraIssue.None;
    public bool ShowStage => !HasCameraIssue;

    public IReadOnlyList<ToneItem> Tones => ColorTones.For(Hunt?.Hex);
    partial void OnHuntChanged(DailyHunt? value)
    {
        foreach (var t in Tasks) t.IsSelected = t.Id == value?.Id;
        foreach (var n in new[] { nameof(Tones), nameof(ChallengeLoading), nameof(SelectedTask), nameof(PillText), nameof(RewardText), nameof(StatusLine) }) OnPropertyChanged(n);
    }
    /// <summary>The hunts have not arrived yet: show a placeholder instead of an empty card.</summary>
    public bool ChallengeLoading => Hunt is null && !HasError;

    public bool HasPhoto => PhotoPath is not null;
    public bool NoPhoto => PhotoPath is null;
    public bool HasPreview => !string.IsNullOrEmpty(PreviewHex);
    /// <summary>The marker sits on pixels that cannot be read as a colour (outside / too small), locally or according to the server.</summary>
    public bool InvalidSample => HasPhoto && !IsPreparing && (!HasPreview || ServerRejectedSample);
    public bool CanCheck => HasPreview && !ServerRejectedSample && !IsAnalyzing;
    public string PreviewScoreText => HasPreview ? PreviewScore.ToString() : "–";
    public string AnalyzeText => IsAnalyzing ? "Analiz ediliyor…" : "Eşleşmeyi kontrol et";

    partial void OnPreviewHexChanged(string? value)
    {
        OnPropertyChanged(nameof(InvalidSample)); OnPropertyChanged(nameof(CanCheck)); OnPropertyChanged(nameof(PreviewScoreText));
    }
    partial void OnPreviewScoreChanged(int value) => OnPropertyChanged(nameof(PreviewScoreText));

    [RelayCommand]
    private async Task BackAsync()
    {
        if (HasPhoto) { Reset(); return; }
        if (IsDuel)
        {
            IsDuel = false; Hunt = null;
            await Shell.Current.GoToAsync("//home");
            await Shell.Current.GoToAsync("duels");
            return;
        }
        if (IsBingo)
        {
            IsBingo = false; Hunt = null;
            await Shell.Current.GoToAsync("//home");
            await Shell.Current.GoToAsync("bingo");
            return;
        }
        TabNav.From = TabNav.Index; TabNav.Direction = -1;
        await Shell.Current.GoToAsync("//home");
    }

    [RelayCommand] private void OpenSettings() => AppInfo.Current.ShowSettingsUI();
    [RelayCommand] private void DismissFailure() { UploadFailed = false; FailureText = null; }

    public async Task LoadAsync()
    {
        if (TabNav.AutoOpenCamera)
        {
            TabNav.AutoOpenCamera = false;
            MainThread.BeginInvokeOnMainThread(async () => { await Task.Delay(450); if (!HasPhoto && !IsCameraOpen) await TakePhotoAsync(); });
        }
        if (TabNav.ExitBingo) { TabNav.ExitBingo = false; if (IsBingo || IsDuel) { IsBingo = false; IsDuel = false; Hunt = null; if (HasPhoto) Reset(); } }
        if (game.PendingDuelId is { } duelId) { game.PendingDuelId = null; IsDuel = true; IsBingo = false; _duelId = duelId; if (HasPhoto) Reset(); }
        else if (game.PendingBingoCell is { } cell) { game.PendingBingoCell = null; IsBingo = true; IsDuel = false; _bingoCell = cell; if (HasPhoto) Reset(); }
        else if (game.PendingHuntId is not null && (IsBingo || IsDuel)) { IsBingo = false; IsDuel = false; Hunt = null; }
        if (IsDuel) { await LoadDuelAsync(); return; }
        if (IsBingo) { await LoadBingoAsync(); return; }

        try
        {
            var state = await game.GetHuntsAsync(force: game.PendingHuntId is not null);
            foreach (var h in state.Hunts)
            {
                var existing = Tasks.FirstOrDefault(t => t.Id == h.Id);
                if (existing is null) Tasks.Add(new HuntTaskItem(h)); else existing.Update(h);
            }
            for (var i = Tasks.Count - 1; i >= 0; i--)
                if (state.Hunts.All(h => h.Id != Tasks[i].Id)) Tasks.RemoveAt(i);

            // the hunt tapped on Home wins; otherwise keep the current one, else the first unfinished, else the first
            var wanted = game.PendingHuntId ?? Hunt?.Id;
            game.PendingHuntId = null;
            var pick = state.Hunts.FirstOrDefault(h => h.Id == wanted) ?? state.Hunts.FirstOrDefault(h => !h.Completed) ?? state.Hunts.FirstOrDefault();
            if (pick is not null) { Hunt = pick; if (HasPhoto) UpdatePreview(); }
            foreach (var n in new[] { nameof(EasyTask), nameof(MediumTask), nameof(HardTask), nameof(SelectedTask) }) OnPropertyChanged(n);
            ErrorMessage = null;
        }
        catch (ApiException ex) { if (Hunt is null) ErrorMessage = ex.Message; }
    }

    private async Task LoadDuelAsync()
    {
        try
        {
            var list = await api.GetDuelsAsync();
            var d = list.Duels.FirstOrDefault(x => x.Id == _duelId);
            if (d is null || !d.CanPlay || d.TargetHex is null)
            {
                // finished, expired or gone: fall back to the normal hunts
                IsDuel = false; Hunt = null; _duel = null;
                await LoadAsync();
                return;
            }
            _duel = d;
            Hunt = new DailyHunt(-1, DateOnly.FromDateTime(DateTime.Now), "Medium", d.TargetName ?? "Düello", d.TargetHex, d.WinXp, 0, d.MyBest ?? 0, null, false, d.MyScores.Count, 0, null);
            if (HasPhoto) UpdatePreview();
            foreach (var n in new[] { nameof(PillText), nameof(RewardText), nameof(StatusLine) }) OnPropertyChanged(n);
            ErrorMessage = null;
        }
        catch (ApiException ex) { if (Hunt is null) ErrorMessage = ex.Message; }
    }

    /// <summary>The duel attempt in the shape the result page already shows (nothing is claimed about the opponent: their score stays hidden).</summary>
    private DailyHuntResult ToHuntResult(DuelAttemptResult r)
        => new(r.AttemptId, -1, "Düello", r.TargetHex, r.FoundHex, r.DeltaE, r.Score, r.Tier, true, 0, r.XpAwarded, false, r.MyBest, r.IsNewBest, 0, 0, false,
            r.TotalXp, r.Level, r.LeveledUp, session.User?.Title ?? "", false, session.User?.StreakDays ?? 0, false, "", "", null, null, r, r.Region);

    private async Task LoadBingoAsync()
    {
        try
        {
            _bingoBoard = await api.GetBingoAsync();
            var c = _bingoBoard.Cells.FirstOrDefault(x => x.Index == _bingoCell);
            if (c is null) { IsBingo = false; Hunt = null; return; }
            _bingoCellData = c;
            Hunt = new DailyHunt(-1, DateOnly.FromDateTime(DateTime.Now), c.Difficulty, c.Name, c.Hex, _bingoBoard.LineXp, _bingoBoard.MinScore, c.BestScore, null, c.Completed, c.Attempts, 0, null);
            if (HasPhoto) UpdatePreview();
            foreach (var n in new[] { nameof(PillText), nameof(RewardText), nameof(StatusLine) }) OnPropertyChanged(n);
            ErrorMessage = null;
        }
        catch (ApiException ex) { if (Hunt is null) ErrorMessage = ex.Message; }
    }

    /// <summary>The bingo result in the shape the result page already shows.</summary>
    private DailyHuntResult ToHuntResult(BingoResult b)
        => new(b.AttemptId, -1, "Bingo", b.TargetHex, b.FoundHex, b.DeltaE, b.Score, b.Tier, b.Passed, b.MinScore,
            b.LineXpAwarded, b.CellCompleted, b.BestScore, b.IsNewBest, b.BoardXpAwarded, b.CompletedCells, b.BoardCompleted,
            b.TotalXp, b.Level, b.LeveledUp, b.Title, b.TitleChanged, session.User?.StreakDays ?? 0, false, "", "", b.AtlasUnlock, b, null, b.Region);

    /// <summary>Switches to another of today's hunts (the photo, if any, is kept and re-scored against the new colour).</summary>
    [RelayCommand]
    private void SelectHunt(HuntTaskItem? item)
    {
        if (item is null || IsAnalyzing) return;
        Hunt = item.Data;
        ServerRejectedSample = false; ErrorMessage = null;
        if (HasPhoto) UpdatePreview();
    }

    /// <summary>True while the in-app live camera covers the screen.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowNavBar))] private bool isCameraOpen;
    /// <summary>The navigation bar belongs to the "see the target" step only: not while a photo is being marked and not under the live camera.</summary>
    public bool ShowNavBar => NoPhoto && !IsCameraOpen;

    [RelayCommand]
    private async Task TakePhotoAsync()
    {
        ErrorMessage = null;
        CameraIssue = CameraIssue.None;
        try
        {
            // ask up-front so a refusal becomes a proper screen state instead of a generic error
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted) status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted) { CameraIssue = CameraIssue.PermissionDenied; return; }

            IsCameraOpen = true;   // the live camera (Controls/LiveCamera) takes over; it reports back through the handlers below
        }
        catch (PermissionException) { CameraIssue = CameraIssue.PermissionDenied; }
        catch (FeatureNotSupportedException) { CameraIssue = CameraIssue.Unavailable; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); ErrorMessage = "Fotoğraf çekilemedi. Lütfen tekrar dene."; }
    }

    /// <summary>A photo came from the live camera: close it and continue with the usual flow (best spot is found automatically).</summary>
    public async Task OnCameraCapturedAsync(string path)
    {
        IsCameraOpen = false;
        try { await OpenAsync(path); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); ErrorMessage = "Fotoğraf açılamadı. Lütfen tekrar dene."; }
    }

    public void CloseCamera() => IsCameraOpen = false;

    /// <summary>"Yeniden çek": back to the live camera in one tap (no detour through the target card).</summary>
    [RelayCommand]
    private async Task RetakeAgainAsync()
    {
        Reset();
        await TakePhotoAsync();
    }

    /// <summary>The live camera could not start (no camera, driver problem): fall back to the system camera app.</summary>
    public async Task OnCameraFailedAsync()
    {
        IsCameraOpen = false;
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported) { CameraIssue = CameraIssue.Unavailable; return; }
            var photo = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions { Title = "Rengi bul" });
            if (photo is not null) await OpenAsync(photo.FullPath);
        }
        catch (PermissionException) { CameraIssue = CameraIssue.PermissionDenied; }
        catch (FeatureNotSupportedException) { CameraIssue = CameraIssue.Unavailable; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); ErrorMessage = "Fotoğraf çekilemedi. Lütfen tekrar dene."; }
    }

    [RelayCommand]
    private async Task PickPhotoAsync()
    {
        ErrorMessage = null;
        CameraIssue = CameraIssue.None;
        try
        {
            var photo = (await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { Title = "Bir fotoğraf seç", SelectionLimit = 1 }))?.FirstOrDefault();
            if (photo is not null) await OpenAsync(photo.FullPath);
        }
        catch (PermissionException) { ErrorMessage = "Fotoğraflara erişim izni verilmedi."; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); ErrorMessage = "Fotoğraf açılamadı."; }
    }

    private async Task OpenAsync(string path)
    {
        try
        {
            IsPreparing = true;
            var prepared = await images.PrepareAsync(path);
            _sampler?.Dispose();
            _sampler = ImageService.OpenSampler(prepared.Path);
            PhotoAspect = prepared.Width / (double)prepared.Height;
            UpdatePreview();   // finds the spot first, so the mark appears right on it
            PhotoPath = prepared.Path;
        }
        finally { IsPreparing = false; }
    }

    private void UpdatePreview()
    {
        if (_sampler is null || Hunt is null) return;
        var target = Rgb.FromHex(Hunt.Hex);
        // the spot is found automatically and cannot be moved: it is the best match in the whole photo
        var (preview, region) = _sampler.Preview(target);
        SelX = region.X; SelY = region.Y; SelRadius = region.Radius;
        PreviewHex = preview?.Found.ToHex();
        PreviewScore = preview?.Score ?? 0;
        HintText = preview is null ? "" : ColorHints.Describe(Hunt.Hex, PreviewHex, PreviewScore, preview.Share);
    }

    [RelayCommand]
    private void Retake() => Reset();

    public void Reset()
    {
        _sampler?.Dispose();
        _sampler = null;
        PhotoPath = null;
        PreviewHex = null;
        ErrorMessage = null;
        ServerRejectedSample = false; UploadFailed = false; FailureText = null;
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (PhotoPath is null || Hunt is null || IsAnalyzing) return;
        var hunt = Hunt;
        try
        {
            IsAnalyzing = true;
            OnPropertyChanged(nameof(AnalyzeText));
            ErrorMessage = null; UploadFailed = false; FailureText = null; ServerRejectedSample = false;
            var result = IsDuel
                ? ToHuntResult(await api.SubmitDuelAttemptAsync(_duelId, PhotoPath, SelX, SelY, SelRadius))
                : IsBingo
                ? ToHuntResult(await api.SubmitBingoAttemptAsync(_bingoCell, PhotoPath, SelX, SelY, SelRadius))
                : await api.SubmitDailyHuntAttemptAsync(hunt.Id, PhotoPath, SelX, SelY, SelRadius);

            game.Invalidate();
            if (session.User is { } u)
                session.UpdateUser(u with { TotalXp = result.TotalXp, Level = result.Level, BestEverScore = Math.Max(u.BestEverScore, result.Score), StreakDays = Math.Max(u.StreakDays, result.CurrentStreak), Title = result.Title });

            var localPhoto = PhotoPath;
            var name = hunt.Name;
            var x = SelX; var y = SelY; var r = SelRadius; var aspect = PhotoAspect;
            Reset();
            await Shell.Current.GoToAsync("result", new ShellNavigationQueryParameters
            {
                ["Result"] = result, ["LocalPhoto"] = localPhoto!, ["ColorName"] = name, ["Region"] = (result.Region is { } g ? (g.X, g.Y, g.Radius) : (x, y, r)), ["Aspect"] = aspect
            });
        }
        // the server could not use the chosen region: keep the photo, let the user move the marker
        catch (ApiException ex) when (ex.Errors?.Keys.Any(k => k.Contains("region", StringComparison.OrdinalIgnoreCase)) == true)
        {
            ServerRejectedSample = true;
        }
        // the server said no on purpose (photo already used, no attempts left, hunt over): retrying the same upload cannot help
        catch (ApiException ex) when (ex.Status is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.NotFound)
        {
            ErrorMessage = ex.Status switch
            {
                _ when IsDuel && ex.Message.Contains("photo", StringComparison.OrdinalIgnoreCase) => "Bu fotoğraf bu düelloda zaten kullanıldı. Yeni bir fotoğraf çek.",
                System.Net.HttpStatusCode.TooManyRequests when IsDuel => "Bu düellodaki deneme hakkın bitti.",
                _ when IsDuel => "Bu düello artık oynanamıyor (süre dolmuş ya da bitirmiş olabilirsin).",
                System.Net.HttpStatusCode.TooManyRequests => "Bu av için bugünkü deneme hakkın bitti.",
                System.Net.HttpStatusCode.NotFound => "Bu av bulunamadı. Listeyi yeniledik, tekrar seç.",
                _ when ex.Message.Contains("photo", StringComparison.OrdinalIgnoreCase) => "Bu fotoğraf başka bir avı tamamlamak için zaten kullanıldı. Yeni bir fotoğraf çek.",
                _ => "Bu av artık geçerli değil. Listeyi yeniledik.",
            };
            game.Invalidate();
            await LoadAsync();
        }
        // network / server / upload problem: dedicated retry state, the photo and marker are kept
        catch (ApiException ex) { UploadFailed = true; FailureText = ex.Message; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); UploadFailed = true; FailureText = "Analiz sırasında beklenmeyen bir hata oluştu."; }
        finally { IsAnalyzing = false; OnPropertyChanged(nameof(AnalyzeText)); }
    }
}

public partial class ResultViewModel(IPostPublisher publisher) : BaseViewModel, IQueryAttributable
{
    [ObservableProperty] private DailyHuntResult? result;
    [ObservableProperty] private string colorName = "";
    [ObservableProperty] private string? localPhoto;
    [ObservableProperty] private string? infoMessage;
    [ObservableProperty] private bool published;

    public string TargetHex => Result?.TargetHex ?? "#CCCCCC";
    public string FoundHex => Result?.FoundHex ?? "#CCCCCC";
    public int Score => Result?.Score ?? 0;
    public string ScoreOutOf => "/ 1000";
    public int TotalXpAwarded => (Result?.XpAwarded ?? 0) + (Result?.BonusXpAwarded ?? 0) + (Result?.AtlasUnlock?.CompletionXpAwarded ?? 0);
    public bool HasAtlasUnlock => Result?.AtlasUnlock is not null;
    public string AtlasHex => Result?.AtlasUnlock?.Hex ?? "#888888";
    public string AtlasTitle => Result?.AtlasUnlock is { } u ? $"Atlas'a eklendi: {u.Name}" : "";
    public string AtlasDetail => Result?.AtlasUnlock is { } u
        ? (u.CollectionCompleted ? $"{u.CollectionName} koleksiyonu tamamlandı" + (u.CompletionXpAwarded > 0 ? $" · +{u.CompletionXpAwarded} XP" : "") : $"{u.CollectionName} · {u.CollectionUnlocked} / {u.CollectionTotal}")
        : "";
    [RelayCommand]
    private async Task OpenAtlasAsync()
    {
        AtlasNav.FocusCollection = Result?.AtlasUnlock?.CollectionKey;
        await Shell.Current.GoToAsync("//atlas");
    }
    public string XpText => Result is null ? "" : TotalXpAwarded > 0 ? $"+{TotalXpAwarded} XP"
        : Result.Duel is { } du ? (du.DuelResolved ? "Düello bitti" : "Deneme kaydedildi")
        : Result.Bingo is { CellCompleted: true } ? "Kare tamamlandı"
        : Result.Passed ? "Ek XP yok" : "XP kazanılmadı";
    public bool EarnedXp => TotalXpAwarded > 0;
    public string BonusText => Result switch
    {
        { Bingo: { } b } => string.Join("\n", b.NewLines.Select(l => $"Çizgi: {l.Name} +{l.Xp} XP").Concat(b.BoardCompleted ? [$"Bingo tahtası tamamlandı +{b.BoardXpAwarded} XP"] : [])),
        { BonusXpAwarded: > 0 } r => $"Günlük bonus +{r.BonusXpAwarded} XP · üç av da tamamlandı",
        _ => "",
    };
    public bool HasBonus => Result is { Bingo: { } b } ? b.NewLines.Count > 0 || b.BoardCompleted : Result?.BonusXpAwarded > 0;
    public bool HasBingo => Result?.Bingo is not null;
    public bool HasDuel => Result?.Duel is not null;
    public string Headline => Result?.Tier switch
    {
        "Perfect" => "Kusursuz eşleşme",
        "Excellent" => "Mükemmel eşleşme",
        "Great" => "Harika eşleşme",
        "Good" => "İyi deneme",
        _ => "Biraz daha yakın bir renk dene",
    };
    // the verdict is never colour-coded alone: every tier also has its own icon and wording
    public string VerdictIcon => Result?.Tier switch { "Perfect" => "star", "Excellent" => "award", "Great" => "check", "Good" => "target", _ => "refresh" };
    public bool IsHighScore => Result?.Tier is "Perfect" or "Excellent";
    public string StatusText => Result is null ? "" : Result.Duel is { } du ? (du.DuelResolved ? du.Duel.Outcome switch { "win" => "Düelloyu kazandın!", "lose" => "Düelloyu kaybettin", "draw" => "Berabere", _ => "Düello bitti" } : $"{du.AttemptsLeft} deneme hakkın kaldı") : Result.FirstCompletion && Result.Bingo is not null ? "Kare tamamlandı" : Result.Bingo is not null && Result.Passed ? "Bu kare zaten tamamlanmıştı" : Result.FirstCompletion ? "Av tamamlandı" : Result.Passed ? "Bu av zaten tamamlanmıştı" : $"Tamamlamak için {Result.MinScore}+ gerekli";
    /// <summary>Advice for the next try, from the difference between target and found colour.</summary>
    public string HintText => Result is null ? "" : ColorHints.Describe(Result.TargetHex, Result.FoundHex, Result.Score, Result.Region?.Share ?? 1);
    public bool HasHint => HintText.Length > 0;
    public string StatusIcon => Result is { Passed: true } ? "check" : "target";
    public bool StatusIsGood => Result?.Passed == true;
    public string DailyText => Result is null ? "" : Result.Duel is { } du ? (du.DuelResolved ? $"Sen {du.Duel.MyBest} · rakip {du.Duel.OtherBest}" : "Rakibin skoru düello bitene kadar gizli") : Result.Bingo is { } b ? $"Bingo: {b.CompletedCells} / 9 kare · {b.CompletedLines} / 8 çizgi" : $"Günlük av: {Result.CompletedCount} / 3 tamamlandı";
    public string LevelUpText => Result is { LeveledUp: true } r ? $"Seviye atladın: {r.Level}" + (r.TitleChanged ? $" · yeni unvan: {r.Title}" : "") : "";
    public bool LeveledUp => Result?.LeveledUp == true;
    public string Detail => Result is null ? "" : $"Renk farkı ΔE00 {Result.DeltaE.ToString("0.0", new System.Globalization.CultureInfo("tr-TR"))} · Görselin %{Math.Round((Result.Region?.Share ?? 1) * 100)}'i bu renk · En iyin {Result.BestScore}";

    // where on the photo the colour was sampled (normalised), so the result can show it
    [ObservableProperty] private double regionX = 0.5;
    [ObservableProperty] private double regionY = 0.5;
    [ObservableProperty] private double regionRadius = 0.07;
    [ObservableProperty] private double photoAspect = 1;
    public bool HasPhoto => LocalPhoto is not null && File.Exists(LocalPhoto);
    public string DeltaText => Result is null ? "" : $"ΔE {Result.DeltaE.ToString("0.0", new System.Globalization.CultureInfo("tr-TR"))}";
    public string LevelText => Result is null ? "" : $"Seviye {Result.Level} · {Result.TotalXp} XP";
    public bool IsNewBest => Result is { IsNewBest: true, Passed: true };
    public bool CanPublish => !Published && Result is not null && Result.Duel is null && !string.IsNullOrEmpty(Result.PhotoKey);

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Result", out var r) && r is DailyHuntResult hr) Result = hr;
        if (query.TryGetValue("LocalPhoto", out var p) && p is string path) LocalPhoto = path;
        if (query.TryGetValue("ColorName", out var n) && n is string name) ColorName = name;
        if (query.TryGetValue("Region", out var reg) && reg is ValueTuple<double, double, double> t) { RegionX = t.Item1; RegionY = t.Item2; RegionRadius = t.Item3; }
        if (query.TryGetValue("Aspect", out var asp) && asp is double a && a > 0) PhotoAspect = a;
        foreach (var prop in new[] { nameof(CaptionSubtitle), nameof(VerdictIcon), nameof(IsHighScore), nameof(TotalXpAwarded), nameof(HasBingo), nameof(HasDuel), nameof(HintText), nameof(HasHint), nameof(HasAtlasUnlock), nameof(AtlasHex), nameof(AtlasTitle), nameof(AtlasDetail), nameof(BonusText), nameof(HasBonus), nameof(StatusText), nameof(StatusIcon), nameof(StatusIsGood), nameof(DailyText), nameof(LevelUpText), nameof(LeveledUp), nameof(HasPhoto), nameof(TargetHex), nameof(FoundHex), nameof(Score), nameof(XpText), nameof(EarnedXp), nameof(Headline), nameof(Detail), nameof(DeltaText), nameof(LevelText), nameof(IsNewBest), nameof(CanPublish) })
            OnPropertyChanged(prop);
    }

    // the note is asked for in an in-app bottom sheet (CaptionSheet), not a native dialog
    [ObservableProperty] private bool isCaptionOpen;
    [ObservableProperty] private string captionText = "";
    public string CaptionSubtitle => $"{ColorName} avın için bir not ekle (isteğe bağlı)";

    [RelayCommand]
    private void Publish()
    {
        if (Result is null || IsBusy) return;
        CaptionText = "";
        IsCaptionOpen = true;
    }

    [RelayCommand] private void CancelPublish() => IsCaptionOpen = false;

    [RelayCommand]
    private async Task ConfirmPublishAsync()
    {
        if (Result is null) return;
        var caption = (CaptionText ?? "").Trim();
        IsCaptionOpen = false;
        InfoMessage = null;
        await GuardAsync(async () =>
        {
            await publisher.PublishHuntPhotoAsync(Result.PhotoKey, caption, Result.TargetHex);
            Published = true;
            OnPropertyChanged(nameof(CanPublish));
            InfoMessage = "Akışta paylaşıldı!";
        });
    }

    [RelayCommand]
    private async Task ShareExternalAsync()
    {
        if (LocalPhoto is null || !File.Exists(LocalPhoto)) { InfoMessage = "Paylaşılacak fotoğraf bulunamadı."; return; }
        // what leaves the app carries the ColorMatch strip: logo, target and found colour, score
        var file = LocalPhoto;
        if (Result is { } r)
        {
            try
            {
                file = await WatermarkComposer.ComposeAsync(LocalPhoto, ColorName, r.TargetHex, r.FoundHex, r.Score, r.DeltaE,
                    Path.Combine(FileSystem.CacheDirectory, $"colormatch-{DateTime.Now:yyyyMMdd-HHmmss}.jpg"));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("watermark: " + ex); file = LocalPhoto; }
        }
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "ColorMatch",
            File = new ShareFile(file, "image/jpeg")
        });
    }

    [RelayCommand]
    private async Task TryAgainAsync()
    {
        TabNav.AutoOpenCamera = true;   // straight back into the camera
        await Shell.Current.GoToAsync("..");
    }
    [RelayCommand] private async Task HomeAsync() => await Shell.Current.GoToAsync("//home");
    [RelayCommand]
    private async Task OpenDuelsAsync()
    {
        await Shell.Current.GoToAsync("//home");
        await Shell.Current.GoToAsync("duels");
    }
    [RelayCommand]
    private async Task OpenBingoAsync()
    {
        await Shell.Current.GoToAsync("//home");
        await Shell.Current.GoToAsync("bingo");
    }
}

public partial class ProfileViewModel(IApiClient api, ISessionService session, AppSettings settings, IAppNavigator nav, ImageService images)
    : BaseViewModel, IQueryAttributable
{
    private Guid? _userId;
    private string? _cursor;
    private bool _hasMore;

    [ObservableProperty] private UserProfile? profile;
    [ObservableProperty] private bool isOwn = true;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NoPosts))] private bool loaded;
    [ObservableProperty] private string? infoMessage;
    [ObservableProperty] private bool canGoBack;

    public ObservableCollection<PostItemViewModel> Posts { get; } = [];
    public bool NoPosts => Loaded && Posts.Count == 0;
    public string LevelText => Profile is null ? "" : $"Seviye {Profile.Level}";
    public string XpText => Profile is null ? "" : $"{Profile.TotalXp} / {Profile.NextLevelXp} XP";
    public double XpProgress => Profile is null ? 0 : Math.Clamp((double)(Profile.TotalXp - Profile.LevelStartXp) / Math.Max(1, Profile.NextLevelXp - Profile.LevelStartXp), 0, 1);
    public string? AvatarUrl => api.ToAbsoluteUrl(Profile?.AvatarUrl);
    public string ThemeText => settings.Theme switch { "light" => "Açık", "dark" => "Koyu", _ => "Sistem" };
    public string ServerUrl => settings.BaseUrl;

    /// <summary>First load only: a placeholder silhouette of the profile instead of a spinner.</summary>
    public bool ShowSkeleton => IsBusy && !Loaded;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsBusy) or nameof(Loaded)) OnPropertyChanged(nameof(ShowSkeleton));
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("UserId", out var u) && u is Guid id)
        {
            _userId = id;
            IsOwn = session.User?.Id == id;
            CanGoBack = true;
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsRefreshing = true;
        await GuardAsync(async () =>
        {
            var id = _userId ?? session.User?.Id ?? (await api.GetMeAsync()).Id;
            IsOwn = session.User?.Id == id;
            Profile = await api.GetUserAsync(id);
            if (IsOwn) session.UpdateUser(Profile);
            await LoadRelationshipAsync();
            var page = await api.GetUserPostsAsync(id, null);
            Posts.Clear();
            foreach (var p in page.Items) Posts.Add(new PostItemViewModel(p, api));
            _cursor = page.NextCursor; _hasMore = _cursor is not null;
            Loaded = true;
            RebuildVisible();
        }, showBusy: !Loaded);
        IsRefreshing = false;
        NotifyProfile();
    }

    // ---- gallery: filter, tile size, colour signature ----
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsAllFilter)), NotifyPropertyChangedFor(nameof(IsPhotoFilter)), NotifyPropertyChangedFor(nameof(IsCollageFilter))]
    private string filter = "all";
    [ObservableProperty] private double tileHeight = 150;

    public ObservableCollection<PostItemViewModel> Visible { get; } = [];
    /// <summary>Distinct colours of the person's posts: their "colour signature", drawn as a ribbon in the header.</summary>
    public ObservableCollection<string> Signature { get; } = [];

    public bool IsAllFilter => Filter == "all";
    public bool IsPhotoFilter => Filter == "photo";
    public bool IsCollageFilter => Filter == "collage";
    public string AllCountText => Posts.Count.ToString();
    public string PhotoCountText => Posts.Count(p => !p.IsCollage).ToString();
    public string CollageCountText => Posts.Count(p => p.IsCollage).ToString();
    public bool NoVisible => Loaded && Posts.Count > 0 && Visible.Count == 0;
    public bool HasSignature => Signature.Count > 0;
    public string JoinedText => Profile is null ? "" : $"Katılım {Profile.JoinedAtUtc.ToString("MMMM yyyy", new System.Globalization.CultureInfo("tr-TR"))}";
    public string TotalXpText => Profile is null ? "–" : Profile.TotalXp.ToString("N0", new System.Globalization.CultureInfo("tr-TR"));
    public string PostCountText => Profile is null ? "0" : Profile.PostCount.ToString();

    [RelayCommand] private void SetFilter(string value) { Filter = value; RebuildVisible(); }
    [RelayCommand] private async Task OpenCreateAsync() => await Shell.Current.GoToAsync("//create");

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return;
        var choice = await page.DisplayActionSheetAsync("Ayarlar", "Vazgeç", null, $"Görünüm: {ThemeText}", "Çıkış yap");
        if (choice is not null && choice.StartsWith("Görünüm")) await ChooseThemeAsync();
        else if (choice == "Çıkış yap") await SignOutAsync();
    }

    private void RebuildVisible()
    {
        Visible.Clear();
        foreach (var p in Posts.Where(p => Filter switch { "photo" => !p.IsCollage, "collage" => p.IsCollage, _ => true })) Visible.Add(p);

        Signature.Clear();
        foreach (var hex in Posts.Select(p => p.ColorHex).Where(h => !string.IsNullOrEmpty(h)).Distinct().Take(8)) Signature.Add(hex!);

        foreach (var n in new[] { nameof(AllCountText), nameof(PhotoCountText), nameof(CollageCountText), nameof(NoVisible), nameof(HasSignature), nameof(NoPosts) })
            OnPropertyChanged(n);
    }

    public ObservableCollection<BadgeItem> Badges { get; } = [];
    public int LevelNumber => Profile?.Level ?? 1;
    public int BadgesUnlocked => Badges.Count(b => b.Unlocked);
    public string BadgesText => $"{BadgesUnlocked} / {Badges.Count}";
    public string BestText => Profile is { BestEverScore: > 0 } p ? p.BestEverScore.ToString() : "–";
    public string StreakText => Profile is { StreakDays: > 0 } p ? $"{p.StreakDays} gün" : "–";

    /// <summary>Badges are computed from the real profile numbers (best score, streak, posts, level).</summary>
    private void RebuildBadges()
    {
        Badges.Clear();
        var p = Profile;
        if (p is null) return;
        Badges.Add(new("target", "İlk av", "İlk renk avını tamamla", p.BestEverScore > 0));
        Badges.Add(new("award", "Keskin göz", "Bir avda 900+ puan al", p.BestEverScore >= 900));
        Badges.Add(new("zap", "3 günlük seri", "3 gün üst üste oyna", p.StreakDays >= 3));
        Badges.Add(new("image", "Paylaşımcı", "İlk gönderini paylaş", p.PostCount >= 1));
        Badges.Add(new("layers", "Koleksiyoncu", "10 gönderiye ulaş", p.PostCount >= 10));
        Badges.Add(new("star", "Usta", "5. seviyeye ulaş", p.Level >= 5));
    }

    private void NotifyProfile()
    {
        RebuildBadges();
        foreach (var p in new[] { nameof(LevelText), nameof(XpText), nameof(XpProgress), nameof(AvatarUrl), nameof(NoPosts), nameof(ThemeText), nameof(ServerUrl), nameof(ShowFriendButton), nameof(LevelNumber), nameof(BadgesText), nameof(BestText), nameof(StreakText), nameof(JoinedText), nameof(TotalXpText), nameof(PostCountText) })
            OnPropertyChanged(p);
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!_hasMore || IsBusy || Profile is null) return;
        try
        {
            var page = await api.GetUserPostsAsync(Profile.Id, _cursor);
            foreach (var p in page.Items) Posts.Add(new PostItemViewModel(p, api));
            _cursor = page.NextCursor; _hasMore = _cursor is not null;
            RebuildVisible();
        }
        catch (ApiException) { }
    }

    [RelayCommand]
    private async Task OpenPostAsync(PostItemViewModel item)
        => await Shell.Current.GoToAsync("post", new ShellNavigationQueryParameters { ["Item"] = item });

    [RelayCommand]
    private async Task ChangeAvatarAsync()
    {
        if (!IsOwn) return;
        InfoMessage = null;
        try
        {
            var picked = (await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { Title = "Profil fotoğrafı", SelectionLimit = 1 }))?.FirstOrDefault();
            if (picked is null) return;
            IsBusy = true;
            var prepared = await images.PrepareAsync(picked.FullPath, 800);
            Profile = await api.UploadAvatarAsync(prepared.Path);
            session.UpdateUser(Profile);
            NotifyProfile();
        }
        catch (ApiException ex) { InfoMessage = ex.Message; }
        catch (PermissionException) { InfoMessage = "Fotoğraflara erişim izni verilmedi."; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); InfoMessage = "Fotoğraf güncellenemedi."; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ChooseThemeAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return;
        var choice = await page.DisplayActionSheetAsync("Görünüm", "Vazgeç", null, "Sistem", "Açık", "Koyu");
        settings.Theme = choice switch { "Açık" => "light", "Koyu" => "dark", "Sistem" => "system", _ => settings.Theme };
        OnPropertyChanged(nameof(ThemeText));
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await session.SignOutAsync();
        nav.ShowAuth(showOnboarding: false);
    }

    // ---- friendship with the profile owner (other users only) ----
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowFriendButton)), NotifyPropertyChangedFor(nameof(FriendButtonText)),
        NotifyPropertyChangedFor(nameof(FriendButtonEnabled)), NotifyPropertyChangedFor(nameof(FriendButtonIsAccent)), NotifyPropertyChangedFor(nameof(ShowFriendSecondary)), NotifyPropertyChangedFor(nameof(FriendSecondaryText))]
    private string relationship = "self";

    public bool ShowFriendButton => !IsOwn && Relationship != "self";
    public string FriendButtonText => Relationship switch { "none" => "Arkadaş ekle", "request_sent" => "İstek gönderildi", "request_received" => "İsteği kabul et", "friends" => "Arkadaşsınız", _ => "" };
    public bool FriendButtonEnabled => Relationship is "none" or "request_received";
    public bool FriendButtonIsAccent => FriendButtonEnabled;
    public bool ShowFriendSecondary => ShowFriendButton && Relationship is "request_sent" or "request_received" or "friends";
    public string FriendSecondaryText => Relationship switch { "request_sent" => "İsteği geri al", "request_received" => "Reddet", "friends" => "Arkadaşlıktan çıkar", _ => "" };

    private async Task LoadRelationshipAsync()
    {
        if (IsOwn || Profile is null) { Relationship = "self"; OnPropertyChanged(nameof(ShowFriendButton)); return; }
        try { Relationship = (await api.GetRelationshipAsync(Profile.Id)).Relationship; }
        catch (ApiException) { Relationship = "self"; }
        OnPropertyChanged(nameof(ShowFriendButton));
    }

    [RelayCommand]
    private async Task FriendPrimaryAsync()
    {
        if (Profile is null) return;
        try
        {
            var rel = Relationship switch
            {
                "none" => await api.SendFriendRequestAsync(Profile.Id),
                "request_received" => await api.AcceptFriendRequestAsync(Profile.Id),
                _ => null
            };
            if (rel is not null) Relationship = rel.Relationship;
            OnPropertyChanged(nameof(ShowFriendButton));
        }
        catch (ApiException ex) { InfoMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task FriendSecondaryAsync()
    {
        if (Profile is null) return;
        try
        {
            if (Relationship == "friends")
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page is null || !await page.DisplayAlertAsync("Arkadaşlıktan çıkar", $"{Profile.UserName} arkadaş listenden çıkarılsın mı?", "Çıkar", "Vazgeç")) return;
            }
            Relationship = (await api.RemoveFriendAsync(Profile.Id)).Relationship;
            OnPropertyChanged(nameof(ShowFriendButton));
        }
        catch (ApiException ex) { InfoMessage = ex.Message; }
    }

    [RelayCommand] private async Task BackAsync() => await Shell.Current.GoToAsync("..");
}

public sealed record LeaderRow(Guid UserId, int Rank, string UserName, string? AvatarUrl, int Score, bool IsMe, string RankText, double Bar);
public sealed record PastRow(string Hex, string Name, string DateText, string ScoreText, bool Played);

public sealed record BadgeItem(string Icon, string Title, string Hint, bool Unlocked);
