# ColorMatch

Renk eşleştirme oyunu + sosyal akış. **.NET MAUI (Android öncelikli, iOS'a hazır mimari) + ASP.NET Core Web API + SQL Server.**

Kullanıcı kayıt olur → günün üç hedef rengini (kolay / orta / zor) görür → kamerayla gerçek dünyada o renge yakın bir nesne bulur →
**sunucu** fotoğrafta hedefe en yakın düz yüzeyi kendisi bulur, temsilî rengini çıkarıp CIEDE2000 ile karşılaştırır (kullanıcı bölge seçmez) →
0–1000 puan + XP + seviye → Renk Atlası'nı doldurur, Renk Bingosu'nu tamamlar, arkadaşlarıyla 24 saatlik düellolara girer →
fotoğrafı akışta paylaşır ya da renk temalı kolaj hazırlayıp Story (9:16) olarak dışarı aktarır / yayınlar.

## Çözüm yapısı

```
ColorMatch.sln
├── src/ColorMatch.Domain          Entity'ler, enum'lar, kurallar (XP, seviye, günlük renk paleti). Framework bağımlılığı yok.
├── src/ColorMatch.ColorEngine     RGB/HEX/CIELAB dönüşümleri, CIEDE2000, skor eğrisi, bölge renk analizi. Saf C# (Skia yok) →
│                                  hem sunucu hem mobil önizleme AYNI kodu kullanır.
├── src/ColorMatch.Application     Servisler (iş kuralları), DTO'lar, arayüzler (IAppDbContext, IFileStorage, IImageProcessor…)
├── src/ColorMatch.Infrastructure  EF Core + SQL Server, Identity, JWT, yerel dosya depolama, SkiaSharp görsel işleme, migration
├── src/ColorMatch.API             Controller'lar (ince), global hata yönetimi (ProblemDetails), JWT, rate limit, OpenAPI/Swagger
├── src/ColorMatch.Mobile          .NET MAUI uygulaması (MVVM, CommunityToolkit.Mvvm, SkiaSharp)
└── tests/ColorMatch.Tests         Color engine, domain kuralları, entegrasyon testleri (gerçek SQL Server LocalDB)
```

### Mimari kararlar
* **CQRS / MediatR / generic repository yok.** Servisler `IAppDbContext` (EF `DbSet`'leri) üzerinden çalışır; DbContext uygulaması Infrastructure'da.
  Application'ın EF Core *paketine* referansı var (async LINQ için) — pragmatik bir ödünleşim.
* **Kimlik ≠ profil.** `ApplicationUser` (Identity, Infrastructure) yalnızca kimlik bilgisi tutar; uygulama verisi Domain'deki `UserProfile` + `UserProgress` içindedir
  (aynı anahtar, 1‑1). Böylece Domain Identity'ye bağımlı olmaz.
* **Resmî skor ve ölçülen bölge sunucuda.** İstemci yalnızca fotoğraf gönderir; skor, renk ve ölçülecek bölge istemciden alınmaz (eski istemcilerin gönderdiği `RegionX/Y/RegionRadius` alanları yok sayılır). `ColorEngine.RegionFinder` fotoğrafı ≤128 px ızgaraya indirir,
  kısa kenarın %7'si yarıçaplı pencereyi her konumda kaydırır (integral görüntüyle O(1)), `ΔE00(ortalama, hedef) + 0,6·yayılım` en küçük olan **düz** bölgeyi seçer (tek bir tesadüfi piksel ya da iki rengin kenarı kazanamaz);
  bölgenin resmî rengi tam çözünürlüklü fotoğraftan okunur. Mobildeki “≈ puan” aynı algoritmanın yerel tahminidir; sonuç ekranındaki işaret sunucunun bildirdiği bölgeyi gösterir.
* **Renk ağırlığı (hile caydırıcısı).** Tek bir noktanın ya da bir renk kartelasındaki/kolajdaki tek karenin tam renkte olması yetmez: puan = renk puanı × `ColorWeight.Factor(pay)`; pay, görüntünün ne kadarının bulunan rengin ailesinden olduğudur (CIELAB, açıklık yarım ağırlıkla; gölgeli yüz aynı renk sayılır). Pay %25 ve üzeriyse tam puan, altında `(pay/0,25)^0,7` ile azalır: mükemmel renkli ama görselin %4'ünü kaplayan bir yama ≈ 276 puan alır, geçemez. Bulucu en iyi 12 farklı pencereyi `exp(−ΔE/15)·Faktör(pay)` ile karşılaştırır; yani büyük bir alan, küçük ama tam renkli bir lekeyi yener. Geçme çizgisi 500 puandır. Sonuç ekranı payı yüzde olarak gösterir ve düşükse “nesneye yaklaş” ipucu verir. Sınır: ekrandaki ya da kâğıttaki tek renkli bir görsel hâlâ geçerli sayılır.
* **Günlük renk:** `DailyColorPalette` 48 renkten tarihe bağlı deterministik seçim yapar; `Challenge:TimeZoneId` (varsayılan `Europe/Istanbul`) gün sınırını belirler. İlk istek günün kaydını oluşturur; `Date` üzerindeki unique index yarışı çözer.
* **XP suistimali engeli:** XP = f(günün en iyi skoru). Yeni deneme yalnızca *önceki ödenen XP ile yeni en iyi ödül arasındaki farkı* öder (`XpRules.XpToAward`). Aynı anda gelen isteklere karşı `UserProgress.RowVersion` (optimistic concurrency) → 409.
* **Medya:** yüklemeler tamamen decode edilip JPEG olarak yeniden kodlanır (içerik türü doğrulama, EXIF/GPS temizleme, EXIF yönü uygulama, max 2048 px, 12 MB sınırı, 60 MP decompression‑bomb koruması).
  Depolama `IFileStorage` arkasında; şimdilik `LocalFileStorage` (`/media/{anahtar}`). Gönderiler yalnızca kullanıcının kendi yüklediği anahtarları (`u/{userId}/…`) referanslayabilir.
* **Pagination:** feed ve yorumlar keyset (cursor) tabanlı: `(CreatedAtUtc, Id)` azalan, index'li.
* **Skor eğrisi:** `score = round(1000·exp(−ΔE00/15))`. ΔE 1 → 936, 3 → 819, 5 → 717, 10 → 513, 20 → 264. Kamera/ışık/ekran kalibrasyonu yüzünden sonuçlar yaklaşıktır.
* **Bölge analizi:** Lab'e çevir → parlama (L\*>94 & düşük chroma) ve çok koyu (L\*<6) pikselleri ele (yeterli piksel kalıyorsa) → Lab medyanı → medyana ΔE≤18 olanların ortalaması.

## Oyun sistemleri
Her sayı `GameBalance` yapılandırma bölümünden gelir (`appsettings`, kullanıcı sırları ya da `GameBalance__…` ortam değişkenleri); varsayılanlar `ColorMatch.Domain/Rules/GameBalance.cs` içindedir.

| Sistem | Kural | Ayar (varsayılan) |
|---|---|---|
| **XP defteri** | Tek XP kaynağı `RewardService`; her ödül benzersiz bir *idempotency anahtarıyla* deftere yazılır, `UserProgress.TotalXp` her zaman defterin toplamıdır. Aynı ödül ikinci kez verilmez (paralel isteklerde de). | — |
| **Seviye / unvan** | Seviye n için gereken XP = `LevelBaseXp·(n−1)^LevelExponent`; unvanlar seviyeye göre. | `LevelBaseXp` 100, `LevelExponent` 2,0, `Titles` |
| **Seri (streak)** | Oyun günü = `Challenge:TimeZoneId`. Günde bir kez sayılır, kaçırılan gün seriyi sıfırlayıp yeni etkinlikte 1 yapar; en uzun seri korunur. Yalnızca geçerli (≥ eşik) günlük av seriyi uzatır. | — |
| **Günlük Av** | Tarihe bağlı deterministik 3 renk (Atlas havuzlarından kolay/orta/zor). Av başına XP ilk geçerli tamamlamada bir kez; üçü de bitince bonus. Deneme limiti. | `DailyHunt`: `EasyXp` 100, `MediumXp` 200, `HardXp` 350, `MinScore` 500, `CompletionBonusXp` 150, `MaxAttemptsPerHunt` 30 |
| **Renk Atlası** | 5 koleksiyon × 20 renk (= 100). Geçerli bir av/bingo sonucu o rengi açar (bir kez). Koleksiyon tamamlanınca bir kez ödül. | `Atlas.CollectionCompleteXp` 500 |
| **Renk Bingosu** | Oyuncu başına tek aktif 3×3 tahta (sunucuda üretilir; 3 kolay + 3 orta + 3 zor, birbirinden ayırt edilir renkler). 8 çizgi (3 satır, 3 sütun, 2 çapraz) tahta başına bir kez ödenir; 9 kare bitince tahta bonusu ve yeni tahta. Kare başına deneme limiti; bir fotoğraf iki kareyi dolduramaz. | `Bingo`: `LineXp` 100, `BoardCompleteXp` 400, `MinScore` 500, `MaxAttemptsPerCell` 10 |
| **Arkadaş düellosu** | Yalnızca kabul edilmiş arkadaşlar. Davet 24 saat içinde yanıtlanır; kabulden sonra 24 saat, oyuncu başına 3 deneme, aynı renk (davetli kabul edene kadar rengi görmez). Yüksek en iyi skor kazanır; rakibin skoru düello bitene kadar gizlidir. Durumlar: Pending → Active → Completed / Expired / Declined. Süre dolumu **tembel**: arka plan zamanlayıcı yoktur, düellolar listelenirken/oynanırken sonuçlandırılır; tek sonuçlandırma `RowVersion` + XP anahtarlarıyla garanti edilir. | `Duel`: `WinXp` 200, `DrawXp` 100, `LoseXp` 50 (en az bir deneme yapan), `AttemptsPerPlayer` 3, `InviteHours` 24, `PlayHours` 24, `MaxOpenDuels` 5, `DailyXpCap` 600 |

Çakışma güvenceleri: `UserProgress`, `BingoBoard`, `BingoCell`, `Duel` üzerinde `rowversion`; `XpTransaction.IdempotencyKey` benzersiz indeks; `UserAtlasColor (UserId, ColorKey)` benzersiz; tahta başına tek aktif tahta için filtreli benzersiz indeks.

## Güvenlik sınırları (kısa ve dürüst)
* **Fotoğrafın gerçekten o an çekildiği kanıtlanamaz.** Başka bir cihazda/ekranda gösterilen ya da internetten bulunan bir görüntü de geçerli skor alır. Önlemler yalnızca *caydırıcıdır*: sunucu tarafı skor ve bölge seçimi, fotoğraf özeti (SHA‑256) ile aynı fotoğrafın aynı bingo tahtasında/düelloda/avda yeniden kullanımının engellenmesi, deneme limitleri, hız sınırı (rate limit), günlük düello XP tavanı. Rekabetçi (sıralama/düello) kullanım için bu sınır bilinmelidir.
* İki hesabın birbirine düello kazandırarak XP biriktirmesi `DailyXpCap` ile sınırlıdır; tamamen engellenmez.
* Oyuncu yalnızca kendi av/bingo/düello kayıtlarına erişir; başkasının düellosu 404 döner (varlığı ifşa edilmez).

## Gereksinimler
* .NET SDK 10 (MAUI iş yükü: `dotnet workload install maui-android`)
* JDK 17 + Android SDK (`dotnet build -t:InstallAndroidDependencies -f net10.0-android -p:AndroidSdkDirectory=<yol> -p:AcceptAndroidSDKLicenses=True`)
* SQL Server veya **SQL Server LocalDB** (varsayılan bağlantı `(localdb)\MSSQLLocalDB`, veritabanı `ColorMatch`)
* iOS derlemesi için macOS + Xcode (bu ortamda **doğrulanmadı**)

## Çalıştırma

### 1) Backend
```bash
cd src/ColorMatch.API
dotnet run --launch-profile http        # http://0.0.0.0:5030 — Development'ta migration otomatik uygulanır
```
* Swagger UI: <http://localhost:5030/swagger> (OpenAPI: `/openapi/v1.json`), sağlık: `/health`
* **Gizli anahtar:** `appsettings.Development.json` yalnızca geliştirme için bir JWT anahtarı içerir. Gerçek ortamda:
  ```bash
  dotnet user-secrets set "Jwt:Key" "<en az 32 karakterlik rastgele değer>" --project src/ColorMatch.API
  # veya ortam değişkeni: Jwt__Key, ConnectionStrings__DefaultConnection
  ```
  `Jwt:Key` yoksa API başlamaz.
* Migration'ı elle uygulamak / yenisini eklemek:
  ```bash
  dotnet tool restore
  dotnet ef database update -p src/ColorMatch.Infrastructure -s src/ColorMatch.API
  dotnet ef migrations add <Ad> -p src/ColorMatch.Infrastructure -s src/ColorMatch.API -o Persistence/Migrations
  ```
* Yüklenen dosyalar `src/ColorMatch.API/storage/` altına yazılır (`Storage:RootPath`). Linux'ta ayrıca `SkiaSharp.NativeAssets.Linux` paketi gerekir.

### 2) Android emülatör
```bash
dotnet build src/ColorMatch.Mobile -f net10.0-android -t:Run -p:AndroidSdkDirectory=%LOCALAPPDATA%\Android\Sdk
```
* Emülatörde host makine **`10.0.2.2`**'dir → uygulamanın varsayılan sunucu adresi `http://10.0.2.2:5030`.
* **Gerçek cihaz:** bilgisayarın yerel IP'siyle çalıştır (Windows güvenlik duvarında 5030'a izin ver) ve giriş ekranının altındaki “Sunucu: …” bağlantısından adresi değiştir (örn. `http://192.168.1.20:5030`).
* Dev derlemesi düz `http` kullanır (`usesCleartextTraffic`). Üretimde https + bu bayrağı kaldır.
* Emülatörde kamera sanaldır; “Galeriden seç” ile de test edebilirsin.

### 3) Testler
```bash
dotnet test
```
Entegrasyon testleri her çalıştırmada geçici bir LocalDB veritabanı oluşturup siler. Başka bir sunucu için `COLORMATCH_TEST_SQL` ortam değişkenine
`Server=...;User Id=...;Password=...;TrustServerCertificate=True` (veritabanı adı olmadan) ver. Testler gerçek migration'lar üzerinde çalışır (model değişince yeni migration eklenmezse hepsi kırılır).

**Faz 7 QA kapsamı** (247 test, üç ardışık çalıştırmada 247/247 geçti):

| # | Senaryo | Doğrulayan testler |
|---|---|---|
| 1 | Aynı günlük görevden iki kez XP yok | `DailyHuntApiTests`: `A_valid_attempt_completes_the_hunt_pays_its_xp_once…`, `Parallel_valid_attempts_on_the_same_hunt_pay_the_xp_once` |
| 2 | Streak doğru günde artar | `GameCoreRulesTests`: `Consecutive_days_extend_the_streak…`; `GameCoreApiTests.Streak_is_stored_extends_by_day_and_resets_after_a_gap` |
| 3 | Kaçırılan gün → sıfırlanır, yeni etkinlikte 1 | `A_missed_day_resets_the_streak_to_one_but_keeps_the_longest`, `Streak_shown_to_the_player_expires_after_the_grace_day` |
| 4 | Seviye XP ile tutarlı | `Level_and_xp_thresholds_agree_for_every_level`, `A_reward_books_one_ledger_row_and_raises_total_and_level`, `Parallel_rewards_keep_total_xp_equal_to_the_ledger` |
| 5 | Atlas rengi tekrar açılmaz | `AtlasApiTests`: `A_failed_attempt_unlocks_nothing_and_a_valid_one_unlocks_the_hunts_colour_once`, `Parallel_valid_attempts_unlock_the_colour_once` |
| 6 | Koleksiyon ödülü bir kez | `Finishing_a_collection_pays_the_completion_xp_exactly_once` |
| 7 | Bingo satır/sütun/çapraz | `BingoRulesTests` (8 çizginin tamamı), `One_square_can_finish_a_row_a_column_and_a_diagonal_at_once` |
| 8 | Bingo ödülü tekrar verilmez | `A_row_pays_its_xp_once…`, `Parallel_valid_attempts_on_one_square_complete_it_and_pay_its_line_once`, `Completing_all_nine_squares_pays_eight_lines_and_the_board_bonus_then_a_new_board_starts` |
| 9 | Düello deneme limiti | `A_full_duel_gives_the_winner_200_xp…` (4. deneme reddedilir), `Finishing_early_…`, `The_same_photo_cannot_be_played_twice_in_one_duel` |
| 10 | Düello süresi | `Overdue_duel_is_settled_lazily_and_a_player_who_never_played_forfeits`, `…equal_best_scores_is_a_draw…`, `…nobody_played_and_unanswered_invitations_just_expire_without_xp`, `An_invitation_hides_the_colour…` (24 saat) |
| 11 | Düello bir kez sonuçlanır | `Parallel_reads_settle_an_overdue_duel_and_pay_it_exactly_once`, `Duel_xp_is_capped_per_day` |
| 12 | Başkasının kaydı değiştirilemez | `Players_only_see_and_change_their_own_hunt_state`, `A_player_cannot_play_on_someone_elses_board`, `Only_the_invited_player_can_accept_and_strangers_see_nothing`, `A_stranger_cannot_decline_finish_or_play_someone_elses_duel…`, `Cannot_publish_someone_elses_media_or_non_images` |
| 13 | Eşzamanlı XP tutarlılığı | `Parallel_requests_with_the_same_key_pay_exactly_once`, `Parallel_rewards_keep_total_xp_equal_to_the_ledger` |
| 14 | Mobil ↔ API | `MobileContractTests` (gün/atlas/bingo/düello/sosyal yanıtları mobil modellere), + emülatörde elle doğrulama (yukarıdaki “Mobil uygulama” notları) |
| 15 | Sosyal ve kolaj bozulmadı | `FriendsTests`, `SocialTests.Upload_publish_like_comment_and_paginate`, `Cannot_publish_…`, `Avatar_upload_updates_profile`, `MobileContractTests.Social_feed_and_posts_match_the_mobile_models` (kolaj *oluşturma* arayüzü otomatik test edilmez, emülatörde önizleme elle doğrulandı) |

## API özeti
| Modül | Uç noktalar |
|---|---|
| Auth | `POST /api/auth/register`, `POST /api/auth/login` |
| Users | `GET /api/users/me`, `GET /api/users/{id}`, `GET /api/users/{id}/posts`, `POST /api/users/me/avatar` |
| Challenges | `GET /api/challenges/today` |
| ColorHunts (eski tek renk) | `POST /api/color-hunts` (multipart: `Photo`; `Region*` alanları yok sayılır, bölgeyi sunucu bulur), `GET /api/color-hunts/today` |
| Game – progression | `GET /api/game/progression/me`, `GET /api/game/progression/history` |
| Game – Daily Hunt | `GET /api/game/daily-hunts`, `GET …/progress`, `GET …/leaderboard`, `POST /api/game/daily-hunts/{id}/attempts` (multipart `Photo`) |
| Game – Atlas | `GET /api/game/atlas` |
| Game – Bingo | `GET /api/game/bingo`, `POST /api/game/bingo/cells/{index}/attempts` |
| Game – Duels | `GET /api/game/duels`, `GET /api/game/duels/{id}`, `POST /api/game/duels` (`{opponentId}`), `POST …/{id}/accept`, `…/decline`, `…/finish`, `POST …/{id}/attempts` |
| Media | `POST /api/media` (multipart `file`) |
| Posts | `GET /api/posts?cursor=&pageSize=`, `GET/DELETE /api/posts/{id}`, `POST /api/posts`, `PUT/DELETE /api/posts/{id}/like` |
| Comments | `GET/POST /api/posts/{id}/comments` |
| Friends | `GET /api/friends`, `GET /api/friends/requests`, `GET /api/friends/search?q=`, `GET /api/friends/status/{id}`, `POST /api/friends/requests`, `POST /api/friends/requests/{id}/accept`, `DELETE /api/friends/{id}` (reddet / geri al / çıkar) |
| Discovery | `GET /api/posts?scope=friends\|all&color=Mavi`, `GET /api/posts/colors` (renk ailesi + gönderi sayısı) |
| Home | `GET /api/challenges/today/leaderboard`, `GET /api/challenges/recent` |

Hatalar RFC 7807 `ProblemDetails`: 400 (doğrulama, `errors` sözlüğü ile), 401, 403, 404, 409, 429 (auth rate limit), 503 (veritabanı), 500 (ayrıntı sızdırmaz).

## Mobil uygulama
Ekranlar: Splash, Onboarding, Giriş/Kayıt, Ana sayfa (Oyna), Görevler (günlük avlar + bingo + düellolar), Av (kamera + otomatik bulunan bölge + anlık tahmin; günlük av, bingo karesi ve düello aynı ekranda oynanır), Sonuç (seviye/seri/bingo/atlas/düello kutlama kartları),
Renk Atlası, Renk Bingosu, Arkadaş düelloları, Kolaj oluşturucu (4 şablon, 2 format, arka plan rengi, renk paleti/HEX etiketleri, galeriye kaydet, paylaşım sayfası, akışta yayınla), Topluluk (akış + keşfet + arkadaşlar), Gönderi detayı (beğeni/yorum), Profil.
Alt gezinme (yüzen buzlu cam çubuk): Oyna · Görevler · Rengi bul (kamera) · Atlas · Topluluk; profil sağ üstteki avatardan, Oluştur Topluluk'taki “+” ile açılır. Açık/Koyu/Sistem teması. Demo modu sunucusuz çalışır (aynı kurallar, yerel veriyle).
Buzlu cam: MAUI'de arka plan bulanıklığı olmadığından Android'de çubuğun altındaki sayfa bölgesinin küçük bir görüntüsü alınıp bulanıklaştırılır (~8 kare/sn, yalnızca sayfa görünürken); diğer platformlarda yarı saydam çubuk.
Design system: `Resources/Styles/Colors.xaml` (renk/boşluk/yarıçap/tipografi token'ları), `Styles.xaml` (kart, buton, input, durum stilleri), `Controls/` (ColorCard, StateView, AvatarView, IconView).
Token `SecureStorage`'da tutulur; 401 gelince oturum otomatik kapanır.

## Bilinen sınırlar / yapılmayanlar
* Arkadaşlık: istek gönder / kabul et / reddet / geri al / çıkar. Akışta “Arkadaşlar” sekmesi (kendin + kabul edilmiş arkadaşlar), “Keşfet” sekmesinde herkesin paylaşımları renk ailesine göre (Kırmızı, Turuncu, Sarı, Yeşil, Turkuaz, Mavi, Mor, Pembe, Kahverengi, Beyaz, Gri, Siyah; CIELCh tonuna göre sınıflanır).
* Takip (tek yönlü) sistemi, mesajlaşma, refresh token, e‑posta doğrulama, şifre sıfırlama, hesap silme **yok**.
* Kamera `MediaPicker.CapturePhotoAsync` (sistem kamera uygulaması) ile açılır; uygulama içi canlı kamera önizlemesi yok.
* Kolajda sürükle‑bırak editör yok (istenildiği gibi); fotoğraflar hücreye merkez‑kırpma ile yerleşir.
* Mobil arayüz için otomatik UI testi yok. Bunun yerine **API ↔ mobil sözleşme testleri** (`MobileContractTests`) mobilin kendi model kayıtlarını gerçek sunucu yanıtlarıyla doldurur ve eksik/fazla alanı yakalar. Arayüz Android emülatöründe elle doğrulandı: onboarding, giriş, ana sayfa, günlük av + sonuç, bingo (kare, çizgi ödülü, seviye atlama), atlas, düello (davet → kabul → deneme), Görevler/Atlas/Topluluk sekmeleri (açık ve koyu tema), otomatik bölge bulma.
  Emülatörde denenmeyenler: gerçek kamera, bingo tahtasının 9 karesinin tamamı ve +400 XP bonusu, koleksiyon tamamlama ekranı, kutlama animasyonları, düello sonucu/XP ekranları (bunlar yalnızca backend testlerinde doğrulandı), demo modunun yeni gezinmeyle akışı, gerçek telefonda performans (özellikle buzlu cam).
* Statü listesindeki “Accepted” düello durumu ayrı değildir: kabul doğrudan `Active` olur. Düello için push bildirimi yoktur.
* iOS kodu (`IosGallerySaver`) macOS'ta derlenmediği için doğrulanmadı.
* CIEDE2000 testinde Sharma referans setinin 33 çifti kullanıldı (33. çift girdileri doğrulanamadığı için çıkarıldı).

## Veritabanı bağlantısı (bu geliştirme makinesi)
Bağlantı dizesi `appsettings.json`'a değil **user-secrets**'a yazıldı (şifre depoya girmez). Yeniden ayarlamak için:
```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=<sunucu>,<port>;Database=ColorMatch;User Id=<kullanıcı>;Password=<şifre>;TrustServerCertificate=True" --project src/ColorMatch.API
```
Sunucu adresi, kullanıcı ve şifre depoda tutulmaz; yalnızca yukarıdaki gibi user-secrets / ortam değişkeniyle verilir. API Development ortamında açılırken migration'ları kendisi uygular.
