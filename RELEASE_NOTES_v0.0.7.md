# Release notes — v0.0.7

## Faits marquants

Refonte complète de la robustesse, de la sécurité et des performances du service. Le binaire est désormais publié en **single‑file self‑contained**, et la version est injectée automatiquement depuis le tag Git par le workflow de release.

## ✨ Nouveautés

- **Auto‑génération de `appsettings.json`** au premier lancement si le fichier est absent (copie du template ou écriture d'un squelette par défaut).
- **Vérification de mise à jour via l'API GitHub Releases** : lecture du dernier tag `v*.*.*` et comparaison sémantique avec la version d'assembly.
- **Désban via le fichier** : retirer une ligne (SteamID64) du fichier surveillé marque automatiquement le ban correspondant comme `RemoveType = 'U'` en base (`ureason = "Removed via Ban Sync file"`).
- **Single‑file self‑contained publish** (`PublishSingleFile`, `SelfContained`, `IncludeNativeLibrariesForSelfExtract`).
- **Workflow de release** : la version (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`) est extraite du tag `vX.Y.Z` et injectée à la compilation.

## 🚀 Performances

- **Batch Steam API** : appel `GetPlayerSummaries` groupé jusqu'à 100 SteamIDs par requête au lieu d'un appel par joueur.
- **Diffs HashSet** pour la détection des lignes ajoutées/retirées (O(n) au lieu de O(n²)).
- **`PeriodicTimer`** + `Parallel.ForEachAsync` (degré 4) pour la boucle de synchronisation et le traitement des nouvelles entrées.
- **`JsonSerializerOptions` statique** réutilisé partout (évite l'allocation et le warmup à chaque sérialisation).
- **Cache Steam** en `ConcurrentDictionary` avec nettoyage périodique des entrées expirées.

## 🔒 Sécurité & fiabilité

- **HTTPS forcé** sur tous les appels à l'API Steam.
- **Requêtes SQL paramétrées et typées** (`MySqlDbType`) — protection contre l'injection SQL.
- **Polly Standard Resilience Handler** (`Microsoft.Extensions.Http.Resilience`) sur tous les `HttpClient` : retry, circuit breaker, timeouts totaux et par tentative.
- **Timeouts HTTP** : 20 s (Steam) / 15 s (Discord, GitHub).
- **Respect de Discord rate‑limit** : prise en compte du header `Retry-After` **et** du champ JSON `retry_after`, avec jusqu'à 3 tentatives.
- **Validation de configuration au démarrage** (`ValidateDataAnnotations().ValidateOnStart()`) — le service refuse de démarrer si la config est invalide.
- **Test de connexion DB** au démarrage (et plus en boucle).
- **Masquage des URLs de webhook Discord** dans les logs.

## 🏗️ Architecture

- **Injection de dépendances généralisée** : `IHostBuilder` + `BackgroundService`, options typées (`IOptions<BanSyncConfiguration>`, `IOptions<DiscordConfiguration>`, `IOptions<GitHubConfiguration>`).
- **`IHttpClientFactory` nommé** pour les 3 services HTTP, services applicatifs enregistrés en Singleton (pour préserver les caches d'état).
- **`FileWatcherService`** : `InternalBufferSize = 65536` et **debouncing réel** (500 ms, le dernier événement gagne) via `CancellationTokenSource`, gestion sûre des `async void` handlers.
- **Suppression de Newtonsoft.Json** au profit de `System.Text.Json` exclusivement.

## 🧹 Nettoyage

- Suppression du champ obsolète `GitHub.CurrentVersion` dans la configuration (la version vient désormais de l'assembly).
- Suppression de `OPTIMIZATION_SUMMARY.md` et `MIGRATION.md` (le `README.md` est la seule source de doc).
- Suppression du `TestConnectionAsync` dupliqué dans `BanSyncService.ExecuteAsync`.

## ⚠️ Breaking changes

- Le champ `GitHub.CurrentVersion` dans `appsettings.json` n'est plus lu — vous pouvez le retirer.
- L'exécutable publié est désormais self‑contained : pas besoin d'installer le runtime .NET sur la machine cible, mais le binaire est plus volumineux.

## 📦 Mise à jour des dépendances

- `Microsoft.Extensions.Http.Resilience` 8.10.0 (ajout)
- `Microsoft.Extensions.Options.DataAnnotations` 8.0.0 (ajout)
- `Microsoft.Extensions.Http` / `Logging` / `DependencyInjection` → 8.0.1

---

**Full changelog** : https://github.com/Royal-Multi-Gamers/Ban-Sync-Sourcebans/compare/v0.0.6...v0.0.7
